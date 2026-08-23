using System;
using Egghead.Authentication;
using NUnit.Framework;
using Unity.Services.Core;
using UnityEngine;

public class AuthStateListenerRegistryTests
{
    [Test]
    public void DuplicateRegistrationReceivesEachEventOnce()
    {
        FakeAuthEventSource source = new();
        AuthStateListenerRegistry registry = new();
        CountingListener listener = new();

        registry.SetEventSource(source);
        registry.Register(listener);
        registry.Register(listener);

        source.RaiseAll();

        Assert.That(registry.ListenerCount, Is.EqualTo(1));
        Assert.That(listener.SignedInCount, Is.EqualTo(1));
        Assert.That(listener.SignInFailedCount, Is.EqualTo(1));
        Assert.That(listener.SignedOutCount, Is.EqualTo(1));
        Assert.That(listener.ExpiredCount, Is.EqualTo(1));
    }

    [Test]
    public void UnregisterIsIdempotentAndStopsAllEvents()
    {
        FakeAuthEventSource source = new();
        AuthStateListenerRegistry registry = new();
        CountingListener listener = new();

        registry.SetEventSource(source);
        registry.Register(listener);
        registry.Unregister(listener);
        registry.Unregister(listener);
        source.RaiseAll();

        Assert.That(registry.ListenerCount, Is.Zero);
        Assert.That(listener.TotalCount, Is.Zero);
    }

    [Test]
    public void RegistrationAndUnregistrationBeforeSourceAttachmentAreSymmetric()
    {
        FakeAuthEventSource source = new();
        AuthStateListenerRegistry registry = new();
        CountingListener activeListener = new();
        CountingListener removedListener = new();

        registry.Register(activeListener);
        registry.Register(removedListener);
        registry.Unregister(removedListener);
        registry.SetEventSource(source);
        source.RaiseAll();

        Assert.That(activeListener.TotalCount, Is.EqualTo(4));
        Assert.That(removedListener.TotalCount, Is.Zero);
    }

    [Test]
    public void ReattachingSameSourceDoesNotDuplicateSubscriptions()
    {
        FakeAuthEventSource source = new();
        AuthStateListenerRegistry registry = new();
        CountingListener listener = new();

        registry.Register(listener);
        registry.SetEventSource(source);
        registry.SetEventSource(source);
        source.RaiseSignedIn();

        Assert.That(source.SignedInSubscriptionCount, Is.EqualTo(1));
        Assert.That(listener.SignedInCount, Is.EqualTo(1));
    }

    [Test]
    public void DestroyedUnityListenerIsPrunedBeforeDispatch()
    {
        FakeAuthEventSource source = new();
        AuthStateListenerRegistry registry = new();
        UnityCountingListener listener = ScriptableObject.CreateInstance<UnityCountingListener>();

        registry.SetEventSource(source);
        registry.Register(listener);
        UnityEngine.Object.DestroyImmediate(listener);
        source.RaiseSignedIn();

        Assert.That(registry.ListenerCount, Is.Zero);
        Assert.That(UnityCountingListener.CallbackCount, Is.Zero);
    }

    [SetUp]
    public void SetUp()
    {
        UnityCountingListener.CallbackCount = 0;
    }

    private sealed class CountingListener : IAuthStateListener
    {
        public int SignedInCount { get; private set; }
        public int SignInFailedCount { get; private set; }
        public int SignedOutCount { get; private set; }
        public int ExpiredCount { get; private set; }
        public int TotalCount => SignedInCount + SignInFailedCount + SignedOutCount + ExpiredCount;

        public void OnSignedIn() => SignedInCount++;
        public void OnSignInFailed(RequestFailedException err) => SignInFailedCount++;
        public void OnSignedOut() => SignedOutCount++;
        public void OnExpired() => ExpiredCount++;
    }

    private sealed class UnityCountingListener : ScriptableObject, IAuthStateListener
    {
        public static int CallbackCount { get; set; }

        public void OnSignedIn() => CallbackCount++;
        public void OnSignInFailed(RequestFailedException err) => CallbackCount++;
        public void OnSignedOut() => CallbackCount++;
        public void OnExpired() => CallbackCount++;
    }

    private sealed class FakeAuthEventSource : IAuthEventSource
    {
        private Action signedIn;

        public int SignedInSubscriptionCount { get; private set; }

        public event Action SignedIn
        {
            add
            {
                signedIn += value;
                SignedInSubscriptionCount++;
            }
            remove
            {
                signedIn -= value;
                SignedInSubscriptionCount--;
            }
        }

        public event Action<RequestFailedException> SignInFailed;
        public event Action SignedOut;
        public event Action Expired;

        public void RaiseSignedIn() => signedIn?.Invoke();

        public void RaiseAll()
        {
            signedIn?.Invoke();
            SignInFailed?.Invoke(null);
            SignedOut?.Invoke();
            Expired?.Invoke();
        }
    }
}
