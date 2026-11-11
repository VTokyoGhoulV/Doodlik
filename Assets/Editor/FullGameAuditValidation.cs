#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Batch-only entry points for an isolated project copy. Never builds a player.
[InitializeOnLoad]
public static class FullGameAuditValidation
{
    private const string ActiveKey = "Doodlik.FullAudit.Focused";
    private static readonly List<string> Failures = new List<string>();
    private static int phase;
    private static double since;
    private static Player playerBeforeModeChange;
    private static bool expectedPortrait, expectedInertia;

    static FullGameAuditValidation()
    {
        if (!SessionState.GetBool(ActiveKey, false)) return;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    private static void Prepare()
    {
        string path = Application.dataPath.Replace('\\', '/');
        if (!Application.isBatchMode || !path.Contains("/.utmp/MenuValidation-"))
            throw new InvalidOperationException("Full audit requires a dedicated .utmp/MenuValidation-* batch project.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikFullAuditSandbox";
        AudioListener.pause = true;
        Directory.CreateDirectory("Logs");
    }

    public static void RunEditMode()
    {
        Prepare();
        ValidateAuthoring();
        JourneyValidation.Run();
        WorldGenerationValidation.RunStress();
        WorldGenerationValidation.RunLayoutAudit();
        FlyingHazardValidation.Run();
        CosmeticsProgressValidation.Validate();
        CosmeticMigrationValidation.Validate();
        AdminProgressValidation.Validate();
        ModeSelectionValidation.ValidatePreferences();
        SixZoneValidation.ValidateRules();
        CoinAttractionValidation.ValidateSchedule();
        HazardValidation.RunBushClearance();
        Debug.Log("FULL_AUDIT_EDITMODE_PASS: scene/prefab references, generation, physics routes, saves, migration, cosmetics, modes, zones, pickup schedule, bush clearance.");
    }

    private static void ValidateAuthoring()
    {
        int enabledScenes = 0, prefabs = 0;
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (!scene.enabled) continue;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path) == null)
                throw new InvalidOperationException("Missing enabled scene: " + scene.path);
            enabledScenes++;
        }
        if (enabledScenes == 0) throw new InvalidOperationException("No enabled game scene.");
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Assets/TextMesh Pro/Examples & Extras/", StringComparison.Ordinal)) continue;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0)
                    throw new InvalidOperationException("Missing prefab script: " + path + "/" + child.name);
            prefabs++;
        }
        Debug.Log($"AUDIT_AUTHORING_PASS enabledScenes={enabledScenes} prefabs={prefabs}");
    }

    public static void RunJourney() { Prepare(); JourneyPlayValidation.Run(); }
    public static void RunJourneyPortrait() { Prepare(); JourneyPlayValidation.RunPortrait(); }
    public static void RunMenu() { Prepare(); MenuPolishValidation.Run(); }
    public static void RunAdmin() { Prepare(); MenuPolishValidation.RunAdminTufts(); }
    public static void RunAudio() { Prepare(); AudioSliderValidation.Run(); }
    public static void RunCosmetics() { Prepare(); CosmeticsVisualValidation.Run(); }
    public static void RunCosmeticsPortrait() { Prepare(); CosmeticsVisualValidation.RunPortrait(); }
    public static void RunDistribution()
    {
        Prepare();
        HazardValidation.RunSlicedDistribution();
        EditorApplication.Exit(0);
    }
    public static void RunAscent()
    {
        Prepare();
        HazardValidation.RunSlicedAscentProgression();
        EditorApplication.Exit(0);
    }
    public static void RunConnected() { Prepare(); ConnectedHazardValidation.RunFinal(); }
    public static void RunHazardPlay() { Prepare(); HazardPlayValidation.Run(); }
    public static void RunRuntimeDensity() { Prepare(); HazardRuntimeDistributionValidation.RunModerate(); }

    public static void RunFocused()
    {
        Prepare();
        Failures.Clear();
        phase = 0;
        SessionState.SetBool(ActiveKey, true);
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void RunCase(string type, string method = "Validate")
    {
        try
        {
            Type check = typeof(FullGameAuditValidation).Assembly.GetType(type, true);
            check.GetMethod(method, BindingFlags.Static | BindingFlags.Public).Invoke(null, null);
        }
        catch (Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null)
                exception = exception.InnerException;
            Failures.Add(type + "." + method + ": " + exception.Message);
            Debug.LogWarning("AUDIT_CASE_FAIL " + type + "." + method + "\n" + exception);
        }
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        AudioListener.pause = true;
        if (EditorApplication.timeSinceStartup - since > 90)
        {
            Failures.Add("Focused runtime timeout at phase " + phase);
            Finish();
            return;
        }
        if (Time.frameCount < 20 || EditorApplication.timeSinceStartup - since < 0.6 || RunFlow.IsTransitioning) return;
        try
        {
            GameMenuUI menu = Object.FindAnyObjectByType<GameMenuUI>();
            if (phase == 0)
            {
                RunCase("UiLifecycleValidation", "ValidateSceneDisplay");
                RunCase("UiLifecycleValidation");
                RunCase("CosmeticLifecycleValidation");
                RunCase("JumpTrailValidation");
                RunCase("ChallengeRewardArtValidation");
                RunCase("GameplaySweepValidation");
                RunCase("GameplaySweepValidation", "ValidateHazardRemoval");
                RunCase("SixZoneValidation", "ValidateScoreBoundaries");
                menu.ReturnToHome();
            }
            else if (phase == 1)
            {
                RunCase("UiLifecycleValidation", "ValidateSceneDisplay");
                if (GameplayPreferences.IsDesktopBuild) { Finish(); return; }
                menu.GetComponent<FrontEndUI>().ShowModes();
                playerBeforeModeChange = Object.FindAnyObjectByType<Player>();
                expectedPortrait = !GameplayPreferences.IsPortrait;
                expectedInertia = !GameplayPreferences.UseInertia;
                if (!menu.SelectGameMode(expectedPortrait, expectedInertia))
                    throw new InvalidOperationException("The requested mobile mode change was rejected.");
            }
            else
            {
                if (GameplayPreferences.IsPortrait != expectedPortrait ||
                    GameplayPreferences.UseInertia != expectedInertia ||
                    ReferenceEquals(Object.FindAnyObjectByType<Player>(), playerBeforeModeChange))
                    throw new InvalidOperationException("Mobile mode change did not apply preferences and reload the scene.");
                RunCase("UiLifecycleValidation", "ValidateSceneDisplay");
                Finish();
                return;
            }
            phase++;
            since = EditorApplication.timeSinceStartup;
        }
        catch (Exception exception)
        {
            Failures.Add(exception.ToString());
            Finish();
        }
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            Failures.Add("Runtime log: " + message);
    }

    private static void Finish()
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        SessionState.SetBool(ActiveKey, false);
        Debug.Log((Failures.Count == 0 ? "FULL_AUDIT_FOCUSED_PASS" : "FULL_AUDIT_FOCUSED_FAIL") +
                  " failures=" + Failures.Count + (Failures.Count == 0 ? "" : "\n" + string.Join("\n", Failures)));
        EditorApplication.Exit(Failures.Count == 0 ? 0 : 1);
    }
}
#endif
