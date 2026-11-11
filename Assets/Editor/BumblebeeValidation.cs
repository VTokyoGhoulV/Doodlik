#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class BumblebeeValidation
{
    private const string Key = "Doodlik.BumblebeeValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int phase, checks;
    private static double since;
    private static string failure;
    private static Player player;
    private static ChargingBumblebee naturalBee;
    private static Vector2 naturalStart, pausedPosition;
    private static IDisposable storage;
    private static readonly List<GameObject> Owned = new List<GameObject>();

    static BumblebeeValidation() { if (SessionState.GetBool(Key, false)) Register(); }
    public static void Run() => Begin(false);
    public static void RunPortrait() => Begin(true);
    public static void RunHazards()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/"))
            throw new InvalidOperationException("Bee generation validation needs a disposable batch Editor.");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        GameObject root = new GameObject("Bee EditMode registration fixture");
        bool registered;
        try
        {
            root.AddComponent<DoodlikHazard>().InitializeBumblebee(Camera.main, Object.FindAnyObjectByType<Player>(),
                1f, new Rect(-1f, -1f, 2f, 2f), GameDifficulty.Normal);
            registered = ChargingBumblebee.HasActive;
            Debug.Log("BUMBLEBEE_EDITOR_REGISTRATION active=" + registered + " isPlaying=" + Application.isPlaying);
        }
        finally { Object.DestroyImmediate(root); }
        Require(registered, "Explicit initialized bee occupies its live slot even during EditMode generation replay.");
        DifficultyGenerationValidation.RunHazards();
    }

    private static void Begin(bool portrait)
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/"))
            throw new InvalidOperationException("Bumblebee validation needs a disposable batch Editor.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikBee_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".portrait", portrait);
        phase = checks = 0; Rules();
        GameplayPreferences.SetMode(portrait, false, false);
        GameplayPreferences.SetDifficulty(GameDifficulty.Normal);
        Register(); RequestViewport();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BeforeScene()
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (storage == null) storage = PlayerProgress.UseIsolatedStorageForValidation(out _, out _);
        RequestViewport();
    }
    private static void RequestViewport()
    {
        bool portrait = SessionState.GetBool(Key + ".portrait", false);
        PlayModeWindow.SetCustomRenderingResolution(portrait ? 720u : 1280u, portrait ? 1280u : 720u, "Doodlik bumblebee validation");
    }
    private static void Register()
    {
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
    }

    private static void Rules()
    {
        foreach (Color32 color in AdventureArt.BeeBody.colors32)
            Require(color.r != 255 || color.g != 204 || color.b != 91 || color.a != 255,
                "The removed pale muzzle overlay must not cover the bee face.");
        Require((int)DoodlikHazard.Kind.Bumblebee == 4, "New hazard kind is appended, not renumbered.");
        Require(ChargingBumblebee.StartHeight >= 180 && ChargingBumblebee.StartHeight <= 250, "Bee starts after the safe opening.");
        for (int i = 0; i < 30; i++)
        {
            Require(ChargingBumblebee.ShouldReplace(i, false) == (i % 3 == 0), "First and every third eligible flying placement is replaced.");
            Require(!ChargingBumblebee.ShouldReplace(i, true), "A live bee prevents a second bee without dropping the seed slot.");
        }
        foreach (float scale in new[] { 1f, 0.544f, 0.48f })
        foreach (float width in new[] { 6.75f, 17.78f, 26.67f })
        {
            float easy = ChargingBumblebee.SpeedFor(scale, width, GameDifficulty.Easy);
            float normal = ChargingBumblebee.SpeedFor(scale, width, GameDifficulty.Normal);
            float hard = ChargingBumblebee.SpeedFor(scale, width, GameDifficulty.Hard);
            Require(easy > 0f && easy < normal && normal < hard, "Bee dash respects scale, screen width and captured difficulty.");
            Require(ChargingBumblebee.WarningFor(GameDifficulty.Easy) > ChargingBumblebee.WarningFor(GameDifficulty.Normal) &&
                ChargingBumblebee.WarningFor(GameDifficulty.Normal) > ChargingBumblebee.WarningFor(GameDifficulty.Hard) &&
                ChargingBumblebee.WarningFor(GameDifficulty.Hard) >= 1f, "Every difficulty provides at least one full second of warning.");
        }
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        AudioListener.volume = 0f; AudioListener.pause = true;
        try
        {
            if (failure != null) throw new InvalidOperationException(failure);
            if (EditorApplication.timeSinceStartup - since > 40d) throw new TimeoutException("Bee phase " + phase);
            if (Time.frameCount < 25 || RunFlow.IsTransitioning || EditorApplication.timeSinceStartup - since < 0.35d) return;
            Require(EditorUtility.audioMasterMute && AudioListener.volume == 0f, "Every fixture is fully silent.");
            if (phase == 0)
            {
                bool portrait = SessionState.GetBool(Key + ".portrait", false);
                Require(Screen.width == (portrait ? 720 : 1280) && Screen.height == (portrait ? 1280 : 720), "Actual GameView matches the mobile format.");
                Object.FindAnyObjectByType<GameMenuUI>().StartRunFromHome(); Advance(); return;
            }
            if (phase == 1)
            {
                if (!RunFlow.IsPlaying) return;
                player = Object.FindAnyObjectByType<Player>();
                player.GetComponent<Rigidbody2D>().simulated = false;
                player.enabled = false;
                Object.FindAnyObjectByType<Spawner>().enabled = false;
                Object.FindAnyObjectByType<CameraFollow>().enabled = false;
                Time.timeScale = 0f;
                foreach (DoodlikHazard old in Object.FindObjectsByType<DoodlikHazard>()) Object.DestroyImmediate(old.gameObject);
                Vector3 savedPosition = player.transform.position;
                foreach (float scale in new[] { 1f, 0.544f, 0.48f })
                foreach (GameDifficulty difficulty in new[] { GameDifficulty.Easy, GameDifficulty.Normal, GameDifficulty.Hard })
                    Lifecycle(scale, difficulty, savedPosition);
                player.transform.position = savedPosition;
                RocketAndSlow(savedPosition);
                CommitPolicy();
                player.transform.position = savedPosition;
                naturalBee = Create(1f, GameDifficulty.Normal, (Vector2)savedPosition + new Vector2(2f, 1f));
                player.transform.position = (Vector2)naturalBee.transform.position + new Vector2(-1f, -0.4f);
                naturalBee.Step(0.01f); naturalBee.Step(naturalBee.WindupDuration + 0.01f);
                naturalStart = naturalBee.transform.position;
                Time.timeScale = 1f; Advance(); return;
            }
            if (phase == 2)
            {
                Require(naturalBee != null && (naturalBee.transform.position - (Vector3)naturalStart).sqrMagnitude > 0.001f,
                    "The real DoodlikHazard.Update drives external movement without controller execution-order tricks.");
                CheckInside(naturalBee);
                pausedPosition = naturalBee.transform.position;
                Time.timeScale = 0f; Advance(); return;
            }
            Require(naturalBee != null && ((Vector2)naturalBee.transform.position - pausedPosition).sqrMagnitude < 0.000001f,
                "Pausing freezes actual bee motion and phase progress.");
            Debug.Log("BUMBLEBEE_PASS checks=" + checks + " lifecycleCases=9 snapshotAim=True windupSafe=True oneShot=True" +
                " bodyOnly=True relativeSweep=True rocketCancel=True slow=True unchangedCommitDensity=True unchangedRng=True" +
                " actualUpdate=True pause=True portrait=" + SessionState.GetBool(Key + ".portrait", false));
            Finish(0);
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    private static ChargingBumblebee Create(float scale, GameDifficulty difficulty, Vector2 center)
    {
        GameObject root = new GameObject("Bee fixture"); Owned.Add(root); root.transform.position = center;
        DoodlikHazard hazard = root.AddComponent<DoodlikHazard>();
        hazard.InitializeBumblebee(Camera.main, player, scale, new Rect(center - new Vector2(1.5f, 1f) * scale,
            new Vector2(3f, 2f) * scale), difficulty);
        return root.GetComponent<ChargingBumblebee>();
    }

    private static void Lifecycle(float scale, GameDifficulty difficulty, Vector2 savedPosition)
    {
        ChargingBumblebee bee = Create(scale, difficulty, savedPosition + new Vector2(2f, 1f) * scale);
        DoodlikHazard hazard = bee.GetComponent<DoodlikHazard>();
        player.transform.position = (Vector2)bee.transform.position + new Vector2(-1f, -0.4f) * scale;
        bee.Step(0.01f);
        Require(bee.CurrentPhase == ChargingBumblebee.Phase.Windup && !bee.IsDamaging, "Windup begins harmless when visible.");
        Vector2 aim = bee.AimPoint;
        player.transform.position += Vector3.right * (8f * scale);
        bee.Step(bee.WindupDuration - 0.001f);
        Require(bee.CurrentPhase == ChargingBumblebee.Phase.Windup && !bee.IsDamaging && bee.AimPoint == aim,
            "A full warning interval passes with a locked aim snapshot, not pursuit.");
        Require(DoodlikHazard.FindHit(bee.transform.position, bee.transform.position, Vector2.one * 0.01f * scale, 100f) == null,
            "Windup has no damaging overlap, even at the body's centre.");
        if (scale == 1f && difficulty == GameDifficulty.Normal) Capture(bee, "Windup");
        bee.Step(0.002f);
        Require(bee.CurrentPhase == ChargingBumblebee.Phase.Dash && bee.IsDamaging, "Dash becomes dangerous only after warning.");
        DenseBodyContainsDamage(hazard);
        Vector2 before = bee.transform.position;
        // The real hazard method captures previousPosition even when paused;
        // the controlled Step then exercises that exact relative-sweep shape.
        typeof(DoodlikHazard).GetMethod("Update", Private).Invoke(hazard, null);
        bee.Step(0.09f);
        Vector2 after = bee.transform.position, halfway = (before + after) * 0.5f;
        Require((after - before).magnitude > 0.35f * scale && bee.AimPoint == aim, "One straight dash moves toward the original aim.");
        Require(DoodlikHazard.FindHit(halfway, halfway, Vector2.one * 0.01f * scale, 100f) == hazard &&
            DoodlikHazard.FindHit(halfway, halfway, Vector2.one * 0.01f * scale, 100f, false) == null,
            "Relative sweep catches a fast crossing missed by endpoint-only overlap.");
        Vector2 wing = (Vector2)bee.transform.position + Vector2.up * (0.2f * scale);
        Require(DoodlikHazard.FindHit(wing, wing, Vector2.one * 0.01f * scale, 100f, false) == null,
            "Harmless wings and surrounding air do not kill the cat.");
        CheckInside(bee);
        if (scale == 1f && difficulty == GameDifficulty.Normal) Capture(bee, "Dash");
        bee.Step(5f); CheckInside(bee);
        Require(bee.IsDamaging, "The final movement frame remains swept even with a long frame time.");
        bee.Step(0.01f);
        Require(bee.CurrentPhase == ChargingBumblebee.Phase.Recovery && !bee.IsDamaging, "Recovery is harmless.");
        if (scale == 1f && difficulty == GameDifficulty.Normal) Capture(bee, "Recovery");
        bee.Step(0.51f); bee.Step(0.7f);
        Require(bee.CurrentPhase == ChargingBumblebee.Phase.Finished && !bee.IsDamaging, "A completed attack retreats and never homes or repeats.");
        Object.DestroyImmediate(bee.gameObject);
        Require(!ChargingBumblebee.HasActive, "Destroying a bee releases the one-live-bee slot.");
    }

    private static void RocketAndSlow(Vector3 savedPosition)
    {
        player.transform.position = savedPosition;
        RunPowerups powers = player.GetComponent<RunPowerups>();
        ChargingBumblebee bee = Create(1f, GameDifficulty.Normal, (Vector2)savedPosition + new Vector2(2f, 1f));
        bee.Step(0.01f); powers.Collect(RunPowerups.Kind.Slow); bee.Step(1f);
        Require(bee.CurrentPhase == ChargingBumblebee.Phase.Windup, "Slow also lengthens the warning, never shortens reaction time.");
        bee.Step(2f); Require(bee.IsDamaging, "Slowed windup eventually progresses normally.");
        Vector2 before = bee.transform.position; bee.Step(0.05f);
        Require(Mathf.Abs(((Vector2)bee.transform.position - before).magnitude - bee.DashSpeed * 0.05f * 0.55f) < 0.001f,
            "Slow scales actual dash displacement.");
        Object.DestroyImmediate(bee.gameObject);
        Time.timeScale = 1f;
        typeof(RunPowerups).GetMethod("TickTimers", Private).Invoke(powers, new object[] { 20f });
        GameObject support = new GameObject("Rocket support fixture"); Owned.Add(support); support.tag = "Platform";
        support.transform.position = player.transform.position + Vector3.down * 1.5f;
        support.AddComponent<BoxCollider2D>().size = new Vector2(5f, 0.2f);
        Physics2D.SyncTransforms();
        bee = Create(1f, GameDifficulty.Normal, (Vector2)savedPosition + new Vector2(2f, 1f)); bee.Step(0.01f);
        powers.Collect(RunPowerups.Kind.Rocket);
        Require(powers.IsRocketActive, "Real rocket protection is active for the interaction fixture.");
        bee.Step(3f); Require(!bee.IsDamaging, "A protected rocket never triggers a bee attack.");
        player.GetComponent<PaperRocketFlight>().Cancel(); Time.timeScale = 0f;
        bee.Step(0.01f); bee.Step(bee.WindupDuration - 0.001f);
        Require(!bee.IsDamaging && bee.CurrentPhase == ChargingBumblebee.Phase.Windup,
            "Rocket exit restarts the complete warning instead of resuming a nearly finished charge.");
        bee.Step(0.002f); Require(bee.IsDamaging, "A fully warned post-rocket attack may start.");
        Time.timeScale = 1f; powers.Collect(RunPowerups.Kind.Rocket); bee.Step(0.01f);
        Require(powers.IsRocketActive && bee.CurrentPhase == ChargingBumblebee.Phase.Recovery && !bee.IsDamaging,
            "Starting a rocket cancels an attack already in flight.");
        player.GetComponent<PaperRocketFlight>().Cancel(); Time.timeScale = 0f;
        bee.Step(1f); Require(bee.CurrentPhase == ChargingBumblebee.Phase.Retreat && !bee.IsDamaging,
            "A cancelled attack never resumes at rocket exit.");
        Object.DestroyImmediate(bee.gameObject); Object.DestroyImmediate(support);
    }

    private static void CommitPolicy()
    {
        Spawner spawner = Object.FindAnyObjectByType<Spawner>();
        HazardSpawner generator = new HazardSpawner(Camera.main, player, spawner.platformPrefab, new TrapSettings(), GameDifficulty.Normal);
        MethodInfo commit = typeof(HazardSpawner).GetMethod("SpawnObstacle", Private);
        FieldInfo count = typeof(HazardSpawner).GetField("majorHazardsPlaced", Private);
        int before = (int)count.GetValue(generator);
        var state = UnityEngine.Random.state;
        var created = new List<DoodlikHazard>();
        DoodlikHazard firstBee = null;
        for (int i = 0; i < 7; i++)
        {
            if (i == 4) Object.DestroyImmediate(firstBee.gameObject);
            Vector2 center = new Vector2(0f, WorldZones.HeightAtScore(350 + i * 100));
            Rect reserved = new Rect(center - Vector2.one, Vector2.one * 2f);
            commit.Invoke(generator, new object[] { center, center, reserved, true, false, 1f,
                new Vector2(0.7f, 0.5f), 1f, 0f, DoodlikHazard.FlightPattern.Horizontal, null, null, null });
            DoodlikHazard hazard = null;
            foreach (DoodlikHazard candidate in Object.FindObjectsByType<DoodlikHazard>())
                if ((Vector2)candidate.transform.position == center) { hazard = candidate; break; }
            Require(hazard != null && hazard.PatrolBounds == reserved, "Commit retains the exact original reservation.");
            bool expectedBee = i == 0 || i == 6;
            Require((hazard.Type == DoodlikHazard.Kind.Bumblebee) == expectedBee, "The commit replaces one slot, with first/every-third cadence and a live cap.");
            if (i == 0) firstBee = hazard;
            created.Add(hazard);
            Require(Object.FindObjectsByType<ChargingBumblebee>().Length <= 1, "At most one live bee exists.");
        }
        Require((int)count.GetValue(generator) == before + 7, "Seven airborne commits spend exactly seven density slots.");
        Require(UnityEngine.Random.state.Equals(state), "Replacement and bee art consume no gameplay RNG.");
        foreach (DoodlikHazard hazard in created) if (hazard != null) Object.DestroyImmediate(hazard.gameObject);
    }

    private static void CheckInside(ChargingBumblebee bee)
    {
        DoodlikHazard hazard = bee.GetComponent<DoodlikHazard>();
        Require(hazard.PatrolBounds.Contains(hazard.DamageBounds.min) && hazard.PatrolBounds.Contains(hazard.DamageBounds.max),
            "Every moving damaging point stays inside the generator's certified full box.");
    }
    private static void DenseBodyContainsDamage(DoodlikHazard hazard)
    {
        Transform art = hazard.transform.Find("Angry paper bumblebee");
        Rect damage = hazard.DamageBounds, dense = AdventureArt.BeeDamageRect;
        foreach (Vector2 corner in new[] { damage.min, damage.max, new Vector2(damage.xMin, damage.yMax), new Vector2(damage.xMax, damage.yMin) })
            Require(dense.Contains(art.InverseTransformPoint(corner)), "All damage-box corners are inside the dense body, not the wing rectangle.");
    }
    private static void Capture(ChargingBumblebee bee, string name)
    {
        foreach (Transform child in bee.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
        GameObject root = new GameObject("Bee preview camera"); Camera camera = root.AddComponent<Camera>();
        camera.orthographic = true; camera.aspect = 1.5f; camera.cullingMask = 1 << 30;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(255, 232, 179, 255);
        Bounds framing = new Bounds(bee.transform.position, Vector3.zero);
        foreach (MeshFilter filter in bee.GetComponentsInChildren<MeshFilter>())
        {
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled || filter.sharedMesh == null) continue;
            Bounds local = filter.sharedMesh.bounds;
            foreach (Vector3 point in new[] { local.min, local.max,
                new Vector3(local.min.x, local.max.y), new Vector3(local.max.x, local.min.y) })
                framing.Encapsulate(filter.transform.TransformPoint(point));
        }
        camera.orthographicSize = Mathf.Max(framing.extents.y, framing.extents.x / 1.5f) + 0.1f;
        camera.transform.position = framing.center + Vector3.back * 10f;
        RenderTexture target = new RenderTexture(540, 360, 24), previous = RenderTexture.active;
        Texture2D texture = new Texture2D(540, 360, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 540, 360), 0, 0); texture.Apply();
            Directory.CreateDirectory("Logs/Bumblebee"); File.WriteAllBytes("Logs/Bumblebee/" + name + ".png", texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(root);
        }
    }
    private static void Advance() { phase++; since = EditorApplication.timeSinceStartup; }
    private static void Require(bool value, string message) { checks++; if (!value) throw new InvalidOperationException("Bumblebee: " + message); }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search") && stack.Contains("IndexationOnStartup")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = message;
    }
    private static void Finish(int code)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
        foreach (GameObject root in Owned) if (root != null) Object.DestroyImmediate(root); Owned.Clear();
        player?.Journal?.EndRun("Проверка шмеля"); storage?.Dispose(); storage = null;
        RunFlow.RunActive = false; EditorApplication.Exit(code);
    }
}
#endif
