using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class AuthenticationListenerLifecycleTests
{
    private const string LocalOnlyModeKey = "SaveManager.LocalOnlyMode";
    private int originalLocalOnlyMode;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        originalLocalOnlyMode = PlayerPrefs.GetInt(LocalOnlyModeKey, 0);
        PlayerPrefs.SetInt(LocalOnlyModeKey, 1);
        SceneManager.LoadScene("Title");
        yield return null;
        yield return WaitUntil(() => FindComponent("SaveManager") != null && FindComponent("TitleScreen") != null);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        PlayerPrefs.SetInt(LocalOnlyModeKey, originalLocalOnlyMode);
        SceneManager.LoadScene("Title");
        yield return null;
    }

    [UnityTest]
    public IEnumerator TitleGameTitleCyclesKeepOnlyTheActiveTitleListener()
    {
        object saveManager = FindComponent("SaveManager");
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

    private static int GetListenerCount(object saveManager)
    {
        object registry = saveManager.GetType()
            .GetField("authStateListeners", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(saveManager);
        return (int)registry.GetType()
            .GetProperty("ListenerCount", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(registry);
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
}
