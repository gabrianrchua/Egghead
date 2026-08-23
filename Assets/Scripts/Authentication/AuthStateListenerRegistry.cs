using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Services.Core;
using UnityEngine;

namespace Egghead.Authentication
{
    public sealed class AuthStateListenerRegistry
    {
        private readonly HashSet<IAuthStateListener> listeners = new(ListenerReferenceComparer.Instance);
        private IAuthEventSource eventSource;

        internal int ListenerCount => listeners.Count;

        public void Register(IAuthStateListener listener)
        {
            if (IsMissing(listener))
            {
                return;
            }

            listeners.Add(listener);
        }

        public void Unregister(IAuthStateListener listener)
        {
            if (listener == null)
            {
                return;
            }

            listeners.Remove(listener);
        }

        public void SetEventSource(IAuthEventSource source)
        {
            if (ReferenceEquals(eventSource, source))
            {
                return;
            }

            if (eventSource != null)
            {
                eventSource.SignedIn -= OnSignedIn;
                eventSource.SignInFailed -= OnSignInFailed;
                eventSource.SignedOut -= OnSignedOut;
                eventSource.Expired -= OnExpired;
            }

            eventSource = source;
            if (eventSource == null)
            {
                return;
            }

            eventSource.SignedIn += OnSignedIn;
            eventSource.SignInFailed += OnSignInFailed;
            eventSource.SignedOut += OnSignedOut;
            eventSource.Expired += OnExpired;
        }

        public void NotifySignedIn() => OnSignedIn();

        private void OnSignedIn()
        {
            Dispatch(listener => listener.OnSignedIn());
        }

        private void OnSignInFailed(RequestFailedException error)
        {
            Dispatch(listener => listener.OnSignInFailed(error));
        }

        private void OnSignedOut()
        {
            Dispatch(listener => listener.OnSignedOut());
        }

        private void OnExpired()
        {
            Dispatch(listener => listener.OnExpired());
        }

        private void Dispatch(System.Action<IAuthStateListener> callback)
        {
            listeners.RemoveWhere(IsMissing);
            IAuthStateListener[] snapshot = new IAuthStateListener[listeners.Count];
            listeners.CopyTo(snapshot);

            foreach (IAuthStateListener listener in snapshot)
            {
                if (!IsMissing(listener) && listeners.Contains(listener))
                {
                    callback(listener);
                }
            }
        }

        private static bool IsMissing(IAuthStateListener listener)
        {
            return listener == null || listener is Object unityObject && unityObject == null;
        }

        private sealed class ListenerReferenceComparer : IEqualityComparer<IAuthStateListener>
        {
            public static readonly ListenerReferenceComparer Instance = new();

            public bool Equals(IAuthStateListener x, IAuthStateListener y) => ReferenceEquals(x, y);

            public int GetHashCode(IAuthStateListener obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
