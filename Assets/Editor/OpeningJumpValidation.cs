#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class OpeningJumpValidation
{
    private const string ActiveKey = "Doodlik.OpeningJumpValidation.Active";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly int[] Seeds = { 73, 1211, 2037 };
    private static int scenario, phase, earlyFailures, earlyPasses, controlledFailures, controlledPasses;
    private static double since, totalSince;
    private static Player player;
    private static Rigidbody2D body;
    private static GameMenuUI menu;
    private static Collider2D target;
    private static Keyboard keyboard;
    private static bool sawFall, firstBounce;
    private static float previousVy;
    private static string error;
    private static bool Inertia => scenario / 6 % 2 == 1;
    private static bool Early => scenario / 3 % 2 == 0;
    private static float Aspect => scenario / 12 == 0 ? 16f / 9f : 20f / 9f;
    private static int Seed => Seeds[scenario % 3];

    static OpeningJumpValidation()
    {
        if (!SessionState.GetBool(ActiveKey, false)) return;
        RegisterCallbacks();
    }

    private static void RegisterCallbacks()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        SceneManager.sceneLoaded -= ConfigureScene;
        SceneManager.sceneLoaded += ConfigureScene;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        since = totalSince = EditorApplication.timeSinceStartup;
    }

    public static void Run()
    {
        Prepare();
        RunResize();
        GameplayPreferences.SetMode(false, false, false);
        GameplayPreferences.SetDifficulty(GameDifficulty.Hard);
        SessionState.SetBool(ActiveKey, true);
        RegisterCallbacks();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void Prepare()
    {
        if (!Application.isBatchMode || Application.dataPath.Replace('\\', '/').IndexOf("/.utmp/MenuValidation-", StringComparison.OrdinalIgnoreCase) < 0)
            throw new InvalidOperationException("Opening probes require an isolated batch-mode MenuValidation copy.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikOpeningJumpSandbox";
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        AudioListener.pause = true;
        AudioListener.volume = 0f;
    }

    private static void ConfigureScene(Scene scene, LoadSceneMode mode)
    {
        Camera.main.aspect = Aspect;
        UnityEngine.Random.InitState(Seed);
        Spawner.ValidationPickupSeed = (uint)Seed;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        AudioListener.pause = true;
        AudioListener.volume = 0f;
        try
        {
            if (error != null) throw new InvalidOperationException(error);
            double elapsed = EditorApplication.timeSinceStartup - since;
            if (EditorApplication.timeSinceStartup - totalSince > 240) throw new TimeoutException("Opening scenarios timed out.");
            if (RunFlow.IsTransitioning || Time.frameCount < 20) return;
            if (phase == 0)
            {
                if (elapsed < 0.7) return;
                menu = Object.FindAnyObjectByType<GameMenuUI>();
                player = Object.FindAnyObjectByType<Player>();
                if (menu == null || player == null) throw new InvalidOperationException("Natural scene is missing menu/player scripts.");
                body = player.GetComponent<Rigidbody2D>();
                GameObject[] platforms = GameObject.FindGameObjectsWithTag("Platform");
                Array.Sort(platforms, (a, b) => a.transform.position.y.CompareTo(b.transform.position.y));
                if (platforms.Length < 2) throw new InvalidOperationException("Natural scene did not spawn the opening supports.");
                target = platforms[1].GetComponent<Collider2D>();
                if (keyboard == null) keyboard = InputSystem.AddDevice<Keyboard>("Opening probe keyboard");
                keyboard.MakeCurrent();
                SetInput(Early ? DirectionToTarget() : 0);
                sawFall = firstBounce = false;
                previousVy = body.linearVelocity.y;
                menu.StartRunFromHome();
                since = EditorApplication.timeSinceStartup;
                phase = 1;
                Debug.Log($"OPENING_CASE_BEGIN seed={Seed} inertia={Inertia} early={Early} aspect={Camera.main.aspect:F4} target={target.transform.position}");
                return;
            }
            if (player == null) throw new InvalidOperationException("Opening player disappeared.");
            float vy = body.linearVelocity.y;
            sawFall |= vy < -0.2f;
            bool risingAfterFall = sawFall && previousVy < 0f && vy > player.NormalJumpForce * 0.5f;
            previousVy = vy;
            bool reachedTarget = risingAfterFall && Mathf.Abs(player.GetComponent<Collider2D>().bounds.min.y - target.bounds.max.y) < 0.25f;
            if (risingAfterFall && !firstBounce)
            {
                firstBounce = true;
                Debug.Log($"OPENING_FIRST_BOUNCE seed={Seed} inertia={Inertia} early={Early} x={body.position.x:F4} target={target.transform.position}");
            }
            if (reachedTarget || player.IsDead || elapsed > 9)
            {
                bool success = reachedTarget;
                if (Early) { if (success) earlyPasses++; else earlyFailures++; }
                else { if (success) controlledPasses++; else controlledFailures++; }
                Debug.Log($"OPENING_CASE_RESULT seed={Seed} inertia={Inertia} early={Early} aspect={Aspect:F4} firstBounce={firstBounce} targetReached={success} died={player.IsDead} elapsed={elapsed:F3} x={body.position.x:F4} y={body.position.y:F4}");
                SetInput(0);
                scenario++;
                if (scenario >= 24) { Finish(null); return; }
                GameplayPreferences.SetMode(false, Inertia, false);
                GameplayPreferences.SetDifficulty(GameDifficulty.Hard);
                phase = 0;
                since = EditorApplication.timeSinceStartup;
                menu.ReturnToHome();
                return;
            }
            SetInput(Early || firstBounce ? DirectionToTarget() : 0);
        }
        catch (Exception exception) { Finish(exception.ToString()); }
    }

    private static int DirectionToTarget()
    {
        if (target == null || body == null) return 0;
        float delta = TrapGeometry.WrappedDelta(body.position.x, target.bounds.center.x,
            Camera.main.orthographicSize * Camera.main.aspect * 2f);
        if (Mathf.Abs(delta) < 0.08f && Mathf.Abs(body.linearVelocity.x) < 0.6f) return 0;
        if (Inertia && Mathf.Sign(body.linearVelocity.x) == Mathf.Sign(delta))
        {
            float deceleration = player.turnAcceleration * (body.linearVelocity.y < 0f ? player.fallingControl : player.risingControl);
            float stoppingDistance = body.linearVelocity.x * body.linearVelocity.x / (2f * deceleration);
            if (stoppingDistance > Mathf.Abs(delta) - 0.03f) return -Math.Sign(body.linearVelocity.x);
        }
        return Math.Sign(delta);
    }

    private static void SetInput(int direction)
    {
        if (keyboard == null) return;
        KeyboardState state = direction == 0 ? new KeyboardState() :
            new KeyboardState(direction > 0 ? Key.RightArrow : Key.LeftArrow);
        keyboard.MakeCurrent();
        InputState.Change(keyboard, state);
    }

    public static void RunResize()
    {
        Prepare();
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run resize proof before Play Mode.");
        GameplayPreferences.SetMode(false, false, false);
        GameplayPreferences.SetDifficulty(GameDifficulty.Hard);
        foreach (int seed in Seeds)
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            Player source = Object.FindAnyObjectByType<Player>();
            Spawner spawner = Object.FindAnyObjectByType<Spawner>();
            Camera camera = Camera.main;
            camera.aspect = 9f / 20f;
            spawner.startPlatformCount = 1;
            spawner.bushSpawnChance = 0f;
            spawner.traps.enabled = false;
            UnityEngine.Random.InitState(seed);
            typeof(Spawner).GetMethod("Start", Private).Invoke(spawner, null);
            Physics2D.SyncTransforms();
            GameObject[] platforms = GameObject.FindGameObjectsWithTag("Platform");
            Array.Sort(platforms, (a, b) => a.transform.position.y.CompareTo(b.transform.position.y));
            Vector2 oldFrom = platforms[0].transform.position, oldTo = platforms[1].transform.position;
            camera.aspect = 20f / 9f;
            typeof(Spawner).GetMethod("UpdateHorizontalBounds", Private).Invoke(spawner, new object[] { true });
            Physics2D.SyncTransforms();
            BoxCollider2D from = platforms[0].GetComponent<BoxCollider2D>(), to = platforms[1].GetComponent<BoxCollider2D>();
            BoxCollider2D feet = source.GetComponent<BoxCollider2D>();
            float centerDistance = Mathf.Abs(TrapGeometry.WrappedDelta(from.bounds.center.x, to.bounds.center.x,
                camera.orthographicSize * camera.aspect * 2f));
            float required = centerDistance - from.bounds.extents.x - to.bounds.extents.x - feet.bounds.size.x;
            float gap = to.bounds.max.y - from.bounds.max.y;
            float measured = MeasurePrecise(source, gap);
            Debug.Log($"OPENING_RESIZE_COUNTEREXAMPLE seed={seed} oldFrom={oldFrom} oldTo={oldTo} newTo={platforms[1].transform.position} gap={gap:F5} minimumEdgeDistance={required:F5} actualMaximumPrecise={measured:F5} impossible={required > measured}");
        }
    }

    private static float MeasurePrecise(Player source, float targetHeight)
    {
        SimulationMode2D previousMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        GameObject probe = new GameObject("Opening actual controller probe");
        try
        {
            SceneManager.MoveGameObjectToScene(probe, scene);
            Rigidbody2D body = probe.AddComponent<Rigidbody2D>();
            body.gravityScale = source.risingGravityScale;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            probe.AddComponent<CircleCollider2D>().radius = 0.1f;
            Player player = probe.AddComponent<Player>();
            EditorUtility.CopySerialized(source, player);
            player.enabled = false;
            typeof(Player).GetField("rb", Private).SetValue(player, body);
            typeof(Player).GetField("horizontalInput", Private).SetValue(player, 1f);
            body.linearVelocity = new Vector2(0f, source.NormalJumpForce);
            body.position = new Vector2(10000f, 0f);
            Physics2D.SyncTransforms();
            MethodInfo horizontal = typeof(Player).GetMethod("ApplyHorizontalMovement", Private);
            MethodInfo gravity = typeof(Player).GetMethod("ApplyGravityCurve", Private);
            PhysicsScene2D physics = scene.GetPhysicsScene2D();
            bool above = false;
            for (int tick = 0; tick < 300; tick++)
            {
                Vector2 previous = body.position;
                horizontal.Invoke(player, null);
                gravity.Invoke(player, null);
                if (!physics.Simulate(Time.fixedDeltaTime)) throw new InvalidOperationException("Opening physics scene did not advance.");
                Vector2 position = body.position;
                above |= position.y >= targetHeight;
                if (above && body.linearVelocity.y < 0f && position.y <= targetHeight)
                    return Mathf.Lerp(previous.x, position.x, Mathf.InverseLerp(previous.y, position.y, targetHeight)) - 10000f;
                if (body.linearVelocity.y < 0f && position.y < 0f) return 0f;
            }
            throw new TimeoutException("Opening Box2D jump probe did not finish.");
        }
        finally { Object.DestroyImmediate(probe); EditorSceneManager.CloseScene(scene, true); Physics2D.simulationMode = previousMode; }
    }

    private static void OnLog(string condition, string trace, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            if (!condition.Contains("ArgumentOutOfRangeException") || !trace.Contains("UnityEditor.Search")) error = condition + "\n" + trace;
    }

    private static void Finish(string failure)
    {
        SessionState.SetBool(ActiveKey, false);
        EditorApplication.update -= Tick;
        SceneManager.sceneLoaded -= ConfigureScene;
        Application.logMessageReceived -= OnLog;
        SetInput(0);
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        Spawner.ValidationPickupSeed = null;
        if (failure == null)
            Debug.Log($"OPENING_VALIDATION_COMPLETE earlyFailed={earlyFailures} earlyPassed={earlyPasses} controlledFailed={controlledFailures} controlledPassed={controlledPasses}");
        else Debug.LogError("OPENING_VALIDATION_FAILED " + failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
