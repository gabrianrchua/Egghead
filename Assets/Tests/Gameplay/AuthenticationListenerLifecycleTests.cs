using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Egghead.Authentication;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class AuthenticationListenerLifecycleTests
{
    private const string LocalOnlyModeKey = "SaveManager.LocalOnlyMode";
    private int originalLocalOnlyMode;
    private object saveManager;
    private IAuthenticationSession originalAuthenticationSession;
    private IAuthEventSource originalAuthEventSource;
    private bool originalCloudAvailable;
    private bool originalManagerLocalOnlyMode;
    private System.DateTime originalCacheExpiry;
    private string saveFilePath;
    private bool saveFileExisted;
    private string originalSaveFileContents;
    private RecordingListener recordingListener;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        originalLocalOnlyMode = PlayerPrefs.GetInt(LocalOnlyModeKey, 0);
        PlayerPrefs.SetInt(LocalOnlyModeKey, 1);
        SceneManager.LoadScene("Title");
        yield return null;
        yield return WaitUntil(() => FindComponent("SaveManager") != null && FindComponent("TitleScreen") != null);

        saveManager = FindComponent("SaveManager");
        yield return WaitUntil(() => GetInitializationTask(saveManager)?.IsCompleted != false);
        GetRegistry(saveManager).Register((IAuthStateListener)FindComponent("TitleScreen"));

        originalAuthenticationSession = GetField<IAuthenticationSession>(saveManager, "authenticationSession");
        originalAuthEventSource = GetField<IAuthEventSource>(saveManager, "authEventSource");
        originalCloudAvailable = GetField<bool>(saveManager, "cloudAvailable");
        originalManagerLocalOnlyMode = GetField<bool>(saveManager, "localOnlyMode");
        originalCacheExpiry = GetField<System.DateTime>(saveManager, "_currentSaveDataExpires");
        saveFilePath = Path.Combine(Application.persistentDataPath, "save.json");
        saveFileExisted = File.Exists(saveFilePath);
        originalSaveFileContents = saveFileExisted ? File.ReadAllText(saveFilePath) : null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        AuthStateListenerRegistry registry = GetRegistry(saveManager);
        if (recordingListener != null)
        {
            registry.Unregister(recordingListener);
        }

        registry.SetEventSource(originalAuthEventSource);
        SetField(saveManager, "authEventSource", originalAuthEventSource);
        SetField(saveManager, "authenticationSession", originalAuthenticationSession);
        SetField(saveManager, "cloudAvailable", originalCloudAvailable);
        SetField(saveManager, "localOnlyMode", originalManagerLocalOnlyMode);
        SetField(saveManager, "_currentSaveDataExpires", originalCacheExpiry);

        PlayerPrefs.SetInt(LocalOnlyModeKey, originalLocalOnlyMode);
        PlayerPrefs.Save();

        if (saveFileExisted)
        {
            File.WriteAllText(saveFilePath, originalSaveFileContents);
        }
        else if (File.Exists(saveFilePath))
        {
            File.Delete(saveFilePath);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator TitleGameTitleCyclesKeepOnlyTheActiveTitleListener()
    {
        Assert.That(GetListenerCount(saveManager), Is.EqualTo(1));

        for (int cycle = 0; cycle < 3; cycle++)
        {
            SceneManager.LoadScene("Main");
            yield return null;
            Assert.That(GetListenerCount(saveManager), Is.Zero);

            SceneManager.LoadScene("Title");
            yield return null;
            yield return WaitUntil(() => FindComponent("TitleScreen") != null);
            Assert.That(GetListenerCount(saveManager), Is.EqualTo(1));
        }
    }

    [UnityTest]
    public IEnumerator SignedInSignOutPublishesFinalStateOnceAndSurvivesTitleReload()
    {
        FakeAuthenticationProvider provider = PrepareSignOut(true);

        InvokeSignOutToLocalOnly();

        AssertFinalLocalOnlyState(provider, expectedSignOutCalls: 1, expectedClearTokenCalls: 0);
        AssertLocalSaveUnchanged();

        SceneManager.LoadScene("Title");
        yield return null;
        yield return WaitUntil(() => FindComponent("TitleScreen") != null);
        object titleScreen = FindComponent("TitleScreen");
        GetRegistry(saveManager).Register((IAuthStateListener)titleScreen);
        AssertSignedOutPanels(titleScreen);
    }

    [Test]
    public void AlreadySignedOutPublishesFinalStateAfterClearingToken()
    {
        FakeAuthenticationProvider provider = PrepareSignOut(false);

        InvokeSignOutToLocalOnly();

        AssertFinalLocalOnlyState(provider, expectedSignOutCalls: 0, expectedClearTokenCalls: 1);
        AssertLocalSaveUnchanged();
    }

    [TestCase(true)]
    [TestCase(false)]
    public void AuthenticationCleanupFailureKeepsLocalOnlyStateAndRefreshesUi(bool initiallySignedIn)
    {
        FakeAuthenticationProvider provider = PrepareSignOut(initiallySignedIn);
        provider.ThrowOnCleanup = true;
        string operation = initiallySignedIn ? "sign-out" : "clear-token";
        LogAssert.Expect(LogType.Error, $"Failed to clear authentication session: injected {operation} failure");

        InvokeSignOutToLocalOnly();

        AssertFinalLocalOnlyState(
            provider,
            expectedSignOutCalls: initiallySignedIn ? 1 : 0,
            expectedClearTokenCalls: initiallySignedIn ? 0 : 1);
        AssertLocalSaveUnchanged();
    }

    private FakeAuthenticationProvider PrepareSignOut(bool initiallySignedIn)
    {
        FakeAuthenticationProvider provider = new(initiallySignedIn);
        AuthStateListenerRegistry registry = GetRegistry(saveManager);
        registry.SetEventSource(provider);
        SetField(saveManager, "authEventSource", provider);
        SetField(saveManager, "authenticationSession", provider);
        SetField(saveManager, "localOnlyMode", false);
        SetField(saveManager, "cloudAvailable", true);
        SetField(saveManager, "_currentSaveDataExpires", System.DateTime.UtcNow.AddMinutes(5));
        PlayerPrefs.SetInt(LocalOnlyModeKey, 0);
        PlayerPrefs.Save();

        recordingListener = new RecordingListener(saveManager);
        registry.Register(recordingListener);
        provider.BeforeCleanup = () =>
        {
            provider.ObservedPreparedState =
                GetProperty<bool>(saveManager, "IsLocalOnly") &&
                !GetProperty<bool>(saveManager, "IsCloudActive") &&
                !GetField<bool>(saveManager, "cloudAvailable") &&
                PlayerPrefs.GetInt(LocalOnlyModeKey, 0) == 1 &&
                GetField<System.DateTime>(saveManager, "_currentSaveDataExpires") == default;
        };

        object titleScreen = FindComponent("TitleScreen");
        GetField<GameObject>(titleScreen, "profilePanel").SetActive(true);
        GetField<GameObject>(titleScreen, "signInPanel").SetActive(false);
        GetField<GameObject>(titleScreen, "registerPanel").SetActive(false);
        return provider;
    }

    private void InvokeSignOutToLocalOnly()
    {
        saveManager.GetType()
            .GetMethod("SignOutToLocalOnly", BindingFlags.Instance | BindingFlags.Public)
            .Invoke(saveManager, null);
    }

    private void AssertFinalLocalOnlyState(
        FakeAuthenticationProvider provider,
        int expectedSignOutCalls,
        int expectedClearTokenCalls)
    {
        Assert.That(provider.SignOutCalls, Is.EqualTo(expectedSignOutCalls));
        Assert.That(provider.ClearTokenCalls, Is.EqualTo(expectedClearTokenCalls));
        Assert.That(recordingListener.SignedOutCount, Is.EqualTo(1));
        Assert.That(recordingListener.ObservedLocalOnly, Is.True);
        Assert.That(recordingListener.ObservedCloudActive, Is.False);
        Assert.That(recordingListener.ObservedPersistedLocalOnly, Is.True);
        Assert.That(provider.ObservedPreparedState, Is.True);
        Assert.That(GetProperty<bool>(saveManager, "IsLocalOnly"), Is.True);
        Assert.That(GetProperty<bool>(saveManager, "IsCloudActive"), Is.False);
        Assert.That(GetField<bool>(saveManager, "cloudAvailable"), Is.False);
        AssertSignedOutPanels(FindComponent("TitleScreen"));
    }

    private void AssertLocalSaveUnchanged()
    {
        Assert.That(File.Exists(saveFilePath), Is.EqualTo(saveFileExisted));
        if (saveFileExisted)
        {
            Assert.That(File.ReadAllText(saveFilePath), Is.EqualTo(originalSaveFileContents));
        }
    }

    private static void AssertSignedOutPanels(object titleScreen)
    {
        Assert.That(GetField<GameObject>(titleScreen, "profilePanel").activeSelf, Is.False);
        Assert.That(GetField<GameObject>(titleScreen, "signInPanel").activeSelf, Is.True);
        Assert.That(GetField<GameObject>(titleScreen, "registerPanel").activeSelf, Is.False);
    }

    private static int GetListenerCount(object saveManager)
    {
        object registry = GetRegistry(saveManager);
        return (int)registry.GetType()
            .GetProperty("ListenerCount", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(registry);
    }

    private static AuthStateListenerRegistry GetRegistry(object manager)
    {
        return GetField<AuthStateListenerRegistry>(manager, "authStateListeners");
    }

    private static System.Threading.Tasks.Task GetInitializationTask(object manager)
    {
        return GetField<System.Threading.Tasks.Task>(manager, "_initializationTask");
    }

    private static T GetField<T>(object target, string fieldName)
    {
        return (T)target.GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(target);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        target.GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);
    }

    private static T GetProperty<T>(object target, string propertyName)
    {
        return (T)target.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            .GetValue(target);
    }

    private static object FindComponent(string typeName)
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(typeName, false))
            .FirstOrDefault(candidate => candidate != null);
        return type == null ? null : UnityEngine.Object.FindAnyObjectByType(type);
    }

    private static IEnumerator WaitUntil(Func<bool> predicate, float timeout = 10f)
    {
        float startedAt = Time.realtimeSinceStartup;
        while (!predicate())
        {
            if (Time.realtimeSinceStartup - startedAt > timeout)
            {
                Assert.Fail("Timed out waiting for authentication lifecycle state.");
            }
            yield return null;
        }
    }

    private sealed class RecordingListener : IAuthStateListener
    {
        private readonly object manager;

        public RecordingListener(object manager)
        {
            this.manager = manager;
        }

        public int SignedOutCount { get; private set; }
        public bool ObservedLocalOnly { get; private set; }
        public bool ObservedCloudActive { get; private set; }
        public bool ObservedPersistedLocalOnly { get; private set; }

        public void OnSignedIn() { }
        public void OnSignInFailed(Unity.Services.Core.RequestFailedException err) { }

        public void OnSignedOut()
        {
            SignedOutCount++;
            ObservedLocalOnly = GetProperty<bool>(manager, "IsLocalOnly");
            ObservedCloudActive = GetProperty<bool>(manager, "IsCloudActive");
            ObservedPersistedLocalOnly = PlayerPrefs.GetInt(LocalOnlyModeKey, 0) == 1;
        }

        public void OnExpired() { }
    }

    private sealed class FakeAuthenticationProvider : IAuthenticationSession, IAuthEventSource
    {
        public FakeAuthenticationProvider(bool isSignedIn)
        {
            IsSignedIn = isSignedIn;
        }

        public bool IsSignedIn { get; private set; }
        public bool ThrowOnCleanup { get; set; }
        public bool ObservedPreparedState { get; set; }
        public int SignOutCalls { get; private set; }
        public int ClearTokenCalls { get; private set; }
        public Action BeforeCleanup { get; set; }

        public event Action SignedIn
        {
            add { }
            remove { }
        }

        public event Action<Unity.Services.Core.RequestFailedException> SignInFailed
        {
            add { }
            remove { }
        }

        public event Action SignedOut;

        public event Action Expired
        {
            add { }
            remove { }
        }

        public void SignOut(bool clearCredentials)
        {
            SignOutCalls++;
            Assert.That(clearCredentials, Is.True);
            BeforeCleanup?.Invoke();
            if (ThrowOnCleanup)
            {
                throw new InvalidOperationException("injected sign-out failure");
            }

            IsSignedIn = false;
            SignedOut?.Invoke();
        }

        public void ClearSessionToken()
        {
            ClearTokenCalls++;
            BeforeCleanup?.Invoke();
            if (ThrowOnCleanup)
            {
                throw new InvalidOperationException("injected clear-token failure");
            }
        }
    }
}
