using Unity.Services.Core;

public interface IAuthStateListener
{
    void OnSignedIn();
    void OnSignInFailed(RequestFailedException err);
    void OnSignedOut();
    void OnExpired();
}
