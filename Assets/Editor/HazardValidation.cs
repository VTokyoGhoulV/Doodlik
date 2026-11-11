#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Repeatable checks using the actual scene, generator and platform prefab.
// Run in a dedicated batch Editor: -executeMethod HazardValidation.Run -quit.
public static class HazardValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static bool loggedPatchBoundsSynchronization;

    public static void PreviewOnly()
    {
        RenderPreview();
        Debug.Log("HAZARD_PREVIEW_PASS");
    }

    // Focused regression for a bush boost crossing several platform rows.
    // Run separately from the expensive full distribution check.
    public static void RunBushClearance()
    {
        bool hadOrientation = PlayerPrefs.HasKey(GameplayPreferences.OrientationKey);
        bool hadInertia = PlayerPrefs.HasKey(GameplayPreferences.InertiaKey);
        int orientation = PlayerPrefs.GetInt(GameplayPreferences.OrientationKey);
        int inertia = PlayerPrefs.GetInt(GameplayPreferences.InertiaKey);
        UnityEngine.Random.State random = UnityEngine.Random.state;
        try
        {
            PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, 0);
            PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, 1);
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            Spawner spawner = Object.FindAnyObjectByType<Spawner>();
            Camera camera = Camera.main;
            Check(spawner != null && camera != null, "bush test scene is ready");
            camera.aspect = 16f / 9f;
            spawner.bushSpawnChance = 1f;
            UnityEngine.Random.InitState(42017);
            typeof(Spawner).GetMethod("Start", Private).Invoke(spawner, null);
            Bush[] bushes = Object.FindObjectsByType<Bush>(FindObjectsSortMode.None);
            HazardSpawner generator = (HazardSpawner)typeof(Spawner)
                .GetField("hazardSpawner", Private).GetValue(spawner);
            Bush lowerBush = null, upperBush = null;
            // A pair of bushes anywhere in the scene is not necessarily a
            // chain. Require the two platforms to be consecutive route nodes.
            IList routes = (IList)typeof(HazardSpawner).GetField("steps", Private)
                .GetValue(generator);
            foreach (object route in routes)
            {
                Type type = route.GetType();
                Vector2 from = (Vector2)type.GetField("from").GetValue(route);
                Vector2 to = (Vector2)type.GetField("to").GetValue(route);
                Bush fromBush = null, toBush = null;
                foreach (Bush candidate in bushes)
                {
                    if (Mathf.Abs(candidate.transform.parent.position.y - from.y) < 0.01f)
                        fromBush = candidate;
                    if (Mathf.Abs(candidate.transform.parent.position.y - to.y) < 0.01f)
                        toBush = candidate;
                }
                if (fromBush == null || toBush == null) continue;
                lowerBush = fromBush;
                upperBush = toBush;
            }
            Check(lowerBush != null && upperBush != null,
                "two bushes were generated on consecutive route platforms");
            SpriteRenderer upperPlatform = upperBush.transform.parent.GetComponent<SpriteRenderer>();
            SpriteRenderer lowerPlatform = lowerBush.transform.parent.GetComponent<SpriteRenderer>();
            Check(upperPlatform != null && lowerPlatform != null,
                "both bushes belong to route platforms");
            Rect upperBoost = new Rect(upperBush.transform.position.x - 0.05f,
                upperPlatform.bounds.max.y + 6.3f, 0.1f, 0.1f);
            Rect chainedBoost = new Rect(lowerBush.transform.position.x - 0.05f,
                lowerPlatform.bounds.max.y + 6.3f, 0.1f, 0.1f);
            Check(generator.OverlapsBushLaunch(upperBoost) &&
                generator.OverlapsBushLaunch(chainedBoost),
                "both consecutive boosts stay reserved above the first 0.3 seconds");
            Rect edgeBoost = new Rect(upperBush.transform.position.x + 2.83f,
                upperPlatform.bounds.max.y + 4.45f, 0.04f, 0.04f);
            Check(generator.OverlapsBushLaunch(edgeBoost),
                "the actual bush trigger and cat body fit inside the launch reserve");
            int reservationCount = ((System.Collections.IList)typeof(HazardSpawner)
                .GetField("bushLaunchAreas", Private).GetValue(generator)).Count;
            generator.InvalidateLayout(100f, -3f, 3f, -2f, 2f);
            Check(((System.Collections.IList)typeof(HazardSpawner)
                .GetField("bushLaunchAreas", Private).GetValue(generator)).Count == reservationCount,
                "resizing does not erase existing bush safety corridors");

            PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, 1);
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            spawner = Object.FindAnyObjectByType<Spawner>();
            camera = Camera.main;
            camera.aspect = 9f / 20f;
            spawner.bushSpawnChance = 1f;
            UnityEngine.Random.InitState(42018);
            typeof(Spawner).GetMethod("Start", Private).Invoke(spawner, null);
            generator = (HazardSpawner)typeof(Spawner)
                .GetField("hazardSpawner", Private).GetValue(spawner);
            Bush portraitBush = null;
            foreach (Bush candidate in Object.FindObjectsByType<Bush>(FindObjectsSortMode.None))
                if (portraitBush == null || candidate.transform.position.y > portraitBush.transform.position.y)
                    portraitBush = candidate;
            Check(portraitBush != null, "portrait mode generates a bush");
            float portraitY = portraitBush.transform.parent.GetComponent<SpriteRenderer>().bounds.max.y;
            Rect portraitBoost = new Rect(portraitBush.transform.position.x - 0.05f,
                portraitY + 6.3f, 0.1f, 0.1f);
            Check(generator.OverlapsBushLaunch(portraitBoost),
                "portrait inertia also reserves the upper boosted ascent");
            Bush[] portraitBushes = Object.FindObjectsByType<Bush>(FindObjectsSortMode.None);
            bool portraitChain = false;
            IList portraitRoutes = (IList)typeof(HazardSpawner)
                .GetField("steps", Private).GetValue(generator);
            foreach (object route in portraitRoutes)
            {
                Type type = route.GetType();
                Vector2 from = (Vector2)type.GetField("from").GetValue(route);
                Vector2 to = (Vector2)type.GetField("to").GetValue(route);
                bool lower = false, upper = false;
                foreach (Bush candidate in portraitBushes)
                {
                    float y = candidate.transform.parent.position.y;
                    lower |= Mathf.Abs(y - from.y) < 0.01f;
                    upper |= Mathf.Abs(y - to.y) < 0.01f;
                }
                if (lower && upper) { portraitChain = true; break; }
            }
            Check(portraitChain, "portrait inertia can generate two protected bushes in a row");
            Debug.Log("BUSH_CLEARANCE_PASS checks=" + checks);
        }
        finally
        {
            Restore(GameplayPreferences.OrientationKey, hadOrientation, orientation);
            Restore(GameplayPreferences.InertiaKey, hadInertia, inertia);
            PlayerPrefs.Save();
            UnityEngine.Random.state = random;
        }
    }

    public static void Run()
    {
        bool hadOrientation = PlayerPrefs.HasKey(GameplayPreferences.OrientationKey);
        bool hadInertia = PlayerPrefs.HasKey(GameplayPreferences.InertiaKey);
        int orientation = PlayerPrefs.GetInt(GameplayPreferences.OrientationKey);
        int inertia = PlayerPrefs.GetInt(GameplayPreferences.InertiaKey);
        UnityEngine.Random.State random = UnityEngine.Random.state;
        float runStartHeight = WorldZones.RunStartHeight;
        float pointsPerUnit = WorldZones.PointsPerUnit;
        int currentScore = WorldZones.CurrentScore;
        uint? pickupSeed = Spawner.ValidationPickupSeed;
        try
        {
            Check(Resources.Load<Sprite>("Hazards/ThornBranch") != null,
                "one-piece thorn branch sprite is imported for both builds");
            GeometryChecks();
            int formats = GameplayPreferences.IsDesktopBuild ? 1 : 2;
            int fewestEarlyHazards = int.MaxValue, mostEarlyHazards = 0;
            for (int format = 0; format < formats; format++)
            for (int control = 0; control < 2; control++)
            {
                int earlyHazards = GenerationChecks(format, control);
                fewestEarlyHazards = Mathf.Min(fewestEarlyHazards, earlyHazards);
                mostEarlyHazards = Mathf.Max(mostEarlyHazards, earlyHazards);
            }
            Check(fewestEarlyHazards >= 35 && mostEarlyHazards <= 65 &&
                mostEarlyHazards <= fewestEarlyHazards * 1.6f,
                "the first 1000 points have similar non-cluttered hazard density in every mode");
            ContactChecks();
            RenderPreview();
            Debug.Log("HAZARD_VALIDATION_PASS checks=" + checks + " formats=" + formats);
        }
        finally
        {
            Restore(GameplayPreferences.OrientationKey, hadOrientation, orientation);
            Restore(GameplayPreferences.InertiaKey, hadInertia, inertia);
            PlayerPrefs.Save();
            UnityEngine.Random.state = random;
            Time.timeScale = 1f;
            WorldZones.ConfigureRun(runStartHeight, pointsPerUnit);
            WorldZones.SetScore(currentScore);
            Spawner.ValidationPickupSeed = pickupSeed;
        }
    }

    public static void DiagnoseFlying()
    {
        HazardSpawner.DiagnoseFlying = true;
        try { Run(); }
        finally { HazardSpawner.DiagnoseFlying = false; }
    }

    public static void DiagnoseConnected()
    {
        HazardSpawner.DiagnoseConnected = true;
        try { DiagnoseFlying(); }
        finally { HazardSpawner.DiagnoseConnected = false; }
    }

    public static void DiagnoseSliced()
    {
        bool active = RunFlow.RunActive, transition = RunFlow.IsTransitioning;
        float timeScale = Time.timeScale;
        HazardSpawner.ValidateSlicedInEditor = true;
        RunFlow.RunActive = true;
        RunFlow.IsTransitioning = false;
        Time.timeScale = 1f;
        try { DiagnoseFlying(); }
        finally
        {
            HazardSpawner.ValidateSlicedInEditor = false;
            RunFlow.RunActive = active;
            RunFlow.IsTransitioning = transition;
            Time.timeScale = timeScale;
        }
    }

    public static void RunSlicedDistribution()
    {
        bool active = RunFlow.RunActive, transition = RunFlow.IsTransitioning;
        float timeScale = Time.timeScale;
        HazardSpawner.ValidateSlicedInEditor = true;
        RunFlow.RunActive = true;
        RunFlow.IsTransitioning = false;
        Time.timeScale = 1f;
        try { RunDistribution(); }
        finally
        {
            HazardSpawner.ValidateSlicedInEditor = false;
            RunFlow.RunActive = active;
            RunFlow.IsTransitioning = transition;
            Time.timeScale = timeScale;
        }
    }

    public static void RunSlicedAscentProgression()
    {
        bool active = RunFlow.RunActive, transition = RunFlow.IsTransitioning;
        float timeScale = Time.timeScale;
        HazardSpawner.ValidateSlicedInEditor = true;
        RunFlow.RunActive = true;
        RunFlow.IsTransitioning = false;
        Time.timeScale = 1f;
        try { RunAscentProgression(); }
        finally
        {
            HazardSpawner.ValidateSlicedInEditor = false;
            RunFlow.RunActive = active;
            RunFlow.IsTransitioning = transition;
            Time.timeScale = timeScale;
        }
    }

    // Release-balance sample: use several independent runs instead of relying
    // on the one deterministic seed covered by the full geometry validation.
    public static void RunDistribution()
    {
        bool hadOrientation = PlayerPrefs.HasKey(GameplayPreferences.OrientationKey);
        bool hadInertia = PlayerPrefs.HasKey(GameplayPreferences.InertiaKey);
        int orientation = PlayerPrefs.GetInt(GameplayPreferences.OrientationKey);
        int inertia = PlayerPrefs.GetInt(GameplayPreferences.InertiaKey);
        UnityEngine.Random.State random = UnityEngine.Random.state;
        float runStartHeight = WorldZones.RunStartHeight;
        float pointsPerUnit = WorldZones.PointsPerUnit;
        int currentScore = WorldZones.CurrentScore;
        uint? pickupSeed = Spawner.ValidationPickupSeed;
        try
        {
            int formats = GameplayPreferences.IsDesktopBuild ? 1 : 2;
            int[] seeds = { 1211, 2037, 3917, 7001 };
            float lowestMean = float.MaxValue, highestMean = 0f;
            bool allModesInRange = true;
            for (int format = 0; format < formats; format++)
            for (int control = 0; control < 2; control++)
            {
                int lowest = int.MaxValue, highest = 0, sum = 0;
                foreach (int seed in seeds)
                {
                    int count = DistributionSample(format, control, seed);
                    lowest = Mathf.Min(lowest, count);
                    highest = Mathf.Max(highest, count);
                    sum += count;
                }
                float mean = sum / (float)seeds.Length;
                // One unusually quiet seed is acceptable if the mode mean
                // remains comparable to the other controls and layouts.
                allModesInRange &= lowest >= 34 && highest <= 65 &&
                    mean >= 39.5f && mean <= 60f;
                lowestMean = Mathf.Min(lowestMean, mean);
                highestMean = Mathf.Max(highestMean, mean);
                Debug.Log($"HAZARD_DISTRIBUTION_MODE format={format} inertia={control} " +
                    $"minimum={lowest} maximum={highest} mean={mean:F1}");
            }
            Check(allModesInRange,
                "trap density stays neither sparse nor cluttered across four runs");
            Check(highestMean <= lowestMean * 1.4f,
                "trap frequency remains comparable across controls and formats");
            Debug.Log("HAZARD_DISTRIBUTION_PASS checks=" + checks);
        }
        finally
        {
            Restore(GameplayPreferences.OrientationKey, hadOrientation, orientation);
            Restore(GameplayPreferences.InertiaKey, hadInertia, inertia);
            PlayerPrefs.Save();
            UnityEngine.Random.state = random;
            WorldZones.ConfigureRun(runStartHeight, pointsPerUnit);
            WorldZones.SetScore(currentScore);
            Spawner.ValidationPickupSeed = pickupSeed;
        }
    }

    // Compare equal-sized score bands across the ascent. A short opening can
    // be fair without letting the hazard generator plateau before space.
    public static void RunAscentProgression()
    {
        bool hadOrientation = PlayerPrefs.HasKey(GameplayPreferences.OrientationKey);
        bool hadInertia = PlayerPrefs.HasKey(GameplayPreferences.InertiaKey);
        int orientation = PlayerPrefs.GetInt(GameplayPreferences.OrientationKey);
        int inertia = PlayerPrefs.GetInt(GameplayPreferences.InertiaKey);
        UnityEngine.Random.State random = UnityEngine.Random.state;
        float runStartHeight = WorldZones.RunStartHeight;
        float pointsPerUnit = WorldZones.PointsPerUnit;
        int currentScore = WorldZones.CurrentScore;
        uint? pickupSeed = Spawner.ValidationPickupSeed;
        try
        {
            int formats = GameplayPreferences.IsDesktopBuild ? 1 : 2;
            int lowestSummit = int.MaxValue, highestSummit = 0;
            bool allModesProgress = true, allModesUncluttered = true;
            for (int format = 0; format < formats; format++)
            for (int control = 0; control < 2; control++)
            {
                int[] totals = new int[5];
                int maximumShortBand = 0;
                foreach (int seed in new[] { 1211, 2037, 3917, 7001 })
                {
                    int[] major = new int[5], patches = new int[5], shortBands = new int[25];
                    AscentSample(format, control, seed, major, patches, shortBands);
                    int[] runTotals = new int[5];
                    for (int band = 0; band < totals.Length; band++)
                    {
                        runTotals[band] = major[band] + patches[band];
                        totals[band] += runTotals[band];
                    }
                    foreach (int count in shortBands)
                        maximumShortBand = Mathf.Max(maximumShortBand, count);
                    Debug.Log($"HAZARD_ASCENT seed={seed} format={format} inertia={control} " +
                        $"total={string.Join(",", runTotals)} major={string.Join(",", major)} " +
                        $"patches={string.Join(",", patches)} short={string.Join(",", shortBands)}");
                }
                Debug.Log($"HAZARD_ASCENT_MODE format={format} inertia={control} " +
                    $"sumOfFour={string.Join(",", totals)} " +
                    $"maximum200={maximumShortBand}");
                allModesProgress &= totals[4] >= totals[0] * 1.25f &&
                    totals[4] >= totals[1] * 0.95f;
                allModesUncluttered &= totals[4] <= 340 && maximumShortBand <= 21;
                lowestSummit = Mathf.Min(lowestSummit, totals[4]);
                highestSummit = Mathf.Max(highestSummit, totals[4]);
            }
            Check(allModesProgress,
                "the summit is meaningfully denser than the opening");
            Check(allModesUncluttered,
                "the harder summit does not become a trap wall");
            Check(highestSummit <= lowestSummit * 1.4f,
                "summit pressure remains comparable across controls and formats");
            Debug.Log("HAZARD_ASCENT_PASS checks=" + checks);
        }
        finally
        {
            Restore(GameplayPreferences.OrientationKey, hadOrientation, orientation);
            Restore(GameplayPreferences.InertiaKey, hadInertia, inertia);
            PlayerPrefs.Save();
            UnityEngine.Random.state = random;
            WorldZones.ConfigureRun(runStartHeight, pointsPerUnit);
            WorldZones.SetScore(currentScore);
            Spawner.ValidationPickupSeed = pickupSeed;
        }
    }

    private static void AscentSample(int format, int control, int seed,
        int[] major, int[] patches, int[] shortBands)
    {
        PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, format);
        PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, control);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        Spawner spawner = Object.FindAnyObjectByType<Spawner>();
        Camera camera = Camera.main;
        camera.aspect = format == 1 ? 9f / 20f : 16f / 9f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        UnityEngine.Random.InitState(seed + format * 100 + control * 1000);
        Spawner.ValidationPickupSeed = (uint)(seed + format * 100 + control * 1000);
        typeof(Spawner).GetMethod("Start", Private).Invoke(spawner, null);
        MethodInfo spawn = typeof(Spawner).GetMethod("SpawnPlatform", Private);
        FieldInfo highestField = typeof(Spawner).GetField("highestPlatformY", Private);
        HazardSpawner generator = (HazardSpawner)typeof(Spawner)
            .GetField("hazardSpawner", Private).GetValue(spawner);
        var seenHazards = new HashSet<DoodlikHazard>();
        for (int row = 0; row < 4500; row++)
        {
            spawn.Invoke(spawner, null);
            DoodlikHazard[] activeHazards = Object.FindObjectsByType<DoodlikHazard>(FindObjectsSortMode.None);
            foreach (DoodlikHazard hazard in activeHazards)
            {
                if (!seenHazards.Add(hazard)) continue;
                int score = WorldZones.ScoreAtHeight(hazard.transform.position.y);
                if (score < 5000)
                {
                    int band = Mathf.Clamp(score / 1000, 0, major.Length - 1);
                    shortBands[Mathf.Clamp(score / 200, 0, shortBands.Length - 1)]++;
                    if (hazard.Type == DoodlikHazard.Kind.ThornPatch) patches[band]++;
                    else major[band]++;
                }
            }
            float height = (float)highestField.GetValue(spawner);
            camera.transform.position = new Vector3(0f, height - 18f, -10f);
            CullExpiredHazards(activeHazards, camera, generator, seenHazards);
            if (WorldZones.ScoreAtHeight(height) >= 5030) return;
        }
        throw new InvalidOperationException("Ascent sample never reached score 5030");
    }

    private static int DistributionSample(int format, int control, int seed)
    {
        PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, format);
        PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, control);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        Spawner spawner = Object.FindAnyObjectByType<Spawner>();
        Camera camera = Camera.main;
        camera.aspect = format == 1 ? 9f / 20f : 16f / 9f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        UnityEngine.Random.InitState(seed + format * 100 + control * 1000);
        Spawner.ValidationPickupSeed = (uint)(seed + format * 100 + control * 1000);
        typeof(Spawner).GetMethod("Start", Private).Invoke(spawner, null);
        MethodInfo spawn = typeof(Spawner).GetMethod("SpawnPlatform", Private);
        FieldInfo highestField = typeof(Spawner).GetField("highestPlatformY", Private);
        HazardSpawner generator = (HazardSpawner)typeof(Spawner)
            .GetField("hazardSpawner", Private).GetValue(spawner);
        var seenHazards = new HashSet<DoodlikHazard>();
        int[] bins = new int[5];
        int horizontal = 0, vertical = 0, flying = 0, meadowFlying = 0, patches = 0;
        for (int row = 0; row < 1000; row++)
        {
            spawn.Invoke(spawner, null);
            DoodlikHazard[] activeHazards = Object.FindObjectsByType<DoodlikHazard>(FindObjectsSortMode.None);
            foreach (DoodlikHazard hazard in activeHazards)
            {
                if (!seenHazards.Add(hazard)) continue;
                int score = WorldZones.ScoreAtHeight(hazard.transform.position.y);
                if (score < 1000)
                {
                    bins[Mathf.Clamp(score / 200, 0, bins.Length - 1)]++;
                    switch (hazard.Type)
                    {
                        case DoodlikHazard.Kind.Thorns: horizontal++; break;
                        case DoodlikHazard.Kind.VerticalThorns: vertical++; break;
                        case DoodlikHazard.Kind.FlyingSeed:
                        case DoodlikHazard.Kind.Bumblebee:
                            flying++;
                            if (WorldZones.AtScore(score) == WorldZones.Zone.Meadow)
                                meadowFlying++;
                            break;
                        case DoodlikHazard.Kind.ThornPatch: patches++; break;
                    }
                }
            }
            float height = (float)highestField.GetValue(spawner);
            camera.transform.position = new Vector3(0f, height - 18f, -10f);
            CullExpiredHazards(activeHazards, camera, generator, seenHazards);
            if (WorldZones.ScoreAtHeight(height) >= 1030) break;
            if (row == 999)
                throw new InvalidOperationException("Distribution sample never reached score 1030");
        }
        int bushes = 0;
        foreach (Bush bush in Object.FindObjectsByType<Bush>(FindObjectsSortMode.None))
            if (WorldZones.ScoreAtHeight(bush.transform.position.y) < 1000)
                bushes++;
        int total = horizontal + vertical + flying + patches;
        Debug.Log($"HAZARD_DISTRIBUTION seed={seed} format={format} inertia={control} " +
            $"total={total} horizontal={horizontal} vertical={vertical} flying={flying} " +
            $"meadowFlying={meadowFlying} " +
            $"patches={patches} bushes={bushes} bins={string.Join(",", bins)}");
        Check(horizontal > 0 && vertical > 0 && flying > 0 && patches > 0,
            "each run contains all four trap kinds before score 1000");
        Check(bins[0] >= 1 && bins[0] + bins[1] >= 4,
            "the gentler opening still shows a trap and avoids a 400-point drought");
        foreach (int bin in bins)
            Check(bin <= 19, "no 200-point span is overfilled with traps");
        return total;
    }

    private static void Restore(string key, bool existed, int value)
    {
        if (existed) PlayerPrefs.SetInt(key, value);
        else PlayerPrefs.DeleteKey(key);
    }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("Hazard check failed: " + message);
    }

    private static void GeometryChecks()
    {
        Check(Mathf.Abs(TrapGeometry.WrappedDelta(4.5f, -4.5f, 10f) - 1f) < 0.001f, "wrapped direction");
        Check(TrapGeometry.OverlapsRoute(new Rect(4.7f, 1f, 0.2f, 0.2f),
            new Vector2(4.5f, 0f), new Vector2(-4.5f, 2f), 10f, 0.3f, 4f, 0.4f), "wrap edge protected");
        Check(!TrapGeometry.OverlapsRoute(new Rect(-0.3f, 1f, 0.6f, 0.4f),
            new Vector2(4.5f, 0f), new Vector2(-4.5f, 2f), 10f, 0.3f, 4f, 0.4f), "center clear on wrap route");
        Check(TrapGeometry.SweptBox(new Vector2(0f, 2f), new Vector2(0f, -2f),
            Vector2.one * 0.2f, new Rect(-0.5f, -0.1f, 1f, 0.2f)), "low FPS crossing caught");
        Check(!TrapGeometry.SweptBox(new Vector2(2f, 2f), new Vector2(2f, -2f),
            Vector2.one * 0.2f, new Rect(-0.5f, -0.1f, 1f, 0.2f)), "near miss allowed");
        MethodInfo lane = typeof(Spawner).GetMethod("LeavesLandingLane", BindingFlags.Static | BindingFlags.NonPublic);
        Check(!(bool)lane.Invoke(null, new object[] { 4.3f, 4.62f, 4.35f, 0.35f, 0.16f }),
            "a tight rightward jump cannot be blocked on its reachable left edge");
        Check((bool)lane.Invoke(null, new object[] { 4.3f, 4.62f, 5f, 0.35f, 0.16f }),
            "the same tight jump may place thorns beyond the reachable landing lane");
        Check((bool)lane.Invoke(null, new object[] { 3.32f, 4.5f, 4.3f, 0.55f, 0.16f }),
            "a middle patch is allowed when a usable side landing exists");
        Check(!(bool)lane.Invoke(null, new object[] { 4f, 5f, 4.1f, 0.35f, 0.16f }),
            "a landing beyond the patch is not accepted as a safe approach");
    }

    private static int GenerationChecks(int format, int control)
    {
        HazardSpawner.ResetFlyingDiagnostics();
        PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, format);
        PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, control);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        Spawner spawner = Object.FindAnyObjectByType<Spawner>();
        Camera camera = Camera.main;
        camera.aspect = format == 1 ? 9f / 20f : 16f / 9f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        UnityEngine.Random.InitState(1200 + format * 10 + control);
        typeof(Spawner).GetMethod("Start", Private).Invoke(spawner, null);
        HazardSpawner probeGenerator = (HazardSpawner)typeof(Spawner)
            .GetField("hazardSpawner", Private).GetValue(spawner);
        GameObject patchProbe = new GameObject("Thorn patch reservation probe");
        patchProbe.transform.position = new Vector3(0f, 5000f, 0f);
        try
        {
            probeGenerator.RegisterThornPatch(patchProbe.AddComponent<DoodlikHazard>());
            Check(!probeGenerator.TryReserveBushLaunch(new Rect(-0.1f, 4999.9f, 0.2f, 0.2f)),
                "new bush launches avoid already placed thorn patches");
        }
        finally { Object.DestroyImmediate(patchProbe); }
        FieldInfo activeJob = typeof(HazardSpawner).GetField("activeJob", Private);
        FieldInfo deferredRow = typeof(HazardSpawner).GetField("deferredRow", Private);
        MethodInfo trySpawn = typeof(HazardSpawner).GetMethod("TrySpawn", Private);
        activeJob.SetValue(probeGenerator, new object());
        try
        {
            trySpawn.Invoke(probeGenerator, new object[] { new Vector2(0f, 80f) });
            trySpawn.Invoke(probeGenerator, new object[] { new Vector2(0f, 83f) });
            Vector2? pending = (Vector2?)deferredRow.GetValue(probeGenerator);
            Check(pending.HasValue && Mathf.Abs(pending.Value.y - 83f) < 0.01f,
                "a busy hazard planner retains the newest eligible offscreen row");
        }
        finally
        {
            activeJob.SetValue(probeGenerator, null);
            deferredRow.SetValue(probeGenerator, null);
        }
        MethodInfo spawn = typeof(Spawner).GetMethod("SpawnPlatform", Private);
        MethodInfo protectedOverlap = typeof(HazardSpawner).GetMethod("ProtectedRouteOverlaps", Private);
        FieldInfo highest = typeof(Spawner).GetField("highestPlatformY", Private);
        int thornCount = 0, verticalVineCount = 0, flyingCount = 0, meadowFlyingCount = 0,
            firstThousandMajor = 0, firstThousandPatches = 0,
            firstThousandHorizontal = 0, firstThousandVertical = 0,
            patchCount = 0, mainPatchCount = 0, interiorPatchCount = 0, earlyPatchCount = 0;
        var seenHazards = new HashSet<DoodlikHazard>();
        int[] flightPatterns = new int[4];
        const int earlySamples = 820;
        const int minimumLateSamples = 480;
        int lateScoreSpan = WorldZones.StartScore(WorldZones.Zone.Moon) -
            WorldZones.StartScore(WorldZones.Zone.Night) + 100;
        int maximumLateSamples = Mathf.Max(minimumLateSamples,
            Mathf.CeilToInt(lateScoreSpan /
                Mathf.Max(0.1f, spawner.minimumVerticalGap * WorldZones.PointsPerUnit)) + 16);
        for (int i = 0; i < earlySamples + maximumLateSamples; i++)
        {
            if (i == earlySamples)
            {
                // The original 820 rows no longer reach night when the journey
                // goal moves from 2000 to 5000. Continue with the same actual
                // route geometry, but map it to late-game scores so both hazard
                // types are tested in their intended zones for every mode.
                float currentHeight = (float)highest.GetValue(spawner);
                int scoreShortfall = Mathf.Max(0,
                    WorldZones.StartScore(WorldZones.Zone.Night) + 2 -
                    WorldZones.ScoreAtHeight(currentHeight));
                if (scoreShortfall > 0)
                    WorldZones.ConfigureRun(WorldZones.RunStartHeight -
                        scoreShortfall / WorldZones.PointsPerUnit, WorldZones.PointsPerUnit);
                Check(WorldZones.IsNightZone(WorldZones.AtHeight(currentHeight)),
                    "late-game hazard sample reaches the night zone");
            }
            if (i >= earlySamples + minimumLateSamples &&
                WorldZones.ScoreAtHeight((float)highest.GetValue(spawner)) >=
                    WorldZones.StartScore(WorldZones.Zone.Moon) + 100)
                break;
            spawn.Invoke(spawner, null);
            HazardSpawner generator = (HazardSpawner)typeof(Spawner).GetField("hazardSpawner", Private).GetValue(spawner);
            IList routes = (IList)typeof(HazardSpawner).GetField("steps", Private).GetValue(generator);
            float jump = (float)typeof(HazardSpawner).GetField("jumpHeight", Private).GetValue(generator);
            float verticalMargin = (float)typeof(HazardSpawner).GetField("playerMargin", Private).GetValue(generator);
            float turnMargin = (float)typeof(HazardSpawner).GetField("inertiaMargin", Private).GetValue(generator);
            DoodlikHazard[] activeHazards = Object.FindObjectsByType<DoodlikHazard>(FindObjectsSortMode.None);
            foreach (DoodlikHazard hazard in activeHazards)
            {
                if (!seenHazards.Add(hazard)) continue;
                if (hazard.Type == DoodlikHazard.Kind.ThornPatch)
                {
                    int patchScore = WorldZones.ScoreAtHeight(hazard.transform.position.y);
                    Check(patchScore >= 500,
                        "small thorn patches start around score 500, never on the opening stretch");
                    if (patchScore < 1000) firstThousandPatches++;
                    if (i < earlySamples && WorldZones.ScoreAtHeight(
                        hazard.transform.position.y) < 900) earlyPatchCount++;
                    BoxCollider2D surface = hazard.GetComponentInParent<BoxCollider2D>();
                    Check(surface != null && surface.CompareTag("Platform") && surface.enabled,
                        "thorn patch belongs to an ordinary usable platform");
                    Check(surface.bounds.size.x > 0.32f * GameplayPreferences.ModelScale * 2f,
                        "localized thorns leave most of the landing surface clear");
                    CheckPatchLanding(hazard, surface, camera);
                    if (Mathf.Abs(hazard.transform.position.x - surface.bounds.center.x) <
                        surface.bounds.extents.x * 0.7f) interiorPatchCount++;
                    Rect warning = hazard.WarningBounds;
                    MeshRenderer patchArt = hazard.GetComponentInChildren<MeshRenderer>();
                    if (patchArt != null)
                    {
                        Bounds drawn = patchArt.bounds;
                        Check(warning.xMin <= drawn.min.x + 0.01f &&
                            warning.xMax >= drawn.max.x - 0.01f &&
                            warning.yMin <= drawn.min.y + 0.01f &&
                            warning.yMax >= drawn.max.y - 0.01f,
                            "thorn-patch warning bounds cover the entire drawn silhouette");
                    }
                    Check(!generator.OverlapsBushLaunch(warning),
                        "small thorns do not block the immediate bush launch");
                    if (surface.name.StartsWith("Platform")) mainPatchCount++;
                    patchCount++;
                    continue;
                }
                Check(hazard.PatrolBounds.yMin > spawner.traps.startingHeight - 1f, "no starting hazards");
                bool withinFirstThousand = WorldZones.ScoreAtHeight(hazard.transform.position.y) < 1000;
                if (withinFirstThousand)
                    firstThousandMajor++;
                Check(!generator.OverlapsBushLaunch(hazard.PatrolBounds),
                    "full-size hazards leave reaction room above bushes");
                bool threatensRealJump = false;
                float size = (float)typeof(DoodlikHazard).GetField("size", Private).GetValue(hazard);
                float patrol = (float)typeof(DoodlikHazard).GetField("patrol", Private).GetValue(hazard);
                float speed = (float)typeof(DoodlikHazard).GetField("speed", Private).GetValue(hazard);
                Vector2 position = hazard.transform.position;
                var routePlanner = (FlyingHazardRoutes)typeof(HazardSpawner).GetField("flyingRoutes", Private).GetValue(generator);
                bool connected = hazard.CertifiedRoute != null;
                if (connected)
                {
                    Check(hazard.CertifiedExistingPatrols != null &&
                        hazard.CertifiedInitialPaths != null,
                        "connected proof keeps the original planning conditions");
                    bool valid = routePlanner.CanTraverseChain(hazard.CertifiedRoute,
                        hazard.PatrolBounds, hazard.CertifiedExistingPatrols,
                        out int simulated, hazard.CertifiedInitialPaths);
                    if (!valid)
                        Debug.LogError($"CHAIN_RECHECK_FAIL format={format} inertia={control} " +
                            $"hazard={hazard.Type} area={hazard.PatrolBounds} " +
                            $"nodes={string.Join(",", hazard.CertifiedRoute)} simulated={simulated}");
                    Check(valid, "actual connected three-hop certificate is valid");
                }
                foreach (object route in routes)
                {
                    Type type = route.GetType();
                    Vector2 from = (Vector2)type.GetField("from").GetValue(route);
                    Vector2 to = (Vector2)type.GetField("to").GetValue(route);
                    float margin = (float)type.GetField("margin").GetValue(route);
                    bool overlap = TrapGeometry.OverlapsRoute(hazard.PatrolBounds, from, to,
                        camera.orthographicSize * camera.aspect * 2f, margin, jump, verticalMargin, turnMargin);
                    if (overlap)
                    {
                        if (!(bool)type.GetField("allowFlyingDetour").GetValue(route))
                        {
                            Check(!(bool)protectedOverlap.Invoke(generator,
                                new object[] { hazard.PatrolBounds, route,
                                    camera.orthographicSize * camera.aspect * 2f }),
                                "hazard stays outside the physical rising reach of a protected route");
                            continue;
                        }
                        var planner = (FlyingHazardRoutes)typeof(HazardSpawner).GetField("flyingRoutes", Private).GetValue(generator);
                        var paths = (FlyingHazardRoutes.Paths)type.GetField("paths").GetValue(route);
                        Check(connected || planner.HasSafeAlternatives(paths, hazard.PatrolBounds, new System.Collections.Generic.List<Rect>()),
                            "hazard leaves a real safe landing for every tested initial state");
                        if (hazard.Type == DoodlikHazard.Kind.Thorns ||
                            hazard.Type == DoodlikHazard.Kind.VerticalThorns)
                        {
                            Rect damage = hazard.DamageBounds;
                            Check(hazard.PatrolBounds.Contains(damage.min) && hazard.PatrolBounds.Contains(damage.max),
                                "generated thorn damage matches the certified rectangle");
                            threatensRealJump |= planner.CanThreatenStatic(paths, damage);
                        }
                        else threatensRealJump |= planner.CanThreatenARealJump(paths,
                            position, hazard.FlightAmplitude, size, speed,
                            hazard.MotionPattern);
                    }
                }
                Check(threatensRealJump, "hazard can actually intersect an ordinary reachable jump");
                if (hazard.Type == DoodlikHazard.Kind.Thorns)
                {
                    thornCount++;
                    if (withinFirstThousand) firstThousandHorizontal++;
                }
                else if (hazard.Type == DoodlikHazard.Kind.VerticalThorns)
                {
                    verticalVineCount++;
                    if (withinFirstThousand) firstThousandVertical++;
                }
                else
                {
                    flyingCount++;
                    flightPatterns[(int)hazard.MotionPattern]++;
                    Vector2 amplitude = hazard.FlightAmplitude;
                    Check(Mathf.Max(amplitude.x, amplitude.y) >=
                        0.23f * size,
                        "flying hazard moves visibly rather than hovering in place");
                    for (int sample = 0; sample < 16; sample++)
                    {
                        Vector2 flight = DoodlikHazard.FlightPosition(position,
                            sample * 0.25f, amplitude, speed, 0f,
                            hazard.MotionPattern);
                        Check(hazard.PatrolBounds.Contains(flight),
                            "full flying trajectory stays inside reserved bounds");
                    }
                    if (i < earlySamples && WorldZones.AtHeight(position.y) == WorldZones.Zone.Meadow)
                        meadowFlyingCount++;
                }
            }
            camera.transform.position = new Vector3(0f, (float)highest.GetValue(spawner) - 18f, -10f);
            CullExpiredHazards(activeHazards, camera, generator, seenHazards);
        }
        Check(WorldZones.ScoreAtHeight((float)highest.GetValue(spawner)) >=
            WorldZones.StartScore(WorldZones.Zone.Moon) + 100,
            "late-game hazard sample reaches beyond the space boundary");
        Debug.Log($"HAZARD_GENERATION format={format} inertia={control} thorns={thornCount} verticalVines={verticalVineCount} patches={patchCount} interiorPatches={interiorPatchCount} earlyPatches={earlyPatchCount} mainPatches={mainPatchCount} flying={flyingCount} meadowFlying={meadowFlyingCount} first1000Major={firstThousandMajor} first1000Horizontal={firstThousandHorizontal} first1000Vertical={firstThousandVertical} first1000Patches={firstThousandPatches} patterns={string.Join(",", flightPatterns)}");
        Check(firstThousandHorizontal > 0,
            "horizontal thorn branches appear before score 1000 in each mode");
        if (HazardSpawner.DiagnoseFlying) Debug.Log(HazardSpawner.FlyingDiagnostics());
        Check(thornCount > 0 && verticalVineCount > 0 && flyingCount > 0,
            "horizontal branches, vertical vines and flying seeds occur in each mode");
        Check(patchCount > 0, "partial thorns appear on flower platforms");
        Check(interiorPatchCount > 0, "some small thorn patches appear away from the platform edges");
        Check(earlyPatchCount > 0, "partial thorns appear between scores 500 and 900");
        Check(mainPatchCount > 0, "partial thorns also appear on the main route");
        Check(meadowFlyingCount > 0, "rare flying hazards occur already in the meadow");
        int distinctPatterns = 0;
        foreach (int count in flightPatterns) if (count > 0) distinctPatterns++;
        Check(distinctPatterns >= 3, "flying hazards use different directions");
        return firstThousandMajor + firstThousandPatches;
    }

    // EditMode has no DoodlikHazard.Update cleanup. Preserve relevant hazards
    // so later bushes see the same occupied space as they would in play, then
    // retire only hazards safely below the camera and the generator lookahead.
    private static void CullExpiredHazards(DoodlikHazard[] activeHazards,
        Camera camera, HazardSpawner generator, HashSet<DoodlikHazard> seenHazards)
    {
        float cutoff = camera.transform.position.y - camera.orthographicSize -
            generator.LookAhead;
        foreach (DoodlikHazard hazard in activeHazards)
        {
            if (hazard == null) continue;
            Rect area = hazard.Type == DoodlikHazard.Kind.ThornPatch
                ? hazard.DamageBounds : hazard.PatrolBounds;
            if (area.yMax >= cutoff) continue;
            seenHazards.Remove(hazard);
            Object.DestroyImmediate(hazard.Type == DoodlikHazard.Kind.ThornPatch
                ? hazard.gameObject : hazard.transform.root.gameObject);
        }
    }

    private static void CheckPatchLanding(DoodlikHazard patch, BoxCollider2D surface,
        Camera camera)
    {
        MeshRenderer drawing = patch.GetComponentInChildren<MeshRenderer>();
        Check(drawing != null, "platform thorn patch has visible artwork");
        Bounds art = drawing.bounds;
        Bounds beforeSync = surface.bounds;
        // Authoring replay instantiates and difficulty-scales a platform in
        // this same frame. Match the current transform geometry that the next
        // runtime physics step would see, not Box2D's cached prefab width.
        Physics2D.SyncTransforms();
        Bounds platform = surface.bounds;
        float sideMargin = 0.02f * GameplayPreferences.ModelScale;
        if (!loggedPatchBoundsSynchronization &&
            (art.min.x < beforeSync.min.x + sideMargin || art.max.x > beforeSync.max.x - sideMargin))
        {
            loggedPatchBoundsSynchronization = true;
            Debug.Log($"PATCH_PHYSICS_SYNC_REGRESSION artX={art.min.x:F6}/{art.max.x:F6} " +
                $"beforeColliderX={beforeSync.min.x:F6}/{beforeSync.max.x:F6} " +
                $"afterColliderX={platform.min.x:F6}/{platform.max.x:F6} margin={sideMargin:F6} " +
                $"mode={GameplayPreferences.IsPortrait}/{GameplayPreferences.UseInertia}/{GameplayPreferences.Difficulty}");
        }
        if (art.min.x < platform.min.x + sideMargin || art.max.x > platform.max.x - sideMargin)
        {
            Vector3 authoredLeft = surface.transform.TransformPoint(surface.offset + new Vector2(-surface.size.x * 0.5f, 0f));
            Vector3 authoredRight = surface.transform.TransformPoint(surface.offset + new Vector2(surface.size.x * 0.5f, 0f));
            Debug.Log($"PATCH_BOUNDS_DIAGNOSTIC art={art} platformCached={platform} " +
                $"authoredX={authoredLeft.x:F6}/{authoredRight.x:F6} " +
                $"patchPosition={patch.transform.position} patchScale={patch.transform.lossyScale} " +
                $"surfacePosition={surface.transform.position} surfaceScale={surface.transform.lossyScale} " +
                $"mode={GameplayPreferences.IsPortrait}/{GameplayPreferences.UseInertia}/{GameplayPreferences.Difficulty}");
        }
        Check(art.min.x >= platform.min.x + sideMargin &&
            art.max.x <= platform.max.x - sideMargin,
            "all of the drawn thorn patch remains on its platform");
        Vector2[] route = patch.CertifiedRoute;
        Check(route != null && route.Length == 2,
            "every small thorn patch records its incoming route");
        Player player = Object.FindAnyObjectByType<Player>();
        BoxCollider2D feet = player.GetComponent<BoxCollider2D>();
        SpriteRenderer cat = player.GetComponent<SpriteRenderer>();
        float modelScale = GameplayPreferences.ModelScale;
        float catScale = Mathf.Abs(player.transform.lossyScale.x) *
            (player.GetComponent<PortraitModelScaler>() == null ? modelScale : 1f);
        float footHalf = feet.size.x * catScale * 0.5f;
        float bodyHalf = cat.sprite.bounds.extents.x * catScale * 0.65f;
        float width = camera.orthographicSize * camera.aspect * 2f;
        float direction = TrapGeometry.WrappedDelta(route[0].x, route[1].x, width);
        JumpReachability.Result jump = JumpReachability.Simulate(player,
            route[1].y - route[0].y, GameplayPreferences.UseInertia,
            GameplayPreferences.HorizontalMovementScale);
        Check(jump.CanLand, "patched platform remains below the normal jump apex");
        float reach = jump.HorizontalDistance * (GameplayPreferences.UseInertia ? 0.9f : 0.92f);
        float target = Mathf.Abs(direction);
        float landingHalf = surface.bounds.extents.x - footHalf - 0.12f * modelScale;
        float minimum = Mathf.Max(0f, target - landingHalf);
        float maximum = Mathf.Min(reach, target + landingHalf);
        float danger = target + Mathf.Sign(direction) *
            (patch.transform.position.x - surface.bounds.center.x);
        float clearance = (0.19f + 0.12f) * modelScale + bodyHalf;
        float approachLane = Mathf.Min(maximum, danger - clearance) - minimum;
        Check(approachLane >= 0.16f * modelScale,
            "small thorn patch preserves a reachable near-side landing lane");
    }

    private static void ContactChecks()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Camera camera = new GameObject("Camera").AddComponent<Camera>();
        DoodlikHazard hazard = new GameObject("Test thorns").AddComponent<DoodlikHazard>();
        hazard.Initialize(DoodlikHazard.Kind.Thorns, camera, 1f, 0f, 1f, 0f, new Rect(-0.7f, -0.1f, 1.4f, 0.6f));
        // EditMode does not dispatch these runtime lifecycle callbacks.
        typeof(DoodlikHazard).GetMethod("OnEnable", Private).Invoke(hazard, null);
        Check(DoodlikHazard.FindHit(new Vector2(0f, 2f), new Vector2(0f, -2f), Vector2.one * 0.2f, 10f) == hazard,
            "real hazard hit detected between frames");
        Check(DoodlikHazard.FindHit(new Vector2(4.9f, 0.2f), new Vector2(-4.9f, 0.2f), Vector2.one * 0.2f, 10f) == null,
            "wrapping does not hit center hazard");
        hazard.gameObject.SetActive(false);
        typeof(DoodlikHazard).GetMethod("OnDisable", Private).Invoke(hazard, null);
        Check(DoodlikHazard.FindHit(new Vector2(0f, 2f), Vector2.zero, Vector2.one * 0.2f, 10f) == null,
            "disabled hazard unregisters");
        Object.DestroyImmediate(hazard.gameObject);
        DoodlikHazard vine = new GameObject("Test vertical vine").AddComponent<DoodlikHazard>();
        vine.Initialize(DoodlikHazard.Kind.VerticalThorns, camera, 1f, 0f, 1f, 0f,
            new Rect(-0.36f, -0.77f, 0.72f, 1.54f));
        typeof(DoodlikHazard).GetMethod("OnEnable", Private).Invoke(vine, null);
        Check(DoodlikHazard.FindHit(new Vector2(-2f, 0f), new Vector2(2f, 0f),
            Vector2.one * 0.1f, 10f) == vine, "vertical vine blocks a sideways crossing");
        Check(DoodlikHazard.FindHit(new Vector2(-2f, 1.2f), new Vector2(2f, 1.2f),
            Vector2.one * 0.1f, 10f) == null, "vertical vine has a finite height");
        Check(DoodlikHazard.FindHit(new Vector2(-0.27f, -1f), new Vector2(-0.27f, 1f),
            Vector2.one * 0.08f, 10f) == null, "visible left gap beside vine is safe");
        Check(DoodlikHazard.FindHit(new Vector2(0.32f, -1f), new Vector2(0.32f, 1f),
            Vector2.one * 0.08f, 10f) == null, "visible right gap beside vine is safe");
        Check(DoodlikHazard.FindHit(new Vector2(-0.18f, -1f), new Vector2(-0.18f, 1f),
            Vector2.one * 0.08f, 10f) == vine, "brushing the visible vine still hurts");
        Object.DestroyImmediate(vine.gameObject);
        DoodlikHazard patch = new GameObject("Test patch").AddComponent<DoodlikHazard>();
        patch.transform.position = new Vector2(2f, 0f);
        patch.InitializePatch(camera, new Vector2(0.16f, 0.075f), Vector3.one);
        typeof(DoodlikHazard).GetMethod("OnEnable", Private).Invoke(patch, null);
        Check(DoodlikHazard.FindHit(new Vector2(2f, 1f), new Vector2(2f, -1f),
            Vector2.one * 0.1f, 10f) == patch, "thorn patch hit detected between frames");
        Check(DoodlikHazard.FindHit(new Vector2(0f, 1f), new Vector2(0f, -1f),
            Vector2.one * 0.1f, 10f) == null, "other side of platform remains safe");
        Object.DestroyImmediate(patch.gameObject);
        DoodlikHazard seed = new GameObject("Test seed").AddComponent<DoodlikHazard>();
        seed.Initialize(DoodlikHazard.Kind.FlyingSeed, camera, 1f, 0.42f, 1f, 0f, new Rect(-0.9f, -0.55f, 1.8f, 1.1f));
        Vector3 before = seed.transform.position;
        Time.timeScale = 0f;
        typeof(DoodlikHazard).GetMethod("Update", Private).Invoke(seed, null);
        Check(seed.transform.position == before, "menu pause stops flight");
        Time.timeScale = 1f;
        Object.DestroyImmediate(seed.gameObject);
    }

    private static void RenderPreview()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            return;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        Camera camera = Camera.main;
        camera.orthographicSize = 2.2f;
        camera.transform.position = new Vector3(0f, 30f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.78f, 0.8f, 0.69f);
        camera.cullingMask = ~0; // Include the scene's global 2D light.
        GameObject thorns = new GameObject("Preview thorny branch");
        thorns.transform.position = new Vector3(-3f, 30f, 0f);
        Transform thornArt = HazardDrawing.Attach(thorns.transform,
            DoodlikHazard.Kind.Thorns);
        thornArt.localScale = Vector3.one * DoodlikHazard.ThornBranchScale;
        thornArt.gameObject.layer = 30;
        GameObject vine = new GameObject("Preview vertical vine");
        vine.transform.position = new Vector3(-1f, 30f, 0f);
        Transform vineArt = HazardDrawing.Attach(vine.transform,
            DoodlikHazard.Kind.VerticalThorns);
        vineArt.localScale = Vector3.one * DoodlikHazard.ThornBranchScale;
        vineArt.localRotation = Quaternion.Euler(0f, 0f, 90f);
        vineArt.gameObject.layer = 30;
        Spawner spawner = Object.FindAnyObjectByType<Spawner>();
        GameObject platform = Object.Instantiate(spawner.platformPrefab,
            new Vector3(1f, 30f, 0f), Quaternion.identity);
        BoxCollider2D surface = platform.GetComponent<BoxCollider2D>();
        GameObject patch = new GameObject("Preview thorn patch");
        patch.transform.SetParent(platform.transform, false);
        patch.transform.localPosition = new Vector3(surface.offset.x,
            surface.offset.y + surface.size.y * 0.5f + 0.72f, -0.05f);
        patch.AddComponent<DoodlikHazard>().InitializePatch(camera,
            new Vector2(0.19f, 0.18f), new Vector3(1.7f, 2f, 1f));
        GameObject seed = new GameObject("Preview seed");
        seed.transform.position = new Vector3(3f, 30f, 0f);
        HazardDrawing.Attach(seed.transform, DoodlikHazard.Kind.FlyingSeed).gameObject.layer = 30;
        RenderTexture render = new RenderTexture(1000, 560, 24);
        camera.targetTexture = render;
        camera.Render();
        RenderTexture.active = render;
        Texture2D image = new Texture2D(1000, 560, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1000, 560), 0, 0);
        image.Apply();
        Directory.CreateDirectory("Logs");
        File.WriteAllBytes("Logs/Hazard-preview.png", image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(render);
        Object.DestroyImmediate(image);
        Debug.Log("HAZARD_PREVIEW Logs/Hazard-preview.png");
    }
}
#endif
