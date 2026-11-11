#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Actual home/start/death/reload/pause/focus/admin paths, in a disposable Editor.
[InitializeOnLoad]
public static class RunJournalValidation
{
    private const string Key = "Doodlik.RunJournalValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static Player player, previousPlayer;
    private static int phase, checks, waitFrame, wallet;
    private static double since, frozenTime;
    private static string failure;

    static RunJournalValidation()
    {
        if (!SessionState.GetBool(Key, false)) return;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    public static void Run() => Begin(false);
    public static void RunPortrait() => Begin(true);
    private static void Begin(bool portrait)
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/"))
            throw new InvalidOperationException("Run journal requires an isolated batch project.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikJournal_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        PlayerProgress.Reload();
        SessionState.SetBool(Key + ".portrait", portrait);
        GameplayPreferences.SetMode(portrait, false, false);
        GameplayPreferences.SetDifficulty(GameDifficulty.Normal);
        RunStatisticsValidation.Validate();
        ChallengeProgressValidation.Validate();
        CosmeticsProgressValidation.Validate();
        CosmeticMigrationValidation.Validate();
        Debug.Log("STATISTICS_AND_EXISTING_PROGRESS_PASS before actual journal lifecycle");
        SessionState.SetBool(Key, true);
        RequestViewport();
        phase = checks = 0;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RequestViewport()
    {
        if (!SessionState.GetBool(Key, false)) return;
        bool portrait = SessionState.GetBool(Key + ".portrait", false);
        PlayModeWindow.SetCustomRenderingResolution(portrait ? 720u : 1280u,
            portrait ? 1280u : 720u, "Doodlik journal validation");
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (failure != null) throw new Exception(failure);
            if (EditorApplication.timeSinceStartup - since > 120) throw new TimeoutException("Journal phase " + phase);
            if (Time.frameCount < 25 || RunFlow.IsTransitioning) return;
            Check(EditorUtility.audioMasterMute && AudioListener.volume == 0f, "Every test frame remains fully muted.");
            GameMenuUI menu = Object.FindFirstObjectByType<GameMenuUI>();
            if (phase == 0)
            {
                player = Object.FindFirstObjectByType<Player>();
                Check(player != null && player.Journal != null && !player.Journal.Started, "Home attaches but never starts the journal.");
                Check(PlayerProgress.Statistics.Runs == 0, "Fresh home has no fabricated history.");
                RunFlow.RunActive = RunFlow.IsTransitioning = true;
                Check(!player.Journal.EndRun("Выход при загрузке") && !player.Journal.Started &&
                    PlayerProgress.Statistics.Runs == 0, "A newly loaded draft during a restart cannot create an empty quit entry.");
                RunFlow.RunActive = RunFlow.IsTransitioning = false;
                bool portrait = SessionState.GetBool(Key + ".portrait", false);
                Check(GameplayPreferences.IsPortrait == portrait && Screen.width == (portrait ? 720 : 1280), "Actual gameplay viewport matches the chosen format.");
                menu.StartRunFromHome();
                phase = 1;
                return;
            }
            if (phase == 1)
            {
                if (!RunFlow.IsPlaying || GameMenuUI.BlocksGameplayInput || !player.Journal.Started || player.Challenges.Session == null) return;
                Check(PlayerProgress.Statistics.Runs == 0, "An unfinished live attempt is not falsely completed.");
                FreezePlayer(player);
                Call(player.Journal, "OnApplicationFocus", true);
                RunPowerups powers = player.GetComponent<RunPowerups>();
                Rigidbody2D body = player.GetComponent<Rigidbody2D>();
                RunPickup.RemoveUnclaimedCoins();
                for (int i = 0; i < 3; i++)
                {
                    Check(RunPickup.SpawnAirCoin(body.position, Camera.main, i) != null, "Actual run coin spawned.");
                    RunPickup.CollectAlong(body.position, body.position, Vector2.one * 0.1f,
                        Camera.main.orthographicSize * Camera.main.aspect * 2f, powers, false);
                }
                player.BounceFromBush();
                Check(player.Challenges.Session.Stats.Coins == 3 && player.Challenges.Session.Stats.Bushes == 1,
                    "Journal source receives actual pickup and accepted bush events.");
                wallet = PlayerProgress.Coins;
                waitFrame = Time.frameCount + 6;
                phase = 2;
                return;
            }
            if (phase == 2)
            {
                if (Time.frameCount < waitFrame) return;
                Check(player.Journal.ActiveSeconds > 0, "Focused gameplay contributes active time.");
                Call(player.Journal, "OnApplicationPause", true);
                Time.timeScale = 0;
                frozenTime = player.Journal.ActiveSeconds;
                waitFrame = Time.frameCount + 4;
                phase = 3;
                return;
            }
            if (phase == 3)
            {
                if (Time.frameCount < waitFrame) return;
                Check(Math.Abs(player.Journal.ActiveSeconds - frozenTime) < 0.000001 && PlayerProgress.Statistics.Runs == 0,
                    "Application pause adds neither time nor a split attempt.");
                Call(player.Journal, "OnApplicationPause", false);
                Call(player.Journal, "OnApplicationFocus", false);
                Time.timeScale = 1;
                waitFrame = Time.frameCount + 4;
                phase = 4;
                return;
            }
            if (phase == 4)
            {
                if (Time.frameCount < waitFrame) return;
                Check(Math.Abs(player.Journal.ActiveSeconds - frozenTime) < 0.000001, "Unfocused background simulation does not add playtime.");
                Call(player.Journal, "OnApplicationFocus", true);
                waitFrame = Time.frameCount + 4;
                phase = 5;
                return;
            }
            if (phase == 5)
            {
                if (Time.frameCount < waitFrame) return;
                Check(player.Journal.ActiveSeconds > frozenTime, "Resume continues the same active-time counter.");
                Call(player, "Die", "Проверка поражения");
                CareerStatisticsSnapshot stats = PlayerProgress.Statistics;
                Check(stats.Runs == 1 && stats.TotalCoins == 3 && stats.TotalBushes == 1, "Death finalizes actual run metrics once.");
                Check(stats.RecentRuns.Count == 1 && stats.RecentRuns[0].ActiveSeconds > 0 &&
                    stats.RecentRuns[0].Portrait == GameplayPreferences.IsPortrait && !stats.RecentRuns[0].Inertia,
                    "Recent history captures the natural format, control and playtime.");
                Check(PlayerProgress.Coins == wallet, "Recording statistics never awards currency.");
                Call(player, "Die", "Повторный callback");
                Check(!player.Journal.EndRun("Дубль") && PlayerProgress.Statistics.Runs == 1,
                    "Repeated death/end callbacks cannot duplicate history.");
                previousPlayer = player;
                menu.ReturnToHome();
                phase = 6;
                return;
            }
            if (phase == 6 || phase == 8 || phase == 10)
            {
                Player next = Object.FindFirstObjectByType<Player>();
                if (next == null || next == previousPlayer || RunFlow.RunActive || next.Journal == null) return;
                player = next;
                Check(!player.Journal.Started, "A rebuilt home draft is not another run.");
                int expected = phase == 6 ? 1 : 2;
                Check(PlayerProgress.Statistics.Runs == expected, "Death/home/admin scene teardown does not double count.");
                menu.StartRunFromHome();
                phase++;
                return;
            }
            if (phase == 7)
            {
                if (!RunFlow.IsPlaying || GameMenuUI.BlocksGameplayInput || !player.Journal.Started) return;
                FreezePlayer(player);
                previousPlayer = player;
                menu.ReturnToHome();
                Check(PlayerProgress.Statistics.Runs == 2 && PlayerProgress.Statistics.RecentRuns[0].EndReason == "В меню",
                    "Voluntary return finalizes a live attempt with its reason.");
                phase = 8;
                return;
            }
            if (phase == 9)
            {
                if (!RunFlow.IsPlaying || GameMenuUI.BlocksGameplayInput || !player.Journal.Started || player.Challenges.Session == null) return;
                FreezePlayer(player);
                PlayerProgress.BeginAdminSandbox();
                player.Challenges.RecordCoin(false);
                PlayerProgress.EndAdminSandbox();
                Check(!player.Challenges.Session.IsEligible, "Admin toggle latches the certified session as ineligible.");
                previousPlayer = player;
                menu.ReturnToHome();
                Check(PlayerProgress.Statistics.Runs == 2, "Disabling admin before leaving cannot legitimize a cheat attempt.");
                phase = 10;
                return;
            }
            if (phase == 11)
            {
                if (!RunFlow.IsPlaying || GameMenuUI.BlocksGameplayInput || !player.Journal.Started) return;
                FreezePlayer(player);
                Call(player.Journal, "OnApplicationQuit");
                Check(PlayerProgress.Statistics.Runs == 3 && PlayerProgress.Statistics.RecentRuns[0].EndReason == "Выход",
                    "Application quit finalizes a live attempt once.");
                previousPlayer = player;
                menu.ReturnToHome();
                phase = 12;
                return;
            }
            if (phase == 12)
            {
                Player homePlayer = Object.FindFirstObjectByType<Player>();
                if (homePlayer == null || homePlayer == previousPlayer || RunFlow.RunActive) return;
                Check(PlayerProgress.Statistics.Runs == 3, "Quit plus teardown remains one entry.");
                PlayerProgress.Reload();
                Check(PlayerProgress.Statistics.Runs == 3 && PlayerProgress.Statistics.TotalCoins == 3,
                    "Actual finalized run history survives profile reload.");
                player = homePlayer;
                menu.StartRunFromHome();
                phase = 13;
                return;
            }
            if (phase == 13)
            {
                if (!RunFlow.IsPlaying || GameMenuUI.BlocksGameplayInput || !player.Journal.Started) return;
                FreezePlayer(player);
                Call(player, "Die", "Перезапуск после поражения");
                Check(PlayerProgress.Statistics.Runs == 4, "The defeated pre-restart attempt was recorded.");
                previousPlayer = player;
                player.RestartGame();
                phase = 14;
                return;
            }
            if (phase == 14)
            {
                Player next = Object.FindFirstObjectByType<Player>();
                if (next == null || next == previousPlayer || !RunFlow.IsPlaying || GameMenuUI.BlocksGameplayInput ||
                    next.Journal == null || !next.Journal.Started) return;
                player = next;
                FreezePlayer(player);
                Check(PlayerProgress.Statistics.Runs == 4, "Actual restart rebuilds a fresh live journal without duplicating the dead one.");
                previousPlayer = player;
                menu.ReturnToHome();
                Check(PlayerProgress.Statistics.Runs == 5, "The resumed post-restart attempt is a distinct run.");
                phase = 15;
                return;
            }
            Player finalHome = Object.FindFirstObjectByType<Player>();
            if (finalHome == null || finalHome == previousPlayer || RunFlow.RunActive) return;
            Check(PlayerProgress.Statistics.Runs == 5 && PlayerProgress.Statistics.RecentRuns.Count == 5,
                "Five real finalized attempts fit the bounded history after all reloads.");
            Debug.Log("RUN_JOURNAL_RUNTIME_PASS checks=" + checks + " runs=5 actualCoins=3 actualBushes=1 deathHomeQuitRestart=True pauseFocus=True adminExcluded=True portrait=" + GameplayPreferences.IsPortrait);
            Finish(0);
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    private static void FreezePlayer(Player value)
    {
        value.GetComponent<Rigidbody2D>().simulated = false;
        Object.FindFirstObjectByType<Spawner>().enabled = false;
        Object.FindFirstObjectByType<CameraFollow>().enabled = false;
    }
    private static void Call(object target, string name, params object[] arguments) =>
        target.GetType().GetMethod(name, Private).Invoke(target, arguments);
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new InvalidOperationException("Run journal: " + message);
    }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (stack.Contains("UnityEditor.Search") && stack.Contains("IndexationOnStartup")) return;
        failure = message + "\n" + stack;
    }
    private static void Finish(int code)
    {
        Time.timeScale = 1;
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        EditorApplication.Exit(code);
    }
}
#endif
