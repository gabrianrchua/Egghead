using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using System.Threading.Tasks;
using System.Collections.Generic;
using Egghead.SaveSystem;
using Egghead.Authentication;
using SaveData = Egghead.SaveSystem.SaveData;

/// <summary>
/// Coordinates local save files, Unity Authentication, and Unity Cloud Save for game progress.
/// </summary>
public class SaveManager : Singleton<SaveManager>
{
    private const string LocalOnlyModeKey = "SaveManager.LocalOnlyMode";
    private const string CloudSaveDataKey = "saveDataJson";

    private SaveData _currentSaveData;
    private System.DateTime _currentSaveDataExpires = default;
    private Task _initializationTask;
    private SaveOperationCoordinator _saveCoordinator;
    private bool cloudAvailable;
    private bool localOnlyMode;
    private bool eventsSetup;
    private readonly AuthStateListenerRegistry authStateListeners = new();
    private IAuthEventSource authEventSource;
    private IAuthenticationSession authenticationSession;

    /// <summary>
    /// Returns whether Cloud Save should be used for this session.
    /// </summary>
    public bool IsCloudActive => cloudAvailable && IsSignedIn && !localOnlyMode;

    /// <summary>
    /// Returns whether Unity Authentication currently has a signed-in player.
    /// </summary>
    public bool IsSignedIn => GetAuthenticationSession()?.IsSignedIn == true;

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
        _saveCoordinator = new SaveOperationCoordinator(new UnitySaveMutationBackend(this), new UnitySaveLogger());

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
        authStateListeners.Register(listener);
    }

    /// <summary>
    /// Stop delivering authentication lifecycle events to a previously registered listener.
    /// Repeated calls and calls made before Unity Services initialization are safe.
    /// </summary>
    /// <param name="listener">The listener to unregister.</param>
    public void UnregisterAuthListener(IAuthStateListener listener)
    {
        authStateListeners.Unregister(listener);
    }

    /// <summary>
    /// Register Unity Authentication event handlers once for logging and cloud state updates.
    /// </summary>
    private void SetupEvents()
    {
        if (!eventsSetup)
        {
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
                cloudAvailable = false;
                InvalidateSaveCache();
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

        // Attach UI listeners after the manager's own handlers so they observe the updated cloud state.
        if (authEventSource == null)
        {
            authEventSource = new UnityAuthenticationEventSource(AuthenticationService.Instance);
            authStateListeners.SetEventSource(authEventSource);
        }

        authenticationSession ??= new UnityAuthenticationSession(AuthenticationService.Instance);
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

        SaveData localSave = (await ReconcileSaveDataAsync(false)).Data;
        SaveWriteRequest request = _saveCoordinator.CaptureSave(localSave);
        await _saveCoordinator.EnqueueSave(request, SaveMutationTargets.Cloud);

        authStateListeners.NotifySignedIn();
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

        await ReconcileSaveDataAsync(true);

        authStateListeners.NotifySignedIn();
    }

    /// <summary>
    /// Sign out of Unity Authentication, clear the cached session token, and persist local-only mode.
    /// The local save file is left untouched.
    /// </summary>
    public void SignOutToLocalOnly()
    {
        SetLocalOnlyMode(true);
        cloudAvailable = false;
        InvalidateSaveCache();

        bool signedOutEventReceived = false;
        System.Action observeSignedOut = () => signedOutEventReceived = true;
        if (authEventSource != null)
        {
            authEventSource.SignedOut += observeSignedOut;
        }

        try
        {
            IAuthenticationSession session = GetAuthenticationSession();
            if (session?.IsSignedIn == true)
            {
                session.SignOut(true);
            }
            else if (session != null)
            {
                session.ClearSessionToken();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to clear authentication session: " + ex.Message);
        }
        finally
        {
            if (authEventSource != null)
            {
                authEventSource.SignedOut -= observeSignedOut;
            }
        }

        if (!signedOutEventReceived)
        {
            authStateListeners.NotifySignedOut();
        }
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

        await ReconcileSaveDataAsync(true);
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
    public Task SaveGame(SaveData data)
    {
        data.SchemaVersion = SaveDataValidator.CurrentSchemaVersion;
        SaveValidationResult validation = SaveDataValidator.ValidateAndNormalize(data);
        if (!validation.IsValid)
        {
            string message = "Refusing to save invalid game data: " + validation.Reason;
            Debug.LogError(message);
            return Task.FromException(new System.ArgumentException(message, nameof(data)));
        }

        data = validation.Data;
        SaveWriteRequest request = _saveCoordinator.CaptureSave(data);
        SaveMutationTargets targets = SaveMutationTargets.Local;
        if (IsCloudActive)
        {
            targets |= SaveMutationTargets.Cloud;
        }

        Task operation = _saveCoordinator.EnqueueSave(request, targets);
        InvalidateSaveCache();
        return operation;
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
    /// Returns whether a resumable game board is available from the active save source.
    /// A save with no tile data represents a new game, so it should not be presented as a game
    /// the player can continue.
    /// </summary>
    public async Task<bool> HasSavedGame()
    {
        SaveData saveData = await GetCurrentSaveData();
        return saveData.LetterTileData != null;
    }

    /// <summary>Load and reconcile independently inspected local and cloud save candidates.</summary>
    private async Task<SaveData> LoadGame()
    {
        if (_initializationTask != null)
        {
            await _initializationTask;
        }

        return (await ReconcileSaveDataAsync(IsCloudActive)).Data;
    }

    private async Task<SaveReconciliationResult> ReconcileSaveDataAsync(bool includeCloud)
    {
        while (true)
        {
            await _saveCoordinator.WaitForIdleAsync();
            SaveOperationEpoch capturedEpoch = _saveCoordinator.CaptureEpoch();
            LocalSaveStorage rawLocal = new(GetSaveFilePath());
            CloudSaveStorage rawCloud = includeCloud ? new CloudSaveStorage() : null;
            ILocalSaveStorage localStorage = new CoordinatedLocalSaveStorage(rawLocal, _saveCoordinator, capturedEpoch);
            ISaveStorage cloudStorage = rawCloud == null
                ? null
                : new CoordinatedCloudSaveStorage(rawCloud, _saveCoordinator, capturedEpoch);
            SaveReconciler reconciler = new(localStorage, cloudStorage, new UnitySaveLogger());
            SaveReconciliationResult result = await reconciler.ReconcileAsync();

            if (_saveCoordinator.IsCurrent(capturedEpoch))
            {
                return result;
            }

            Debug.LogWarning("Save deletion overlapped reconciliation; retrying against the current generation.");
        }
    }

    private sealed class LocalSaveStorage : ILocalSaveStorage
    {
        private readonly string path;

        public LocalSaveStorage(string path)
        {
            this.path = path;
        }

        public string Name => "local";

        public Task<string> ReadAsync()
        {
            return Task.FromResult(System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null);
        }

        public Task WriteAsync(string json)
        {
            WriteNow(json);
            return Task.CompletedTask;
        }

        public void WriteNow(string json) => System.IO.File.WriteAllText(path, json);

        public void DeleteNow()
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }

        public Task BackupInvalidAsync()
        {
            if (!System.IO.File.Exists(path))
            {
                return Task.CompletedTask;
            }

            string directory = System.IO.Path.GetDirectoryName(path);
            string fileName = $"save.invalid.{System.DateTime.UtcNow:yyyyMMddTHHmmssfffZ}.json";
            System.IO.File.Move(path, System.IO.Path.Combine(directory, fileName));
            return Task.CompletedTask;
        }
    }

    private sealed class CloudSaveStorage : ISaveStorage
    {
        public string Name => "cloud";

        public async Task<string> ReadAsync()
        {
            Dictionary<string, Unity.Services.CloudSave.Models.Item> playerData =
                await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { CloudSaveDataKey });
            return playerData.TryGetValue(CloudSaveDataKey, out var item) ? item.Value.GetAs<string>() : null;
        }

        public async Task WriteAsync(string json)
        {
            Dictionary<string, object> dataToSave = new() { { CloudSaveDataKey, json } };
            await CloudSaveService.Instance.Data.Player.SaveAsync(dataToSave);
        }
    }

    private sealed class CoordinatedLocalSaveStorage : ILocalSaveStorage
    {
        private readonly LocalSaveStorage storage;
        private readonly SaveOperationCoordinator coordinator;
        private readonly SaveOperationEpoch epoch;

        public CoordinatedLocalSaveStorage(LocalSaveStorage storage, SaveOperationCoordinator coordinator, SaveOperationEpoch epoch)
        {
            this.storage = storage;
            this.coordinator = coordinator;
            this.epoch = epoch;
        }

        public string Name => storage.Name;
        public Task<string> ReadAsync() => storage.ReadAsync();
        public Task BackupInvalidAsync()
        {
            return coordinator.IsCurrent(epoch) ? storage.BackupInvalidAsync() : Task.CompletedTask;
        }

        public Task WriteAsync(string json)
        {
            SaveData data = SaveJson.Deserialize(json);
            SaveWriteRequest request = coordinator.CaptureSave(data, epoch);
            return coordinator.EnqueueSave(request, SaveMutationTargets.Local);
        }
    }

    private sealed class CoordinatedCloudSaveStorage : ISaveStorage
    {
        private readonly CloudSaveStorage storage;
        private readonly SaveOperationCoordinator coordinator;
        private readonly SaveOperationEpoch epoch;

        public CoordinatedCloudSaveStorage(CloudSaveStorage storage, SaveOperationCoordinator coordinator, SaveOperationEpoch epoch)
        {
            this.storage = storage;
            this.coordinator = coordinator;
            this.epoch = epoch;
        }

        public string Name => storage.Name;
        public Task<string> ReadAsync() => storage.ReadAsync();

        public Task WriteAsync(string json)
        {
            SaveData data = SaveJson.Deserialize(json);
            SaveWriteRequest request = coordinator.CaptureSave(data, epoch);
            return coordinator.EnqueueSave(request, SaveMutationTargets.Cloud);
        }
    }

    private sealed class UnitySaveMutationBackend : ISaveMutationBackend
    {
        private readonly SaveManager manager;

        public UnitySaveMutationBackend(SaveManager manager)
        {
            this.manager = manager;
        }

        public void WriteLocal(string json)
        {
            new LocalSaveStorage(manager.GetSaveFilePath()).WriteNow(json);
            Debug.Log("Saved game data to local file: " + SaveJson.Deserialize(json).ToPrettyString());
        }

        public void DeleteLocal()
        {
            new LocalSaveStorage(manager.GetSaveFilePath()).DeleteNow();
        }

        public async Task WriteCloudAsync(string json)
        {
            await new CloudSaveStorage().WriteAsync(json);
            manager.InvalidateSaveCache();
            Debug.Log("Saved game data to CloudSave");
        }

        public async Task DeleteCloudAsync()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            await CloudSaveService.Instance.Data.Player.DeleteAsync(CloudSaveDataKey);
#pragma warning restore CS0618 // Type or member is obsolete
            manager.InvalidateSaveCache();
        }
    }

    private sealed class UnitySaveLogger : ISaveReconciliationLogger
    {
        public void Info(string message) => Debug.Log(message);
        public void Warning(string message) => Debug.LogWarning(message);
        public void Error(string message) => Debug.LogError(message);
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

    private IAuthenticationSession GetAuthenticationSession()
    {
        if (authenticationSession == null && AuthenticationService.Instance != null)
        {
            authenticationSession = new UnityAuthenticationSession(AuthenticationService.Instance);
        }

        return authenticationSession;
    }

    /// <summary>
    /// Delete the local save file and delete the cloud save key when cloud save is active.
    /// </summary>
    public Task DeleteData()
    {
        Task operation = _saveCoordinator.EnqueueDelete(IsCloudActive);
        InvalidateSaveCache();
        return operation;
    }
}
