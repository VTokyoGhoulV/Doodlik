#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Dedicated batch Editor check: Windows covers the two desktop controls;
// Android covers all four format/control combinations for each difficulty.
public static class DifficultyGenerationValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private sealed class Metrics
    {
        public int Demanding, Wide, Moving, Rows;
        public float Width, Distance;
    }

    private sealed class HazardMetrics
    {
        public int Total, Horizontal, Vertical, Flying, Seeds, Patches, Certificates;
        public float FlightSpeed;
        public void Add(HazardMetrics other)
        {
            Total += other.Total; Horizontal += other.Horizontal; Vertical += other.Vertical;
            Flying += other.Flying; Patches += other.Patches; Certificates += other.Certificates;
            Seeds += other.Seeds;
            FlightSpeed += other.FlightSpeed;
        }
    }

    // Reproduce procedural placement in the same frame as Instantiate and
    // difficulty/portrait scaling, before Box2D receives those transforms.
    public static void RunPlacementGeometry()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run placement geometry in a dedicated EditMode Editor.");
        string[] keys = { GameplayPreferences.OrientationKey, GameplayPreferences.InertiaKey, GameplayPreferences.DifficultyKey };
        bool[] had = new bool[keys.Length];
        int[] values = new int[keys.Length];
        for (int i = 0; i < keys.Length; i++) { had[i] = PlayerPrefs.HasKey(keys[i]); values[i] = PlayerPrefs.GetInt(keys[i]); }
        UnityEngine.Random.State random = UnityEngine.Random.state;
        uint? pickupSeed = Spawner.ValidationPickupSeed;
        bool autoSync = Physics2D.autoSyncTransforms;
        checks = 0;
        try
        {
            Physics2D.autoSyncTransforms = false;
            int formats = GameplayPreferences.IsDesktopBuild ? 1 : 2;
            int accepted = 0, cases = 0;
            for (int format = 0; format < formats; format++)
            for (int control = 0; control < 2; control++)
            foreach (GameDifficulty difficulty in new[] { GameDifficulty.Hard, GameDifficulty.Easy, GameDifficulty.Normal })
            {
                Spawner spawner = PrepareHazardScene(difficulty, format, control, 33117);
                Player player = Object.FindAnyObjectByType<Player>();
                PortraitModelScaler.Attach(player.gameObject);
                Invoke(player.GetComponent<PortraitModelScaler>(), "Awake");
                typeof(Spawner).GetField("mainCamera", Private).SetValue(spawner, Camera.main);
                typeof(Spawner).GetField("playerMovement", Private).SetValue(spawner, player);
                var profile = new DifficultyGenerationProfile(difficulty);
                typeof(Spawner).GetField("balance", Private).SetValue(spawner, profile);
                int placed = 0;
                for (int side = -1; side <= 1; side += 2)
                for (int offsetCase = 0; offsetCase < 2; offsetCase++)
                for (int seed = 0; seed < 32; seed++)
                {
                    GameObject platform = Object.Instantiate(spawner.platformPrefab,
                        new Vector3(side * 2f * GameplayPreferences.HorizontalMovementScale, 40f, 0f), Quaternion.identity);
                    try
                    {
                        BoxCollider2D surface = platform.GetComponent<BoxCollider2D>();
                        if (offsetCase != 0) surface.offset += new Vector2(0.25f * side, 0f);
                        Physics2D.SyncTransforms();
                        float originalWidth = surface.bounds.size.x;
                        Vector3 scale = platform.transform.localScale;
                        scale.x *= profile.PlatformWidth;
                        platform.transform.localScale = scale;
                        PortraitModelScaler.Attach(platform);
                        Invoke(platform.GetComponent<PortraitModelScaler>(), "Awake");
                        float currentWidth = surface.size.x * Mathf.Abs(platform.transform.lossyScale.x);
                        float cachedWidth = surface.bounds.size.x;
                        Vector2 source = new Vector2(0f, 38.5f);
                        UnityEngine.Random.InitState(77100 + seed);
                        bool before = (bool)Invoke(spawner, "AddThornPatch", platform, source);
                        DoodlikHazard patch = platform.GetComponentInChildren<DoodlikHazard>();
                        Vector3 beforePosition = patch != null ? patch.transform.localPosition : Vector3.zero;
                        if (seed == 0 && side == -1 && offsetCase == 0)
                            Debug.Log($"DIFFICULTY_PATCH_GEOMETRY difficulty={difficulty} format={format} control={control} " +
                                $"originalWidth={originalWidth:F6} cachedWidth={cachedWidth:F6} currentWidth={currentWidth:F6} placed={before}");
                        if (before)
                        {
                            Check(patch != null, "Successful patch creation returns actual thorn geometry.");
                            CheckPatchGeometry(patch, surface, source, player, profile, difficulty, format, control, seed);
                            placed++;
                            Object.DestroyImmediate(patch.gameObject);
                        }
                        Physics2D.SyncTransforms();
                        UnityEngine.Random.InitState(77100 + seed);
                        bool after = (bool)Invoke(spawner, "AddThornPatch", platform, source);
                        patch = platform.GetComponentInChildren<DoodlikHazard>();
                        Check(before == after && (!before || Vector3.Distance(beforePosition, patch.transform.localPosition) < 0.00001f),
                            $"Patch decision/position is independent of physics synchronization: {difficulty} {format}/{control} seed={seed} " +
                            $"cachedWidth={cachedWidth:F6} currentWidth={currentWidth:F6} before={beforePosition} " +
                            $"after={(patch != null ? patch.transform.localPosition.ToString() : "rejected")}.");
                        if (after) CheckPatchGeometry(patch, surface, source, player, profile, difficulty, format, control, seed);
                    }
                    finally { Object.DestroyImmediate(platform); }
                }
                Check(placed > 0, $"Placement fixture creates real patches for {difficulty}, {format}/{control}.");
                accepted += placed;
                cases++;
            }
            Debug.Log($"DIFFICULTY_PATCH_GEOMETRY_PASS cases={cases} checks={checks} accepted={accepted} noPhysicsSync=true offset=true lane=true");
        }
        finally
        {
            for (int i = 0; i < keys.Length; i++)
                if (had[i]) PlayerPrefs.SetInt(keys[i], values[i]); else PlayerPrefs.DeleteKey(keys[i]);
            PlayerPrefs.Save();
            UnityEngine.Random.state = random;
            Spawner.ValidationPickupSeed = pickupSeed;
            Physics2D.autoSyncTransforms = autoSync;
        }
    }

    private static void CheckPatchGeometry(DoodlikHazard patch, BoxCollider2D surface, Vector2 source,
        Player player, DifficultyGenerationProfile profile, GameDifficulty difficulty, int format, int control, int seed)
    {
        float scale = GameplayPreferences.ModelScale;
        Vector2 center = surface.transform.TransformPoint(surface.offset);
        float half = surface.size.x * Mathf.Abs(surface.transform.lossyScale.x) * 0.5f;
        Bounds drawn = patch.GetComponentInChildren<MeshRenderer>().bounds;
        Check(drawn.min.x >= center.x - half + 0.02f * scale && drawn.max.x <= center.x + half - 0.02f * scale,
            $"Patch art remains inside current transformed collider before physics sync: {difficulty} {format}/{control} seed={seed} " +
            $"art={drawn.min.x:F6}..{drawn.max.x:F6} collider={center.x - half:F6}..{center.x + half:F6}.");
        BoxCollider2D feet = player.GetComponent<BoxCollider2D>();
        float catScale = Mathf.Abs(player.transform.lossyScale.x);
        float footHalf = feet.size.x * catScale * 0.5f;
        float bodyHalf = player.GetComponent<SpriteRenderer>().sprite.bounds.extents.x * catScale * 0.65f;
        float direction = TrapGeometry.WrappedDelta(source.x, surface.transform.position.x,
            Camera.main.orthographicSize * Camera.main.aspect * 2f);
        float target = Mathf.Abs(direction);
        JumpReachability.Result jump = JumpReachability.Simulate(player, surface.transform.position.y - source.y,
            GameplayPreferences.UseInertia, GameplayPreferences.HorizontalMovementScale);
        float reach = jump.HorizontalDistance * (GameplayPreferences.UseInertia ? 0.9f : 0.92f) * profile.ReachMargin;
        float landingHalf = half - footHalf - 0.12f * scale;
        float minimum = Mathf.Max(0f, target - landingHalf), maximum = Mathf.Min(reach, target + landingHalf);
        float danger = target + Mathf.Sign(direction) * (patch.transform.position.x - center.x);
        float lane = Mathf.Min(maximum, danger - (0.19f + 0.12f) * scale - bodyHalf) - minimum;
        Check(jump.CanLand && lane >= 0.16f * scale - 0.00001f,
            $"Current-width patch preserves a reachable approach lane: {difficulty} {format}/{control} seed={seed} " +
            $"lane={lane:F6} required={0.16f * scale:F6}.");
    }

    // Separate, more expensive entry point. The actual sliced placement proof
    // runs for every candidate, retaining live patrols exactly as the existing
    // runtime-distribution harness does; this is not a target-constant test.
    public static void RunHazards()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run difficulty hazards in a dedicated EditMode Editor.");
        string[] keys = { GameplayPreferences.OrientationKey, GameplayPreferences.InertiaKey, GameplayPreferences.DifficultyKey };
        bool[] had = new bool[keys.Length];
        int[] values = new int[keys.Length];
        for (int i = 0; i < keys.Length; i++) { had[i] = PlayerPrefs.HasKey(keys[i]); values[i] = PlayerPrefs.GetInt(keys[i]); }
        UnityEngine.Random.State random = UnityEngine.Random.state;
        uint? pickupSeed = Spawner.ValidationPickupSeed;
        float worldStart = WorldZones.RunStartHeight, worldPoints = WorldZones.PointsPerUnit;
        int worldScore = WorldZones.CurrentScore;
        bool sliced = HazardSpawner.ValidateSlicedInEditor;
        bool active = RunFlow.RunActive, transition = RunFlow.IsTransitioning;
        float timeScale = Time.timeScale;
        checks = 0;
        try
        {
            HazardSpawner.ValidateSlicedInEditor = true;
            RunFlow.RunActive = true;
            RunFlow.IsTransitioning = false;
            Time.timeScale = 1f;
            int formats = GameplayPreferences.IsDesktopBuild ? 1 : 2;
            int[] seeds = { 1211, 2037 };
            HazardMetrics[] all = { new HazardMetrics(), new HazardMetrics(), new HazardMetrics() };
            int cases = 0;
            for (int format = 0; format < formats; format++)
            for (int control = 0; control < 2; control++)
            {
                HazardMetrics[] modes = { new HazardMetrics(), new HazardMetrics(), new HazardMetrics() };
                for (int difficulty = 0; difficulty < 3; difficulty++)
                {
                    CheckBushChains((GameDifficulty)difficulty, format, control);
                    foreach (int seed in seeds)
                        modes[difficulty].Add(SampleHazards((GameDifficulty)difficulty, format, control, seed));
                    HazardMetrics m = modes[difficulty];
                    Check(m.Horizontal > 0 && m.Vertical > 0 && m.Flying > 0 && m.Patches > 0,
                        $"All trap kinds survive safety vetos: difficulty={difficulty} mode={format}/{control}.");
                    Debug.Log($"DIFFICULTY_HAZARD_METRICS difficulty={(GameDifficulty)difficulty} format={format} control={control} " +
                        $"runs={seeds.Length} total={m.Total} horizontal={m.Horizontal} vertical={m.Vertical} flying={m.Flying} " +
                        $"patches={m.Patches} meanFlightSpeed={m.FlightSpeed / Mathf.Max(1, m.Seeds):F3} certificates={m.Certificates}");
                    all[difficulty].Add(m);
                    cases++;
                }
                // Seeds take different routes, and harder routes can correctly
                // veto more hazards. Require the pooled trend below and permit
                // only a small local reversal, not a mandatory per-seed count.
                int tolerance = Mathf.Max(2, Mathf.CeilToInt(modes[1].Total * 0.10f));
                Check(modes[0].Total <= modes[1].Total + tolerance && modes[2].Total + tolerance >= modes[1].Total,
                    $"Mode density remains comparable after safety vetos: {format}/{control} " +
                    $"easy/normal/hard={modes[0].Total}/{modes[1].Total}/{modes[2].Total} tolerance={tolerance}.");
            }
            Check(all[0].Total < all[1].Total && all[1].Total < all[2].Total,
                $"Actual pooled hazard counts increase with difficulty: {all[0].Total}/{all[1].Total}/{all[2].Total}.");
            Check(all[0].FlightSpeed / all[0].Seeds < all[1].FlightSpeed / all[1].Seeds &&
                all[1].FlightSpeed / all[1].Seeds < all[2].FlightSpeed / all[2].Seeds,
                "Actual spawned flying hazards are slower on Easy and faster on Hard.");
            Debug.Log($"DIFFICULTY_HAZARDS_PASS cases={cases} samples={cases * seeds.Length} checks={checks} " +
                $"totals={all[0].Total},{all[1].Total},{all[2].Total} sliced=true chainedBushes=true");
        }
        finally
        {
            for (int i = 0; i < keys.Length; i++)
                if (had[i]) PlayerPrefs.SetInt(keys[i], values[i]); else PlayerPrefs.DeleteKey(keys[i]);
            PlayerPrefs.Save();
            UnityEngine.Random.state = random;
            Spawner.ValidationPickupSeed = pickupSeed;
            WorldZones.ConfigureRun(worldStart, worldPoints);
            WorldZones.SetScore(worldScore);
            HazardSpawner.ValidateSlicedInEditor = sliced;
            RunFlow.RunActive = active;
            RunFlow.IsTransitioning = transition;
            Time.timeScale = timeScale;
        }
    }

    private static Spawner PrepareHazardScene(GameDifficulty difficulty, int format, int control, int seed)
    {
        PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, format);
        PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, control);
        PlayerPrefs.SetInt(GameplayPreferences.DifficultyKey, (int)difficulty);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        Camera camera = Camera.main;
        camera.aspect = format == 1 ? 9f / 20f : 16f / 9f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        UnityEngine.Random.InitState(seed + format * 100 + control * 1000);
        Spawner.ValidationPickupSeed = (uint)(seed + format * 100 + control * 1000);
        return Object.FindAnyObjectByType<Spawner>();
    }

    private static HazardMetrics SampleHazards(GameDifficulty difficulty, int format, int control, int seed)
    {
        Spawner spawner = PrepareHazardScene(difficulty, format, control, seed);
        Invoke(spawner, "Start");
        Camera camera = Camera.main;
        var generator = (HazardSpawner)Read(spawner, "hazardSpawner");
        var planner = (FlyingHazardRoutes)Read(generator, "flyingRoutes");
        var seen = new HashSet<DoodlikHazard>();
        var certificateKinds = new HashSet<DoodlikHazard.Kind>();
        var m = new HazardMetrics();
        int[] bins = new int[5];
        MethodInfo cull = typeof(HazardValidation).GetMethod("CullExpiredHazards", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo patchCheck = typeof(HazardValidation).GetMethod("CheckPatchLanding", BindingFlags.Static | BindingFlags.NonPublic);
        for (int row = 0; row < 1000; row++)
        {
            Invoke(spawner, "SpawnPlatform");
            DoodlikHazard[] hazards = Object.FindObjectsByType<DoodlikHazard>(FindObjectsSortMode.None);
            foreach (DoodlikHazard hazard in hazards)
            {
                Rect area = hazard.Type == DoodlikHazard.Kind.ThornPatch ? hazard.WarningBounds : hazard.PatrolBounds;
                Check(!generator.OverlapsBushLaunch(area),
                    $"Live hazard never invades any current bush ascent: difficulty={difficulty} mode={format}/{control} seed={seed} row={row}.");
                if (!seen.Add(hazard)) continue;
                int score = WorldZones.ScoreAtHeight(hazard.transform.position.y);
                if (score >= 1000) continue;
                m.Total++;
                bins[Mathf.Clamp(score / 200, 0, 4)]++;
                switch (hazard.Type)
                {
                    case DoodlikHazard.Kind.Thorns: m.Horizontal++; break;
                    case DoodlikHazard.Kind.VerticalThorns: m.Vertical++; break;
                    case DoodlikHazard.Kind.FlyingSeed:
                        m.Flying++;
                        m.Seeds++;
                        m.FlightSpeed += hazard.FlightSpeed;
                        // The permitted ramp bounds use the captured profile;
                        // relevance is replayed with the actual spawned speed.
                        var profile = new DifficultyGenerationProfile(difficulty);
                        Check(hazard.FlightSpeed >= 0.9f * profile.HazardSpeed - 0.001f &&
                            hazard.FlightSpeed <= 1.55f * profile.HazardSpeed + 0.001f,
                            "Actual flying speed uses the captured difficulty ramp.");
                        bool threatens = false;
                        foreach (object step in (IList)Read(generator, "steps"))
                        {
                            var paths = (FlyingHazardRoutes.Paths)step.GetType().GetField("paths").GetValue(step);
                            if (paths != null && planner.CanThreatenARealJump(paths, hazard.transform.position,
                                hazard.FlightAmplitude, (float)Read(hazard, "size"), hazard.FlightSpeed, hazard.MotionPattern))
                            { threatens = true; break; }
                        }
                        Check(threatens, "A flying hazard has a real-jump witness using its actual difficulty-adjusted speed.");
                        for (int sample = 0; sample < 16; sample++)
                            Check(hazard.PatrolBounds.Contains(DoodlikHazard.FlightPosition(hazard.transform.position,
                                sample * 0.25f, hazard.FlightAmplitude, hazard.FlightSpeed, 0f, hazard.MotionPattern)),
                                "Difficulty-adjusted flying motion stays inside its certified patrol.");
                        break;
                    case DoodlikHazard.Kind.Bumblebee:
                        m.Flying++;
                        ChargingBumblebee bee = hazard.GetComponent<ChargingBumblebee>();
                        Check(bee != null && bee.WindupDuration >= 1f && !hazard.DamageEnabled,
                            "A replacement bee begins harmless with a full reaction interval.");
                        Check(hazard.PatrolBounds.Contains(hazard.DamageBounds.min) &&
                            hazard.PatrolBounds.Contains(hazard.DamageBounds.max),
                            "Bumblebee core stays inside the original certified airborne reservation.");
                        break;
                    case DoodlikHazard.Kind.ThornPatch:
                        m.Patches++;
                        patchCheck.Invoke(null, new object[] { hazard, hazard.GetComponentInParent<BoxCollider2D>(), camera });
                        break;
                }
                // Bounded independent replay: one connected certificate per
                // major trap kind per seed, using the original planning inputs.
                if (hazard.Type != DoodlikHazard.Kind.ThornPatch && hazard.CertifiedRoute != null &&
                    certificateKinds.Add(hazard.Type))
                {
                    Check(hazard.CertifiedExistingPatrols != null && hazard.CertifiedInitialPaths != null,
                        "Connected proof keeps its original patrol and incoming-state inputs.");
                    Check(planner.CanTraverseChain(hazard.CertifiedRoute, hazard.PatrolBounds,
                        hazard.CertifiedExistingPatrols, out int simulated, hazard.CertifiedInitialPaths),
                        $"Actual connected route remains traversable on {difficulty}, {format}/{control}, kind={hazard.Type}.");
                    m.Certificates++;
                }
            }
            float height = (float)Read(spawner, "highestPlatformY");
            camera.transform.position = new Vector3(0f, height - 18f, -10f);
            cull.Invoke(null, new object[] { hazards, camera, generator, seen });
            if (WorldZones.ScoreAtHeight(height) >= 1030)
            {
                Debug.Log($"DIFFICULTY_HAZARD_SAMPLE difficulty={difficulty} format={format} control={control} seed={seed} " +
                    $"total={m.Total} horizontal={m.Horizontal} vertical={m.Vertical} flying={m.Flying} patches={m.Patches} bins={string.Join(",", bins)}");
                return m;
            }
        }
        throw new InvalidOperationException("Difficulty hazard sample never reached raw height-score 1030.");
    }

    private static void CheckBushChains(GameDifficulty difficulty, int format, int control)
    {
        Spawner spawner = PrepareHazardScene(difficulty, format, control, 42017);
        spawner.bushSpawnChance = 1f;
        Invoke(spawner, "Start");
        var generator = (HazardSpawner)Read(spawner, "hazardSpawner");
        Bush lower = null, upper = null;
        for (int row = 0; row < 30 && lower == null; row++)
        {
            Bush[] bushes = Object.FindObjectsByType<Bush>(FindObjectsSortMode.None);
            foreach (object step in (IList)Read(generator, "steps"))
            {
                Vector2 from = (Vector2)step.GetType().GetField("from").GetValue(step);
                Vector2 to = (Vector2)step.GetType().GetField("to").GetValue(step);
                Bush a = null, b = null;
                foreach (Bush bush in bushes)
                {
                    if (bush.transform.parent.GetComponent<MovingPlatform>() != null) continue;
                    if (Mathf.Abs(bush.transform.parent.position.y - from.y) < 0.01f) a = bush;
                    if (Mathf.Abs(bush.transform.parent.position.y - to.y) < 0.01f) b = bush;
                }
                if (a != null && b != null) { lower = a; upper = b; break; }
            }
            if (lower == null) Invoke(spawner, "SpawnPlatform");
        }
        Check(lower != null && upper != null,
            $"Consecutive route bushes exist on {difficulty}, mode={format}/{control}.");
        var reservations = (IList)Read(generator, "bushLaunchAreas");
        float width = Camera.main.orthographicSize * Camera.main.aspect * 2f;
        foreach (Bush bush in new[] { lower, upper })
        {
            Rect[] launch = (Rect[])Invoke(spawner, "BuildBushLaunchAreas", bush.GetComponent<BoxCollider2D>(), 0f);
            Check(launch.Length == 5 && launch[4].yMax > bush.transform.position.y + 6f,
                "Each of two chained bushes protects its full ascent, not only the reaction window.");
            foreach (Rect slice in launch)
            {
                bool retained = false;
                foreach (Rect reserved in reservations)
                    if (Vector2.Distance(reserved.position, slice.position) < 0.001f &&
                        Vector2.Distance(reserved.size, slice.size) < 0.001f) { retained = true; break; }
                Check(retained, "Each physical trigger/body ascent slice is retained for both consecutive bushes.");
                for (int copy = -1; copy <= 1; copy++)
                for (int side = 0; side < 2; side++)
                {
                    float edge = side == 0 ? slice.xMin + 0.002f : slice.xMax - 0.002f;
                    Rect probe = new Rect(edge + width * copy - 0.001f, slice.center.y - 0.001f, 0.002f, 0.002f);
                    Check(generator.OverlapsBushLaunch(probe), "Chain launch edges remain protected through screen wrap.");
                }
            }
        }
        foreach (DoodlikHazard hazard in Object.FindObjectsByType<DoodlikHazard>(FindObjectsSortMode.None))
            Check(!generator.OverlapsBushLaunch(hazard.Type == DoodlikHazard.Kind.ThornPatch ? hazard.WarningBounds : hazard.PatrolBounds),
                "The real chained-bush fixture has no hazard intersecting either launch.");
        int count = reservations.Count;
        generator.InvalidateLayout(100f, -width * 0.5f, width * 0.5f, -width * 0.4f, width * 0.4f);
        Check(((IList)Read(generator, "bushLaunchAreas")).Count == count,
            "Resizing preserves both chains' launch reservations on every difficulty.");
        Debug.Log($"DIFFICULTY_BUSH_CHAIN_PASS difficulty={difficulty} format={format} control={control} slices=10 wrap=true");
    }

    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run difficulty generation in a dedicated EditMode Editor.");
        string[] keys = { GameplayPreferences.OrientationKey, GameplayPreferences.InertiaKey, GameplayPreferences.DifficultyKey };
        bool[] had = new bool[keys.Length];
        int[] values = new int[keys.Length];
        for (int i = 0; i < keys.Length; i++) { had[i] = PlayerPrefs.HasKey(keys[i]); values[i] = PlayerPrefs.GetInt(keys[i]); }
        UnityEngine.Random.State random = UnityEngine.Random.state;
        uint? pickupSeed = Spawner.ValidationPickupSeed;
        float worldStart = WorldZones.RunStartHeight, worldPoints = WorldZones.PointsPerUnit;
        int worldScore = WorldZones.CurrentScore;
        checks = 0;
        try
        {
            RunPlacementGeometry();
            CheckNormalIdentity();
            int formats = GameplayPreferences.IsDesktopBuild ? 1 : 2;
            int cases = 0;
            for (int format = 0; format < formats; format++)
            for (int control = 0; control < 2; control++)
            {
                Metrics[] modes = { new Metrics(), new Metrics(), new Metrics() };
                for (int difficulty = 0; difficulty < 3; difficulty++)
                {
                    for (int seed = 0; seed < 2; seed++)
                        CheckRoutes((GameDifficulty)difficulty, format, control, seed, modes[difficulty]);
                    Metrics m = modes[difficulty];
                    Debug.Log($"DIFFICULTY_GENERATION_METRICS difficulty={(GameDifficulty)difficulty} format={format} control={control} " +
                        $"rows={m.Rows} demanding={m.Demanding} wide={m.Wide} moving={m.Moving} " +
                        $"meanWidth={m.Width / m.Rows:F3} meanDistance={m.Distance / m.Rows:F3}");
                    cases++;
                }
                Check(modes[0].Width > modes[1].Width && modes[1].Width > modes[2].Width,
                    $"Actual average landing widths decrease with difficulty in mode {format}/{control}.");
                Check(modes[0].Demanding < modes[1].Demanding && modes[1].Demanding <= modes[2].Demanding,
                    $"Demanding-jump frequency increases in mode {format}/{control}: " +
                    $"{modes[0].Demanding}/{modes[1].Demanding}/{modes[2].Demanding}.");
                CheckHazardTargets(format, control);
            }
            Debug.Log($"DIFFICULTY_GENERATION_PASS cases={cases} checks={checks} rowsPerCase=360 normalIdentity=true immutable=true");
        }
        finally
        {
            for (int i = 0; i < keys.Length; i++)
                if (had[i]) PlayerPrefs.SetInt(keys[i], values[i]); else PlayerPrefs.DeleteKey(keys[i]);
            PlayerPrefs.Save();
            UnityEngine.Random.state = random;
            Spawner.ValidationPickupSeed = pickupSeed;
            WorldZones.ConfigureRun(worldStart, worldPoints);
            WorldZones.SetScore(worldScore);
        }
    }

    private static void CheckNormalIdentity()
    {
        var normal = new DifficultyGenerationProfile(GameDifficulty.Normal);
        Check(normal.PlatformWidth == 1f && normal.ReachMargin == 1f && normal.VerticalGap == 1f &&
            normal.MovingChance == 1f && normal.MovingSpeed == 1f && normal.NarrowChance == 1f &&
            normal.HazardChance == 1f && normal.HazardSpacing == 1f && normal.HazardDensity == 1f &&
            normal.HazardSpeed == 1f && normal.ThornPatchChance == 1f, "Normal uses identity scalars.");
        Check(normal.PrecisionMinimumInterval(false) == 3 && normal.PrecisionMinimumInterval(true) == 2 &&
            normal.PrecisionMaximumInterval(false) == 6 && normal.PrecisionMaximumInterval(true) == 4 &&
            normal.PrecisionChance(false) == 0.5f && normal.PrecisionChance(true) == 0.6f &&
            normal.WideMinimumInterval == 2 && normal.WideMaximumInterval == 4 && normal.WideChance == 0.75f &&
            normal.ChallengeInterval == 4 && normal.ThornPatchInterval(false) == 35 &&
            normal.ThornPatchInterval(true) == 18, "Normal retains exact scheduling thresholds.");

        int reference = 0;
        for (int difficulty = 0; difficulty < 3; difficulty++)
        {
            PlayerPrefs.SetInt(GameplayPreferences.DifficultyKey, difficulty);
            var schedule = new PickupSpawnSchedule(17427);
            int fingerprint = 17;
            unchecked
            {
                for (int index = 0; index < 400; index++)
                    if (schedule.TryGetKind(index, out int kind)) fingerprint = fingerprint * 31 + index * 7 + kind;
                while (schedule.TryTakeCoinBand(1000f, out PickupSpawnSchedule.CoinBand band))
                    fingerprint = fingerprint * 31 + band.Seed + Mathf.RoundToInt(band.Minimum * 10f);
            }
            if (difficulty == 0) reference = fingerprint;
            Check(fingerprint == reference, "Difficulty does not change seeded bonus or coin opportunity clocks.");
        }
    }

    private static void CheckRoutes(GameDifficulty difficulty, int format, int control, int seed, Metrics metric)
    {
        PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, format);
        PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, control);
        PlayerPrefs.SetInt(GameplayPreferences.DifficultyKey, (int)difficulty);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        Spawner spawner = Object.FindAnyObjectByType<Spawner>();
        Camera camera = Camera.main;
        camera.aspect = format == 1 ? 9f / 20f : 16f / 9f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        spawner.traps.enabled = false;
        Spawner.ValidationPickupSeed = 2917;
        UnityEngine.Random.InitState(79100 + format * 100 + control * 10 + seed);
        var scaled = new HashSet<PortraitModelScaler>();
        // This entry invokes Start manually in EditMode. Match the runtime
        // scaler lifecycle explicitly; AddComponent does not dispatch Awake.
        PortraitModelScaler.Attach(Object.FindAnyObjectByType<Player>().gameObject);
        ApplyPendingPortraitScalers(scaled);
        Invoke(spawner, "Start");
        ApplyPendingPortraitScalers(scaled);
        var profile = new DifficultyGenerationProfile(difficulty);
        Player player = (Player)Read(spawner, "playerMovement");
        BoxCollider2D template = spawner.platformPrefab.GetComponent<BoxCollider2D>();
        float scale = GameplayPreferences.ModelScale;
        float normalWidth = template.size.x * Mathf.Abs(template.transform.lossyScale.x) * scale;
        float jumpForce = player.NormalJumpForce, speed = player.moveSpeed;
        // Changing a saved next-run option must not rebalance existing runs.
        PlayerPrefs.SetInt(GameplayPreferences.DifficultyKey, difficulty == GameDifficulty.Hard ? 0 : 2);
        for (int row = 0; row < 180; row++)
        {
            float fromX = (float)Read(spawner, "lastPlatformX"), fromY = (float)Read(spawner, "highestPlatformY");
            float sourceRange = (float)Read(spawner, "lastPlatformMovementRange");
            int wideBefore = (int)Read(spawner, "lastWideJumpTier");
            int precisionBefore = (int)Read(spawner, "lastPrecisionJumpTier");
            Invoke(spawner, "SpawnPlatform");
            float toX = (float)Read(spawner, "lastPlatformX"), toY = (float)Read(spawner, "highestPlatformY");
            float targetRange = (float)Read(spawner, "lastPlatformMovementRange");
            bool narrow = (bool)Read(spawner, "lastPlatformWasNarrow");
            bool wide = (int)Read(spawner, "lastWideJumpTier") != wideBefore;
            bool precision = (int)Read(spawner, "lastPrecisionJumpTier") != precisionBefore;
            float width = camera.orthographicSize * camera.aspect * 2f;
            float distance = Mathf.Abs(TrapGeometry.WrappedDelta(fromX, toX, width));
            JumpReachability.Result jump = wide
                ? JumpReachability.SimulatePrepared(player, toY - fromY, control == 1, GameplayPreferences.HorizontalMovementScale)
                : JumpReachability.Simulate(player, toY - fromY, control == 1, GameplayPreferences.HorizontalMovementScale,
                    sourceRange > 0f ? player.maximumInheritedSpeed * GameplayPreferences.HorizontalMovementScale : 0f);
            float safety = (wide ? 0.87f : control == 1 ? 0.9f : 0.92f) * profile.ReachMargin;
            Check(jump.CanLand && distance + sourceRange + targetRange <= jump.HorizontalDistance * safety + 0.002f,
                $"Reachable successor difficulty={difficulty} mode={format}/{control} seed={seed} row={row} " +
                $"gap={toY - fromY:F3} travel={distance:F3} patrol={sourceRange + targetRange:F3} reach={jump.HorizontalDistance * safety:F3}.");
            Collider2D target = null;
            foreach (GameObject platform in GameObject.FindGameObjectsWithTag("Platform"))
            {
                if (Mathf.Abs(platform.transform.position.y - toY) < 0.001f) target = platform.GetComponent<Collider2D>();
                if (platform.transform.position.y < toY - 30f) Object.DestroyImmediate(platform);
            }
            Check(target != null, "Each route row has a real landing collider.");
            float beforeSync = target.bounds.size.x;
            ApplyPendingPortraitScalers(scaled);
            float expectedWidth = normalWidth * profile.PlatformWidth * (narrow ? spawner.narrowPlatformScale : 1f);
            float transformWidth = ((BoxCollider2D)target).size.x * Mathf.Abs(target.transform.lossyScale.x);
            float actualWidth = target.bounds.size.x;
            if (row == 0 || Mathf.Abs(actualWidth - expectedWidth) >= 0.002f)
                Debug.Log($"DIFFICULTY_COLLIDER_WIDTH difficulty={difficulty} format={format} control={control} seed={seed} row={row} " +
                    $"beforeSync={beforeSync:F6} actual={actualWidth:F6} transformWidth={transformWidth:F6} expected={expectedWidth:F6} " +
                    $"localScale={target.transform.localScale} narrow={narrow} enabled={target.enabled}");
            Check(Mathf.Abs(transformWidth - expectedWidth) < 0.002f && Mathf.Abs(actualWidth - expectedWidth) < 0.002f,
                $"Collider width matches the captured difficulty: {difficulty} mode={format}/{control} row={row} " +
                $"actual={actualWidth:F6} expected={expectedWidth:F6} transformWidth={transformWidth:F6} beforeSync={beforeSync:F6}.");
            Check(target.bounds.min.x >= -width * 0.5f - 0.002f && target.bounds.max.x <= width * 0.5f + 0.002f,
                "Difficulty-adjusted platforms remain inside the wrap viewport.");
            Check(spawner.RunDifficulty == difficulty && player.NormalJumpForce == jumpForce && player.moveSpeed == speed,
                "Difficulty is immutable during generation and never changes player physics.");
            metric.Rows++;
            if (wide || precision) metric.Demanding++;
            if (wide) metric.Wide++;
            if (targetRange > 0f) metric.Moving++;
            metric.Width += target.bounds.size.x;
            metric.Distance += distance;
            camera.transform.position = new Vector3(0f, toY - 18f, -10f);
        }
    }

    private static void ApplyPendingPortraitScalers(HashSet<PortraitModelScaler> initialized)
    {
        foreach (PortraitModelScaler scaler in Object.FindObjectsByType<PortraitModelScaler>(FindObjectsSortMode.None))
            if (initialized.Add(scaler)) Invoke(scaler, "Awake");
        // autoSyncTransforms is deliberately disabled in the project. Manual
        // EditMode generation has no intervening physics tick to sync bounds.
        Physics2D.SyncTransforms();
    }

    private static void CheckHazardTargets(int format, int control)
    {
        Player player = Object.FindAnyObjectByType<Player>();
        Spawner spawner = Object.FindAnyObjectByType<Spawner>();
        Camera camera = Camera.main;
        float[] spacing = new float[3], chance = new float[3], speed = new float[3];
        for (int difficulty = 0; difficulty < 3; difficulty++)
        {
            var profile = new DifficultyGenerationProfile((GameDifficulty)difficulty);
            var hazards = new HazardSpawner(camera, player, spawner.platformPrefab, new TrapSettings(), (GameDifficulty)difficulty);
            PlayerPrefs.SetInt(GameplayPreferences.DifficultyKey, (difficulty + 1) % 3);
            spacing[difficulty] = (float)Invoke(hazards, "HazardSpacing", WorldZones.Zone.Meadow, 120f);
            chance[difficulty] = (float)Invoke(hazards, "HazardAttemptChance", 0.5f);
            speed[difficulty] = (float)Invoke(hazards, "HazardPatrolSpeed", 0.5f);
            Check((float)Invoke(hazards, "HazardPatrolSpeed", 1f) > (float)Invoke(hazards, "HazardPatrolSpeed", 0f),
                "Flying motion continues to grow with height on every difficulty.");
            Check(hazards.RunDifficulty == (GameDifficulty)difficulty, "Hazard planning captures difficulty once.");
            var planner = (FlyingHazardRoutes)Read(hazards, "flyingRoutes");
            BoxCollider2D prefab = spawner.platformPrefab.GetComponent<BoxCollider2D>();
            float expected = prefab.size.x * Mathf.Abs(prefab.transform.localScale.x) * GameplayPreferences.ModelScale *
                0.5f * profile.PlatformWidth;
            Check(Mathf.Abs((float)Read(planner, "halfPlatform") - expected) < 0.0001f,
                "Hazard witnesses use the actual difficulty-adjusted landing width.");
            FlyingHazardRoutes.Paths paths = planner.Build(new Vector2(0f, 100f),
                new Vector2(GameplayPreferences.HorizontalMovementScale, 101.5f));
            Check(paths.Complete, $"All incoming-state witnesses remain available in mode {format}/{control}, difficulty {difficulty}.");
        }
        Check(spacing[0] > spacing[1] && spacing[1] > spacing[2] && chance[0] < chance[1] && chance[1] <= chance[2] &&
            speed[0] < speed[1] && speed[1] < speed[2], "Hazard spacing, attempt targets and motion respect difficulty order.");
        Debug.Log($"DIFFICULTY_HAZARD_TARGETS format={format} control={control} spacing={string.Join(",", spacing)} " +
            $"chance={string.Join(",", chance)} speed={string.Join(",", speed)}");
    }

    private static object Read(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    private static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException("Difficulty generation: " + message); }
}
#endif
