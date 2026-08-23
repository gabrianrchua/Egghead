using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Egghead.SaveSystem;
using NUnit.Framework;

public class SaveReconcilerTests
{
    private static readonly DateTime BaseTime = new(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task NewerLocalWinsAndUploadsToCloud()
    {
        FakeStorage local = Local(Save(BaseTime.AddMinutes(2), 20));
        FakeStorage cloud = Cloud(Save(BaseTime, 10));

        SaveReconciliationResult result = await Reconcile(local, cloud);

        Assert.That(result.Source, Is.EqualTo(SaveSource.Local));
        Assert.That(result.Data.Score, Is.EqualTo(20));
        Assert.That(local.WriteCount, Is.Zero);
        Assert.That(cloud.WrittenData.Score, Is.EqualTo(20));
    }

    [Test]
    public async Task NewerCloudWinsAndWritesLocal()
    {
        FakeStorage local = Local(Save(BaseTime, 10));
        FakeStorage cloud = Cloud(Save(BaseTime.AddMinutes(2), 20));

        SaveReconciliationResult result = await Reconcile(local, cloud);

        Assert.That(result.Source, Is.EqualTo(SaveSource.Cloud));
        Assert.That(result.Data.Score, Is.EqualTo(20));
        Assert.That(local.WrittenData.Score, Is.EqualTo(20));
        Assert.That(cloud.WriteCount, Is.Zero);
    }

    [Test]
    public async Task EqualTimestampsChooseCloudDeterministically()
    {
        FakeStorage local = Local(Save(BaseTime, 10));
        FakeStorage cloud = Cloud(Save(BaseTime, 20));

        SaveReconciliationResult result = await Reconcile(local, cloud);

        Assert.That(result.Source, Is.EqualTo(SaveSource.Cloud));
        Assert.That(result.Data.Score, Is.EqualTo(20));
        Assert.That(local.WrittenData.Score, Is.EqualTo(20));
    }

    [Test]
    public async Task OnlyLocalLoadsAndUploadsWhenCloudIsMissing()
    {
        FakeStorage local = Local(Save(BaseTime, 10));
        FakeStorage cloud = Cloud(null);

        SaveReconciliationResult result = await Reconcile(local, cloud);

        Assert.That(result.Source, Is.EqualTo(SaveSource.Local));
        Assert.That(cloud.WrittenData.Score, Is.EqualTo(10));
    }

    [Test]
    public async Task OnlyCloudLoadsAndWritesLocal()
    {
        FakeStorage local = Local(null);
        FakeStorage cloud = Cloud(Save(BaseTime, 10));

        SaveReconciliationResult result = await Reconcile(local, cloud);

        Assert.That(result.Source, Is.EqualTo(SaveSource.Cloud));
        Assert.That(local.WrittenData.Score, Is.EqualTo(10));
    }

    [Test]
    public async Task NeitherSourceCreatesUnsavedNonResumableGame()
    {
        FakeStorage local = Local(null);
        FakeStorage cloud = Cloud(null);

        SaveReconciliationResult result = await Reconcile(local, cloud);

        Assert.That(result.Source, Is.EqualTo(SaveSource.NewGame));
        Assert.That(result.Data.SchemaVersion, Is.EqualTo(1));
        Assert.That(result.Data.LetterTileData, Is.Null);
        Assert.That(local.WriteCount, Is.Zero);
        Assert.That(cloud.WriteCount, Is.Zero);
    }

    [Test]
    public async Task CloudReadFailureLoadsLocalWithoutUploadingUnknownRemoteState()
    {
        FakeStorage local = Local(Save(BaseTime, 10));
        FakeStorage cloud = Cloud(null);
        cloud.ReadException = new InvalidOperationException("request failed");

        SaveReconciliationResult result = await Reconcile(local, cloud);

        Assert.That(result.Source, Is.EqualTo(SaveSource.Local));
        Assert.That(result.CloudStatus, Is.EqualTo(SaveCandidateStatus.Failed));
        Assert.That(cloud.WriteCount, Is.Zero);
    }

    [Test]
    public async Task LocalReadFailureLoadsCloudWithoutOverwritingUnknownLocalState()
    {
        FakeStorage local = Local(null);
        local.ReadException = new InvalidOperationException("file locked");
        FakeStorage cloud = Cloud(Save(BaseTime, 10));

        SaveReconciliationResult result = await Reconcile(local, cloud);

        Assert.That(result.Source, Is.EqualTo(SaveSource.Cloud));
        Assert.That(result.LocalStatus, Is.EqualTo(SaveCandidateStatus.Failed));
        Assert.That(local.WriteCount, Is.Zero);
    }

    [Test]
    public async Task MirrorFailureDoesNotDiscardWinner()
    {
        FakeStorage local = Local(Save(BaseTime.AddMinutes(1), 20));
        FakeStorage cloud = Cloud(Save(BaseTime, 10));
        cloud.WriteException = new InvalidOperationException("upload failed");

        SaveReconciliationResult result = await Reconcile(local, cloud);

        Assert.That(result.Source, Is.EqualTo(SaveSource.Local));
        Assert.That(result.Data.Score, Is.EqualTo(20));
        Assert.That(cloud.WriteCount, Is.EqualTo(1));
    }

    [Test]
    public async Task InvalidNewerLocalIsBackedUpAndValidCloudWins()
    {
        SaveData invalid = Save(BaseTime.AddHours(1), 100);
        invalid.LetterTileData[0] = null;
        FakeStorage local = Local(invalid);
        FakeStorage cloud = Cloud(Save(BaseTime, 10));

        SaveReconciliationResult result = await Reconcile(local, cloud);

        Assert.That(result.Source, Is.EqualTo(SaveSource.Cloud));
        Assert.That(local.BackupCount, Is.EqualTo(1));
        Assert.That(local.WrittenData.Score, Is.EqualTo(10));
    }

    [Test]
    public async Task InvalidOnlyLocalIsBackedUpWithoutPersistingGeneratedGame()
    {
        FakeStorage local = Local("{not json");

        SaveReconciliationResult result = await Reconcile(local, null);

        Assert.That(result.Source, Is.EqualTo(SaveSource.NewGame));
        Assert.That(local.BackupCount, Is.EqualTo(1));
        Assert.That(local.WriteCount, Is.Zero);
    }

    [Test]
    public async Task LegacyWinnerIsMigratedAndPersistedOnce()
    {
        SaveData legacy = Save(BaseTime, 10);
        legacy.SchemaVersion = 0;
        FakeStorage local = Local(legacy);

        SaveReconciliationResult result = await Reconcile(local, null);

        Assert.That(result.Data.SchemaVersion, Is.EqualTo(1));
        Assert.That(local.WrittenData.SchemaVersion, Is.EqualTo(1));
    }

    [Test]
    public async Task InvalidCloudCannotDefeatValidLocal()
    {
        SaveData invalidCloud = Save(BaseTime.AddHours(1), 100);
        invalidCloud.SchemaVersion = 99;
        FakeStorage local = Local(Save(BaseTime, 10));
        FakeStorage cloud = Cloud(invalidCloud);

        SaveReconciliationResult result = await Reconcile(local, cloud);

        Assert.That(result.Source, Is.EqualTo(SaveSource.Local));
        Assert.That(cloud.WrittenData.Score, Is.EqualTo(10));
    }

    private static Task<SaveReconciliationResult> Reconcile(FakeStorage local, FakeStorage cloud)
    {
        SaveReconciler reconciler = new(local, cloud, new FakeLogger(), () => BaseTime);
        return reconciler.ReconcileAsync();
    }

    private static FakeStorage Local(SaveData data) => new("local", SaveJson.Serialize(data));
    private static FakeStorage Local(string json) => new("local", json);
    private static FakeStorage Cloud(SaveData data) => new("cloud", SaveJson.Serialize(data));
    private static FakeStorage Cloud(string json) => new("cloud", json);

    private static SaveData Save(DateTime timestamp, int score)
    {
        return new SaveData
        {
            SchemaVersion = SaveDataValidator.CurrentSchemaVersion,
            Score = score,
            Timestamp = timestamp,
            LetterTileData = Board()
        };
    }

    internal static SavedLetterTileData[][] Board()
    {
        SavedLetterTileData[][] board = new SavedLetterTileData[7][];
        for (int column = 0; column < board.Length; column++)
        {
            board[column] = new SavedLetterTileData[column % 2 == 0 ? 7 : 8];
            for (int row = 0; row < board[column].Length; row++)
            {
                board[column][row] = new SavedLetterTileData
                {
                    letter = 'A',
                    column = column,
                    row = row,
                    tileType = 0
                };
            }
        }

        return board;
    }

    private sealed class FakeStorage : ILocalSaveStorage
    {
        private readonly string json;

        public FakeStorage(string name, string json)
        {
            Name = name;
            this.json = json;
        }

        public string Name { get; }
        public Exception ReadException { get; set; }
        public Exception WriteException { get; set; }
        public int WriteCount { get; private set; }
        public int BackupCount { get; private set; }
        public SaveData WrittenData { get; private set; }

        public Task<string> ReadAsync()
        {
            return ReadException == null ? Task.FromResult(json) : Task.FromException<string>(ReadException);
        }

        public Task WriteAsync(string value)
        {
            WriteCount++;
            if (WriteException != null)
            {
                return Task.FromException(WriteException);
            }

            WrittenData = SaveJson.Deserialize(value);
            return Task.CompletedTask;
        }

        public Task BackupInvalidAsync()
        {
            BackupCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLogger : ISaveReconciliationLogger
    {
        public readonly List<string> Messages = new();
        public void Info(string message) => Messages.Add(message);
        public void Warning(string message) => Messages.Add(message);
        public void Error(string message) => Messages.Add(message);
    }
}
