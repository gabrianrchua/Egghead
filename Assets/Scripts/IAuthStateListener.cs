public interface IAuthStateListener
{
    public void OnSignedIn();
    public void OnSignInFailed(Unity.Services.Core.RequestFailedException err);
    public void OnSignedOut();
    public void OnExpired();
}
