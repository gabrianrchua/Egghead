using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Egghead.DictionaryData.Editor
{
    public static class DictionaryDataGenerator
    {
        public const string WordsPath = "Assets/GameData/words.csv";
        public const string LettersPath = "Assets/GameData/letters.csv";
        public const string OutputPath = "Assets/GameData/dictionary.bytes";

        [MenuItem("Tools/Egghead/Regenerate Dictionary Data")]
        public static void Regenerate()
        {
            byte[] generated = DictionaryDataCompiler.Compile(File.ReadAllBytes(WordsPath), File.ReadAllBytes(LettersPath));
            if (File.Exists(OutputPath) && BytesEqual(File.ReadAllBytes(OutputPath), generated))
            {
                Debug.Log($"Dictionary data is already current ({generated.Length:N0} bytes).");
                return;
            }

            File.WriteAllBytes(OutputPath, generated);
            AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"Regenerated {OutputPath} ({generated.Length:N0} bytes).");
        }

        public static void ValidateCurrent()
        {
            if (!File.Exists(OutputPath))
            {
                throw new BuildFailedException(
                    $"Generated dictionary data is missing. Run Tools > Egghead > Regenerate Dictionary Data to create {OutputPath}.");
            }

            if (!DictionaryDataCompiler.IsCurrent(
                    File.ReadAllBytes(OutputPath),
                    File.ReadAllBytes(WordsPath),
                    File.ReadAllBytes(LettersPath),
                    out string reason))
            {
                throw new BuildFailedException(
                    $"Dictionary data validation failed: {reason}. Run Tools > Egghead > Regenerate Dictionary Data.");
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal sealed class DictionaryDataBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => DictionaryDataGenerator.ValidateCurrent();
    }
}
