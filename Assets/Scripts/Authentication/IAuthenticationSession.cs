namespace Egghead.Authentication
{
    public interface IAuthenticationSession
    {
        bool IsSignedIn { get; }

        System.Threading.Tasks.Task DeleteAccountAsync();

        void SignOut(bool clearCredentials);

        void ClearSessionToken();
    }
}
