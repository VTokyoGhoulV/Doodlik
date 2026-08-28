#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildReleases
{
    private static readonly string[] Scenes =
    {
        "Assets/Scenes/SampleScene.unity"
    };

    public static void BuildAll()
    {
        string releaseRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../Releases"));
        Directory.CreateDirectory(releaseRoot);

        BuildWindows(releaseRoot);
        BuildAndroid(releaseRoot);

        Debug.Log("Both Doodlik release builds completed: " + releaseRoot);
    }

    private static void BuildWindows(string releaseRoot)
    {
        string windowsDirectory = Path.Combine(releaseRoot, "Doodlik-Windows");
        Directory.CreateDirectory(windowsDirectory);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = Path.Combine(windowsDirectory, "Doodlik.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        EnsureSucceeded(BuildPipeline.BuildPlayer(options), "Windows");
    }

    private static void BuildAndroid(string releaseRoot)
    {
        EditorUserBuildSettings.buildAppBundle = false;
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.tokyoghoul.doodlik");

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = Path.Combine(releaseRoot, "Doodlik-Android.apk"),
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        EnsureSucceeded(BuildPipeline.BuildPlayer(options), "Android");
    }

    private static void EnsureSucceeded(BuildReport report, string platform)
    {
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                platform + " build failed: " + report.summary.result +
                ", errors: " + report.summary.totalErrors
            );
        }

        Debug.Log(platform + " build size: " + report.summary.totalSize + " bytes");
    }
}
#endif
