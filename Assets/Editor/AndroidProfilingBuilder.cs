using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class AndroidProfilingBuilder
{
    private const string DefaultOutputPath = "Builds/EGGHEAD-010/current-0.5.apk";

    [MenuItem("Tools/Egghead/Build Android Profiling APK")]
    public static void Build()
    {
        string outputPath = GetOutputPath();
        string directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.Development | BuildOptions.ConnectWithProfiler
        });

        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Android profiling build failed: {report.summary.result} ({report.summary.totalErrors} errors).");
        }

        Debug.Log(
            $"Built Android profiling APK at {Path.GetFullPath(outputPath)} " +
            $"({report.summary.totalSize:N0} bytes, {report.summary.totalTime}).");
    }

    private static string GetOutputPath()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length - 1; i++)
        {
            if (string.Equals(arguments[i], "-profilingBuildPath", StringComparison.Ordinal))
            {
                return arguments[i + 1];
            }
        }

        return DefaultOutputPath;
    }
}
