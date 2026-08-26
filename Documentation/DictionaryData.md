# Dictionary runtime data

`Assets/GameData/words.csv` and `Assets/GameData/letters.csv` are the editable sources for Egghead's dictionary. Player builds consume only the generated `Assets/GameData/dictionary.bytes` asset.

## Regenerating the asset

After changing either CSV, open the project in Unity `6000.4.6f1` and select:

`Tools > Egghead > Regenerate Dictionary Data`

The generator validates both sources and updates the binary only when its deterministic contents change. Player builds fail with a regeneration instruction if the binary is missing, corrupt, or stale.

Words must contain only ASCII A-Z letters and have a positive 32-bit score. Duplicate words are rejected using ordinal case-insensitive comparison. Letter metadata must contain each letter A-Z exactly once.

The word CSV supports quoted fields following normal CSV escaping rules. Definitions may contain commas, escaped quotes, and line breaks. Empty definitions take no per-word space in the runtime asset. A populated definition is UTF-8 encoded in a sparse section and decoded only when `TryGetDefinition` is called.

## Runtime format

The versioned little-endian format contains source SHA-256 hashes, letter metadata and precomputed weights, a front-coded word table, and an optional sparse definition section. Words are ordinal-ignore-case sorted in blocks of 16. Scores are stored as unsigned variable-length integers.

At startup, `DictionaryDataProvider` retains the `TextAsset` byte buffer and creates 26 immutable letter records. Word lookups compare directly against the front-coded ASCII data without creating strings or per-word objects.

Changing the binary layout requires incrementing `DictionaryDataFormat.Version`, regenerating the asset, and updating decoder compatibility tests.

## Measurements

The generated asset is 2,754,231 bytes, compared with 6,360,574 bytes for `words.csv` alone: a 56.7% reduction before Unity build compression. The source dictionary contains 369,556 words.

Measurements were captured on August 25, 2026 with a Google Pixel 9 Pro running Android 17. Both APKs were ARM64 IL2CPP development builds from Unity `6000.4.6f1`, with Connect With Profiler enabled and Deep Profiling and Script Debugging disabled. The baseline was built from commit `affa7c7`; the current build used version `0.5`.

Each result below is from five force-stopped Title-to-Main launches. Provider duration and Unity managed-heap usage were sampled immediately around `CSVReader.Awake` or `DictionaryDataProvider.Awake`. The maximum post-initialization value is the highest managed-heap-used sample over those five runs. Android PSS is included only as a whole-process corroborating measurement because it contains native, graphics, and Profiler overhead.

| Measurement | Baseline CSV build | Preprocessed build |
| --- | ---: | ---: |
| Median provider main-thread time | 618.936 ms | 5.724 ms |
| Maximum provider main-thread time | 659.680 ms | 14.984 ms |
| Median provider managed-heap increase | 83,984,384 bytes | 2,715,648 bytes |
| Maximum post-initialization managed heap used | 97,157,120 bytes | 15,421,440 bytes |
| Median Title-to-Main transition | 1,806 ms | 1,158 ms |
| Median longest presented-frame gap | 862.539 ms | 221.884 ms |
| Median Android PSS increase | 140,456 KB | 61,468 KB |
| Raw dictionary asset size | 6,360,574 bytes | 2,754,231 bytes |
| Development APK size | 77,301,380 bytes | 76,664,404 bytes |

The median provider time decreased by 99.1%, and its managed-heap increase decreased by 96.8%. The new provider retains the 2.75 MB binary plus its 26 letter records; it no longer creates approximately 370,000 word objects and strings or the temporary arrays used by CSV splitting and sorting.

`GC.GetAllocatedBytesForCurrentThread` returned zero in both Android IL2CPP builds, so it was not used as the allocation result. The managed-heap increase is the reliable Unity Profiler allocation proxy for this synchronous initialization region; no explicit collection occurs inside either measured provider. The raw five-run CSV files and profiling APKs are retained under the git-ignored `Builds/EGGHEAD-010/` directory.

Use the `Egghead.DictionaryData.Initialize` Profiler marker for future measurements of the preprocessed build. For the baseline, use the `CSVReader.Awake` script callback sample.

The current profiling APK can be built with `Tools > Egghead > Build Android Profiling APK`. It writes `Builds/EGGHEAD-010/current-0.5.apk` as a Development Build with profiler connection support.
