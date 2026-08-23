using System;
using Unity.Services.Authentication;
using Unity.Services.Core;

namespace Egghead.Authentication
{
    public sealed class UnityAuthenticationEventSource : IAuthEventSource
    {
        private readonly IAuthenticationService authenticationService;

        public UnityAuthenticationEventSource(IAuthenticationService authenticationService)
        {
            this.authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        }

        public event Action SignedIn
        {
            add => authenticationService.SignedIn += value;
            remove => authenticationService.SignedIn -= value;
        }

        public event Action<RequestFailedException> SignInFailed
        {
            add => authenticationService.SignInFailed += value;
            remove => authenticationService.SignInFailed -= value;
        }

        public event Action SignedOut
        {
            add => authenticationService.SignedOut += value;
            remove => authenticationService.SignedOut -= value;
        }

        public event Action Expired
        {
            add => authenticationService.Expired += value;
            remove => authenticationService.Expired -= value;
        }
    }
}
