#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildReleases
{
    private static readonly string[] Scenes =
    {
        "Assets/Scenes/SampleScene.unity"
    };

    [MenuItem("Doodlik/Build Both To Desktop")]
    public static void BuildAll()
    {
        string releaseRoot = ReleaseRoot();
        Directory.CreateDirectory(releaseRoot);

        BuildWindows(releaseRoot);
        BuildAndroid(releaseRoot, false);

        Debug.Log("Both Doodlik release builds completed: " + releaseRoot);
    }

    // Keep the installed release intact until the replacement passes artifact checks.
    public static void BuildToStaging()
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        if (!Application.isBatchMode || !project.Replace('\\', '/').Contains("/.utmp/"))
            throw new InvalidOperationException("Staged release builds require an isolated .utmp project.");
        string releaseRoot = Path.Combine(Path.GetDirectoryName(project),
            "Release-v" + PlayerSettings.bundleVersion + "-Staging");
        Directory.CreateDirectory(releaseRoot);
        BuildWindows(releaseRoot);
        BuildAndroid(releaseRoot, false);
        Debug.Log("Both staged Doodlik builds completed: " + releaseRoot);
    }

    // Private cheat players use a separate application identity and never replace
    // the ordinary release. The symbol is scoped to the player compilation only.
    public static void BuildCheatsToStaging() => BuildCheatsStaged(true, true);

    public static void BuildCheatWindowsToStaging() => BuildCheatsStaged(true, false);

    public static void BuildCheatAndroidToStaging() => BuildCheatsStaged(false, true);

    private static void BuildCheatsStaged(bool windows, bool android)
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        if (!Application.isBatchMode || !project.Replace('\\', '/').Contains("/.utmp/"))
            throw new InvalidOperationException("Cheat builds require an isolated .utmp project.");
        if (PlayerSettings.bundleVersion != "0.0.4" || PlayerSettings.Android.bundleVersionCode != 4)
            throw new InvalidOperationException("This private cheat variant must retain version 0.0.4 / Android code 4.");
        string releaseRoot = Path.Combine(Path.GetDirectoryName(project),
            "Cheats-v" + PlayerSettings.bundleVersion + "-Staging");
        Directory.CreateDirectory(releaseRoot);

        string previousProduct = PlayerSettings.productName;
        string previousId = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
        bool wasBundle = EditorUserBuildSettings.buildAppBundle;
        bool wasExport = EditorUserBuildSettings.exportAsGoogleAndroidProject;
        bool wasDevelopment = EditorUserBuildSettings.development;
        try
        {
            PlayerSettings.productName = "Doodlik Cheats";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.tokyoghoul.doodlik.cheats");
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.development = false;
            if (windows)
            {
                string directory = Path.Combine(releaseRoot,
                    "Doodlik-Windows-Cheats-v" + PlayerSettings.bundleVersion);
                Directory.CreateDirectory(directory);
                BuildChecked(new BuildPlayerOptions
                {
                    scenes = Scenes,
                    locationPathName = Path.Combine(directory, "Doodlik Cheats.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                    extraScriptingDefines = new[] { "DOODLIK_CHEATS" }
                }, "Windows Cheats");
            }
            if (android)
            {
                BuildChecked(new BuildPlayerOptions
                {
                    scenes = Scenes,
                    locationPathName = Path.Combine(releaseRoot,
                        "Doodlik-Android-Cheats-v" + PlayerSettings.bundleVersion + ".apk"),
                    target = BuildTarget.Android,
                    options = BuildOptions.None,
                    extraScriptingDefines = new[] { "DOODLIK_CHEATS" }
                }, "Android Cheats");
            }
            Debug.Log("Staged Doodlik Cheats builds completed: " + releaseRoot);
        }
        finally
        {
            PlayerSettings.productName = previousProduct;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, previousId);
            EditorUserBuildSettings.buildAppBundle = wasBundle;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = wasExport;
            EditorUserBuildSettings.development = wasDevelopment;
        }
    }

    [MenuItem("Doodlik/Build Android Test APK")]
    public static void BuildAndroidOnly()
    {
        string releaseRoot = ReleaseRoot();
        Directory.CreateDirectory(releaseRoot);
        BuildAndroid(releaseRoot, true);
        Debug.Log("Doodlik Android test build completed: " + releaseRoot);
    }

    [MenuItem("Doodlik/Build Windows")]
    public static void BuildWindowsOnly()
    {
        string releaseRoot = ReleaseRoot();
        Directory.CreateDirectory(releaseRoot);
        BuildWindows(releaseRoot);
        Debug.Log("Doodlik Windows build completed: " + releaseRoot);
    }

    private static string ReleaseRoot()
    {
        string version = PlayerSettings.bundleVersion;
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException("Set the Doodlik version in Player Settings before building.");
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Doodlik",
            "v" + version);
    }

    private static void BuildWindows(string releaseRoot)
    {
        string windowsDirectory = Path.Combine(
            releaseRoot,
            "Doodlik-Windows-v" + PlayerSettings.bundleVersion
        );
        Directory.CreateDirectory(windowsDirectory);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = Path.Combine(windowsDirectory, "Doodlik.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        BuildChecked(options, "Windows");
    }

    private static void BuildAndroid(string releaseRoot, bool testBuild)
    {
        bool wasBundle = EditorUserBuildSettings.buildAppBundle;
        string previousId = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
        string previousProduct = PlayerSettings.productName;
        try
        {
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,
                testBuild ? "com.tokyoghoul.doodlik.test" : "com.tokyoghoul.doodlik");
            if (testBuild) PlayerSettings.productName = "Doodlik Test";
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = Path.Combine(releaseRoot,
                    "Doodlik-Android" + (testBuild ? "-Test" : "") +
                    "-v" + PlayerSettings.bundleVersion + ".apk"),
                target = BuildTarget.Android,
                options = testBuild ? BuildOptions.Development : BuildOptions.None
            };
            BuildChecked(options, testBuild ? "Android test" : "Android");
        }
        finally
        {
            PlayerSettings.productName = previousProduct;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, previousId);
            EditorUserBuildSettings.buildAppBundle = wasBundle;
        }
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

    private static void BuildChecked(BuildPlayerOptions options, string platform)
    {
        string bindingError = null;
        Application.LogCallback watch = (message, stack, type) =>
        {
            if (message.IndexOf("Script attached to", StringComparison.OrdinalIgnoreCase) >= 0 &&
                message.IndexOf("missing or no valid script", StringComparison.OrdinalIgnoreCase) >= 0)
                bindingError = message;
        };
        Application.logMessageReceived += watch;
        BuildReport report;
        try { report = BuildPipeline.BuildPlayer(options); }
        finally { Application.logMessageReceived -= watch; }
        if (bindingError != null)
            throw new BuildFailedException(platform + " contains an unbound gameplay script: " + bindingError);
        EnsureSucceeded(report, platform);
    }
}
#endif
