#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ReleasePlayerSmokeBuild
{
    // Launch from an isolated QA project, never from the user's open project.
    public static void Run()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string normalized = projectRoot.Replace('\\', '/');
        if (!Application.isBatchMode || normalized.IndexOf("/.utmp/", StringComparison.OrdinalIgnoreCase) < 0)
            throw new InvalidOperationException("Release smoke builds require a batch-mode .utmp QA copy.");
        string output = Path.GetFullPath(Path.Combine(projectRoot, "..", "ReleaseSmoke-Windows"));
        string expectedParent = Path.GetFullPath(Path.Combine(projectRoot, "..")).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetDirectoryName(output) != expectedParent)
            throw new InvalidOperationException("The release smoke output escaped the isolated QA directory.");
        Directory.CreateDirectory(output);
        string originalCompany = PlayerSettings.companyName;
        string originalProduct = PlayerSettings.productName;
        List<string> missingScripts = new List<string>();
        Application.LogCallback capture = (message, stack, type) =>
        {
            if (message.IndexOf("is missing or no valid script is attached", StringComparison.OrdinalIgnoreCase) >= 0)
                missingScripts.Add(message);
        };
        try
        {
            PlayerSettings.companyName = "CodexValidation";
            PlayerSettings.productName = "DoodlikReleaseSmoke";
            Application.logMessageReceived += capture;
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = Path.Combine(output, "Doodlik.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
                extraScriptingDefines = new[] { "DOODLIK_RELEASE_SMOKE" }
            });
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors > 0)
                throw new InvalidOperationException("Release smoke player build failed: " + report.summary.result);
            if (missingScripts.Count != 0)
                throw new InvalidOperationException("Release smoke player lost script bindings: " + string.Join(" | ", missingScripts));
            Debug.Log("RELEASE_PLAYER_SMOKE_BUILD_PASS output=" + output + " size=" + report.summary.totalSize);
        }
        finally
        {
            Application.logMessageReceived -= capture;
            PlayerSettings.companyName = originalCompany;
            PlayerSettings.productName = originalProduct;
        }
    }
}
#endif
