namespace Egghead.Authentication
{
    public interface IAuthenticationSession
    {
        bool IsSignedIn { get; }

        void SignOut(bool clearCredentials);

        void ClearSessionToken();
    }
}
