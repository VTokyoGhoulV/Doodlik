#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ChallengeRuntimeValidation
{
    private const string Key = "Doodlik.ChallengeRuntimeValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int phase, checks;
    private static double since;
    private static string failure;
    private static Player player;
    private static int eventFrame, eventBushes, eventWraps;
    private static float eventTop, eventFixedTime;
    private static BoxCollider2D eventSource, eventDestination;
    private static readonly List<GameObject> probes = new List<GameObject>();

    static ChallengeRuntimeValidation()
    {
        if (!SessionState.GetBool(Key, false)) return;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    public static void Run() => RunViewport(false);
    public static void RunPortrait() => RunViewport(true);

    private static void RunViewport(bool portrait)
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/"))
            throw new InvalidOperationException("Challenge runtime validation requires an isolated batch project.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikChallengeRuntime_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        PlayerProgress.Reload();
        SessionState.SetBool(Key + ".portrait", portrait);
        GameplayPreferences.SetMode(portrait, false, false);
        GameplayPreferences.SetDifficulty(GameDifficulty.Normal);
        ChallengeProgressValidation.Validate();
        CosmeticsProgressValidation.Validate();
        CosmeticMigrationValidation.Validate();
        Debug.Log("CHALLENGE_CORE_AND_MIGRATION_PASS before runtime integration");
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
            portrait ? 1280u : 720u, "Doodlik quest event validation");
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (failure != null) throw new Exception(failure);
            if (EditorApplication.timeSinceStartup - since > 60) throw new TimeoutException("Challenge runtime phase " + phase);
            if (Time.frameCount < 20 || RunFlow.IsTransitioning) return;
            Check(EditorUtility.audioMasterMute && AudioListener.volume == 0f, "All test audio is master-muted.");
            if (phase == 0)
            {
                bool portrait = SessionState.GetBool(Key + ".portrait", false);
                Check(Screen.width == (portrait ? 720 : 1280) && Screen.height == (portrait ? 1280 : 720) &&
                    GameplayPreferences.IsPortrait == portrait, "Real GameView and actual controller model format match this test.");
                Debug.Log("CHALLENGE_RUNTIME_VIEWPORT " + Screen.width + "x" + Screen.height + " portrait=" + portrait);
                player = Object.FindFirstObjectByType<Player>();
                Check(player != null && player.Challenges != null, "Player naturally attaches the challenge bridge.");
                Check(player.Challenges.Session == null, "Home does not start or advance a run session.");
                Check(PlayerProgress.SelectChallenge("run_coin10"), "Select a task before the run.");
                Object.FindFirstObjectByType<GameMenuUI>().StartRunFromHome();
                phase = 1;
                return;
            }
            if (!RunFlow.IsPlaying || GameMenuUI.BlocksGameplayInput || player.Challenges.Session == null) return;
            Rigidbody2D body = player.GetComponent<Rigidbody2D>();
            body.simulated = false;
            Object.FindFirstObjectByType<Spawner>().enabled = false;
            Object.FindFirstObjectByType<CameraFollow>().enabled = false;
            if (phase == 1)
            {
                RunAssertions(body);
                phase = 2;
                return;
            }
            RunEventAssertions(body);
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    private static void RunAssertions(Rigidbody2D body)
    {
        RunChallenges run = player.Challenges;
        Check(run.Session.SelectedId == "run_coin10", "Play captures the currently selected task.");
        Check(!PlayerProgress.SelectChallenge("run_wide5"), "Task cannot change during a run.");
        foreach (bool inertia in new[] { false, true })
        foreach (float aspect in new[] { 9f / 20f, 16f / 9f, 20f / 9f, 32f / 9f })
        {
            float scale = aspect < 1f ? 0.64f : 1f;
            JumpReachability.Result flight = JumpReachability.SimulatePrepared(player, 2f, inertia, scale);
            float threshold = RunChallenges.LongJumpThreshold(player, 2f, 10f * aspect, inertia, scale);
            Check(flight.CanLand && threshold <= flight.HorizontalDistance * 0.75f + 0.0001f,
                "Long-jump task metric stays below physical reach, including ultrawide and portrait.");
        }
        RunPowerups powers = player.GetComponent<RunPowerups>();
        RunPickup.RemoveUnclaimedCoins();
        int before = PlayerProgress.Coins;
        for (int i = 0; i < 10; i++)
        {
            Check(RunPickup.SpawnAirCoin(body.position, Camera.main, i) != null, "Actual coin artwork spawns.");
            RunPickup.CollectAlong(body.position, body.position, Vector2.one * 0.1f,
                Camera.main.orthographicSize * Camera.main.aspect * 2f, powers, false);
        }
        Check(run.Session.Stats.Coins == 10, "Real pickup callback reaches challenge counters.");
        Check(PlayerProgress.Coins == before + 40, "Ten actual coins and one 30-coin challenge reward are additive.");
        Check(PlayerProgress.IsChallengeCompleted("run_coin10") && run.SelectedCompleted, "Task is completed in save and captured session.");
        Check(PlayerProgress.SelectedChallengeId.Length == 0, "Completed task is cleared for the next run.");
        Check(run.CompletedThisRun.Count == 1, "Completion feedback captures the real grant once.");
        run.RecordCoin(false);
        Check(PlayerProgress.Coins == before + 40, "Repeated events cannot award the task twice.");

        Vector2 original = body.position;
        Collider2D feet = player.GetComponent<Collider2D>();
        float top = feet.bounds.min.y;
        BoxCollider2D initial = Surface(feet.bounds.center.x, top, 2f);
        run.RecordLanding(initial);
        int start = run.Session.Stats.CenterLandings;
        for (int i = 0; i < 50; i++)
        {
            body.position += Vector2.up * 0.5f;
            Physics2D.SyncTransforms();
            BoxCollider2D next = Surface(feet.bounds.center.x, feet.bounds.min.y, 0.65f);
            run.RecordLanding(next);
            run.RecordLanding(next);
        }
        Check(run.Session.Stats.CenterLandings == start + 50, "New higher centered surfaces count exactly once each.");
        Check(PlayerProgress.IsChallengeCompleted("mastery_center50") && PlayerProgress.Owns("accessory_target"), "Real geometry grants the exclusive accuracy reward.");
        body.position = original;
        Physics2D.SyncTransforms();
        foreach (GameObject probe in probes) Object.DestroyImmediate(probe);
        probes.Clear();

        powers.Collect(RunPowerups.Kind.Magnet);
        Check(run.Session.Stats.UsedMagnet && run.Session.Stats.Powerups == 1, "Powerup pickup hook marks magnet use.");
        for (int i = 0; i < 3; i++)
        {
            powers.Collect(RunPowerups.Kind.Shield);
            body.position = new Vector2(0f, Camera.main.transform.position.y - Camera.main.orthographicSize - 1.2f);
            Physics2D.SyncTransforms();
            Check(powers.TryProtect(true, body, Camera.main, feet), "Shield uses a real visible support for falling rescue.");
        }
        Check(run.Session.Stats.FallingRescues == 3 && run.Session.Stats.ShieldUses == 3, "Only actual consumed falling shields count as rescues.");
        Check(PlayerProgress.IsChallengeCompleted("mastery_rescue3") && PlayerProgress.Owns("trail_ninelives"), "Three actual rescue hooks unlock the paw trail.");

        float runStart = (float)typeof(Player).GetField("startHeight", Private).GetValue(player);
        body.position = new Vector2(0f, runStart + 700.25f / player.pointsPerUnit);
        Debug.Log($"CHALLENGE_HEIGHT_PROBE rbY={body.position.y} renderY={player.transform.position.y} start={runStart} rate={player.pointsPerUnit}");
        // With interpolation and simulated=false the rendered Transform can
        // retain the previous frame. Match the game's actual teleport paths:
        // set both physical and presentation positions for this fixture.
        player.transform.position = new Vector3(body.position.x, body.position.y, player.transform.position.z);
        Physics2D.SyncTransforms();
        typeof(Player).GetMethod("UpdateHeightScore", Private).Invoke(player, null);
        Check(player.CurrentHeight == 700 && run.Session.Stats.Height == 700,
            $"Actual height scoring forwards raw height into challenge progress: player={player.CurrentHeight} session={run.Session.Stats.Height}.");
        Check(!PlayerProgress.IsChallengeCompleted("run_height1000"),
            "A different unselected task is not granted by height updates.");

    }

    private static void FinalAssertions()
    {
        RunChallenges run = player.Challenges;
        string saved = PlayerPrefs.GetString(PlayerProgress.SaveKey);
        PlayerProgress.BeginAdminSandbox();
        run.RecordHeight(WorldZones.GoalScore);
        Check(!run.Session.IsEligible, "Admin immediately invalidates challenge session.");
        PlayerProgress.EndAdminSandbox();
        int previousHeight = run.Session.Stats.Height;
        run.RecordHeight(WorldZones.GoalScore);
        Check(run.Session.Stats.Height == previousHeight, "Disabling admin never restores eligibility.");
        Check(PlayerPrefs.GetString(PlayerProgress.SaveKey) == saved, "Sandbox cannot mutate real progress, coins or challenge save.");
        int coins = run.Session.Stats.Coins;
        run.EndRun(); run.RecordCoin(false);
        Check(run.Session.Stats.Coins == coins, "Ended attempt cannot keep progressing.");
    }

    private static void RunEventAssertions(Rigidbody2D body)
    {
        RunChallenges run = player.Challenges;
        Collider2D feet = player.GetComponent<Collider2D>();
        if (phase == 2)
        {
            eventBushes = run.Session.Stats.Bushes;
            body.linearVelocity = Vector2.down * 2f;
            eventTop = feet.bounds.min.y;
            eventSource = Surface(feet.bounds.center.x, eventTop, 2f);
            // Contact order A: support collision, then the bush trigger.
            run.RecordLanding(eventSource);
            TriggerBush(feet);
            Check(run.Session.Stats.Bushes == eventBushes + 1 &&
                body.linearVelocity.y >= player.bushJumpForce, "Actual bush trigger forwards the accepted boost exactly once.");
            eventFrame = Time.frameCount;
            eventFixedTime = Time.fixedTime;
            phase = 3;
            return;
        }
        if (Time.frameCount <= eventFrame || Time.fixedTime <= eventFixedTime) return;
        if (phase == 3)
        {
            Check(run.Session.Stats.BushChain == 1, "LateUpdate does not erase collision-before-bush chain.");
            MoveFeet(body, feet, 0f, eventTop + 1f);
            eventTop = feet.bounds.min.y;
            eventSource = Surface(0f, eventTop, 2f);
            BoxCollider2D skipA = Surface(0f, eventTop + 1f, 2f);
            BoxCollider2D skipB = Surface(0f, eventTop + 2f, 2f);
            skipA.tag = skipB.tag = "Platform";
            eventDestination = Surface(0f, eventTop + 4f, 2f);
            eventDestination.tag = "Platform";
            // Contact order B: trigger first. The support collision may arrive
            // later, even if the actual controller already has upward speed.
            TriggerBush(feet);
            run.RecordLanding(eventSource);
            Check(run.Session.Stats.Bushes == eventBushes + 2 && run.Session.Stats.BestBushChain >= 2,
                "Two actual bushes chain across frames with both callback orders.");
            eventFrame = Time.frameCount;
            eventFixedTime = Time.fixedTime;
            phase = 4;
            return;
        }
        if (phase == 4)
        {
            Check(run.Session.Stats.BushChain == 2, "LateUpdate does not erase bush-before-collision chain.");
            RunPowerups powers = player.GetComponent<RunPowerups>();
            powers.Collect(RunPowerups.Kind.Slow);
            Check(run.Session.Stats.PowerupKinds == 3, "Actual shield, magnet and slow pickups remain distinct.");
            int fallingBefore = run.Session.Stats.FallingCoinSaves;
            int apexBefore = run.Session.Stats.ApexCoins;
            body.linearVelocity = Vector2.up * 0.2f;
            run.RecordCoin(false);
            Check(run.Session.Stats.ApexCoins == apexBefore + 1, "Coin hook uses actual vertical speed near the apex.");
            body.linearVelocity = Vector2.zero;
            run.BeginContactCollection(true);
            try { run.RecordCoin(false); }
            finally { run.EndContactCollection(); }
            Check(run.Session.Stats.ApexCoins == apexBefore + 1,
                "Solver-zeroed velocity at a landing contact is not a jump apex.");
            body.linearVelocity = Vector2.down * 2f;
            run.RecordCoin(false);
            MoveFeet(body, feet, 0.1f, eventDestination.bounds.max.y);
            eventDestination.gameObject.AddComponent<MovingPlatform>();
            run.RecordLanding(eventDestination);
            Check(run.Session.Stats.BushSkippedRows >= 2, "Snapshot preserves two skipped support rows until safe landing.");
            Check(run.Session.Stats.FallingCoinSaves == fallingBefore + 1,
                "A falling coin counts only after a new higher safe landing.");
            Check(run.Session.Stats.SlowMovingLandings > 0, "Actual slow timer and moving support geometry reach challenge rules.");
            int edges = run.Session.Stats.EdgeLandings;
            MoveFeet(body, feet, 0.75f, eventDestination.bounds.max.y + 1f);
            BoxCollider2D edgeSurface = Surface(0f, feet.bounds.min.y, 2f);
            run.RecordLanding(edgeSurface);
            Check(run.Session.Stats.EdgeLandings == edges + 1, "Visible outer platform area counts as an edge landing.");
            int spaceCoins = run.Session.Stats.SpaceCoins;
            MoveFeet(body, feet, 0f, WorldZones.HeightAtScore(WorldZones.GoalScore) + 1f);
            run.RecordCoin(false);
            Check(run.Session.Stats.SpaceCoins == spaceCoins + 1, "Space coins use actual cat height at pickup.");
            MoveFeet(body, feet, 0f, WorldZones.HeightAtScore(WorldZones.GoalScore) - 2f);
            run.RecordCoin(false);
            Check(run.Session.Stats.SpaceCoins == spaceCoins + 1, "Falling below space does not count coins by an old maximum height.");
            float width = Camera.main.orthographicSize * Camera.main.aspect * 2f;
            eventWraps = run.Session.Stats.Wraps;
            body.position = new Vector2(Camera.main.transform.position.x + width * 0.5f + 0.2f, body.position.y);
            player.transform.position = body.position;
            typeof(Player).GetMethod("ScreenWrap", Private).Invoke(player, null);
            Check(run.Session.Stats.Wraps == eventWraps + 1 && body.position.x < Camera.main.transform.position.x,
                "Actual controller wrap forwards only a real boundary crossing.");
            typeof(Player).GetMethod("ScreenWrap", Private).Invoke(player, null);
            Check(run.Session.Stats.Wraps == eventWraps + 1, "Staying inside the viewport cannot farm screen wraps.");
            eventFrame = Time.frameCount;
            eventFixedTime = Time.fixedTime;
            phase = 5;
            return;
        }
        Check(run.Session.Stats.BushChain == 0, "A normal safe landing ends the bush chain after physics callbacks.");
        FinalAssertions();
        Debug.Log("CHALLENGE_RUNTIME_PASS checks=" + checks +
            " actualPickups=10 realShieldRescues=3 landingGeometry=54 duplicateFarmBlocked=True actualBushTriggers=2 bothCallbackOrders=True");
        Finish(0);
    }

    private static void TriggerBush(Collider2D feet)
    {
        GameObject root = new GameObject("Challenge actual bush trigger probe");
        probes.Add(root);
        Bush bush = root.AddComponent<Bush>();
        int before = player.Challenges.Session.Stats.Bushes;
        MethodInfo trigger = typeof(Bush).GetMethod("OnTriggerEnter2D", Private);
        trigger.Invoke(bush, new object[] { feet });
        trigger.Invoke(bush, new object[] { feet });
        Check(player.Challenges.Session.Stats.Bushes == before + 1,
            "Repeated callbacks on the same actual Bush cannot farm activations or chain progress.");
    }

    private static void MoveFeet(Rigidbody2D body, Collider2D feet, float x, float top)
    {
        float offset = body.position.y - feet.bounds.min.y;
        body.position = new Vector2(x, top + offset);
        player.transform.position = body.position;
        Physics2D.SyncTransforms();
    }

    private static BoxCollider2D Surface(float x, float top, float width)
    {
        GameObject root = new GameObject("Challenge landing probe");
        root.transform.position = new Vector3(x, top - 0.1f, 0f);
        BoxCollider2D surface = root.AddComponent<BoxCollider2D>();
        surface.size = new Vector2(width, 0.2f);
        Physics2D.SyncTransforms();
        probes.Add(root);
        return surface;
    }

    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new InvalidOperationException("Challenge runtime: " + message);
    }
    private static void OnLog(string message, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (trace.Contains("UnityEditor.Search") && trace.Contains("IndexationOnStartup")) return;
        failure = message + "\n" + trace;
    }
    private static void Finish(int code)
    {
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        foreach (GameObject probe in probes) if (probe != null) Object.DestroyImmediate(probe);
        probes.Clear();
        EditorApplication.Exit(code);
    }
}
#endif
