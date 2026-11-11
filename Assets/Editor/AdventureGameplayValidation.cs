#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class AdventureGameplayValidation
{
    private const string Key = "Doodlik.AdventureGameplayValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<GameObject> fixtures = new List<GameObject>();
    private static int phase, index, checks, steps, frame;
    private static double since;
    private static string failure;
    private static Player player;
    private static RunPowerups powers;
    private static PaperRocketFlight flight;
    private static Rigidbody2D body;
    private static Collider2D feet;
    private static Camera camera;
    private static float simulated, cruiseStartY;
    private static bool paused, sawProtectedHit;
    private static SimulationMode2D previousSimulation;

    static AdventureGameplayValidation()
    {
        if (!SessionState.GetBool(Key, false)) return;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }
    public static void Run()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/"))
            throw new InvalidOperationException("Adventure physics requires an isolated batch Editor.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikAdventure_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        PlayerProgress.Reload();
        AdventurePickupValidation.ValidateSchedule();
        RunStatisticsValidation.Validate();
        ChallengeProgressValidation.Validate();
        SessionState.SetBool(Key, true);
        phase = index = checks = 0;
        since = EditorApplication.timeSinceStartup;
        ConfigureCase();
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static bool Portrait => !GameplayPreferences.IsDesktopBuild && index >= 6;
    private static int Cases => GameplayPreferences.IsDesktopBuild ? 6 : 12;
    private static void ConfigureCase()
    {
        bool portrait = Portrait;
        GameplayPreferences.SetMode(portrait, index / 3 % 2 == 1, false);
        GameplayPreferences.SetDifficulty((GameDifficulty)(index % 3));
        PlayModeWindow.SetCustomRenderingResolution(portrait ? 720u : 1280u,
            portrait ? 1280u : 720u, "Doodlik adventure physics");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Viewport() { if (SessionState.GetBool(Key, false)) ConfigureCase(); }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (failure != null) throw new Exception(failure);
            if (EditorApplication.timeSinceStartup - since > 180) throw new TimeoutException("Adventure phase " + phase + " case " + index);
            if (Time.frameCount < 20 || RunFlow.IsTransitioning) return;
            Check(EditorUtility.audioMasterMute && AudioListener.volume == 0f, "Whole test Editor stays muted.");
            if (phase == 0)
            {
                player = Object.FindFirstObjectByType<Player>();
                if (player == null || player.Challenges == null) return;
                if (!RunFlow.RunActive) { Object.FindFirstObjectByType<GameMenuUI>().StartRunFromHome(); phase = 1; return; }
                phase = 1;
            }
            if (phase == 1)
            {
                if (!RunFlow.IsPlaying || GameMenuUI.BlocksGameplayInput || player.Challenges.Session == null) return;
                if (index == 0) AdventurePickupValidation.ValidatePickups();
                SetFixture();
                phase = 2;
                return;
            }
            if (phase == 2)
            {
                if (paused)
                {
                    Check(powers.IsRocketActive && Mathf.Abs(powers.RocketTimeRemaining - simulated) < 0.00001f,
                        "A paused rocket neither expires nor moves.");
                    Time.timeScale = 1;
                    paused = false;
                }
                for (int repeat = 0; repeat < 6; repeat++)
                {
                    if (!powers.IsRocketActive) { CompleteCase(); return; }
                    Vector2 before = body.position;
                    if (steps == 35)
                    {
                        GameObject crossing = new GameObject("Guaranteed protected contact probe");
                        crossing.transform.position = player.GetComponent<SpriteRenderer>().bounds.center;
                        crossing.AddComponent<DoodlikHazard>().InitializePatch(camera,
                            Vector2.one * (0.3f * GameplayPreferences.ModelScale), Vector3.one * GameplayPreferences.ModelScale);
                        fixtures.Add(crossing);
                    }
                    typeof(Player).GetField("horizontalInput", Private).SetValue(player, index % 2 == 0 ? 1f : -1f);
                    Call("ScreenWrap");
                    Call("ApplyHorizontalMovement");
                    powers.ApplyRocketMotion(0.02f);
                    Physics2D.Simulate(0.02f);
                    Physics2D.SyncTransforms();
                    camera.transform.position = new Vector3(camera.transform.position.x,
                        Mathf.Max(camera.transform.position.y, body.position.y - 1.4f), camera.transform.position.z);
                    if (DoodlikHazard.FindHit(before, body.position, Vector2.one * 0.1f,
                        camera.orthographicSize * camera.aspect * 2f) != null) sawProtectedHit = true;
                    Call("LateUpdate");
                    Check(!player.IsDead && powers.HasShield && player.Challenges.Session.Stats.ShieldUses == 0,
                        "Rocket crossings cannot kill or consume a separately carried shield.");
                    steps++;
                    Check(steps < 400, "Rocket has a finite flight and exit, never immortal hover.");
                    if (steps == 20)
                    {
                        Time.timeScale = 0;
                        simulated = powers.RocketTimeRemaining;
                        powers.ApplyRocketMotion(0.2f);
                        paused = true;
                        break;
                    }
                }
                return;
            }
            if (Time.frameCount <= frame + 1) return;
            player = Object.FindFirstObjectByType<Player>();
            phase = 0;
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    private static void SetFixture()
    {
        Check(GameplayPreferences.IsPortrait == Portrait && Screen.width == (Portrait ? 720 : 1280), "Real controller and viewport match the test case.");
        previousSimulation = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
        Object.FindFirstObjectByType<Spawner>().enabled = false;
        Object.FindFirstObjectByType<CameraFollow>().enabled = false;
        player.enabled = false;
        body = player.GetComponent<Rigidbody2D>(); feet = player.GetComponent<Collider2D>();
        body.interpolation = RigidbodyInterpolation2D.None;
        camera = Camera.main;
        camera.transform.position = new Vector3(0f, 1000f, -10f);
        body.position = new Vector2(0, 1001f); player.transform.position = body.position;
        body.linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
        cruiseStartY = feet.bounds.min.y;
        float scale = GameplayPreferences.ModelScale;
        for (int i = 0; i < 7; i++)
        {
            GameObject pad = new GameObject("Rocket real one-way support " + i);
            pad.tag = "Platform";
            pad.transform.position = new Vector3(i == 0 ? 0f : (i % 3 - 1) * 3f * scale,
                cruiseStartY - 0.1f + i * 2.5f, 0f);
            BoxCollider2D box = pad.AddComponent<BoxCollider2D>(); box.size = new Vector2(2.6f * scale, 0.2f);
            PlatformEffector2D oneWay = pad.AddComponent<PlatformEffector2D>(); oneWay.useOneWay = true;
            box.usedByEffector = true; fixtures.Add(pad);
        }
        GameObject trap = new GameObject("Rocket protection crossing probe");
        trap.transform.position = new Vector2(0f, cruiseStartY + 4f);
        DoodlikHazard hazard = trap.AddComponent<DoodlikHazard>();
        hazard.InitializePatch(camera, Vector2.one * (0.3f * scale), Vector3.one * scale);
        fixtures.Add(trap);
        Physics2D.SyncTransforms();
        powers = player.GetComponent<RunPowerups>();
        if (index == 0) ValidatePartialSupport();
        int previousPowers = player.Challenges.Session.Stats.Powerups;
        powers.Collect(RunPowerups.Kind.Shield);
        powers.Collect(RunPowerups.Kind.Rocket);
        flight = player.GetComponent<PaperRocketFlight>();
        Check(powers.IsRocketFlying && flight != null && powers.RocketDuration == 2.2f, "Real rocket collection starts a bounded controlled flight.");
        float remaining = powers.RocketTimeRemaining;
        powers.Collect(RunPowerups.Kind.Rocket);
        Check(Mathf.Approximately(remaining, powers.RocketTimeRemaining), "Another rocket cannot stack or extend the current flight.");
        // A rocket is a powerup, but not a fourth required kind for the old task.
        Check(player.Challenges.Session.Stats.PowerupKinds == 1 && player.Challenges.Session.Stats.Powerups == previousPowers + 3,
            "Rocket fails no-power restrictions without changing the existing three-kind collection task.");
        Call("LateUpdate");
        steps = 0; paused = sawProtectedHit = false;
    }

    private static void ValidatePartialSupport()
    {
        var supports = new List<GameObject>();
        foreach (GameObject fixture in fixtures)
            if (fixture.CompareTag("Platform")) { supports.Add(fixture); fixture.SetActive(false); }
        powers.Collect(RunPowerups.Kind.Rocket);
        Check(!powers.IsRocketActive && player.Challenges.Session.Stats.Powerups == 1,
            "An empty world cannot launch an immortal rocket, but the pickup still counts for no-powers rules.");
        Check(player.Challenges.Session.Stats.RocketLaunches == 0 && player.Challenges.Session.Stats.RocketLandings == 0,
            "A refused rocket launch cannot complete a flight quest.");
        GameObject wide = new GameObject("Wide support with a trapped centre");
        wide.tag = "Platform";
        wide.transform.position = new Vector2(0f, feet.bounds.min.y - 1.1f);
        BoxCollider2D box = wide.AddComponent<BoxCollider2D>(); box.size = new Vector2(8f * GameplayPreferences.ModelScale, 0.2f);
        Physics2D.SyncTransforms();
        GameObject patch = new GameObject("Visible central thorn patch");
        patch.transform.SetParent(wide.transform, true);
        patch.transform.position = new Vector2(0f, box.bounds.max.y + 0.08f);
        patch.AddComponent<DoodlikHazard>().InitializePatch(camera,
            Vector2.one * 0.2f * GameplayPreferences.ModelScale, Vector3.one * GameplayPreferences.ModelScale);
        Physics2D.SyncTransforms();
        try
        {
            PaperRocketFlight probe = PaperRocketFlight.Attach(powers);
            object[] arguments = { false, 0f };
            Collider2D chosen = (Collider2D)typeof(PaperRocketFlight).GetMethod("FindSupport", Private).Invoke(probe, arguments);
            Check(chosen == box && Mathf.Abs((float)arguments[1]) > 1.2f * GameplayPreferences.ModelScale,
                "Exit planner uses the clear edge of a partially trapped platform instead of rejecting the entire support.");
        }
        finally
        {
            Object.DestroyImmediate(wide);
            foreach (GameObject support in supports) support.SetActive(true);
            Physics2D.SyncTransforms();
        }
    }

    private static void CompleteCase()
    {
        Check(!player.IsDead && flight.EmergencyExits == 0, "A real support contact, not an emergency launch, ends the rocket.");
        Check(sawProtectedHit, "Each flight actually crossed a lethal shape while protected.");
        Check(body.linearVelocity.y > 0f && powers.ProtectionTimeRemaining > 0f,
            "Ordinary bounce and short exit protection hand control back safely.");
        Check(feet.bounds.min.y - cruiseStartY < 18f, "Rare rocket gains a few platforms, not an entire biome.");
        Check(player.Challenges.Session.Stats.CenterLandings == 0 && player.Challenges.Session.Stats.WideJumps == 0,
            "Automatic rocket handoff cannot farm precision or long-jump tasks.");
        Check(player.Challenges.Session.Stats.RocketLaunches == 1 && player.Challenges.Session.Stats.RocketLandings == 1,
            "Only the accepted launch and its actual safe support handoff count; stacked pickups do not create flights.");
        Check(player.Challenges.Session.Stats.RocketWrappedLandings <= 1 &&
            player.Challenges.Session.Stats.BestPostRocketClimb == 0,
            "Exit movement cannot duplicate wrapped landings or count assisted height as a post-flight climb.");
        Debug.Log("ADVENTURE_ROCKET_CASE_PASS case=" + index + " portrait=" + Portrait + " inertia=" + GameplayPreferences.UseInertia +
            " difficulty=" + player.RunDifficulty + " steps=" + steps + " protectedCrossing=" + sawProtectedHit);
        foreach (GameObject fixture in fixtures) if (fixture != null) Object.DestroyImmediate(fixture);
        fixtures.Clear();
        Physics2D.simulationMode = previousSimulation;
        index++;
        if (index >= Cases)
        {
            Debug.Log("ADVENTURE_GAMEPLAY_PASS checks=" + checks + " rocketCases=" + index + " realBox2D=True safeExit=True pause=True weightedPickups=True");
            Finish(0); return;
        }
        ConfigureCase();
        RunFlow.RunActive = true;
        SceneManager.LoadScene("SampleScene");
        frame = Time.frameCount;
        phase = 3;
    }
    private static void Call(string method) => typeof(Player).GetMethod(method, Private).Invoke(player, null);
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new InvalidOperationException("Adventure: " + message);
    }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (stack.Contains("UnityEditor.Search") && stack.Contains("IndexationOnStartup")) return;
        failure = message + "\n" + stack;
    }
    private static void Finish(int code)
    {
        Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
        Time.timeScale = 1;
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        EditorApplication.Exit(code);
    }
}
#endif
