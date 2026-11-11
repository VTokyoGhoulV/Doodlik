#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Uses actual GameView surfaces; Screen.SetResolution alone does not resize them.
[InitializeOnLoad]
public static class OpeningViewportGuardValidation
{
    private const string ActiveKey = "Doodlik.OpeningViewportGuard.Active";
    private static int scenario, phase, checks;
    private static Player previousPlayer;
    private static double startedAt, since;
    private static GameMenuUI menu;
    private static Player player;
    private static Rigidbody2D body;
    private static Collider2D target;
    private static Keyboard keyboard;
    private static float previousVy;
    private static bool sawFall, firstBounce;
    private static string error;
    private static bool Portrait => scenario / 6 % 2 == 1;
    private static bool Inertia => scenario / 3 % 2 == 1;
    private static GameDifficulty Difficulty => (GameDifficulty)(scenario % 3);
    private static int LongSide => scenario / 12 == 0 ? 1280 : 1600;
    private static int ShortSide => 720;
    private static int FinalWidth => Portrait ? ShortSide : LongSide;
    private static int FinalHeight => Portrait ? LongSide : ShortSide;
    private static int DraftWidth => Portrait ? LongSide : ShortSide;
    private static int DraftHeight => Portrait ? ShortSide : LongSide;

    static OpeningViewportGuardValidation()
    {
        if (SessionState.GetBool(ActiveKey, false)) Register();
    }

    private static void Register()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        startedAt = since = EditorApplication.timeSinceStartup;
    }

    public static void Run()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-"))
            throw new InvalidOperationException("Opening viewport validation requires an isolated batch-mode MenuValidation copy.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikOpeningGuardSandbox";
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        AudioListener.pause = true;
        AudioListener.volume = 0f;
        if (!EditorUtility.audioMasterMute || AudioListener.volume != 0f)
            throw new InvalidOperationException("The isolated Editor audio must be master-muted before a UI cue can run.");
        SessionState.SetBool(ActiveKey, true);
        Register();
        ConfigureNextCase();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (phase < 2) Camera.main.aspect = (float)DraftWidth / DraftHeight;
        else Camera.main.ResetAspect();
        UnityEngine.Random.InitState(1211);
        Spawner.ValidationPickupSeed = 1211;
    }

    private static void ConfigureNextCase()
    {
        GameplayPreferences.SetMode(Portrait, Inertia, false);
        GameplayPreferences.SetDifficulty(Difficulty);
        PlayModeWindow.SetCustomRenderingResolution((uint)DraftWidth, (uint)DraftHeight, "Doodlik opening viewport guard");
        phase = 0;
        since = EditorApplication.timeSinceStartup;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        AudioListener.pause = true;
        AudioListener.volume = 0f;
        try
        {
            if (error != null) throw new InvalidOperationException(error);
            if (EditorApplication.timeSinceStartup - startedAt > 300) throw new TimeoutException("Viewport guard matrix timed out.");
            double elapsed = EditorApplication.timeSinceStartup - since;
            if (Time.frameCount < 20 || RunFlow.IsTransitioning) return;
            if (phase == 0)
            {
                if (elapsed < 0.5 || Screen.width != DraftWidth || Screen.height != DraftHeight) return;
                player = Object.FindAnyObjectByType<Player>();
                menu = Object.FindAnyObjectByType<GameMenuUI>();
                Spawner spawner = Object.FindAnyObjectByType<Spawner>();
                Check(player != null && menu != null && spawner != null && !RunFlow.RunActive,
                    "The late-orientation scenario begins at a natural paused home scene.");
                previousPlayer = player;
                Check(Mathf.Abs(spawner.PreparedAspect - (float)DraftWidth / DraftHeight) < 0.002f,
                    "The route was genuinely generated for the old surface.");
                PlayModeWindow.SetCustomRenderingResolution((uint)FinalWidth, (uint)FinalHeight, "Doodlik opening viewport guard");
                Camera.main.ResetAspect();
                Advance(1);
                return;
            }
            if (phase == 1)
            {
                if (Screen.width != FinalWidth || Screen.height != FinalHeight ||
                    Mathf.Abs(Camera.main.aspect - (float)FinalWidth / FinalHeight) > 0.002f) return;
                Check(!RunFlow.RunActive && Time.timeScale == 0f, "The changing home surface never starts physics.");
                Check(!Object.FindAnyObjectByType<Spawner>().DraftMatchesCurrentSurface,
                    "The final mobile surface invalidates the old draft.");
                Check(EditorUtility.audioMasterMute && AudioListener.volume == 0f,
                    "Editor master mute and zero listener volume cover interface cues.");
                Advance(2);
                menu.StartRunFromHome();
                Check(!RunFlow.RunActive && Time.timeScale == 0f, "Play waits for a certified fresh draft.");
                return;
            }
            if (phase == 2)
            {
                Check(elapsed < 9, "The final settled viewport must permit a new run.");
                if (!RunFlow.IsPlaying) return;
                player = Object.FindAnyObjectByType<Player>();
                Spawner spawner = Object.FindAnyObjectByType<Spawner>();
                Check(player != null && !ReferenceEquals(player, previousPlayer),
                    "Play rebuilt the stale opening instead of stretching it.");
                Check(spawner != null && spawner.DraftMatchesCurrentSurface &&
                    Mathf.Abs(spawner.PreparedAspect - (float)FinalWidth / FinalHeight) < 0.002f,
                    "The new scene was certified for the final physical viewport.");
                Check(player.RunDifficulty == Difficulty && spawner.RunDifficulty == Difficulty,
                    "Rebuilding retains the selected difficulty.");
                Collider2D[] supports = OpeningSupports();
                Check(supports.Length >= 4, "A fresh opening contains enough support platforms.");
                float width = Camera.main.orthographicSize * Camera.main.aspect * 2f;
                for (int row = 1; row <= 3; row++)
                {
                    float gap = supports[row].bounds.max.y - supports[row - 1].bounds.max.y;
                    float distance = Mathf.Abs(TrapGeometry.WrappedDelta(supports[row - 1].transform.position.x,
                        supports[row].transform.position.x, width));
                    JumpReachability.Result flight = JumpReachability.Simulate(player, gap, Inertia,
                        GameplayPreferences.HorizontalMovementScale);
                    Check(flight.CanLand && distance <= flight.HorizontalDistance + 0.03f,
                        "The first three jumps stay within the controller's worst-direction reach.");
                }
                body = player.GetComponent<Rigidbody2D>();
                target = supports[1];
                if (keyboard == null) keyboard = InputSystem.AddDevice<Keyboard>("Opening guard keyboard");
                sawFall = firstBounce = false;
                previousVy = body.linearVelocity.y;
                SetInput(0);
                Advance(3);
                return;
            }
            if (phase == 3)
            {
                Check(!player.IsDead && elapsed < 10, "The rebuilt opening must allow a natural first jump.");
                float vy = body.linearVelocity.y;
                sawFall |= vy < -0.2f;
                bool bounce = sawFall && previousVy < 0f && vy > player.NormalJumpForce * 0.5f;
                previousVy = vy;
                bool reached = bounce && Mathf.Abs(player.GetComponent<Collider2D>().bounds.min.y - target.bounds.max.y) < 0.25f;
                firstBounce |= bounce;
                if (!reached)
                {
                    SetInput(firstBounce ? DirectionToTarget() : 0);
                    return;
                }
                Debug.Log($"OPENING_VIEWPORT_GUARD_CASE_PASS difficulty={Difficulty} portrait={Portrait} inertia={Inertia} " +
                    $"surface={FinalWidth}x{FinalHeight} seed=1211 freshPlayer=true controllerRows=3 naturalBounces=2 height={player.CurrentHeight}");
                SetInput(0);
                scenario++;
                if (scenario >= 24) { Finish(null); return; }
                ConfigureNextCase();
                menu.ReturnToHome();
            }
        }
        catch (Exception exception) { Finish(exception.ToString()); }
    }

    private static Collider2D[] OpeningSupports()
    {
        GameObject[] platforms = GameObject.FindGameObjectsWithTag("Platform");
        Array.Sort(platforms, (a, b) => a.transform.position.y.CompareTo(b.transform.position.y));
        Collider2D[] result = new Collider2D[platforms.Length];
        for (int i = 0; i < platforms.Length; i++) result[i] = platforms[i].GetComponent<Collider2D>();
        return result;
    }

    private static int DirectionToTarget()
    {
        float delta = TrapGeometry.WrappedDelta(body.position.x, target.bounds.center.x,
            Camera.main.orthographicSize * Camera.main.aspect * 2f);
        if (Mathf.Abs(delta) < 0.08f && Mathf.Abs(body.linearVelocity.x) < 0.6f) return 0;
        if (Inertia && Mathf.Sign(body.linearVelocity.x) == Mathf.Sign(delta))
        {
            float deceleration = player.turnAcceleration * GameplayPreferences.HorizontalMovementScale *
                (body.linearVelocity.y < 0f ? player.fallingControl : player.risingControl);
            if (body.linearVelocity.x * body.linearVelocity.x / (2f * deceleration) > Mathf.Abs(delta) - 0.03f)
                return -Math.Sign(body.linearVelocity.x);
        }
        return Math.Sign(delta);
    }

    private static void SetInput(int direction)
    {
        if (keyboard == null) return;
        keyboard.MakeCurrent();
        InputState.Change(keyboard, direction == 0 ? new KeyboardState() :
            new KeyboardState(direction > 0 ? Key.RightArrow : Key.LeftArrow));
    }

    private static void Advance(int next) { phase = next; since = EditorApplication.timeSinceStartup; }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message + $" case={scenario} phase={phase}");
        checks++;
    }
    private static void OnLog(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            if (!message.Contains("ArgumentOutOfRangeException") || !trace.Contains("UnityEditor.Search")) error = message + "\n" + trace;
    }
    private static void Finish(string failure)
    {
        SessionState.SetBool(ActiveKey, false);
        EditorApplication.update -= Tick;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Application.logMessageReceived -= OnLog;
        SetInput(0);
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        Spawner.ValidationPickupSeed = null;
        if (failure == null) Debug.Log($"OPENING_VIEWPORT_GUARD_PASS cases={scenario} checks={checks}");
        else Debug.LogError("OPENING_VIEWPORT_GUARD_FAIL " + failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }
}
#endif
