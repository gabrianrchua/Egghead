using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;

public class SaveManager : Singleton<SaveManager>
{
    [System.Serializable]
    public struct SaveData
    {
        public int Score;
        public System.DateTime Timestamp;
        // LetterTileData is saved as a string in CloudSave and must be JSON decoded first
        public LetterTile.LetterTileData[][] LetterTileData;

        public readonly string ToPrettyString()
        {
            // TODO: remove full LetterTileData logging
            return $"[{Timestamp.ToShortDateString()} {Timestamp.ToShortTimeString()}] {Score} {LetterTileData}";
        }
    }

    private SaveData _currentSaveData;
    private System.DateTime _currentSaveDataExpires = default;
    private Task _initializationTask;

    protected override void Awake()
    {
        base.Awake();

        // kick off and save initialization task, but don't block execution
        _initializationTask = Initialize();
    }

    private async Task Initialize()
    {
        try
        {
            Debug.Log("Initializing UnityServices");
            await UnityServices.InitializeAsync();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }

        SetupEvents();

        await SignInCachedUserAsync();
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
    /// <returns>The persistent data path + save.json</returns>
    private string GetSaveFilePath()
    {
        return System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "save.json");
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
        Debug.Log("SignInCachedUserAsync");
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

    public async Task SaveGame(SaveData data)
    {
        // save to local file
        try
        {
            string json = JsonConvert.SerializeObject(data);
            System.IO.File.WriteAllText(GetSaveFilePath(), json);
            // TODO: remove full json logging
            Debug.Log("Saved game data to local file: " + json);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to save game data to local file: " + ex.Message);
        }

        // save to cloud
        try
        {
            Dictionary<string, object> dataToSave = new()
            {
                { "score", data.Score },
                // save as string in CloudSave
                { "tiles", JsonConvert.SerializeObject(data.LetterTileData) },
                { "timestamp", data.Timestamp }
            };
            await CloudSaveService.Instance.Data.Player.SaveAsync(dataToSave);
            Debug.Log("Saved game data to CloudSave");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to save game data to CloudSave: " + ex.Message);
        }
    }

    /// <summary>
    /// Get current game save data, which is cached for 5 seconds.
    /// </summary>
    /// <returns>Current <c>SaveData</c></returns>
    public async Task<SaveData> GetCurrentSaveData()
    {
        if (_currentSaveDataExpires == default || System.DateTime.UtcNow > _currentSaveDataExpires)
        {
            _currentSaveData = await LoadGame();
            // expire in 5 seconds
            _currentSaveDataExpires = System.DateTime.UtcNow.AddSeconds(5d);
        }

        return _currentSaveData;
    }

    private async Task<SaveData> LoadGame()
    {
        // wait for sign in before continuing to load game
        await _initializationTask;

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
                // TODO: make sure this deserialization actually works
                data.LetterTileData = JsonConvert.DeserializeObject<LetterTile.LetterTileData[][]>(tiles.Value.GetAs<string>());
                data.Timestamp = new System.DateTime(timestamp.Value.GetAs<long>());
            }
            else
            {
                Debug.LogWarning("Tried to load save data but one or more keys were not present!");
                Debug.LogWarning(playerData.Keys);
                throw new System.Exception("Save data invalid");
            }

            Debug.Log("Loaded game data from CloudSave: " + data.ToPrettyString());

            return data;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to load game data from CloudSave: " + ex.Message + "; falling back to local file");
        }

        try
        {
            string json = System.IO.File.ReadAllText(GetSaveFilePath());
            SaveData data = JsonConvert.DeserializeObject<SaveData>(json);

            Debug.Log("Loaded game data from local file: " + data.ToPrettyString());

            return data;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to load game from local file, returning new game data: " + ex.Message);
            SaveData data = new()
            {
                Score = 0,
                Timestamp = System.DateTime.UtcNow,
                LetterTileData = null
            };
            return data;
        }
    }

    public async Task DeleteData()
    {
        try
        {
#pragma warning disable CS0618 // Type or member is obsolete
            await CloudSaveService.Instance.Data.Player.DeleteAsync("score");
            await CloudSaveService.Instance.Data.Player.DeleteAsync("tiles");
            await CloudSaveService.Instance.Data.Player.DeleteAsync("timestamp");
#pragma warning restore CS0618 // Type or member is obsolete
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to delete game data from CloudSave: " + ex.Message);
        }

        try
        {
            string path = GetSaveFilePath();
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to delete game data from local file: " + ex.Message);
        }
    }
}
