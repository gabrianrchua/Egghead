using System;
using Unity.Services.Core;

namespace Egghead.Authentication
{
    public interface IAuthEventSource
    {
        event Action SignedIn;
        event Action<RequestFailedException> SignInFailed;
        event Action SignedOut;
        event Action Expired;
    }
}
