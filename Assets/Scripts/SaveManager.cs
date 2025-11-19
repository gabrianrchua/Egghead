using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using System.Threading.Tasks;
using System.Collections.Generic;

public class SaveManager : Singleton<SaveManager>
{
    public struct SaveData
    {
        public int Score;
        public System.DateTime Timestamp;
        public string LetterTileData;
    }

    private new async void Awake()
    {
        base.Awake();

        try
        {
            await UnityServices.InitializeAsync();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }

        SetupEvents();
    }

    /// <summary>
    /// Set up authentication event handlers for logging
    /// </summary>
    private void SetupEvents()
    {
        AuthenticationService.Instance.SignedIn += () =>
        {
            // Shows how to get a playerID
            Debug.Log($"PlayerID: {AuthenticationService.Instance.PlayerId}");

            // Shows how to get an access token
            Debug.Log($"Access Token: {AuthenticationService.Instance.AccessToken}");

        };

        AuthenticationService.Instance.SignInFailed += (err) =>
        {
            Debug.LogError(err);
        };

        AuthenticationService.Instance.SignedOut += () =>
        {
            Debug.Log("Player signed out.");
        };

        AuthenticationService.Instance.Expired += () =>
        {
            Debug.Log("Player session could not be refreshed and expired.");
        };
    }

    /// <summary>
    /// Sign up a new anonymous player
    /// </summary>
    public async Task SignUpAnonymouslyAsync()
    {
        try
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log("Sign in anonymously succeeded!");

            // Shows how to get the playerID
            Debug.Log($"PlayerID: {AuthenticationService.Instance.PlayerId}");

        }
        catch (AuthenticationException ex)
        {
            // Compare error code to AuthenticationErrorCodes
            // Notify the player with the proper error message
            Debug.LogException(ex);
        }
        catch (RequestFailedException ex)
        {
            // Compare error code to CommonErrorCodes
            // Notify the player with the proper error message
            Debug.LogException(ex);
        }
    }

    /// <summary>
    /// Sign in an existing player. This will also refresh a username/password user
    /// </summary>
    private async Task SignInAnonymouslyAsync()
    {
        // Sign in Anonymously
        // This call will sign in the cached player.
        try
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log("Sign in anonymously succeeded!");

            // Shows how to get the playerID
            Debug.Log($"PlayerID: {AuthenticationService.Instance.PlayerId}");
        }
        catch (AuthenticationException ex)
        {
            // Compare error code to AuthenticationErrorCodes
            // Notify the player with the proper error message
            Debug.LogException(ex);
        }
        catch (RequestFailedException ex)
        {
            // Compare error code to CommonErrorCodes
            // Notify the player with the proper error message
            Debug.LogException(ex);
        }
    }

    /// <summary>
    /// Sign in an existing cached user
    /// </summary>
    public async Task SignInCachedUserAsync()
    {
        // Check if a cached player already exists by checking if the session token exists
        if (!AuthenticationService.Instance.SessionTokenExists)
        {
            // if not, then do nothing
            return;
        }

        // Else, re/sign in
        await SignInAnonymouslyAsync();
    }

    /// <summary>
    /// Sign in the user using a username and password.
    /// See <a href="https://docs.unity.com/ugs/en-us/manual/authentication/manual/platform-signin-username-password">Unity documentation on username/password requirements</a>
    /// </summary>
    /// <param name="username">Username string</param>
    /// <param name="password">Password string</param>
    public async Task SignInWithUsernamePasswordAsync(string username, string password)
    {
        try
        {
            await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);
            Debug.Log("SignIn is successful.");
        }
        catch (AuthenticationException ex)
        {
            // Compare error code to AuthenticationErrorCodes
            // Notify the player with the proper error message
            Debug.LogException(ex);
        }
        catch (RequestFailedException ex)
        {
            // Compare error code to CommonErrorCodes
            // Notify the player with the proper error message
            Debug.LogException(ex);
        }
    }

    /// <summary>
    /// Add a username/password sign in option for an anonymously signed in user.
    /// See <a href="https://docs.unity.com/ugs/en-us/manual/authentication/manual/platform-signin-username-password">Unity documentation on username/password requirements</a>
    /// </summary>
    /// <param name="username">Username string</param>
    /// <param name="password">Password string</param>
    public async Task AddUsernamePasswordAsync(string username, string password)
    {
        try
        {
            await AuthenticationService.Instance.AddUsernamePasswordAsync(username, password);
            Debug.Log("Username and password added.");
        }
        catch (AuthenticationException ex)
        {
            // Compare error code to AuthenticationErrorCodes
            // Notify the player with the proper error message
            Debug.LogException(ex);
        }
        catch (RequestFailedException ex)
        {
            // Compare error code to CommonErrorCodes
            // Notify the player with the proper error message
            Debug.LogException(ex);
        }
    }

    public async void SaveGame(SaveData data)
    {
        Dictionary<string, object> dataToSave = new()
        {
            { "score", data.Score },
            { "tiles", data.LetterTileData },
            { "timestamp", data.Timestamp.Ticks }
        };

        await CloudSaveService.Instance.Data.Player.SaveAsync(dataToSave);
    }

    public async Task<SaveData> LoadGame()
    {
        Dictionary<string, Unity.Services.CloudSave.Models.Item> playerData =
            await CloudSaveService.Instance.Data.Player.LoadAsync(
                new HashSet<string> { "score", "tiles", "timestamp" }
            );

        SaveData data = new();
        if (playerData.TryGetValue("score", out var score) && playerData.TryGetValue("tiles", out var tiles) && playerData.TryGetValue("timestamp", out var timestamp))
        {
            data.Score = score.Value.GetAs<int>();
            data.LetterTileData = tiles.Value.GetAs<string>();
            data.Timestamp = new System.DateTime(timestamp.Value.GetAs<long>());
        }
        else
        {
            Debug.LogWarning("Tried to load save data but one or more keys were not present!");
            Debug.LogWarning(playerData.Keys);
            throw new System.Exception("Save data invalid");
        }

        return data;
    }
}
