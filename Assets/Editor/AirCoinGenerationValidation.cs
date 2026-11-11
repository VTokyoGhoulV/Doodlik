using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// Synchronous Play Mode integration check. Unlike the witness tests this drives
// the production camera-frontier clock and observes actual RunPickup objects.
public static class AirCoinGenerationValidation
{
    private const float SandboxX = 8000f;
    private const float TestHeight = 320f;
    private const uint Seed = 0x7ac312u;
    private static readonly float[] Gaps = { 2.35f, 2.8f, 2.1f, 2.6f, 2.45f };
    private static readonly float[] Offsets = { 1.2f, -1.6f, 1.8f, -1.4f, 1.1f, -1.5f };

    // Separate EditMode entry point: actual scene/prefabs and synchronous Editor
    // hazard planning, not the deliberately moderate synthetic Play Mode route.
    public static void ValidateProduction() => ValidateProductionRange(360f);

    // The first golden fish needs eight successful optional placements after
    // height 200. Keep the original smoke range and offer a longer proof rather
    // than changing rarity merely to force a fish into the opening viewport.
    public static void ValidateProductionExtended() => ValidateProductionRange(720f);

    private static void ValidateProductionRange(float maximumHeight)
    {
        Require(Application.isBatchMode && !Application.isPlaying &&
            Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-"),
            "Production validation must run in an isolated .utmp/MenuValidation-* batch project, before Play Mode.");
        ValidateAerialBonusRouteLifetime();
        bool hadFormat = PlayerPrefs.HasKey(GameplayPreferences.OrientationKey);
        bool hadControl = PlayerPrefs.HasKey(GameplayPreferences.InertiaKey);
        int format = PlayerPrefs.GetInt(GameplayPreferences.OrientationKey);
        int control = PlayerPrefs.GetInt(GameplayPreferences.InertiaKey);
        float oldTimeScale = Time.timeScale;
        bool oldActive = RunFlow.RunActive, oldTransition = RunFlow.IsTransitioning;
        float startHeight = WorldZones.RunStartHeight, pointsPerUnit = WorldZones.PointsPerUnit;
        int score = WorldZones.CurrentScore;
        UnityEngine.Random.State random = UnityEngine.Random.state;
        SceneSetup[] scenes = EditorSceneManager.GetSceneManagerSetup();
        string progress = PlayerPrefs.GetString(PlayerProgress.SaveKey, "<absent>");
        string backup = PlayerPrefs.GetString(PlayerProgress.BackupKey, "<absent>");
        int formats = GameplayPreferences.IsDesktopBuild ? 1 : 2;
        try
        {
            RunFlow.RunActive = true;
            RunFlow.IsTransitioning = false;
            Time.timeScale = 1f;
            for (int f = 0; f < formats; f++)
            for (int c = 0; c < 2; c++)
            {
                PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, f);
                PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, c);
                ValidateProductionMode(f, c, maximumHeight);
            }
            Require(PlayerPrefs.GetString(PlayerProgress.SaveKey, "<absent>") == progress &&
                PlayerPrefs.GetString(PlayerProgress.BackupKey, "<absent>") == backup,
                "Production generation never changes saved wallet/inventory.");
            Debug.Log($"AIR_COIN_PRODUCTION_PASS modes={formats * 2} heightPerMode={maximumHeight} actualSpawner=true actualHazards=true");
        }
        finally
        {
            RunFlow.RunActive = false;
            WorldZones.ConfigureRun(startHeight, pointsPerUnit);
            WorldZones.SetScore(score);
            RunFlow.RunActive = oldActive;
            RunFlow.IsTransitioning = oldTransition;
            Time.timeScale = oldTimeScale;
            UnityEngine.Random.state = random;
            Restore(GameplayPreferences.OrientationKey, hadFormat, format);
            Restore(GameplayPreferences.InertiaKey, hadControl, control);
            PlayerPrefs.Save();
            // A fresh batch Editor can start without any loaded/active scene.
            // Restoring that empty setup throws after all checks have passed.
            if (Array.Exists(scenes, scene => scene.isLoaded && scene.isActive))
                EditorSceneManager.RestoreSceneManagerSetup(scenes);
        }
    }

    private static void ValidateAerialBonusRouteLifetime()
    {
        GameObject root = new GameObject("Aerial bonus route lifetime validation");
        root.hideFlags = HideFlags.DontSave;
        GameObject sourceObject = new GameObject("Source");
        GameObject targetObject = new GameObject("Target");
        sourceObject.transform.SetParent(root.transform, false);
        targetObject.transform.SetParent(root.transform, false);
        try
        {
            BoxCollider2D source = sourceObject.AddComponent<BoxCollider2D>();
            BoxCollider2D target = targetObject.AddComponent<BoxCollider2D>();
            Require(AirCoinSpawner.HasLiveSurfaces(source, target),
                "Live route surfaces permit aerial relocation.");
            source.enabled = false;
            Require(!AirCoinSpawner.HasLiveSurfaces(source, target),
                "A crumbled source cancels aerial relocation.");
            source.enabled = true;
            target.enabled = false;
            Require(!AirCoinSpawner.HasLiveSurfaces(source, target),
                "A disabled target cancels aerial relocation.");
            target.enabled = true;
            root.SetActive(false);
            Require(!AirCoinSpawner.HasLiveSurfaces(source, target),
                "Inactive route surfaces cancel aerial relocation.");
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void ValidateProductionMode(int format, int control, float maximumHeight)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        Player player = Object.FindAnyObjectByType<Player>();
        Spawner spawner = Object.FindAnyObjectByType<Spawner>();
        Camera camera = Camera.main;
        Require(player != null && spawner != null && camera != null && spawner.platformPrefab != null,
            "The production SampleScene has its player, camera, spawner and platform prefab.");
        camera.aspect = format == 1 ? 9f / 20f : 16f / 9f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        var scaled = new HashSet<PortraitModelScaler>();
        // AddComponent in EditMode does not run Awake. Invoke the real scaler
        // lifecycle once, instead of treating unscaled Editor bounds as a bug.
        PortraitModelScaler.Attach(player.gameObject);
        ApplyPendingPortraitScalers(scaled);
        uint seed = Seed + (uint)(format * 100 + control);
        UnityEngine.Random.InitState(unchecked((int)seed));
        typeof(Spawner).GetMethod("Start", fields).Invoke(spawner, null);
        ApplyPendingPortraitScalers(scaled);
        PickupSpawnSchedule schedule = (PickupSpawnSchedule)typeof(Spawner).GetField("pickupSchedule", fields).GetValue(spawner);
        // Re-seed the existing shared schedule before any camera Advance, without
        // replacing its object or the platform references in the coin planner.
        var deterministic = new PickupSpawnSchedule(seed);
        foreach (FieldInfo field in typeof(PickupSpawnSchedule).GetFields(fields))
            field.SetValue(schedule, field.GetValue(deterministic));
        MethodInfo spawn = typeof(Spawner).GetMethod("SpawnPlatform", fields);
        MethodInfo update = typeof(Spawner).GetMethod("Update", fields);
        FieldInfo highest = typeof(Spawner).GetField("highestPlatformY", fields);
        FieldInfo lastX = typeof(Spawner).GetField("lastPlatformX", fields);
        HazardSpawner hazards = (HazardSpawner)typeof(Spawner).GetField("hazardSpawner", fields).GetValue(spawner);
        Require(hazards != null, "Production traps remain enabled; no balance setting was weakened for this test.");
        var observed = new HashSet<RunPickup>();
        var observedAerialBonuses = new HashSet<RunPickup>();
        var surfaces = new List<Rect>();
        var danger = new List<Rect>();
        int coins = 0, bonuses = 0, aerialBonuses = 0, hazardCount = 0, calls = 0, eligibleCoins = 0, goldenCoins = 0;
        int rockets = 0, bumblebees = 0;
        float minGap = float.PositiveInfinity, maxGap = 0f, maximumDx = 0f;
        double maximumAdvanceMs = 0, totalAdvanceMs = 0, maximumPlatformMs = 0;
        var seenHazards = new HashSet<DoodlikHazard>();
        try
        {
            for (float y = 0f; y <= maximumHeight; y += 0.75f)
            {
                camera.transform.position = new Vector3(0f, y, -10f);
                float frontier = y + camera.orthographicSize + hazards.LookAhead;
                while ((float)highest.GetValue(spawner) < frontier)
                {
                    float previousY = (float)highest.GetValue(spawner), previousX = (float)lastX.GetValue(spawner);
                    Stopwatch watch = Stopwatch.StartNew();
                    spawn.Invoke(spawner, null);
                    watch.Stop();
                    maximumPlatformMs = Math.Max(maximumPlatformMs, watch.Elapsed.TotalMilliseconds);
                    ApplyPendingPortraitScalers(scaled);
                    float gap = (float)highest.GetValue(spawner) - previousY;
                    minGap = Mathf.Min(minGap, gap); maxGap = Mathf.Max(maxGap, gap);
                    maximumDx = Mathf.Max(maximumDx, Mathf.Abs(TrapGeometry.WrappedDelta(previousX,
                        (float)lastX.GetValue(spawner), camera.orthographicSize * camera.aspect * 2f)));
                }
                Physics2D.SyncTransforms();
                // Geometry is already ahead, so this is the actual Update hook
                // with coin planning cost, not the synchronous Editor hazard cost.
                Stopwatch advanceWatch = Stopwatch.StartNew();
                update.Invoke(spawner, null);
                advanceWatch.Stop();
                maximumAdvanceMs = Math.Max(maximumAdvanceMs, advanceWatch.Elapsed.TotalMilliseconds);
                totalAdvanceMs += advanceWatch.Elapsed.TotalMilliseconds; calls++;
                surfaces.Clear(); danger.Clear();
                foreach (GameObject platform in GameObject.FindGameObjectsWithTag("Platform"))
                {
                    Collider2D collider = platform.GetComponent<Collider2D>();
                    if (collider == null || !collider.enabled) continue;
                    Bounds bounds = collider.bounds;
                    SpriteRenderer sprite = platform.GetComponent<SpriteRenderer>();
                    if (sprite != null) bounds.Encapsulate(sprite.bounds);
                    float patrol = platform.GetComponent<MovingPlatform>() != null ? spawner.movingPlatformRange.y * 2f : 0f;
                    surfaces.Add(new Rect(bounds.min.x - patrol, bounds.min.y, bounds.size.x + patrol * 2f, bounds.size.y));
                }
                foreach (DoodlikHazard hazard in Object.FindObjectsByType<DoodlikHazard>(FindObjectsSortMode.None))
                {
                    danger.Add(hazard.PatrolBounds);
                    if (seenHazards.Add(hazard))
                    {
                        hazardCount++;
                        if (hazard.Type == DoodlikHazard.Kind.Bumblebee) bumblebees++;
                    }
                }
                foreach (RunPickup pickup in Object.FindObjectsByType<RunPickup>(FindObjectsSortMode.None))
                {
                    // Recheck every frame, not just initial creation: later
                    // routes must not add a hazard over an existing air bonus.
                    if (!pickup.IsCoinReward && pickup.transform.parent == null)
                    {
                        if (observedAerialBonuses.Add(pickup)) aerialBonuses++;
                        Require(AirCoinSpawner.IsAirClear(pickup.transform.position,
                            GameplayPreferences.ModelScale,
                            camera.orthographicSize * camera.aspect * 2f, surfaces, danger),
                            "Aerial bonus stays clear of newly generated platforms and hazards.");
                    }
                    if (!observed.Add(pickup)) continue;
                    if (!pickup.IsCoinReward)
                    {
                        bonuses++;
                        if (pickup.KindId == 4) rockets++;
                        continue;
                    }
                    Require(pickup.transform.parent == null, "Production coins have no platform parent.");
                    if (WorldZones.ScoreAtHeight(pickup.transform.position.y) >= AirCoinSpawner.GoldenFishMinimumHeight)
                    {
                        eligibleCoins++;
                        bool expectedGolden = eligibleCoins % AirCoinSpawner.GoldenFishInterval == 0;
                        Require((pickup.KindId == 5) == expectedGolden,
                            "Every eighth successful eligible production coin is replaced by a fish, without extra rewards.");
                    }
                    else Require(pickup.KindId == 0, "Golden fish cannot appear before its raw-height introduction.");
                    if (pickup.KindId == 5) goldenCoins++;
                    Require(AirCoinSpawner.IsAirClear(pickup.transform.position, GameplayPreferences.ModelScale,
                        camera.orthographicSize * camera.aspect * 2f, surfaces, danger),
                        "Production coin clears actual platform art, moving patrols and hazard regions.");
                    coins++;
                }
                float cleanup = y - camera.orthographicSize - 3f;
                foreach (GameObject platform in GameObject.FindGameObjectsWithTag("Platform"))
                    if (platform.transform.position.y < cleanup) Object.DestroyImmediate(platform);
                foreach (DoodlikHazard hazard in Object.FindObjectsByType<DoodlikHazard>(FindObjectsSortMode.None))
                    if (hazard.PatrolBounds.yMax < cleanup) Object.DestroyImmediate(hazard.transform.root.gameObject);
                foreach (RunPickup pickup in Object.FindObjectsByType<RunPickup>(FindObjectsSortMode.None))
                    if (pickup.transform.position.y < cleanup) Object.DestroyImmediate(pickup.gameObject);
            }
            Debug.Log($"AIR_COIN_PRODUCTION_MODE format={format} inertia={control} seed={seed} coins={coins} eligibleCoins={eligibleCoins} golden={goldenCoins} bonuses={bonuses} rockets={rockets} aerialBonuses={aerialBonuses} hazards={hazardCount} bumblebees={bumblebees} " +
                $"gapRange={minGap:F2}..{maxGap:F2} maximumWrappedDx={maximumDx:F2} " +
                $"meanAdvanceMs={totalAdvanceMs / Math.Max(1, calls):F3} worstAdvanceMs={maximumAdvanceMs:F3} " +
                $"worstEditorPlatformAndHazardMs={maximumPlatformMs:F3}");
            Require(coins > 0, $"Actual production routes must create optional air coins: format={format} inertia={control} seed={seed}.");
            Require(bonuses > 0 && aerialBonuses > 0,
                $"Actual production routes must create rare safe aerial bonuses: format={format} inertia={control} seed={seed}.");
            Require(rockets > 0,
                $"A real production route past height 250 must contain its independent rare rocket: format={format} inertia={control} seed={seed}.");
            Require(maximumHeight <= 360f || goldenCoins > 0,
                $"The extended real route must demonstrate a reachable golden replacement: format={format} inertia={control} seed={seed}.");
        }
        finally { CoinRouteReservations.ClearForCamera(camera); }
    }

    private static void ApplyPendingPortraitScalers(HashSet<PortraitModelScaler> initialized)
    {
        MethodInfo awake = typeof(PortraitModelScaler).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (PortraitModelScaler scaler in Object.FindObjectsByType<PortraitModelScaler>(FindObjectsSortMode.None))
            if (initialized.Add(scaler)) awake.Invoke(scaler, null);
        Physics2D.SyncTransforms();
    }

    public static void Validate()
    {
        Require(Application.isPlaying, "Generation validation requires Play Mode.");
        Player source = Object.FindAnyObjectByType<Player>();
        Require(source != null && source.GetComponent<SpriteRenderer>() != null &&
            source.GetComponent<BoxCollider2D>() != null, "The actual cat sprite and paw collider exist.");
        bool hadFormat = PlayerPrefs.HasKey(GameplayPreferences.OrientationKey);
        bool hadControl = PlayerPrefs.HasKey(GameplayPreferences.InertiaKey);
        int format = PlayerPrefs.GetInt(GameplayPreferences.OrientationKey);
        int control = PlayerPrefs.GetInt(GameplayPreferences.InertiaKey);
        float oldTimeScale = Time.timeScale;
        bool oldActive = RunFlow.RunActive, oldTransition = RunFlow.IsTransitioning;
        // Only read saves. The test never calls PlayerProgress or collects a coin.
        string progress = PlayerPrefs.GetString(PlayerProgress.SaveKey, "<absent>");
        string backup = PlayerPrefs.GetString(PlayerProgress.BackupKey, "<absent>");
        var scoreKeys = new List<string>(Player.ModeHighScoreKeys) { Player.HighScoreKey };
        var scores = new Dictionary<string, int>();
        foreach (string key in scoreKeys) scores.Add(key, PlayerPrefs.GetInt(key, int.MinValue));
        Vector3 authoredScale = source.transform.localScale / GameplayPreferences.ModelScale;
        int formats = GameplayPreferences.IsDesktopBuild ? 1 : 2;
        int totalCoins = 0;
        try
        {
            RunFlow.RunActive = true;
            RunFlow.IsTransitioning = false;
            Time.timeScale = 1f;
            for (int f = 0; f < formats; f++)
            for (int c = 0; c < 2; c++)
            {
                // Do not apply device orientation or resize any live scene object.
                PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, f);
                PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, c);
                totalCoins += ValidateMode(source, authoredScale, f, c);
            }
            Require(PlayerPrefs.GetString(PlayerProgress.SaveKey, "<absent>") == progress &&
                PlayerPrefs.GetString(PlayerProgress.BackupKey, "<absent>") == backup,
                "Coin generation never changes the saved wallet or inventory.");
            foreach (string key in scoreKeys)
                Require(PlayerPrefs.GetInt(key, int.MinValue) == scores[key], "Records remain untouched: " + key);
            Debug.Log($"AIR_COIN_GENERATION_PASS modes={formats * 2} actualCoins={totalCoins} heightPerMode={TestHeight}");
        }
        finally
        {
            RunFlow.RunActive = oldActive;
            RunFlow.IsTransitioning = oldTransition;
            Time.timeScale = oldTimeScale;
            Restore(GameplayPreferences.OrientationKey, hadFormat, format);
            Restore(GameplayPreferences.InertiaKey, hadControl, control);
            PlayerPrefs.Save();
        }
    }

    private static int ValidateMode(Player source, Vector3 authoredScale, int format, int control)
    {
        GameObject sandbox = new GameObject("Temporary air-coin generation fixture");
        var platforms = new List<BoxCollider2D>();
        var spawned = new List<RunPickup>();
        var observed = new HashSet<RunPickup>(Object.FindObjectsByType<RunPickup>(FindObjectsSortMode.None));
        var bands = new List<PickupSpawnSchedule.CoinBand>();
        var occupiedBands = new HashSet<int>();
        var surfaces = new List<Rect>();
        var emptyDanger = new List<Rect>();
        Camera camera = null;
        int calls = 0, opportunities = 0, coinCount = 0, platformCount = 0, eligibleCoins = 0;
        double elapsedMs = 0, worstMs = 0, bandElapsedMs = 0;
        float previousCoinY = float.NegativeInfinity;
        try
        {
            GameObject cat = new GameObject("Disabled generation cat");
            cat.transform.SetParent(sandbox.transform, false);
            cat.transform.position = new Vector3(SandboxX, 0f, 0f);
            cat.transform.localScale = authoredScale * GameplayPreferences.ModelScale;
            SpriteRenderer renderer = cat.AddComponent<SpriteRenderer>();
            renderer.sprite = source.GetComponent<SpriteRenderer>().sprite;
            Require(renderer.sprite != null, "The cat has an imported sprite.");
            BoxCollider2D feet = cat.AddComponent<BoxCollider2D>();
            EditorUtility.CopySerialized(source.GetComponent<BoxCollider2D>(), feet);
            Rigidbody2D body = cat.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            Player player = cat.AddComponent<Player>();
            EditorUtility.CopySerialized(source, player);
            player.enabled = false;
            renderer.enabled = false;

            GameObject cameraObject = new GameObject("Disabled generation camera", typeof(Camera));
            cameraObject.transform.SetParent(sandbox.transform, false);
            camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            float width = format == 1 ? 5.625f : 17.7778f;
            camera.aspect = width / (2f * camera.orthographicSize);
            camera.transform.position = new Vector3(SandboxX, -4f, -10f);
            float scale = GameplayPreferences.ModelScale;
            float movementScale = GameplayPreferences.HorizontalMovementScale;
            float apex = JumpReachability.Simulate(player, 0f, GameplayPreferences.UseInertia, movementScale).Apex;
            // The current 10-strength jump peaks just below the old 3-unit
            // threshold once gravity and the physics step are applied.
            Require(player.NormalJumpForce >= 10f && apex > 2.3f,
                $"The fixture uses normal game jump physics, not a zero/default probe (force={player.NormalJumpForce:F2}, apex={apex:F2}).");
            var schedule = new PickupSpawnSchedule(Seed);
            var expectedSchedule = new PickupSpawnSchedule(Seed);
            var planner = new AirCoinSpawner(player, camera, schedule, 1.2f);
            float highest = -2f, routeX = SandboxX;

            for (float cameraY = -4f; cameraY <= TestHeight; cameraY += 0.75f)
            {
                camera.transform.position = new Vector3(SandboxX, cameraY, -10f);
                float top = cameraY + camera.orthographicSize;
                // Enough finalized geometry for the same pending-hazard margin
                // as production; all supports are ordinary static route landings.
                while (highest < top + apex + 8f)
                {
                    highest += Gaps[platformCount % Gaps.Length];
                    routeX += Offsets[platformCount % Offsets.Length] * movementScale;
                    routeX = SandboxX + TrapGeometry.WrappedDelta(SandboxX, routeX, width);
                    GameObject surface = new GameObject("Fixture platform " + platformCount);
                    surface.tag = "Platform";
                    surface.transform.SetParent(sandbox.transform, false);
                    surface.transform.position = new Vector3(routeX, highest - 0.06f * scale, 0f);
                    BoxCollider2D collider = surface.AddComponent<BoxCollider2D>();
                    collider.size = new Vector2(1.737f * scale, 0.12f * scale);
                    platforms.Add(collider);
                    planner.RegisterPlatform(surface, 0f);
                    platformCount++;
                }
                Physics2D.SyncTransforms();
                if (calls == 0)
                    foreach (RunPickup pickup in Object.FindObjectsByType<RunPickup>(FindObjectsSortMode.None))
                        Require(observed.Contains(pickup), "RegisterPlatform alone never spawns a coin.");
                float knownHeight = Mathf.Min(top + 4f, highest - apex - 4.5f);
                int beforeBands = bands.Count;
                while (expectedSchedule.TryTakeCoinBand(knownHeight, out PickupSpawnSchedule.CoinBand band))
                {
                    if (bands.Count > 0)
                    {
                        float spacing = band.Minimum - bands[bands.Count - 1].Minimum;
                        Require(spacing >= 15f && spacing <= 25f, "Opportunity bands remain sparse, 15–25 world units apart.");
                    }
                    bands.Add(band);
                }
                int opened = bands.Count - beforeBands;
                opportunities += opened;
                Stopwatch clock = Stopwatch.StartNew();
                planner.Advance(highest, true);
                clock.Stop();
                double ms = clock.Elapsed.TotalMilliseconds;
                elapsedMs += ms; worstMs = Math.Max(worstMs, ms); calls++;
                if (opened != 0) bandElapsedMs += ms;

                surfaces.Clear();
                foreach (BoxCollider2D collider in platforms)
                {
                    Bounds bounds = collider.bounds;
                    surfaces.Add(new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y));
                }
                foreach (RunPickup coin in Object.FindObjectsByType<RunPickup>(FindObjectsSortMode.None))
                {
                    if (!observed.Add(coin)) continue;
                    spawned.Add(coin);
                    Require(coin.IsCoinReward && (coin.KindId == 0 && coin.Value == 1 || coin.KindId == 5 && coin.Value == 5),
                        "Advance only creates one normal or golden coin reward per verified detour, never bonus kinds.");
                    Require(coin.transform.parent == null, "Actual generated coins are never platform children.");
                    Vector2 point = coin.transform.position;
                    if (WorldZones.ScoreAtHeight(point.y) >= AirCoinSpawner.GoldenFishMinimumHeight)
                    {
                        eligibleCoins++;
                        Require((coin.KindId == 5) == (eligibleCoins % AirCoinSpawner.GoldenFishInterval == 0),
                            "Safe successful placements, not failed bands, drive the one-in-eight golden replacement clock.");
                    }
                    else Require(coin.KindId == 0, "No golden reward appears before raw height 200.");
                    Require(Mathf.Abs(point.x - SandboxX) <= width * 0.5f + 0.01f, "Coin is inside the current wrap interval.");
                    Require(AirCoinSpawner.IsAirClear(point, scale, width, surfaces, emptyDanger),
                        "Actual generated coin has visible free air around every platform surface.");
                    int matchingBand = bands.FindIndex(b => point.y >= b.Minimum && point.y <= b.Maximum);
                    Require(matchingBand >= 0 && occupiedBands.Add(matchingBand),
                        "At most one actual coin is created per opened height band.");
                    Require(point.y - previousCoinY >= 10.99f,
                        "Independent coin bands cannot collapse into a height cluster.");
                    previousCoinY = point.y;
                    coinCount++;
                }
                int beforeRepeat = Object.FindObjectsByType<RunPickup>(FindObjectsSortMode.None).Length;
                planner.Advance(highest, true);
                Require(Object.FindObjectsByType<RunPickup>(FindObjectsSortMode.None).Length == beforeRepeat,
                    "Repeated Advance at an unchanged frontier never duplicates a coin.");

                // Mirror normal off-screen cleanup without yielding a live frame.
                float cleanupY = cameraY - camera.orthographicSize - 3f;
                for (int i = platforms.Count - 1; i >= 0; i--)
                    if (platforms[i].bounds.max.y < cleanupY)
                    {
                        Object.DestroyImmediate(platforms[i].gameObject);
                        platforms.RemoveAt(i);
                    }
                foreach (RunPickup coin in spawned)
                    if (coin != null && coin.transform.position.y < cleanupY)
                        Object.DestroyImmediate(coin.gameObject);
            }
            Require(opportunities >= 10, "The advancing camera actually opens enough generation bands.");
            Require(coinCount > 0, $"Real Advance spawns coins: format={format} inertia={control}, opportunities={opportunities}.");
            Require(coinCount <= opportunities && coinCount <= Mathf.CeilToInt(TestHeight / 15f),
                "Actual density stays within the independent height-band budget.");
            Debug.Log($"AIR_COIN_GENERATION_MODE_PASS format={format} inertia={control} coins={coinCount} " +
                $"opportunities={opportunities} platforms={platformCount} advanceCalls={calls} " +
                $"meanAdvanceMs={elapsedMs / Math.Max(1, calls):F3} meanBandMs={bandElapsedMs / Math.Max(1, opportunities):F3} worstAdvanceMs={worstMs:F3}");
            return coinCount;
        }
        finally
        {
            if (camera != null) CoinRouteReservations.ClearForCamera(camera);
            foreach (RunPickup coin in spawned)
                if (coin != null) Object.DestroyImmediate(coin.gameObject);
            Object.DestroyImmediate(sandbox);
        }
    }

    private static void Restore(string key, bool existed, int value)
    {
        if (existed) PlayerPrefs.SetInt(key, value);
        else PlayerPrefs.DeleteKey(key);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Air coin generation validation: " + message);
    }
}
