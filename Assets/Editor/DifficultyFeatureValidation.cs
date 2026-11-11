#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// All preference mutations and rendered fixtures stay in the isolated QA copy.
// Runtime entries self-exit: invoke without -quit; no player build is produced.
[InitializeOnLoad]
public static class DifficultyFeatureValidation
{
    private const string ActiveKey = "Doodlik.DifficultyValidation.Active";
    private const string PortraitKey = "Doodlik.DifficultyValidation.Portrait";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int phase, configuration, checks;
    private static double since;
    private static string error;
    private static Player previousPlayer;

    static DifficultyFeatureValidation()
    {
        if (!SessionState.GetBool(ActiveKey, false)) return;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    private static void Prepare()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-"))
            throw new InvalidOperationException("Difficulty validation requires the isolated MenuValidation-* batch project.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikDifficultySandbox";
        AudioListener.pause = true;
        Directory.CreateDirectory("Logs");
    }

    public static void RunRules()
    {
        Prepare();
        DifficultyRecordsValidation.Validate();
        ModeSelectionValidation.ValidatePreferences();
        CosmeticsProgressValidation.Validate();
        SixZoneValidation.ValidateRules();
        Debug.Log("DIFFICULTY_RULES_PASS: score math, migration, isolated records, unchanged economy/save schema and height zones.");
    }

    public static void RunGeneration()
    {
        Prepare();
        DifficultyGenerationValidation.Run();
        EditorApplication.Exit(0);
    }

    public static void RunHazards()
    {
        Prepare();
        DifficultyGenerationValidation.RunHazards();
        EditorApplication.Exit(0);
    }

    public static void RunLiveHazardsEasy() => RunLiveHazards(GameDifficulty.Easy);
    public static void RunLiveHazardsNormal() => RunLiveHazards(GameDifficulty.Normal);
    public static void RunLiveHazardsHard() => RunLiveHazards(GameDifficulty.Hard);
    private static void RunLiveHazards(GameDifficulty difficulty)
    {
        Prepare();
        GameplayPreferences.SetDifficulty(difficulty);
        SessionState.SetInt("Doodlik.DifficultyValidation.HazardSeed", 1211);
        Debug.Log("DIFFICULTY_LIVE_HAZARDS_PROFILE=" + difficulty);
        HazardRuntimeDistributionValidation.RunModerate();
    }

    public static void RunLandscape() => Run(false);
    public static void RunPortrait() => Run(true);

    private static void Run(bool portrait)
    {
        Prepare();
        RunRecords.ResetAll();
        GameplayPreferences.SetMode(portrait, false, false);
        GameplayPreferences.SetDifficulty(GameDifficulty.Normal);
        SessionState.SetBool(PortraitKey, portrait);
        SessionState.SetBool(ActiveKey, true);
        phase = configuration = checks = 0;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        AudioListener.pause = true;
        try
        {
            if (error != null) throw new InvalidOperationException(error);
            if (EditorApplication.timeSinceStartup - since > 75)
                throw new TimeoutException("Difficulty runtime phase " + phase + ", config " + configuration);
            if (EditorApplication.timeSinceStartup - since < 0.8 || RunFlow.IsTransitioning || Time.frameCount < 20) return;
            GameMenuUI menu = Object.FindAnyObjectByType<GameMenuUI>();
            Player player = Object.FindAnyObjectByType<Player>();
            Check(menu != null && player != null, "Scene contains menu and player.");
            FrontEndUI ui = menu.GetComponent<FrontEndUI>();
            GameDifficulty difficulty = (GameDifficulty)(configuration % 3);
            bool inertia = configuration >= 3;
            bool portrait = SessionState.GetBool(PortraitKey, false) && !GameplayPreferences.IsDesktopBuild;

            if (phase == 0)
            {
                Check(!RunFlow.RunActive && ui.IsVisible, "Selection starts at home without an active run.");
                if (previousPlayer != null)
                    Check(!ReferenceEquals(previousPlayer, player), "Returning home creates a fresh run snapshot.");
                ui.ShowModes();
                if (GameplayPreferences.UseInertia != inertia || GameplayPreferences.IsPortrait != portrait)
                {
                    Check(menu.SelectGameMode(portrait, inertia), "Format/control selection is accepted before a run.");
                    since = EditorApplication.timeSinceStartup;
                    return;
                }
                if (GameplayPreferences.Difficulty != difficulty)
                {
                    Check(menu.SelectDifficulty(difficulty), "Difficulty selection is accepted before a run.");
                    since = EditorApplication.timeSinceStartup;
                    return;
                }
                Check(player.RunDifficulty == difficulty, "Reload applies difficulty to the new player.");
                DifficultyUiValidation.Validate();
                UiLifecycleValidation.ValidateSceneDisplay();
                ui.ShowModes();
                Capture("Modes-" + configuration);
                player.GetComponent<Rigidbody2D>().simulated = false;
                Object.FindAnyObjectByType<Spawner>().enabled = false;
                Object.FindAnyObjectByType<CameraFollow>().enabled = false;
                menu.StartRunFromHome();
                Time.timeScale = 0f;
                SetHeight(player, 127);
                phase = 1;
                since = EditorApplication.timeSinceStartup;
            }
            else if (phase == 1)
            {
                Capture("HUD-" + configuration);
                ValidateMetrics(player, menu, difficulty);
                Invoke(player, "Die", "Тест результатов");
                TMP_Text stats = player.gameOverPanel.transform.Find("Game Over Card/Game Over Stats").GetComponent<TMP_Text>();
                Check(stats.text.Contains("Высота: 5000") && stats.text.Contains("Счёт: " + player.CurrentScore) &&
                    stats.text.Contains(DifficultyRules.MultiplierLabel(difficulty)), "Results show independent height, score and multiplier.");
                phase = 2;
                since = EditorApplication.timeSinceStartup;
            }
            else
            {
                Capture("Results-" + configuration);
                ValidateAdminRollback(player);
                previousPlayer = player;
                configuration++;
                if (configuration >= 6) { Finish(0); return; }
                menu.ReturnToHome();
                phase = 0;
                since = EditorApplication.timeSinceStartup;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void ValidateMetrics(Player player, GameMenuUI menu, GameDifficulty difficulty)
    {
        Check(RunFlow.RunActive && !menu.SelectDifficulty(difficulty == GameDifficulty.Easy ? GameDifficulty.Hard : GameDifficulty.Easy),
            "An active, including paused, attempt cannot change difficulty.");
        Check(GameplayPreferences.Difficulty == difficulty, "Rejected selection leaves saved difficulty untouched.");
        Check(player.NormalJumpForce == 11f, "Difficulty leaves jump strength unchanged.");
        using (PlayerProgress.UseIsolatedStorageForValidation(out _, out _))
        {
            // Simulate an external preference edit too: the live run owns its multiplier.
            GameplayPreferences.SetDifficulty(difficulty == GameDifficulty.Hard ? GameDifficulty.Easy : GameDifficulty.Hard);
            SetHeight(player, 4000);
            Check(player.RunDifficulty == difficulty && player.CurrentScore == DifficultyRules.ScoreForHeight(4000, difficulty),
                "Multiplier remains fixed even when stored preferences change externally.");
            GameplayPreferences.SetDifficulty(difficulty);
            Check(player.CurrentHeight == 4000 && WorldZones.CurrentHeight == 4000 &&
                WorldZones.Current == WorldZones.AtScore(4000), "World zones use height, not awarded points.");
            Check(!PlayerProgress.Owns("accessory_astronaut_helmet"), "Even 5000 Hard points do not unlock the helmet below height 5000.");
            HeightProgressUI progress = Object.FindAnyObjectByType<HeightProgressUI>();
            Invoke(progress, "Update");
            Check(Mathf.Approximately(progress.DisplayedProgress, 0.8f), "Space rail uses 4000/5000 height on every difficulty.");
            SetHeight(player, 4999);
            Check(!PlayerProgress.Owns("clothing_astronaut"), "No early suit at height 4999.");
            SetHeight(player, 5000);
            Check(PlayerProgress.Owns("clothing_astronaut") && PlayerProgress.Owns("accessory_astronaut_helmet"),
                "Height 5000 unlocks both suit pieces on every difficulty.");
            Check(player.CurrentScore == DifficultyRules.ScoreForHeight(5000, difficulty), "Final score uses the selected coefficient.");
            string scoreKey = RunRecords.ScoreKey(GameplayPreferences.IsPortrait, GameplayPreferences.UseInertia, difficulty);
            string heightKey = RunRecords.HeightKey(GameplayPreferences.IsPortrait, GameplayPreferences.UseInertia, difficulty);
            Check(PlayerPrefs.GetInt(scoreKey) == player.CurrentScore && PlayerPrefs.GetInt(heightKey) == 5000,
                "Both independent records persist in the correct difficulty slot.");
            int coins = PlayerProgress.Coins;
            RunPowerups powers = player.GetComponent<RunPowerups>();
            Vector2 center = player.GetComponentInChildren<SpriteRenderer>().bounds.center;
            RunPickup coin = RunPickup.SpawnAirCoin(center, Camera.main, 0f);
            Check(coin != null, "Real coin factory creates a collectible.");
            try
            {
                Time.timeScale = 1f;
                RunPickup.CollectAlong(center, center, Vector2.one * 0.05f, Camera.main.orthographicSize * Camera.main.aspect * 2f, powers);
                Check(PlayerProgress.Coins == coins + 1, "A collected coin is still worth one on every difficulty.");
            }
            finally
            {
                Time.timeScale = 0f;
                if (coin != null) Object.DestroyImmediate(coin.gameObject);
            }
        }
        Debug.Log($"DIFFICULTY_RUNTIME_CASE difficulty={difficulty} portrait={GameplayPreferences.IsPortrait} inertia={GameplayPreferences.UseInertia} height={player.CurrentHeight} score={player.CurrentScore}");
    }

    private static void ValidateAdminRollback(Player player)
    {
        string scoreKey = Get<string>(player, "currentHighScoreKey");
        string heightKey = Get<string>(player, "currentBestHeightKey");
        bool scoreExisted = Get<bool>(player, "scoreKeyExistedAtRunStart");
        bool heightExisted = Get<bool>(player, "heightKeyExistedAtRunStart");
        Set(player, "isDead", false);
        PlayerProgress.BeginAdminSandbox();
        try
        {
            player.RefreshAdminState();
            Check(player.IsTestAttempt && PlayerPrefs.HasKey(scoreKey) == scoreExisted &&
                PlayerPrefs.HasKey(heightKey) == heightExisted, "Admin activation rolls back both record key presences.");
            Check(PlayerPrefs.GetInt(scoreKey, 0) == Get<int>(player, "highScoreAtRunStart") &&
                PlayerPrefs.GetInt(heightKey, 0) == Get<int>(player, "bestHeightAtRunStart"), "Admin restores both pre-run record values.");
            SetHeight(player, 6000);
        }
        finally { PlayerProgress.EndAdminSandbox(); }
        SetHeight(player, 6100);
        Check(player.IsTestAttempt && PlayerPrefs.HasKey(scoreKey) == scoreExisted && PlayerPrefs.HasKey(heightKey) == heightExisted,
            "Disabling admin cannot make this test attempt record-eligible again.");
    }

    private static void SetHeight(Player player, int height)
    {
        Set(player, "maximumHeight", WorldZones.HeightAtScore(height));
        Invoke(player, "UpdateHeightScore");
    }

    private static void Capture(string name)
    {
        Canvas.ForceUpdateCanvases();
        typeof(JourneyPlayValidation).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { "Difficulty-" + name });
    }

    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    private static void Check(bool condition, string description)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(description);
    }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = message;
    }
    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        SessionState.SetBool(ActiveKey, false);
        Debug.Log($"DIFFICULTY_RUNTIME_{(code == 0 ? "PASS" : "FAIL")} checks={checks} configurations={configuration} target={EditorUserBuildSettings.activeBuildTarget} portrait={SessionState.GetBool(PortraitKey, false)}");
        EditorApplication.Exit(code);
    }
}
#endif
