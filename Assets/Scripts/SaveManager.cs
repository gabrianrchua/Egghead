using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Coordinates local save files, Unity Authentication, and Unity Cloud Save for game progress.
/// </summary>
public class SaveManager : Singleton<SaveManager>
{
    /// <summary>
    /// Serializable snapshot of the player's current progress and board state.
    /// </summary>
    [System.Serializable]
    public struct SaveData
    {
        /// <summary>
        /// Total score at the time this save was created.
        /// </summary>
        public int Score;

        /// <summary>
        /// UTC timestamp used to resolve conflicts between local and cloud saves.
        /// </summary>
        public System.DateTime Timestamp;

        /// <summary>
        /// Serialized tile grid. <c>null</c> means a new game should be initialized.
        /// </summary>
        public LetterTile.LetterTileData[][] LetterTileData;

        /// <summary>
        /// Create a short human-readable description of this save for debug logs.
        /// </summary>
        /// <returns>Formatted timestamp and score.</returns>
        public readonly string ToPrettyString()
        {
            return $"[{Timestamp.ToShortDateString()} {Timestamp.ToShortTimeString()}] {Score}";
        }
    }

    private const string LocalOnlyModeKey = "SaveManager.LocalOnlyMode";
    private const string CloudSaveDataKey = "saveDataJson";

    private SaveData _currentSaveData;
    private System.DateTime _currentSaveDataExpires = default;
    private Task _initializationTask;
    private bool cloudAvailable;
    private bool localOnlyMode;
    private bool eventsSetup;
    private List<IAuthStateListener> authStateListeners = new();

    /// <summary>
    /// Returns whether Cloud Save should be used for this session.
    /// </summary>
    public bool IsCloudActive => cloudAvailable && IsSignedIn && !localOnlyMode;

    /// <summary>
    /// Returns whether Unity Authentication currently has a signed-in player.
    /// </summary>
    public bool IsSignedIn => AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn;

    /// <summary>
    /// Current Unity Authentication player data, or <c>null</c> when signed out.
    /// </summary>
    public PlayerInfo PlayerInfo => IsSignedIn ? AuthenticationService.Instance.PlayerInfo : null;

    /// <summary>
    /// Returns whether the user explicitly chose local-only mode.
    /// </summary>
    public bool IsLocalOnly => localOnlyMode;

    /// <summary>
    /// Read persisted save mode and start asynchronous Unity Services initialization.
    /// </summary>
    protected override void Awake()
    {
        base.Awake();

        // Extra duplicate check - disabling script doesn't immediately halt execution
        if (!enabled)
        {
            return;
        }

        // If we got here, this is the only SaveManager; we need to persist this object across scene changes
        DontDestroyOnLoad(gameObject);

        localOnlyMode = PlayerPrefs.GetInt(LocalOnlyModeKey, 0) == 1;

        // Intentional non-await: kick off and save initialization task, but don't block execution
        _initializationTask = Initialize();
    }

    /// <summary>
    /// Initialize Unity Services and sign into anonymous cloud save unless local-only mode is enabled.
    /// Falls back to local saves if Unity Services or authentication fails.
    /// </summary>
    private async Task Initialize()
    {
        cloudAvailable = false;

        try
        {
            Debug.Log("Initializing UnityServices");
            await EnsureUnityServicesInitializedAsync();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to initialize UnityServices; using local save only for this session: " + ex.Message);
            return;
        }

        SetupEvents();

        if (localOnlyMode)
        {
            Debug.Log("Local-only save mode is active; skipping cloud authentication.");
            return;
        }

        try
        {
            await SignInAnonymouslyForCloudAsync();
        }
        catch (System.Exception ex)
        {
            cloudAvailable = false;
            Debug.LogError("Failed to sign in anonymously; using local save only for this session: " + ex.Message);
        }
    }

    /// <summary>
    /// Initialize Unity Services if they have not already been initialized.
    /// </summary>
    private async Task EnsureUnityServicesInitializedAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Initialized)
        {
            return;
        }

        await UnityServices.InitializeAsync();
    }

    /// <summary>
    /// Register an auth state listener to consume <c>AuthenticationService</c> lifecycle events
    /// </summary>
    /// <param name="listener">The listener to register, which implements interface <c>IAuthStateListener</c></param>
    public void RegisterAuthListener(IAuthStateListener listener)
    {
        authStateListeners.Add(listener);
        AuthenticationService.Instance.SignedIn += listener.OnSignedIn;
        AuthenticationService.Instance.SignInFailed += listener.OnSignInFailed;
        AuthenticationService.Instance.SignedOut += listener.OnSignedOut;
        AuthenticationService.Instance.Expired += listener.OnExpired;
    }

    /// <summary>
    /// Register Unity Authentication event handlers once for logging and cloud state updates.
    /// </summary>
    private void SetupEvents()
    {
        if (eventsSetup)
        {
            return;
        }

        AuthenticationService.Instance.SignedIn += () =>
        {
            // Authentication raises SignedIn before the awaiting sign-in method resumes.
            // Set this first so UI listeners observe an active cloud session.
            cloudAvailable = true;
            Debug.Log($"PlayerID: {AuthenticationService.Instance.PlayerId}");
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
            cloudAvailable = false;
            InvalidateSaveCache();
            Debug.Log("Player session could not be refreshed and expired.");
        };

        eventsSetup = true;
    }

    /// <summary>
    /// Get the full path to the local save file.
    /// </summary>
    /// <returns>The persistent data path plus <c>save.json</c>.</returns>
    private string GetSaveFilePath()
    {
        return System.IO.Path.Combine(Application.persistentDataPath, "save.json");
    }

    /// <summary>
    /// Sign into Unity Authentication anonymously and mark cloud save as available.
    /// </summary>
    private async Task SignInAnonymouslyForCloudAsync()
    {
        await AuthenticationService.Instance.SignInAnonymouslyAsync();
        cloudAvailable = true;
        InvalidateSaveCache();
        Debug.Log("Sign in anonymously succeeded!");
    }

    /// <summary>
    /// Attach username/password credentials to the current anonymous player and upload the local save.
    /// If the player is local-only or signed out, an anonymous player is created first.
    /// </summary>
    /// <param name="username">Unity Authentication username to register.</param>
    /// <param name="password">Unity Authentication password to register.</param>
    public async Task RegisterWithUsernamePasswordAsync(string username, string password)
    {
        await EnsureReadyForExplicitCloudAuthAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await SignInAnonymouslyForCloudAsync();
        }

        await AuthenticationService.Instance.AddUsernamePasswordAsync(username, password);
        Debug.Log("Username and password added.");

        SetLocalOnlyMode(false);
        cloudAvailable = true;
        InvalidateSaveCache();

        SaveData localSave = LoadLocalOrNew();
        await SaveCloudSaveDataAsync(localSave);
    }

    /// <summary>
    /// Sign into an existing username/password account, merge local and cloud saves by newest timestamp,
    /// then sync the winning save to both locations.
    /// </summary>
    /// <param name="username">Unity Authentication username.</param>
    /// <param name="password">Unity Authentication password.</param>
    public async Task LoginWithUsernamePasswordAsync(string username, string password)
    {
        await EnsureReadyForExplicitCloudAuthAsync();

        if (AuthenticationService.Instance.IsSignedIn)
        {
            AuthenticationService.Instance.SignOut(true);
        }

        await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);
        Debug.Log("SignIn is successful.");

        SetLocalOnlyMode(false);
        cloudAvailable = true;
        InvalidateSaveCache();

        (bool cloudLoaded, SaveData cloudSave) = await TryLoadCloudSaveDataAsync();
        bool localLoaded = TryLoadLocalSaveData(out SaveData localSave);

        SaveData winner = ChooseNewestSave(cloudLoaded, cloudSave, localLoaded, localSave);
        WriteLocalSaveData(winner);
        await SaveCloudSaveDataAsync(winner);
    }

    /// <summary>
    /// Sign out of Unity Authentication, clear the cached session token, and persist local-only mode.
    /// The local save file is left untouched.
    /// </summary>
    public void SignOutToLocalOnly()
    {
        try
        {
            if (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
            {
                AuthenticationService.Instance.SignOut(true);
            }
            else if (AuthenticationService.Instance != null)
            {
                AuthenticationService.Instance.ClearSessionToken();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to clear authentication session: " + ex.Message);
        }

        SetLocalOnlyMode(true);
        cloudAvailable = false;
        InvalidateSaveCache();
    }

    /// <summary>
    /// Leave local-only mode, sign in anonymously if needed, and merge local/cloud saves by newest timestamp.
    /// </summary>
    public async Task ContinueWithAnonymousCloudAsync()
    {
        await EnsureReadyForExplicitCloudAuthAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await SignInAnonymouslyForCloudAsync();
        }

        SetLocalOnlyMode(false);
        cloudAvailable = true;
        InvalidateSaveCache();

        (bool cloudLoaded, SaveData cloudSave) = await TryLoadCloudSaveDataAsync();
        bool localLoaded = TryLoadLocalSaveData(out SaveData localSave);
        SaveData winner = ChooseNewestSave(cloudLoaded, cloudSave, localLoaded, localSave);
        WriteLocalSaveData(winner);
        await SaveCloudSaveDataAsync(winner);
    }

    /// <summary>
    /// Wait for startup initialization and ensure Unity Services/auth events are ready for explicit auth actions.
    /// </summary>
    private async Task EnsureReadyForExplicitCloudAuthAsync()
    {
        if (_initializationTask != null)
        {
            await _initializationTask;
        }

        await EnsureUnityServicesInitializedAsync();
        SetupEvents();
    }

    /// <summary>
    /// Save progress locally, then save the same JSON payload to Cloud Save when cloud is active.
    /// </summary>
    /// <param name="data">Progress snapshot to save.</param>
    public async Task SaveGame(SaveData data)
    {
        bool savedLocal = false;
        try
        {
            WriteLocalSaveData(data);
            savedLocal = true;
            Debug.Log("Saved game data to local file: " + data.ToPrettyString());
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to save game data to local file: " + ex.Message);
        }

        if (savedLocal)
        {
            InvalidateSaveCache();
        }

        if (!IsCloudActive)
        {
            return;
        }

        try
        {
            await SaveCloudSaveDataAsync(data);
            InvalidateSaveCache();
            Debug.Log("Saved game data to CloudSave");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to save game data to CloudSave: " + ex.Message);
        }
    }

    /// <summary>
    /// Get the current save data, reusing a short-lived cache to avoid repeated load calls.
    /// </summary>
    /// <returns>The latest loaded <c>SaveData</c>.</returns>
    public async Task<SaveData> GetCurrentSaveData()
    {
        if (_currentSaveDataExpires == default || System.DateTime.UtcNow > _currentSaveDataExpires)
        {
            _currentSaveData = await LoadGame();
            _currentSaveDataExpires = System.DateTime.UtcNow.AddSeconds(5d);
        }

        return _currentSaveData;
    }

    /// <summary>
    /// Load from Cloud Save when active, mirror successful cloud loads locally, and fall back to local/new data.
    /// </summary>
    /// <returns>The loaded save data, or a new save if no valid data exists.</returns>
    private async Task<SaveData> LoadGame()
    {
        if (_initializationTask != null)
        {
            await _initializationTask;
        }

        if (IsCloudActive)
        {
            try
            {
                (bool cloudLoaded, SaveData cloudSave) = await TryLoadCloudSaveDataAsync();
                if (cloudLoaded)
                {
                    WriteLocalSaveData(cloudSave);
                    Debug.Log("Loaded game data from CloudSave: " + cloudSave.ToPrettyString());
                    return cloudSave;
                }

                Debug.LogWarning("CloudSave did not contain save data; falling back to local file.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError("Failed to load game data from CloudSave: " + ex.Message + "; falling back to local file");
            }
        }

        return LoadLocalOrNew();
    }

    /// <summary>
    /// Try to load and deserialize the single Cloud Save JSON key.
    /// </summary>
    /// <returns>
    /// <c>loaded</c> is true when valid cloud data was found; <c>data</c> contains the loaded save.
    /// </returns>
    private async Task<(bool loaded, SaveData data)> TryLoadCloudSaveDataAsync()
    {
        SaveData data = default;

        Dictionary<string, Unity.Services.CloudSave.Models.Item> playerData =
            await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { CloudSaveDataKey });

        if (!playerData.TryGetValue(CloudSaveDataKey, out var saveJsonItem))
        {
            return (false, data);
        }

        string json = saveJsonItem.Value.GetAs<string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return (false, data);
        }

        data = JsonConvert.DeserializeObject<SaveData>(json);
        bool loaded = data.Timestamp != default || data.Score != 0 || data.LetterTileData != null;
        return (loaded, data);
    }

    /// <summary>
    /// Serialize and write save data to the Cloud Save <c>saveDataJson</c> key.
    /// </summary>
    /// <param name="data">Progress snapshot to upload.</param>
    private async Task SaveCloudSaveDataAsync(SaveData data)
    {
        Dictionary<string, object> dataToSave = new()
        {
            { CloudSaveDataKey, JsonConvert.SerializeObject(data) }
        };

        await CloudSaveService.Instance.Data.Player.SaveAsync(dataToSave);
    }

    /// <summary>
    /// Load local save data, or create a new save if the local file is missing or invalid.
    /// </summary>
    /// <returns>Valid local save data or a new save.</returns>
    private SaveData LoadLocalOrNew()
    {
        if (TryLoadLocalSaveData(out SaveData data))
        {
            Debug.Log("Loaded game data from local file: " + data.ToPrettyString());
            return data;
        }

        Debug.LogWarning("No valid local save data found; returning new game data.");
        return CreateNewSaveData();
    }

    /// <summary>
    /// Try to read and deserialize the local save file.
    /// </summary>
    /// <param name="data">Loaded save data when successful; otherwise the default value.</param>
    /// <returns><c>true</c> when the local save exists and contains meaningful save data.</returns>
    private bool TryLoadLocalSaveData(out SaveData data)
    {
        data = default;

        try
        {
            string json = System.IO.File.ReadAllText(GetSaveFilePath());
            data = JsonConvert.DeserializeObject<SaveData>(json);
            return data.Timestamp != default || data.Score != 0 || data.LetterTileData != null;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to load game from local file: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Serialize and write save data to the local save file.
    /// </summary>
    /// <param name="data">Progress snapshot to write.</param>
    private void WriteLocalSaveData(SaveData data)
    {
        string json = JsonConvert.SerializeObject(data);
        System.IO.File.WriteAllText(GetSaveFilePath(), json);
    }

    /// <summary>
    /// Select the newest save by timestamp when both local and cloud saves are available.
    /// </summary>
    /// <param name="hasCloudSave">Whether <paramref name="cloudSave"/> contains valid data.</param>
    /// <param name="cloudSave">Cloud Save progress snapshot.</param>
    /// <param name="hasLocalSave">Whether <paramref name="localSave"/> contains valid data.</param>
    /// <param name="localSave">Local progress snapshot.</param>
    /// <returns>The newest valid save, or a new save when neither source is valid.</returns>
    private SaveData ChooseNewestSave(bool hasCloudSave, SaveData cloudSave, bool hasLocalSave, SaveData localSave)
    {
        if (hasCloudSave && hasLocalSave)
        {
            return cloudSave.Timestamp >= localSave.Timestamp ? cloudSave : localSave;
        }

        if (hasCloudSave)
        {
            return cloudSave;
        }

        if (hasLocalSave)
        {
            return localSave;
        }

        return CreateNewSaveData();
    }

    /// <summary>
    /// Create an empty save that tells the game to initialize a new board.
    /// </summary>
    /// <returns>New save data with zero score, current timestamp, and no tile data.</returns>
    private SaveData CreateNewSaveData()
    {
        return new SaveData
        {
            Score = 0,
            Timestamp = System.DateTime.UtcNow,
            LetterTileData = null
        };
    }

    /// <summary>
    /// Update local-only mode in memory and persist the choice to <c>PlayerPrefs</c>.
    /// </summary>
    /// <param name="value">Whether local-only mode should be enabled.</param>
    private void SetLocalOnlyMode(bool value)
    {
        localOnlyMode = value;
        PlayerPrefs.SetInt(LocalOnlyModeKey, value ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Force the next save-data request to reload from storage instead of using the cached value.
    /// </summary>
    private void InvalidateSaveCache()
    {
        _currentSaveDataExpires = default;
    }

    /// <summary>
    /// Delete the local save file and delete the cloud save key when cloud save is active.
    /// </summary>
    public async Task DeleteData()
    {
        bool deletedLocal = false;
        try
        {
            string path = GetSaveFilePath();
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }

            deletedLocal = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to delete game data from local file: " + ex.Message);
        }

        if (deletedLocal)
        {
            InvalidateSaveCache();
        }

        if (!IsCloudActive)
        {
            return;
        }

        try
        {
#pragma warning disable CS0618 // Type or member is obsolete
            await CloudSaveService.Instance.Data.Player.DeleteAsync(CloudSaveDataKey);
#pragma warning restore CS0618 // Type or member is obsolete
            InvalidateSaveCache();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to delete game data from CloudSave: " + ex.Message);
        }
    }
}
