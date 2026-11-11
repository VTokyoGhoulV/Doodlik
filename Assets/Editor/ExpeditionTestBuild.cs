#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Private test identities live only inside an isolated build copy. Ordinary
// build menu commands and the source project's PlayerSettings are not changed.
public static class ExpeditionTestBuild
{
    public const string Product = "Doodlik Expedition Test";
    public const string Package = "com.tokyoghoul.doodlik.expeditiontest";
    private static readonly string[] Scenes = { "Assets/Scenes/SampleScene.unity" };
    public static void Run() => Build(true, true);
    public static void WindowsOnly() => Build(true, false);
    public static void AndroidOnly() => Build(false, true);

    private static void Build(bool windows, bool android)
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        if (!Application.isBatchMode || !project.Replace('\\', '/').Contains("/.utmp/"))
            throw new InvalidOperationException("Private test builds require an isolated .utmp project.");
        if (PlayerSettings.bundleVersion != "0.0.4" || PlayerSettings.Android.bundleVersionCode != 4)
            throw new InvalidOperationException("Keep this test on version0.0.4 / Android code4.");
        string output = Path.Combine(Path.GetDirectoryName(project), "ExpeditionTest-20261102-Staging");
        string windowsDirectory = Path.Combine(output, "Doodlik-Windows-Expedition-Test-v0.0.4");
        string executable = Path.Combine(windowsDirectory, Product + ".exe");
        string apk = Path.Combine(output, "Doodlik-Android-Expedition-Test-v0.0.4.apk");
        if (windows && File.Exists(executable) || android && File.Exists(apk))
            throw new InvalidOperationException("Do not overwrite an existing private test artifact.");
        Directory.CreateDirectory(output);
        string oldProduct = PlayerSettings.productName;
        string oldAndroidId = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
        string oldWindowsId = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Standalone);
        bool oldBundle = EditorUserBuildSettings.buildAppBundle;
        bool oldExport = EditorUserBuildSettings.exportAsGoogleAndroidProject;
        bool oldDevelopment = EditorUserBuildSettings.development;
        ScriptingImplementation oldBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
        AndroidArchitecture oldArchitecture = PlayerSettings.Android.targetArchitectures;
        try
        {
            PlayerSettings.productName = Product;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, Package);
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, Package);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.development = true;
            BuildScriptBindingValidation.Validate();
            if (windows)
            {
                Directory.CreateDirectory(windowsDirectory);
                Checked(new BuildPlayerOptions { scenes = Scenes, locationPathName = executable,
                    target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development }, "Windows Expedition Test");
            }
            if (android)
                Checked(new BuildPlayerOptions { scenes = Scenes, locationPathName = apk,
                    target = BuildTarget.Android, options = BuildOptions.Development }, "Android Expedition Test");
            Debug.Log("EXPEDITION_TEST_BUILD_PASS product=" + Product + " package=" + Package +
                " version=0.0.4 code=4 development=true output=" + output);
        }
        finally
        {
            PlayerSettings.productName = oldProduct;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, oldAndroidId);
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, oldWindowsId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, oldBackend);
            PlayerSettings.Android.targetArchitectures = oldArchitecture;
            EditorUserBuildSettings.buildAppBundle = oldBundle;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = oldExport;
            EditorUserBuildSettings.development = oldDevelopment;
        }
    }

    private static void Checked(BuildPlayerOptions options, string label)
    {
        string missingScript = null;
        Application.LogCallback watch = (message, stack, type) =>
        {
            if (message.IndexOf("Script attached to", StringComparison.OrdinalIgnoreCase) >= 0 &&
                message.IndexOf("missing or no valid script", StringComparison.OrdinalIgnoreCase) >= 0)
                missingScript = message;
        };
        BuildReport report;
        Application.logMessageReceived += watch;
        try { report = BuildPipeline.BuildPlayer(options); }
        finally { Application.logMessageReceived -= watch; }
        if (missingScript != null) throw new BuildFailedException(label + ": " + missingScript);
        if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors > 0)
            throw new BuildFailedException(label + " failed: " + report.summary.result + ", errors=" + report.summary.totalErrors);
        Debug.Log("EXPEDITION_TEST_ARTIFACT_PASS platform=" + options.target + " output=" + options.locationPathName +
            " bytes=" + report.summary.totalSize + " warnings=" + report.summary.totalWarnings);
    }
}
#endif
