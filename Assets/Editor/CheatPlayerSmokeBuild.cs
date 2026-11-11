#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class CheatPlayerSmokeBuild
{
    public static void Run()
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        if (!Application.isBatchMode || !project.Replace('\\', '/').Contains("/.utmp/"))
            throw new InvalidOperationException("Cheat smoke builds require an isolated .utmp project.");
        string output = Path.Combine(Path.GetDirectoryName(project), "CheatsSmoke-Windows");
        Directory.CreateDirectory(output);
        string company = PlayerSettings.companyName, product = PlayerSettings.productName;
        try
        {
            PlayerSettings.companyName = "CodexValidation";
            PlayerSettings.productName = "DoodlikCheatsSmoke";
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = Path.Combine(output, "DoodlikCheatsSmoke.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
                extraScriptingDefines = new[] { "DOODLIK_CHEATS", "DOODLIK_CHEATS_SMOKE" }
            });
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
                throw new InvalidOperationException("Cheat smoke build failed: " + report.summary.result);
            Debug.Log("CHEATS_PLAYER_SMOKE_BUILD_PASS output=" + output + " size=" + report.summary.totalSize);
        }
        finally
        {
            PlayerSettings.companyName = company;
            PlayerSettings.productName = product;
        }
    }
}
#endif
