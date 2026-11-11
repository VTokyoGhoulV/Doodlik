#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Live generation diagnostic, not an automated claim of human playthroughs.
// The real Spawner.Update and sliced hazard coroutines run normally. Only the
// player body/follow camera are controlled so every optional opportunity can be counted.
[InitializeOnLoad]
public static class ExpeditionGenerationValidation
{
    private const string Key = "Doodlik.ExpeditionGenerationValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly int[] Seeds = { 73, 1211 };
    private static readonly FieldInfo HazardField = typeof(Spawner).GetField("hazardSpawner", Private);
    private static readonly FieldInfo MovingLeft = typeof(MovingPlatform).GetField("leftX", Private);
    private static readonly FieldInfo MovingRight = typeof(MovingPlatform).GetField("rightX", Private);
    private static int phase, scenario, loadedFrame, lastFrame = -1, checks, idleFrames, tailFrames;
    private static double since, began, progressAt;
    private static string failure, classicOpening;
    private static UnityEngine.Random.State classicRandom;
    private static Player player;
    private static Camera camera;
    private static Spawner spawner;
    private static HazardSpawner hazards;
    private static float cameraY, targetY;
    private static IDisposable normalStorage, expeditionStorage;
    private static readonly HashSet<ExpeditionPickup> seenFinds = new HashSet<ExpeditionPickup>();
    private static readonly HashSet<DoodlikHazard> seenHazards = new HashSet<DoodlikHazard>();
    private static readonly HashSet<ExpeditionEnemy> seenEnemies = new HashSet<ExpeditionEnemy>();
    private static readonly List<Rect> surfaces = new List<Rect>(), danger = new List<Rect>();
    private static readonly List<string> report = new List<string>();
    private static int firstFind, beforeRest, beforeThousand, cloud, shard, totalBeforeRest, lowCases, emptyCases;
    private static bool HighAscent => SessionState.GetBool(Key + ".high", false);
    private static bool RogueSmoke => SessionState.GetBool(Key + ".rogue", false);
    private static int Scenarios => HighAscent ? 1 : (GameplayPreferences.IsDesktopBuild ? 2 : 4) * SeedCount;
    private static int SeedCount => Mathf.Clamp(SessionState.GetInt(Key + ".seeds", 2), 1, Seeds.Length);
    private static int Mode => scenario / SeedCount;
    private static int Seed => Seeds[scenario % SeedCount] + Mode * 100;
    private static bool Portrait => Mode >= 2;
    private static bool Inertia => Mode % 2 != 0;

    static ExpeditionGenerationValidation() { if (SessionState.GetBool(Key, false)) Register(); }
    public static void RunNatural() => Begin(2);
    public static void RunNaturalSmoke() => Begin(1);
    public static void RunNaturalHigh() => Begin(1, true);
    public static void RunNaturalRogueSmoke() => Begin(1, false, true);

    private static void Begin(int seedCount, bool highAscent = false, bool rogueSmoke = false)
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/") ||
            EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Natural expedition generation needs a disposable batch Editor before Play Mode.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikExpeditionGeneration_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        SessionState.SetInt(Key + ".seeds", seedCount); SessionState.SetBool(Key + ".high", highAscent);
        SessionState.SetBool(Key + ".rogue", rogueSmoke);
        SessionState.SetBool(Key, true);
        phase = scenario = checks = 0; report.Clear();
        totalBeforeRest = lowCases = emptyCases = 0;
        ConfigurePreferences(GameRunKind.Classic);
        Register(); RequestViewport();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void Register()
    {
        since = began = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        SceneManager.sceneLoaded -= SceneLoaded; SceneManager.sceneLoaded += SceneLoaded;
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void PrepareStorage()
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (normalStorage == null) normalStorage = PlayerProgress.UseIsolatedStorageForValidation(out _, out _);
        if (expeditionStorage == null) expeditionStorage = ExpeditionProgress.UseIsolatedStorageForValidation(out _, out _);
        AudioListener.volume = 0f; AudioListener.pause = true;
        RequestViewport();
    }
    private static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!SessionState.GetBool(Key, false)) return;
        camera = Camera.main;
        if (camera != null) camera.aspect = Portrait ? 720f / 1280f : 1280f / 720f;
        UnityEngine.Random.InitState(Seed);
        Spawner.ValidationPickupSeed = unchecked((uint)Seed ^ 0x45d9f3bu);
        loadedFrame = Time.frameCount; since = EditorApplication.timeSinceStartup;
    }
    private static void ConfigurePreferences(GameRunKind kind)
    {
        RunFlow.RunActive = RunFlow.IsTransitioning = false;
        GameplayPreferences.SetMode(Portrait, Inertia, false);
        GameplayPreferences.SetDifficulty(GameDifficulty.Normal);
        if (!GameplayPreferences.SetRunKind(kind, false)) throw new InvalidOperationException("Cannot select diagnostic run kind.");
    }
    private static void RequestViewport() => PlayModeWindow.SetCustomRenderingResolution(
        Portrait ? 720u : 1280u, Portrait ? 1280u : 720u, "Doodlik expedition generation QA");

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        AudioListener.volume = 0f; AudioListener.pause = true;
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try
        {
            if (failure != null) throw new InvalidOperationException(failure);
            // Measured inertia planning drains make a 1000-unit ascent take
            // roughly100 seconds even with the18-unit diagnostic pace. Give
            // narrow mobile patrol proofs headroom without altering them.
            if (EditorApplication.timeSinceStartup - began > 1600 ||
                EditorApplication.timeSinceStartup - since > (HighAscent ? 360 : 180))
                throw new TimeoutException("Expedition generation scenario=" + scenario + " phase=" + phase + " " + Progress());
            if (Time.frameCount - loadedFrame < 18 || RunFlow.IsTransitioning) return;
            Check(EditorUtility.audioMasterMute && AudioListener.volume == 0f, "Generation replay is silent.");
            if (phase == 0)
            {
                Bind();
                Check(Screen.width == (Portrait ? 720 : 1280) && Screen.height == (Portrait ? 1280 : 720),
                    "The actual rendering surface matches the sampled mobile/desktop format.");
                Check(!RunFlow.RunActive && Time.timeScale == 0f && spawner.ExpeditionFinds == null,
                    "Classic opening creates no expedition planner or cargo.");
                Check(ExpeditionPickup.ActiveCount == 0 && ExpeditionEnemy.ActiveCount == 0 && spawner.ExpeditionEnemies == null,
                    "Classic opening contains neither expedition cargo nor combat enemies.");
                Physics2D.SyncTransforms(); classicOpening = OpeningStamp(); classicRandom = UnityEngine.Random.state;
                ConfigurePreferences(GameRunKind.Expedition); phase = 1; Reload(); return;
            }
            if (phase == 1)
            {
                Bind(); Physics2D.SyncTransforms();
                Check(!RunFlow.RunActive && spawner.ExpeditionFinds != null, "The paired expedition opening owns its optional planner.");
                Check(OpeningStamp() == classicOpening, "Identical-seed classic/expedition opening platform/hazard geometry matches.");
                Check(UnityEngine.Random.state.Equals(classicRandom), "Adding the expedition planner does not consume shared opening RNG.");
                Object.FindAnyObjectByType<GameMenuUI>().StartRunFromHome(); phase = 2; since = EditorApplication.timeSinceStartup; return;
            }
            if (phase == 2)
            {
                if (!RunFlow.IsPlaying || Time.timeScale <= 0f) return;
                Bind();
                player.enabled = false;
                Rigidbody2D body = player.GetComponent<Rigidbody2D>(); body.simulated = false;
                CameraFollow follow = Object.FindAnyObjectByType<CameraFollow>(); if (follow != null) follow.enabled = false;
                cameraY = camera.transform.position.y; targetY = WorldZones.HeightAtScore(HighAscent ? 2400 : RogueSmoke ? 600 : 1000);
                seenFinds.Clear(); seenHazards.Clear(); seenEnemies.Clear(); firstFind = -1; beforeRest = beforeThousand = cloud = shard = 0;
                idleFrames = tailFrames = 0; phase = 3; since = EditorApplication.timeSinceStartup;
                Debug.Log("EXPEDITION_GENERATION_START mode=" + Mode + " seed=" + Seed + " portrait=" + Portrait + " inertia=" + Inertia +
                    " ascentTo=" + WorldZones.ScoreAtHeight(targetY));
                return;
            }
            Observe();
            Check(player != null && !player.IsDead && RunFlow.IsPlaying && Time.timeScale > 0f, "Replay keeps its natural generator active.");
            if (EditorApplication.timeSinceStartup >= progressAt)
            {
                progressAt = EditorApplication.timeSinceStartup + 10;
                Debug.Log("EXPEDITION_GENERATION_PROGRESS " + Progress());
            }
            if (phase == 3)
            {
                // Let every existing asynchronous proof finish before moving
                // its offscreen candidate toward the viewport. No proof is
                // cancelled or forced to pass to make room for cargo.
                if (hazards.HasPendingPlanning) { idleFrames = 0; return; }
                if (++idleFrames < 2) return;
                // Raw height currently maps to one world unit. Match the
                // existing natural hazard replay's 18-unit/sec diagnostic
                // pace; the pending-planner gate above still drains every
                // proof and the per-frame movement remains capped at .6.
                float step = 18f * Mathf.Min(Mathf.Max(0f, Time.unscaledDeltaTime), 1f / 30f);
                cameraY = Mathf.Min(targetY, cameraY + step);
                camera.transform.position = new Vector3(camera.transform.position.x, cameraY, camera.transform.position.z);
                Vector2 catPosition = new Vector2(camera.transform.position.x, cameraY + .5f);
                player.transform.position = catPosition; player.GetComponent<Rigidbody2D>().position = catPosition;
                Physics2D.SyncTransforms();
                if (cameraY >= targetY) { phase = 4; tailFrames = 0; }
                return;
            }
            if (hazards.HasPendingPlanning || ++tailFrames < 20) return;
            string line = "EXPEDITION_GENERATION_CASE mode=" + Mode + " seed=" + Seed +
                " portrait=" + Portrait + " inertia=" + Inertia + " ascentTo=" + WorldZones.ScoreAtHeight(targetY) + " firstFind=" + firstFind +
                " before750=" + beforeRest + " before1000=" + beforeThousand + " cloud=" + cloud + " shard=" + shard +
                " placements=" + spawner.ExpeditionFinds.SpawnedCount + " attempts=" + spawner.ExpeditionFinds.PlacementAttempts +
                " hazardsSeen=" + seenHazards.Count + " enemiesSeen=" + seenEnemies.Count +
                " enemyAttempts=" + spawner.ExpeditionEnemies.PlacementAttempts +
                " openingMatchesClassic=true sharedOpeningRngMatches=true";
            Debug.Log(line); report.Add(line);
            if (RogueSmoke) Check(seenEnemies.Count >= 1, "Each representative real route offers a reachable combat enemy before height600.");
            totalBeforeRest += beforeRest;
            if (beforeRest < 3) lowCases++;
            if (beforeThousand == 0) emptyCases++;
            player.Expedition?.EndRun(ExpeditionOutcome.Abandoned);
            scenario++;
            if (scenario >= Scenarios)
            {
                string summary = "EXPEDITION_GENERATION_SUMMARY scenarios=" + Scenarios + " totalBefore750=" + totalBeforeRest +
                    " meanBefore750=" + ((float)totalBeforeRest / Scenarios).ToString("0.00", CultureInfo.InvariantCulture) +
                    " fewerThan3Cases=" + lowCases + " emptyCases=" + emptyCases + " checks=" + checks;
                Debug.Log(summary); report.Add(summary); WriteReport();
                Check(emptyCases == 0, "Every real generated mode/seed must offer cargo by height1000.");
                if (HighAscent) Check(shard > 0, "The natural high ascent publishes a safely reachable starshard above its2200 gate.");
                // Scarcity is reported per case, not hidden by one forced
                // fallback. Parent review chooses density using these samples.
                Debug.Log("EXPEDITION_GENERATION_PASS checks=" + checks + " cases=" + Scenarios + " naturalUpdates=true slicedPlanning=true");
                Finish(0); return;
            }
            ConfigurePreferences(GameRunKind.Classic); phase = 0; Reload();
        }
        catch (Exception exception) { Debug.LogException(exception); report.Add("FAILED " + exception); WriteReport(); Finish(1); }
    }

    private static void Bind()
    {
        player = Object.FindAnyObjectByType<Player>(); spawner = Object.FindAnyObjectByType<Spawner>(); camera = Camera.main;
        Check(player != null && spawner != null && camera != null, "Production scene provides its actual player, spawner and camera.");
        hazards = (HazardSpawner)HazardField.GetValue(spawner);
        Check(hazards != null && spawner.traps.enabled, "No hazard setting is disabled for cargo measurement.");
    }
    private static string OpeningStamp()
    {
        var lines = new List<string>();
        foreach (GameObject platform in GameObject.FindGameObjectsWithTag("Platform"))
        {
            Vector3 p = platform.transform.position, s = platform.transform.lossyScale;
            lines.Add("P:" + F(p.x) + "," + F(p.y) + "," + F(s.x) + "," + F(s.y) + ":" +
                (platform.GetComponent<MovingPlatform>() != null ? "M" : "S") + ":" +
                (platform.GetComponentInChildren<Bush>() != null ? "B" : "-") + ":" +
                (platform.GetComponent<CrumblingPlatform>() != null ? "C" : "-"));
        }
        foreach (DoodlikHazard hazard in Object.FindObjectsByType<DoodlikHazard>())
        {
            Rect r = hazard.PatrolBounds;
            lines.Add("H:" + hazard.Type + ":" + F(r.x) + "," + F(r.y) + "," + F(r.width) + "," + F(r.height));
        }
        lines.Sort(StringComparer.Ordinal); return string.Join("|", lines);
    }
    private static string F(float value) => value.ToString("F4", CultureInfo.InvariantCulture);
    private static string Progress() => "scenario=" + scenario + " frame=" + Time.frameCount +
        " rawHeight=" + WorldZones.ScoreAtHeight(cameraY) + " targetY=" + F(targetY) +
        " cameraY=" + F(cameraY) + " delta=" + F(Time.unscaledDeltaTime) +
        " pending=" + (hazards != null && hazards.HasPendingPlanning) + " idleFrames=" + idleFrames +
        " spawned=" + (spawner != null && spawner.ExpeditionFinds != null ? spawner.ExpeditionFinds.SpawnedCount : -1) +
        " attempts=" + (spawner != null && spawner.ExpeditionFinds != null ? spawner.ExpeditionFinds.PlacementAttempts : -1) +
        " nextBand=" + (spawner != null && spawner.ExpeditionFinds != null ? spawner.ExpeditionFinds.NextBandHeight : -1);
    private static void Observe()
    {
        surfaces.Clear(); danger.Clear();
        foreach (GameObject platform in GameObject.FindGameObjectsWithTag("Platform"))
        {
            Collider2D collider = platform.GetComponent<Collider2D>(); if (collider == null || !collider.enabled) continue;
            Bounds b = collider.bounds; SpriteRenderer art = platform.GetComponent<SpriteRenderer>(); if (art != null) b.Encapsulate(art.bounds);
            MovingPlatform moving = platform.GetComponent<MovingPlatform>();
            if (moving != null)
            {
                // Check the true configured patrol, not a broad padding around
                // its changing centre (which would itself slide every frame).
                float left = (float)MovingLeft.GetValue(moving), right = (float)MovingRight.GetValue(moving);
                surfaces.Add(new Rect(left - b.extents.x, b.min.y, right - left + b.size.x, b.size.y));
            }
            else surfaces.Add(new Rect(b.min.x, b.min.y, b.size.x, b.size.y));
        }
        foreach (DoodlikHazard hazard in Object.FindObjectsByType<DoodlikHazard>())
        { seenHazards.Add(hazard); danger.Add(hazard.PatrolBounds); }
        float width = camera.orthographicSize * camera.aspect * 2f;
        ExpeditionEnemy[] enemies = Object.FindObjectsByType<ExpeditionEnemy>();
        foreach (ExpeditionEnemy enemy in enemies)
        {
            seenEnemies.Add(enemy);
            foreach (Rect area in danger)
                Check(!WrappedOverlap(enemy.PatrolBounds, area, width), "A published enemy's patrol stays clear of later hazards.");
            foreach (Rect area in surfaces)
                Check(!WrappedOverlap(enemy.PatrolBounds, area, width), "Enemy wings and body stay out of platform landing surfaces.");
        }
        foreach (ExpeditionEnemy enemy in enemies) danger.Add(enemy.PatrolBounds);
        foreach (ExpeditionPickup pickup in Object.FindObjectsByType<ExpeditionPickup>())
        {
            Check(AirCoinSpawner.IsAirClear(pickup.transform.position, GameplayPreferences.ModelScale,
                camera.orthographicSize * camera.aspect * 2f, surfaces, danger),
                "A published cargo remains clear of subsequently generated surfaces and actual hazard patrols.");
            if (!seenFinds.Add(pickup)) continue;
            int height = WorldZones.ScoreAtHeight(pickup.transform.position.y);
            Check(height >= ExpeditionCatalog.Find(pickup.Kind).MinimumHeight && pickup.transform.parent == null,
                "Every natural find respects its raw-height gate and is not parented to a platform.");
            if (firstFind < 0) firstFind = height;
            if (height <= 750) beforeRest++;
            if (height <= 1000) beforeThousand++;
            if (pickup.Kind == ExpeditionFindKind.CloudWool) cloud++;
            if (pickup.Kind == ExpeditionFindKind.StarShard) shard++;
        }
    }
    private static void Reload()
    {
        Time.timeScale = 0f; RequestViewport();
        loadedFrame = Time.frameCount; since = EditorApplication.timeSinceStartup;
        SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
    }
    private static void WriteReport()
    {
        Directory.CreateDirectory("Logs/ExpeditionGeneration");
        File.WriteAllLines("Logs/ExpeditionGeneration/" + (HighAscent ? "NaturalHigh.txt" : RogueSmoke ? "NaturalRogue.txt" : "Natural.txt"), report);
    }
    private static bool WrappedOverlap(Rect a, Rect b, float width)
    {
        for (int copy = -1; copy <= 1; copy++)
        {
            Rect shifted = b; shifted.x += copy * width;
            if (a.Overlaps(shifted)) return true;
        }
        return false;
    }
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException("Expedition generation: " + message); }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search") && stack.Contains("IndexationOnStartup")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = message;
    }
    private static void Finish(int code)
    {
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick; SceneManager.sceneLoaded -= SceneLoaded; Application.logMessageReceived -= OnLog;
        player?.Expedition?.EndRun(ExpeditionOutcome.Abandoned); player?.Journal?.EndRun("Проверка генерации");
        RunFlow.RunActive = false; Spawner.ValidationPickupSeed = null;
        expeditionStorage?.Dispose(); expeditionStorage = null; normalStorage?.Dispose(); normalStorage = null;
        EditorApplication.Exit(code);
    }
}
#endif
