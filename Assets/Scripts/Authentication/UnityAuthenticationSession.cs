using System;
using Unity.Services.Authentication;

namespace Egghead.Authentication
{
    public sealed class UnityAuthenticationSession : IAuthenticationSession
    {
        private readonly IAuthenticationService authenticationService;

        public UnityAuthenticationSession(IAuthenticationService authenticationService)
        {
            this.authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        }

        public bool IsSignedIn => authenticationService.IsSignedIn;

        public void SignOut(bool clearCredentials) => authenticationService.SignOut(clearCredentials);

        public void ClearSessionToken() => authenticationService.ClearSessionToken();
    }
}
