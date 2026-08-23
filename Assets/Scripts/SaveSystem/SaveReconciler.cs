using System;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Egghead.SaveSystem
{
    public static class SaveJson
    {
        public static string Serialize(SaveData data) => JsonConvert.SerializeObject(data);

        public static SaveData Deserialize(string json) => JsonConvert.DeserializeObject<SaveData>(json);
    }

    public interface ISaveStorage
    {
        string Name { get; }
        Task<string> ReadAsync();
        Task WriteAsync(string json);
    }

    public interface ILocalSaveStorage : ISaveStorage
    {
        Task BackupInvalidAsync();
    }

    public interface ISaveReconciliationLogger
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }

    public enum SaveCandidateStatus
    {
        NotAttempted,
        Missing,
        Valid,
        Invalid,
        Failed
    }

    public enum SaveSource
    {
        NewGame,
        Local,
        Cloud
    }

    public readonly struct SaveReconciliationResult
    {
        public SaveReconciliationResult(SaveData data, SaveSource source, SaveCandidateStatus localStatus, SaveCandidateStatus cloudStatus)
        {
            Data = data;
            Source = source;
            LocalStatus = localStatus;
            CloudStatus = cloudStatus;
        }

        public SaveData Data { get; }
        public SaveSource Source { get; }
        public SaveCandidateStatus LocalStatus { get; }
        public SaveCandidateStatus CloudStatus { get; }
    }

    public sealed class SaveReconciler
    {
        private readonly ILocalSaveStorage localStorage;
        private readonly ISaveStorage cloudStorage;
        private readonly ISaveReconciliationLogger logger;
        private readonly Func<DateTime> utcNow;

        public SaveReconciler(ILocalSaveStorage localStorage, ISaveStorage cloudStorage, ISaveReconciliationLogger logger, Func<DateTime> utcNow = null)
        {
            this.localStorage = localStorage ?? throw new ArgumentNullException(nameof(localStorage));
            this.cloudStorage = cloudStorage;
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public async Task<SaveReconciliationResult> ReconcileAsync()
        {
            Task<Candidate> localTask = ReadCandidateAsync(localStorage);
            Task<Candidate> cloudTask = cloudStorage == null
                ? Task.FromResult(Candidate.NotAttempted("cloud"))
                : ReadCandidateAsync(cloudStorage);

            Candidate local = await localTask;
            Candidate cloud = await cloudTask;
            LogAvailability(local, cloud);

            SaveSource source;
            Candidate winner;
            if (local.IsValid && cloud.IsValid)
            {
                bool cloudWins = cloud.Data.Timestamp >= local.Data.Timestamp;
                source = cloudWins ? SaveSource.Cloud : SaveSource.Local;
                winner = cloudWins ? cloud : local;
                string reason = cloud.Data.Timestamp == local.Data.Timestamp
                    ? "timestamps are equal; cloud wins deterministic tie"
                    : $"{source.ToString().ToLowerInvariant()} timestamp is newer";
                logger.Info($"Selected {source.ToString().ToLowerInvariant()} save because {reason}: {winner.Data.ToPrettyString()}");
            }
            else if (cloud.IsValid)
            {
                source = SaveSource.Cloud;
                winner = cloud;
                logger.Info($"Selected cloud save because local is {local.Status.ToString().ToLowerInvariant()}: {winner.Data.ToPrettyString()}");
            }
            else if (local.IsValid)
            {
                source = SaveSource.Local;
                winner = local;
                logger.Info($"Selected local save because cloud is {cloud.Status.ToString().ToLowerInvariant()}: {winner.Data.ToPrettyString()}");
            }
            else
            {
                await BackupInvalidLocalAsync(local);
                SaveData newGame = CreateNewGameData();
                logger.Warning("No valid local or cloud save was available; created new-game data in memory.");
                return new SaveReconciliationResult(newGame, SaveSource.NewGame, local.Status, cloud.Status);
            }

            await SynchronizeAsync(source, winner, local, cloud);
            return new SaveReconciliationResult(winner.Data, source, local.Status, cloud.Status);
        }

        private async Task<Candidate> ReadCandidateAsync(ISaveStorage storage)
        {
            try
            {
                string json = await storage.ReadAsync();
                if (string.IsNullOrWhiteSpace(json))
                {
                    return Candidate.Missing(storage.Name);
                }

                SaveData data;
                try
                {
                    data = SaveJson.Deserialize(json);
                }
                catch (Exception ex)
                {
                    return Candidate.Invalid(storage.Name, "malformed JSON: " + ex.Message);
                }

                SaveValidationResult validation = SaveDataValidator.ValidateAndNormalize(data);
                return validation.IsValid
                    ? Candidate.Valid(storage.Name, validation.Data, validation.WasMigrated, validation.Reason)
                    : Candidate.Invalid(storage.Name, validation.Reason);
            }
            catch (Exception ex)
            {
                return Candidate.Failed(storage.Name, ex.Message);
            }
        }

        private async Task SynchronizeAsync(SaveSource source, Candidate winner, Candidate local, Candidate cloud)
        {
            if (local.Status == SaveCandidateStatus.Invalid)
            {
                await BackupInvalidLocalAsync(local);
            }

            string json = SaveJson.Serialize(winner.Data);
            bool writeLocal = local.Status != SaveCandidateStatus.Failed &&
                (source == SaveSource.Cloud || source == SaveSource.Local && winner.WasMigrated);
            bool writeCloud = cloudStorage != null && cloud.Status != SaveCandidateStatus.Failed &&
                (source == SaveSource.Local || source == SaveSource.Cloud && winner.WasMigrated);

            if (writeLocal)
            {
                await TryWriteAsync(localStorage, json);
            }

            if (writeCloud)
            {
                await TryWriteAsync(cloudStorage, json);
            }
        }

        private async Task TryWriteAsync(ISaveStorage storage, string json)
        {
            try
            {
                await storage.WriteAsync(json);
                logger.Info($"Synchronized selected save to {storage.Name} storage.");
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to synchronize selected save to {storage.Name} storage: {ex.Message}");
            }
        }

        private async Task BackupInvalidLocalAsync(Candidate local)
        {
            if (local.Status != SaveCandidateStatus.Invalid)
            {
                return;
            }

            try
            {
                await localStorage.BackupInvalidAsync();
                logger.Warning("Preserved invalid local save as a diagnostic backup.");
            }
            catch (Exception ex)
            {
                logger.Error("Failed to preserve invalid local save: " + ex.Message);
            }
        }

        private void LogAvailability(Candidate local, Candidate cloud)
        {
            logger.Info($"Save candidates: local={Describe(local)}, cloud={Describe(cloud)}.");
        }

        private static string Describe(Candidate candidate)
        {
            string description = candidate.Status.ToString().ToLowerInvariant();
            return string.IsNullOrEmpty(candidate.Reason) ? description : $"{description} ({candidate.Reason})";
        }

        private SaveData CreateNewGameData()
        {
            return new SaveData
            {
                SchemaVersion = SaveDataValidator.CurrentSchemaVersion,
                Score = 0,
                Timestamp = utcNow().ToUniversalTime(),
                LetterTileData = null
            };
        }

        private readonly struct Candidate
        {
            private Candidate(string name, SaveCandidateStatus status, SaveData data, bool wasMigrated, string reason)
            {
                Name = name;
                Status = status;
                Data = data;
                WasMigrated = wasMigrated;
                Reason = reason;
            }

            public string Name { get; }
            public SaveCandidateStatus Status { get; }
            public SaveData Data { get; }
            public bool WasMigrated { get; }
            public string Reason { get; }
            public bool IsValid => Status == SaveCandidateStatus.Valid;

            public static Candidate NotAttempted(string name) => new(name, SaveCandidateStatus.NotAttempted, default, false, null);
            public static Candidate Missing(string name) => new(name, SaveCandidateStatus.Missing, default, false, null);
            public static Candidate Valid(string name, SaveData data, bool migrated, string reason) => new(name, SaveCandidateStatus.Valid, data, migrated, reason);
            public static Candidate Invalid(string name, string reason) => new(name, SaveCandidateStatus.Invalid, default, false, reason);
            public static Candidate Failed(string name, string reason) => new(name, SaveCandidateStatus.Failed, default, false, reason);
        }
    }
}
