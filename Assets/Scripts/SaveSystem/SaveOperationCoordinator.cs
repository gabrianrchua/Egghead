using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Egghead.SaveSystem
{
    [Flags]
    public enum SaveMutationTargets
    {
        None = 0,
        Local = 1,
        Cloud = 2
    }

    public interface ISaveMutationBackend
    {
        void WriteLocal(string json);
        void DeleteLocal();
        Task WriteCloudAsync(string json);
        Task DeleteCloudAsync();
    }

    public readonly struct SaveOperationEpoch
    {
        internal SaveOperationEpoch(int value) => Value = value;
        internal int Value { get; }
    }

    public sealed class SaveWriteRequest
    {
        internal SaveWriteRequest(string json, DateTime timestamp, int epoch)
        {
            Json = json;
            Timestamp = timestamp;
            Epoch = epoch;
        }

        internal string Json { get; }
        internal DateTime Timestamp { get; }
        internal int Epoch { get; }
    }

    /// <summary>
    /// Owns ordering for every save mutation. Local writes happen synchronously when accepted,
    /// while cloud writes are serialized and pending saves are coalesced to the newest snapshot.
    /// </summary>
    public sealed class SaveOperationCoordinator
    {
        private readonly object sync = new();
        private readonly ISaveMutationBackend backend;
        private readonly ISaveReconciliationLogger logger;
        private readonly LinkedList<Mutation> pending = new();

        private int epoch;
        private bool workerRunning;
        private bool hasNewestTimestamp;
        private DateTime newestTimestamp;
        private int outstandingCloudWrites;

        public SaveOperationCoordinator(ISaveMutationBackend backend, ISaveReconciliationLogger logger)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public SaveOperationEpoch CaptureEpoch()
        {
            lock (sync)
            {
                return new SaveOperationEpoch(epoch);
            }
        }

        public bool IsCurrent(SaveOperationEpoch capturedEpoch)
        {
            lock (sync)
            {
                return capturedEpoch.Value == epoch;
            }
        }

        public SaveWriteRequest CaptureSave(SaveData data)
        {
            return CaptureSave(data, CaptureEpoch());
        }

        public SaveWriteRequest CaptureSave(SaveData data, SaveOperationEpoch capturedEpoch)
        {
            // Serialization is the deep copy: callers can mutate their arrays after this point
            // without changing the queued payload.
            return new SaveWriteRequest(SaveJson.Serialize(data), data.Timestamp, capturedEpoch.Value);
        }

        public Task EnqueueSave(SaveWriteRequest request, SaveMutationTargets targets)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            TaskCompletionSource<bool> completion = NewCompletion();
            lock (sync)
            {
                if (request.Epoch != epoch)
                {
                    logger.Warning("Ignored save captured before the latest delete barrier.");
                    completion.SetResult(true);
                    return completion.Task;
                }

                if (hasNewestTimestamp && request.Timestamp < newestTimestamp)
                {
                    logger.Warning($"Ignored older save snapshot [{request.Timestamp:u}]; newest accepted timestamp is [{newestTimestamp:u}].");
                    completion.SetResult(true);
                    return completion.Task;
                }

                if ((targets & SaveMutationTargets.Local) != 0)
                {
                    try
                    {
                        backend.WriteLocal(request.Json);
                    }
                    catch (Exception ex)
                    {
                        logger.Error("Save operation failed while writing local storage: " + ex.Message);
                        completion.SetException(ex);
                        return completion.Task;
                    }
                }

                hasNewestTimestamp = true;
                newestTimestamp = request.Timestamp;

                if ((targets & SaveMutationTargets.Cloud) == 0)
                {
                    completion.SetResult(true);
                    return completion.Task;
                }

                Mutation last = pending.Last?.Value;
                if (last != null && last.Type == MutationType.Save && last.Epoch == epoch)
                {
                    last.Json = request.Json;
                    last.Timestamp = request.Timestamp;
                    last.Completions.Add(completion);
                    logger.Info($"Coalesced pending cloud save to snapshot [{request.Timestamp:u}].");
                }
                else
                {
                    Mutation save = Mutation.Save(request.Json, request.Timestamp, epoch, completion);
                    pending.AddLast(save);
                    outstandingCloudWrites++;
                }

                StartWorkerLocked();
            }

            return completion.Task;
        }

        public Task EnqueueDelete(bool includeCloud)
        {
            TaskCompletionSource<bool> completion = NewCompletion();
            lock (sync)
            {
                epoch++;
                hasNewestTimestamp = false;
                newestTimestamp = default;

                Exception localException = null;
                try
                {
                    backend.DeleteLocal();
                }
                catch (Exception ex)
                {
                    localException = ex;
                    logger.Error("Delete operation failed while deleting local storage: " + ex.Message);
                }

                Mutation deletion = Mutation.Delete(epoch, includeCloud || outstandingCloudWrites > 0, localException, completion);

                LinkedListNode<Mutation> node = pending.First;
                while (node != null)
                {
                    LinkedListNode<Mutation> next = node.Next;
                    if (node.Value.Type == MutationType.Save)
                    {
                        deletion.Completions.AddRange(node.Value.Completions);
                        pending.Remove(node);
                        outstandingCloudWrites--;
                    }
                    node = next;
                }

                if (deletion.IncludeCloud)
                {
                    pending.AddLast(deletion);
                    StartWorkerLocked();
                }
                else
                {
                    Complete(deletion, localException);
                }
            }

            return completion.Task;
        }

        /// <summary>Return a task that completes after all mutations accepted before this call.</summary>
        public Task WaitForIdleAsync()
        {
            TaskCompletionSource<bool> completion = NewCompletion();
            lock (sync)
            {
                if (!workerRunning && pending.Count == 0)
                {
                    completion.SetResult(true);
                }
                else
                {
                    pending.AddLast(Mutation.Barrier(epoch, completion));
                    StartWorkerLocked();
                }
            }
            return completion.Task;
        }

        private void StartWorkerLocked()
        {
            if (workerRunning)
            {
                return;
            }

            workerRunning = true;
            _ = ProcessQueueAsync();
        }

        private async Task ProcessQueueAsync()
        {
            while (true)
            {
                Mutation mutation;
                lock (sync)
                {
                    if (pending.Count == 0)
                    {
                        workerRunning = false;
                        return;
                    }

                    mutation = pending.First.Value;
                    pending.RemoveFirst();
                }

                Exception failure = mutation.LocalException;
                try
                {
                    if (mutation.Type == MutationType.Save)
                    {
                        await backend.WriteCloudAsync(mutation.Json);
                    }
                    else if (mutation.Type == MutationType.Delete)
                    {
                        await backend.DeleteCloudAsync();
                    }
                }
                catch (Exception ex)
                {
                    failure = failure == null ? ex : new AggregateException(failure, ex);
                    logger.Error($"{mutation.Type} operation failed in cloud storage: {ex.Message}");
                }
                finally
                {
                    if (mutation.Type == MutationType.Save)
                    {
                        lock (sync)
                        {
                            outstandingCloudWrites--;
                        }
                    }
                }

                Complete(mutation, failure);
            }
        }

        private static void Complete(Mutation mutation, Exception failure)
        {
            foreach (TaskCompletionSource<bool> completion in mutation.Completions)
            {
                if (failure == null)
                {
                    completion.TrySetResult(true);
                }
                else
                {
                    completion.TrySetException(failure);
                }
            }
        }

        private static TaskCompletionSource<bool> NewCompletion()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private enum MutationType
        {
            Save,
            Delete,
            Barrier
        }

        private sealed class Mutation
        {
            private Mutation(MutationType type, int epoch, TaskCompletionSource<bool> completion)
            {
                Type = type;
                Epoch = epoch;
                Completions.Add(completion);
            }

            public MutationType Type { get; }
            public int Epoch { get; }
            public string Json { get; set; }
            public DateTime Timestamp { get; set; }
            public bool IncludeCloud { get; private set; }
            public Exception LocalException { get; private set; }
            public List<TaskCompletionSource<bool>> Completions { get; } = new();

            public static Mutation Save(string json, DateTime timestamp, int epoch, TaskCompletionSource<bool> completion)
            {
                return new Mutation(MutationType.Save, epoch, completion) { Json = json, Timestamp = timestamp, IncludeCloud = true };
            }

            public static Mutation Delete(int epoch, bool includeCloud, Exception localException, TaskCompletionSource<bool> completion)
            {
                return new Mutation(MutationType.Delete, epoch, completion) { IncludeCloud = includeCloud, LocalException = localException };
            }

            public static Mutation Barrier(int epoch, TaskCompletionSource<bool> completion)
            {
                return new Mutation(MutationType.Barrier, epoch, completion);
            }
        }
    }
}
