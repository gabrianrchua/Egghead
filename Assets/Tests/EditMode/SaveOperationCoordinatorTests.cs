using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Egghead.SaveSystem;
using NUnit.Framework;

public class SaveOperationCoordinatorTests
{
    private static readonly DateTime BaseTime = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task PendingCloudSavesCoalesceToNewestWhileLocalIsImmediatelyCurrent()
    {
        FakeBackend backend = new();
        TaskCompletionSource<bool> firstWrite = backend.BlockNextCloudWrite();
        SaveOperationCoordinator coordinator = Coordinator(backend);

        Task first = Save(coordinator, Data(1, BaseTime), SaveMutationTargets.Local | SaveMutationTargets.Cloud);
        Task second = Save(coordinator, Data(2, BaseTime.AddSeconds(1)), SaveMutationTargets.Local | SaveMutationTargets.Cloud);
        Task third = Save(coordinator, Data(3, BaseTime.AddSeconds(2)), SaveMutationTargets.Local | SaveMutationTargets.Cloud);

        Assert.That(Deserialize(backend.LocalJson).Score, Is.EqualTo(3));
        Assert.That(backend.CloudWriteCount, Is.EqualTo(1));

        firstWrite.SetResult(true);
        await Task.WhenAll(first, second, third);

        Assert.That(backend.CloudScores, Is.EqualTo(new[] { 1, 3 }));
        Assert.That(Deserialize(backend.CloudJson).Score, Is.EqualTo(3));
    }

    [Test]
    public async Task OlderTimestampCannotReplaceNewerLocalOrCloudState()
    {
        FakeBackend backend = new();
        TaskCompletionSource<bool> firstWrite = backend.BlockNextCloudWrite();
        SaveOperationCoordinator coordinator = Coordinator(backend);

        Task newer = Save(coordinator, Data(20, BaseTime.AddMinutes(1)), SaveMutationTargets.Local | SaveMutationTargets.Cloud);
        Task older = Save(coordinator, Data(10, BaseTime), SaveMutationTargets.Local | SaveMutationTargets.Cloud);

        firstWrite.SetResult(true);
        await Task.WhenAll(newer, older);

        Assert.That(Deserialize(backend.LocalJson).Score, Is.EqualTo(20));
        Assert.That(Deserialize(backend.CloudJson).Score, Is.EqualTo(20));
        Assert.That(backend.CloudWriteCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DeleteSupersedesPendingSaveAndRunsAfterInflightSave()
    {
        FakeBackend backend = new();
        TaskCompletionSource<bool> firstWrite = backend.BlockNextCloudWrite();
        SaveOperationCoordinator coordinator = Coordinator(backend);

        Task first = Save(coordinator, Data(1, BaseTime), SaveMutationTargets.Local | SaveMutationTargets.Cloud);
        Task pending = Save(coordinator, Data(2, BaseTime.AddSeconds(1)), SaveMutationTargets.Local | SaveMutationTargets.Cloud);
        Task deletion = coordinator.EnqueueDelete(true);

        Assert.That(backend.LocalJson, Is.Null);
        firstWrite.SetResult(true);
        await Task.WhenAll(first, pending, deletion);

        Assert.That(backend.Events, Is.EqualTo(new[] { "save:1", "delete" }));
        Assert.That(backend.CloudJson, Is.Null);
    }

    [Test]
    public async Task RequestCapturedBeforeDeleteIsIgnoredIfSubmittedAfterBarrier()
    {
        FakeBackend backend = new();
        SaveOperationCoordinator coordinator = Coordinator(backend);
        SaveWriteRequest staleRequest = coordinator.CaptureSave(Data(1, BaseTime));

        await coordinator.EnqueueDelete(false);
        await coordinator.EnqueueSave(staleRequest, SaveMutationTargets.Local | SaveMutationTargets.Cloud);

        Assert.That(backend.LocalJson, Is.Null);
        Assert.That(backend.CloudWriteCount, Is.Zero);
    }

    [Test]
    public async Task SaveDeleteNewSavePreservesBarrierOrder()
    {
        FakeBackend backend = new();
        TaskCompletionSource<bool> firstWrite = backend.BlockNextCloudWrite();
        SaveOperationCoordinator coordinator = Coordinator(backend);

        Task oldSave = Save(coordinator, Data(1, BaseTime), SaveMutationTargets.Local | SaveMutationTargets.Cloud);
        Task deletion = coordinator.EnqueueDelete(true);
        Task newSave = Save(coordinator, Data(2, BaseTime.AddSeconds(1)), SaveMutationTargets.Local | SaveMutationTargets.Cloud);

        Assert.That(Deserialize(backend.LocalJson).Score, Is.EqualTo(2));
        firstWrite.SetResult(true);
        await Task.WhenAll(oldSave, deletion, newSave);

        Assert.That(backend.Events, Is.EqualTo(new[] { "save:1", "delete", "save:2" }));
        Assert.That(Deserialize(backend.CloudJson).Score, Is.EqualTo(2));
    }

    [Test]
    public async Task CloudFailureFaultsCallerButDoesNotStallDelete()
    {
        FakeBackend backend = new();
        TaskCompletionSource<bool> firstWrite = backend.BlockNextCloudWrite(new InvalidOperationException("upload failed"));
        SaveOperationCoordinator coordinator = Coordinator(backend);

        Task save = Save(coordinator, Data(1, BaseTime), SaveMutationTargets.Local | SaveMutationTargets.Cloud);
        Task deletion = coordinator.EnqueueDelete(true);
        firstWrite.SetResult(true);

        Exception failure = await CaptureFailure(save);
        Assert.That(failure, Is.TypeOf<InvalidOperationException>());
        await deletion;

        Assert.That(backend.Events, Is.EqualTo(new[] { "save:1", "delete" }));
        Assert.That(backend.CloudJson, Is.Null);
    }

    [Test]
    public async Task LocalFailureFaultsCallerWithoutStartingCloudWrite()
    {
        FakeBackend backend = new() { LocalWriteException = new InvalidOperationException("disk full") };
        FakeLogger logger = new();
        SaveOperationCoordinator coordinator = new(backend, logger);

        Task save = Save(coordinator, Data(1, BaseTime), SaveMutationTargets.Local | SaveMutationTargets.Cloud);

        Exception failure = await CaptureFailure(save);
        Assert.That(failure, Is.TypeOf<InvalidOperationException>());
        Assert.That(backend.CloudWriteCount, Is.Zero);
        Assert.That(logger.Errors, Has.Some.Contains("Save operation"));
    }

    [Test]
    public async Task CapturedPayloadIsImmutable()
    {
        FakeBackend backend = new();
        SaveOperationCoordinator coordinator = Coordinator(backend);
        SaveData data = Data(1, BaseTime);
        SaveWriteRequest request = coordinator.CaptureSave(data);
        data.LetterTileData[0][0].letter = 'Z';

        await coordinator.EnqueueSave(request, SaveMutationTargets.Local);

        Assert.That(Deserialize(backend.LocalJson).LetterTileData[0][0].letter, Is.EqualTo('A'));
    }

    private static SaveOperationCoordinator Coordinator(FakeBackend backend)
    {
        return new SaveOperationCoordinator(backend, new FakeLogger());
    }

    private static Task Save(SaveOperationCoordinator coordinator, SaveData data, SaveMutationTargets targets)
    {
        return coordinator.EnqueueSave(coordinator.CaptureSave(data), targets);
    }

    private static SaveData Data(int score, DateTime timestamp)
    {
        return new SaveData
        {
            SchemaVersion = SaveDataValidator.CurrentSchemaVersion,
            Score = score,
            Timestamp = timestamp,
            LetterTileData = SaveReconcilerTests.Board()
        };
    }

    private static SaveData Deserialize(string json) => SaveJson.Deserialize(json);

    private static async Task<Exception> CaptureFailure(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class FakeBackend : ISaveMutationBackend
    {
        private readonly Queue<CloudGate> cloudGates = new();

        public string LocalJson { get; private set; }
        public string CloudJson { get; private set; }
        public Exception LocalWriteException { get; set; }
        public int CloudWriteCount { get; private set; }
        public List<int> CloudScores { get; } = new();
        public List<string> Events { get; } = new();

        public TaskCompletionSource<bool> BlockNextCloudWrite(Exception failure = null)
        {
            TaskCompletionSource<bool> gate = new();
            cloudGates.Enqueue(new CloudGate(gate, failure));
            return gate;
        }

        public void WriteLocal(string json)
        {
            if (LocalWriteException != null)
            {
                throw LocalWriteException;
            }
            LocalJson = json;
        }

        public void DeleteLocal() => LocalJson = null;

        public async Task WriteCloudAsync(string json)
        {
            SaveData data = Deserialize(json);
            CloudWriteCount++;
            CloudScores.Add(data.Score);
            Events.Add("save:" + data.Score);

            if (cloudGates.Count > 0)
            {
                CloudGate gate = cloudGates.Dequeue();
                await gate.Completion.Task;
                if (gate.Failure != null)
                {
                    throw gate.Failure;
                }
            }

            CloudJson = json;
        }

        public Task DeleteCloudAsync()
        {
            Events.Add("delete");
            CloudJson = null;
            return Task.CompletedTask;
        }

        private readonly struct CloudGate
        {
            public CloudGate(TaskCompletionSource<bool> completion, Exception failure)
            {
                Completion = completion;
                Failure = failure;
            }

            public TaskCompletionSource<bool> Completion { get; }
            public Exception Failure { get; }
        }
    }

    private sealed class FakeLogger : ISaveReconciliationLogger
    {
        public List<string> Errors { get; } = new();
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) => Errors.Add(message);
    }
}
