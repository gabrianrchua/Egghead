using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using System.Threading.Tasks;
using System.Collections.Generic;

public class SaveManager : Singleton<SaveManager>
{
    [System.Serializable]
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
    /// Helper to get the persistent save file path
    /// </summary>
    /// <returns></returns>
    private string GetSaveFilePath()
    {
        return System.IO.Path.Combine(Application.persistentDataPath, "save.json");
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

        // save to local file
        string json = JsonUtility.ToJson(data);
        try
        {
            System.IO.File.WriteAllText(GetSaveFilePath(), json);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to save game data to local file: " + ex.Message);
        }

        // save to cloud
        try
        {
            await CloudSaveService.Instance.Data.Player.SaveAsync(dataToSave);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to save game data to CloudSave: " + ex.Message);
        }
    }

    public async Task<SaveData> LoadGame()
    {
        // TODO: cache save on load so we don't fetch multiple times
        try
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
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to load game data from CloudSave: " + ex.Message + "; falling back to local file");

            try
            {
                string json = System.IO.File.ReadAllText(GetSaveFilePath());
                SaveData data = JsonUtility.FromJson<SaveData>(json);
                return data;
            }
            catch (System.Exception ex2)
            {
                Debug.LogError("Failed to load game from local file: " + ex2.Message);
                return new SaveData();
            }
        }
    }
}
