using Egghead.DictionaryData;
using System;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

public class DictionaryDataProvider : MonoBehaviour
{
    private static readonly ProfilerMarker InitializeMarker = new("Egghead.DictionaryData.Initialize");

    [SerializeField] private TextAsset dictionaryData;

    private DictionaryLookup lookup;

    public int LetterCount => lookup.LetterCount;
    public float LetterWeightsTotal => lookup.LetterWeightsTotal;

    private void Awake()
    {
#if DEVELOPMENT_BUILD
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long monoUsedBefore = Profiler.GetMonoUsedSizeLong();
        Stopwatch stopwatch = Stopwatch.StartNew();
#endif

        using (InitializeMarker.Auto())
        {
            if (dictionaryData == null)
            {
                throw new System.InvalidOperationException(
                    "Dictionary runtime data is not assigned. Regenerate it and assign it to DictionaryDataProvider.");
            }

            lookup = new DictionaryLookup(dictionaryData.bytes);
        }

#if DEVELOPMENT_BUILD
        stopwatch.Stop();
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        long monoUsedAfter = Profiler.GetMonoUsedSizeLong();
        UnityEngine.Debug.Log(
            $"Egghead.DictionaryData.Profile durationMs={stopwatch.Elapsed.TotalMilliseconds:F3} " +
            $"allocatedBytes={allocatedBytes} monoUsedBeforeBytes={monoUsedBefore} " +
            $"monoUsedAfterBytes={monoUsedAfter}");
#endif
    }

    public LetterData GetLetter(int index) => lookup.GetLetter(index);

    public bool TryGetPoints(string word, out int points) => lookup.TryGetPoints(word, out points);

    public bool TryGetDefinition(string word, out string definition) => lookup.TryGetDefinition(word, out definition);
}
