#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Dedicated Editor/batch validation; preserves saved player preferences.
public static class WorldGenerationValidation
{
    private struct Node
    {
        public float x, y, range;
    }
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;

    public static void RunAll()
    {
        Run();
        RunStress();
        RunLayoutAudit();
    }

    public static void CompareJumpForces()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        Player player = Object.FindAnyObjectByType<Player>();
        float original = player.jumpForce;
        try
        {
            foreach (float force in new[] { 10f, 10.6f, 11f, 11.3f })
            {
                player.jumpForce = force / player.normalJumpMultiplier;
                for (int format = 0; format < (GameplayPreferences.IsDesktopBuild ? 1 : 2); format++)
                for (int control = 0; control < 2; control++)
                {
                    float scale = format == 1 ? 0.64f : 1f;
                    float apex = JumpReachability.Simulate(player, 0f,
                        control == 1, scale).Apex;
                    float reach12 = JumpReachability.Simulate(player, 1.2f,
                        control == 1, scale).HorizontalDistance;
                    float reach15 = JumpReachability.Simulate(player, 1.5f,
                        control == 1, scale).HorizontalDistance;
                    float reach19 = JumpReachability.Simulate(player, 1.9f,
                        control == 1, scale).HorizontalDistance;
                    Debug.Log($"JUMP_FORCE_COMPARISON force={force:F1} format={format} " +
                        $"control={control} apex={apex:F2} reach1.2={reach12:F2} " +
                        $"reach1.5={reach15:F2} reach1.9={reach19:F2}");
                }
            }
        }
        finally { player.jumpForce = original; }
    }

    public static void RunLayoutAudit()
    {
        bool hadFormat = PlayerPrefs.HasKey(GameplayPreferences.OrientationKey);
        bool hadControl = PlayerPrefs.HasKey(GameplayPreferences.InertiaKey);
        int formatValue = PlayerPrefs.GetInt(GameplayPreferences.OrientationKey);
        int controlValue = PlayerPrefs.GetInt(GameplayPreferences.InertiaKey);
        UnityEngine.Random.State random = UnityEngine.Random.state;
        try
        {
            int formats = GameplayPreferences.IsDesktopBuild ? 1 : 2;
            for (int format = 0; format < formats; format++)
            for (int control = 0; control < 2; control++)
            {
                int closePairs = 0, extraPlatforms = 0;
                string examples = "";
                for (int seed = 0; seed < 8; seed++)
                {
                    PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, format);
                    PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, control);
                    EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
                    Camera camera = Camera.main;
                    camera.aspect = format == 1 ? 9f / 20f : 16f / 9f;
                    Spawner spawner = Object.FindAnyObjectByType<Spawner>();
                    spawner.traps.enabled = false;
                    UnityEngine.Random.InitState(38400 + format * 100 + control * 10 + seed);
                    typeof(Spawner).GetMethod("Start", Private).Invoke(spawner, null);
                    if (seed == 0)
                    {
                        Player player = (Player)typeof(Spawner)
                            .GetField("playerMovement", Private).GetValue(spawner);
                        float scale = GameplayPreferences.HorizontalMovementScale;
                        float apex = JumpReachability.Simulate(player, 0f,
                            control == 1, scale).Apex;
                        float reach15 = JumpReachability.Simulate(player, 1.5f,
                            control == 1, scale).HorizontalDistance;
                        float reach18 = JumpReachability.Simulate(player, 1.8f,
                            control == 1, scale).HorizontalDistance;
                        float reach20 = JumpReachability.Simulate(player, 2f,
                            control == 1, scale).HorizontalDistance;
                        Debug.Log($"WORLD_LAYOUT_REACH format={format} control={control} " +
                            $"apex={apex:F2} reach1.5={reach15:F2} " +
                            $"reach1.8={reach18:F2} reach2.0={reach20:F2}");
                    }
                    MethodInfo spawn = typeof(Spawner).GetMethod("SpawnPlatform", Private);
                    for (int row = 0; row < 100; row++) spawn.Invoke(spawner, null);
                    GameObject[] platforms = GameObject.FindGameObjectsWithTag("Platform");
                    foreach (GameObject platform in platforms)
                        if (platform.name == "Connected route platform" ||
                            platform.name == "Choice platform" ||
                            platform.name == "Tempting high platform") extraPlatforms++;
                    for (int i = 0; i < platforms.Length; i++)
                    for (int j = i + 1; j < platforms.Length; j++)
                    {
                        Vector2 a = platforms[i].transform.position;
                        Vector2 b = platforms[j].transform.position;
                        float vertical = Mathf.Abs(a.y - b.y);
                        float horizontal = Mathf.Abs(a.x - b.x);
                        float modelScale = GameplayPreferences.ModelScale;
                        if (vertical < 0.35f * modelScale ||
                            vertical > 1.1f * modelScale ||
                            horizontal > 2f * modelScale)
                            continue;
                        closePairs++;
                        if (closePairs <= 12)
                            examples += $" seed={seed} ({platforms[i].name} {a.x:F2},{a.y:F2})" +
                                $"-({platforms[j].name} {b.x:F2},{b.y:F2})";
                    }
                }
                Debug.Log($"WORLD_LAYOUT_AUDIT format={format} control={control} " +
                    $"closePairs={closePairs} extraPlatforms={extraPlatforms} examples={examples}");
                Check(closePairs == 0 && extraPlatforms == 0,
                    $"mode={format}/{control} has crowded or extra route platforms: " +
                    $"closePairs={closePairs} extraPlatforms={extraPlatforms}{examples}");
            }
        }
        finally
        {
            Restore(GameplayPreferences.OrientationKey, hadFormat, formatValue);
            Restore(GameplayPreferences.InertiaKey, hadControl, controlValue);
            PlayerPrefs.Save();
            UnityEngine.Random.state = random;
            WorldZones.SetHeight(0f);
        }
    }

    public static void Run()
    {
        bool hadFormat = PlayerPrefs.HasKey(GameplayPreferences.OrientationKey);
        bool hadControl = PlayerPrefs.HasKey(GameplayPreferences.InertiaKey);
        int formatValue = PlayerPrefs.GetInt(GameplayPreferences.OrientationKey);
        int controlValue = PlayerPrefs.GetInt(GameplayPreferences.InertiaKey);
        UnityEngine.Random.State random = UnityEngine.Random.state;
        checks = 0;
        try
        {
            int formats = GameplayPreferences.IsDesktopBuild ? 1 : 2;
            for (int format = 0; format < formats; format++)
            for (int control = 0; control < 2; control++)
                CheckRoutes(format, control);
            Check(WorldZones.StartScore(WorldZones.Zone.Moon) == WorldZones.GoalScore,
                "arrival in space matches the map goal");
            for (int zone = 1; zone < WorldZones.ZoneCount; zone++)
            {
                int start = WorldZones.StartScore((WorldZones.Zone)zone);
                Check(WorldZones.AtScore(start - 1) == (WorldZones.Zone)(zone - 1),
                    "previous zone lasts until its next boundary");
                Check(WorldZones.AtScore(start) == (WorldZones.Zone)zone,
                    "new zone begins at its boundary");
            }
            Debug.Log("WORLD_GENERATION_PASS checks=" + checks + " formats=" + formats);
        }
        finally
        {
            Restore(GameplayPreferences.OrientationKey, hadFormat, formatValue);
            Restore(GameplayPreferences.InertiaKey, hadControl, controlValue);
            PlayerPrefs.Save();
            UnityEngine.Random.state = random;
            WorldZones.SetHeight(0f);
        }
    }

    public static void RunStress()
    {
        bool hadFormat = PlayerPrefs.HasKey(GameplayPreferences.OrientationKey);
        bool hadControl = PlayerPrefs.HasKey(GameplayPreferences.InertiaKey);
        int formatValue = PlayerPrefs.GetInt(GameplayPreferences.OrientationKey);
        int controlValue = PlayerPrefs.GetInt(GameplayPreferences.InertiaKey);
        UnityEngine.Random.State random = UnityEngine.Random.state;
        checks = 0;
        try
        {
            int formats = GameplayPreferences.IsDesktopBuild ? 1 : 2;
            for (int format = 0; format < formats; format++)
            for (int control = 0; control < 2; control++)
            for (int seed = 0; seed < 3; seed++)
            {
                PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, format);
                PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, control);
                EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
                Camera camera = Camera.main;
                camera.aspect = format == 1 ? 9f / 20f : 16f / 9f;
                Spawner spawner = Object.FindAnyObjectByType<Spawner>();
                spawner.traps.enabled = false;
                UnityEngine.Random.InitState(81100 + format * 100 + control * 10 + seed);
                typeof(Spawner).GetMethod("Start", Private).Invoke(spawner, null);
                Player player = (Player)typeof(Spawner).GetField("playerMovement", Private).GetValue(spawner);
                MethodInfo spawn = typeof(Spawner).GetMethod("SpawnPlatform", Private);
                FieldInfo wideTier = typeof(Spawner).GetField("lastWideJumpTier", Private);
                int stressPlatforms = 0;
                for (int row = 0; row < 120; row++)
                {
                    Node[] previous = ReadTier(spawner);
                    int wideBefore = (int)wideTier.GetValue(spawner);
                    spawn.Invoke(spawner, null);
                    Node[] next = ReadTier(spawner);
                    bool widePlaced = (int)wideTier.GetValue(spawner) != wideBefore;
                    Check(previous.Length == 1 && next.Length == 1,
                        $"seed={seed} row={row} mode={format}/{control} must have one route");
                    stressPlatforms += next.Length;
                    float width = camera.orthographicSize * camera.aspect * 2f;
                    foreach (Node source in previous)
                    {
                        bool linked = false;
                        foreach (Node target in next)
                        {
                            JumpReachability.Result jump = widePlaced
                                ? JumpReachability.SimulatePrepared(player,
                                    target.y - source.y, control == 1,
                                    GameplayPreferences.HorizontalMovementScale)
                                : JumpReachability.Simulate(player,
                                    target.y - source.y, control == 1,
                                    GameplayPreferences.HorizontalMovementScale,
                                    source.range > 0f ? player.maximumInheritedSpeed *
                                        GameplayPreferences.HorizontalMovementScale : 0f);
                            float distance = Mathf.Abs(TrapGeometry.WrappedDelta(
                                source.x, target.x, width));
                            if (jump.CanLand && distance + source.range + target.range <=
                                jump.HorizontalDistance * (widePlaced ? 0.87f :
                                    control == 1 ? 0.9f : 0.92f) + 0.001f)
                            { linked = true; break; }
                        }
                        Check(linked, $"seed={seed} row={row} mode={format}/{control} has no way upward");
                    }
                    camera.transform.position = new Vector3(0f, next[0].y - 18f, -10f);
                    foreach (GameObject platform in GameObject.FindGameObjectsWithTag("Platform"))
                        if (platform.transform.position.y < next[0].y - 30f)
                            Object.DestroyImmediate(platform);
                }
                Check(stressPlatforms <= 180,
                    $"seed={seed} mode={format}/{control} field is too dense: {stressPlatforms}/120");
                Check(Object.FindObjectsByType<DoodlikHazard>().Length == 0,
                    "disabling traps also disables partial thorn patches");
            }
            Debug.Log("WORLD_FIELD_STRESS_PASS checks=" + checks + " seedsPerMode=3");
        }
        finally
        {
            Restore(GameplayPreferences.OrientationKey, hadFormat, formatValue);
            Restore(GameplayPreferences.InertiaKey, hadControl, controlValue);
            PlayerPrefs.Save();
            UnityEngine.Random.state = random;
            WorldZones.SetHeight(0f);
        }
    }

    private static void CheckRoutes(int format, int control)
    {
        PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, format);
        PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, control);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        Camera camera = Camera.main;
        camera.aspect = format == 1 ? 9f / 20f : 16f / 9f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        Spawner spawner = Object.FindAnyObjectByType<Spawner>();
        UnityEngine.Random.InitState(43200 + format * 10 + control);
        typeof(Spawner).GetMethod("Start", Private).Invoke(spawner, null);
        Player player = (Player)typeof(Spawner).GetField("playerMovement", Private).GetValue(spawner);
        MethodInfo spawn = typeof(Spawner).GetMethod("SpawnPlatform", Private);
        FieldInfo height = typeof(Spawner).GetField("highestPlatformY", Private);
        FieldInfo x = typeof(Spawner).GetField("lastPlatformX", Private);
        FieldInfo range = typeof(Spawner).GetField("lastPlatformMovementRange", Private);
        FieldInfo precisionTier = typeof(Spawner).GetField("lastPrecisionJumpTier", Private);
        FieldInfo wideTier = typeof(Spawner).GetField("lastWideJumpTier", Private);
        FieldInfo approachDirection = typeof(Spawner).GetField("lastApproachDirection", Private);
        float scale = GameplayPreferences.HorizontalMovementScale;
        JumpReachability.Result landscape = JumpReachability.Simulate(player, 2f, control == 1, 1f);
        JumpReachability.Result portrait = JumpReachability.Simulate(player, 2f, control == 1, 0.64f);
        float normalApex = JumpReachability.Simulate(player, 0f, control == 1, scale).Apex;
        Debug.Log($"WORLD_JUMP_MODEL format={format} control={control} apex={normalApex:F2} " +
            $"reach1.3={JumpReachability.Simulate(player, 1.3f, control == 1, scale).HorizontalDistance:F2} " +
            $"reach1.65={JumpReachability.Simulate(player, 1.65f, control == 1, scale).HorizontalDistance:F2} " +
            $"reach2.0={JumpReachability.Simulate(player, 2f, control == 1, scale).HorizontalDistance:F2}");
        Check(landscape.CanLand && portrait.CanLand, "normal target reachable");
        Check(portrait.HorizontalDistance < landscape.HorizontalDistance * 0.65f,
            "portrait reach accounts for slower horizontal movement");
        Check(!JumpReachability.Simulate(player, landscape.Apex + 0.1f, control == 1, scale).CanLand,
            "above-apex jump rejected");
        if (control == 1)
        {
            JumpReachability.Result prepared = JumpReachability.SimulatePrepared(player,
                1f, true, scale);
            JumpReachability.Result reversed = JumpReachability.Simulate(player,
                1f, true, scale);
            Check(prepared.CanLand && prepared.HorizontalDistance >
                reversed.HorizontalDistance * 1.3f,
                "momentum-aligned jumps should reach materially farther than reversals");
        }

        int clippedPlatforms = 0, stationaryLandings = 0, stationaryStreak = 0,
            longestStationaryStreak = 0, earlyChallengeRows = 0;
        int farRows = 0, lowerFarRows = 0;
        int preparedWideRows = 0;
        int precisionRows = 0, precisionReversals = 0;
        int sideTraversals = 0, lastVisitedSide = 0;
        float sumGap = 0f, sumHorizontal = 0f;
        int[] horizontalBands = new int[5];
        for (int i = 0; i < 600; i++)
        {
            Check(GameplayPreferences.UseInertia == (control == 1) &&
                Mathf.Abs(GameplayPreferences.HorizontalMovementScale - scale) < 0.001f,
                $"preferences changed during generation row={i} mode={format}/{control} " +
                $"actual={GameplayPreferences.IsPortrait}/{GameplayPreferences.UseInertia} " +
                $"scale={scale:F2}/{GameplayPreferences.HorizontalMovementScale:F2} " +
                $"stored={PlayerPrefs.GetInt(GameplayPreferences.OrientationKey, -1)}/" +
                $"{PlayerPrefs.GetInt(GameplayPreferences.InertiaKey, -1)}");
            float widthBefore = camera.orthographicSize * camera.aspect * 2f;
            Node[] previousTier = ReadTier(spawner);
            float fromY = (float)height.GetValue(spawner);
            float fromX = (float)x.GetValue(spawner);
            float sourceRange = (float)range.GetValue(spawner);
            int precisionBefore = (int)precisionTier.GetValue(spawner);
            int wideBefore = (int)wideTier.GetValue(spawner);
            int previousApproach = (int)approachDirection.GetValue(spawner);
            int platformCountBefore = GameObject.FindGameObjectsWithTag("Platform").Length;
            spawn.Invoke(spawner, null);
            Check(Mathf.Abs(camera.orthographicSize * camera.aspect * 2f - widthBefore) < 0.001f,
                $"camera width changed during generation row={i} mode={format}/{control}");
            float toY = (float)height.GetValue(spawner);
            float toX = (float)x.GetValue(spawner);
            float targetRange = (float)range.GetValue(spawner);
            if (i < 60 && (targetRange > 0f || (bool)typeof(Spawner)
                .GetField("lastPlatformWasNarrow", Private).GetValue(spawner)))
                earlyChallengeRows++;
            Node[] nextTier = ReadTier(spawner);
            bool precisionPlaced = (int)precisionTier.GetValue(spawner) != precisionBefore;
            bool widePlaced = (int)wideTier.GetValue(spawner) != wideBefore;
            Check(previousTier.Length == 1 && nextTier.Length == 1,
                $"generator must keep one path row={i} mode={format}/{control}");
            GameObject[] platformsAfter = GameObject.FindGameObjectsWithTag("Platform");
            Check(platformsAfter.Length == platformCountBefore + 1,
                $"generator added more than one platform row={i} mode={format}/{control}");
            foreach (GameObject platform in platformsAfter)
                Check(platform.name != "Choice platform" &&
                    platform.name != "Connected route platform" &&
                    platform.name != "Tempting high platform",
                    $"obsolete side route row={i} mode={format}/{control}");
            if (precisionPlaced)
            {
                precisionRows++;
                float precisionTravel = TrapGeometry.WrappedDelta(fromX, toX,
                    camera.orthographicSize * camera.aspect * 2f);
                if (previousApproach != 0 && precisionTravel * previousApproach < 0f)
                    precisionReversals++;
                Check(sourceRange <= 0f && targetRange <= 0f,
                    $"precision jump uses stationary takeoff and landing row={i} mode={format}/{control}");
            }
            float halfScreen = camera.orthographicSize * camera.aspect;
            int side = toX > halfScreen * 0.45f ? 1 :
                toX < -halfScreen * 0.45f ? -1 : 0;
            if (side != 0)
            {
                if (lastVisitedSide != 0 && lastVisitedSide != side)
                    sideTraversals++;
                lastVisitedSide = side;
            }
            foreach (Node node in nextTier)
            {
                float normalizedX = (node.x - camera.transform.position.x + halfScreen) /
                    (halfScreen * 2f);
                int band = Mathf.Clamp(Mathf.FloorToInt(normalizedX * horizontalBands.Length),
                    0, horizontalBands.Length - 1);
                horizontalBands[band]++;
                if (normalizedX < 0f || normalizedX > 1f) clippedPlatforms++;
            }
            foreach (Node source in previousTier)
            {
                bool connected = false;
                string distances = "";
                foreach (Node target in nextTier)
                {
                    JumpReachability.Result option = widePlaced
                        ? JumpReachability.SimulatePrepared(player,
                            target.y - source.y, control == 1, scale)
                        : JumpReachability.Simulate(player,
                            target.y - source.y, control == 1, scale,
                            source.range > 0f ? player.maximumInheritedSpeed * scale : 0f);
                    float lateral = Mathf.Abs(TrapGeometry.WrappedDelta(source.x, target.x,
                        camera.orthographicSize * camera.aspect * 2f));
                    distances += $" [gap={target.y - source.y:F2} x={lateral:F2} " +
                        $"reach={option.HorizontalDistance * (widePlaced ? 0.87f : control == 1 ? 0.9f : 0.92f):F2} " +
                        $"land={option.CanLand}]";
                    if (option.CanLand && lateral + source.range + target.range <=
                        option.HorizontalDistance * (widePlaced ? 0.87f :
                            control == 1 ? 0.9f : 0.92f) + 0.001f)
                    { connected = true; break; }
                }
                string targets = "";
                foreach (Node target in nextTier)
                    targets += $" ({target.x:F2},{target.y:F2},range={target.range:F2})";
                Check(connected, $"field platform has a safe successor row={i} format={format} control={control} " +
                    $"source=({source.x:F2},{source.y:F2},range={source.range:F2}) " +
                    $"width={camera.orthographicSize * camera.aspect * 2f:F2} " +
                    $"prefs={GameplayPreferences.IsPortrait}/{GameplayPreferences.UseInertia} " +
                    $"scale={scale:F2}/{GameplayPreferences.HorizontalMovementScale:F2} " +
                    $"targets={targets} {distances}");
            }
            JumpReachability.Result jump = widePlaced
                ? JumpReachability.SimulatePrepared(player,
                    toY - fromY, control == 1, scale)
                : JumpReachability.Simulate(player,
                    toY - fromY, control == 1, scale,
                    sourceRange > 0f ? player.maximumInheritedSpeed * scale : 0f);
            float distance = Mathf.Abs(TrapGeometry.WrappedDelta(fromX, toX,
                camera.orthographicSize * camera.aspect * 2f));
            if (widePlaced)
            {
                preparedWideRows++;
                float travel = TrapGeometry.WrappedDelta(fromX, toX,
                    camera.orthographicSize * camera.aspect * 2f);
                Check(format == 0,
                    $"low-and-wide leap must not turn into a free portrait step row={i} control={control}");
                Check(previousTier.Length == 1 && nextTier.Length == 1 &&
                    sourceRange <= 0f && targetRange <= 0f &&
                    previousApproach != 0 && travel * previousApproach > 0f,
                    $"wide leap needs a stationary momentum-aligned route row={i} mode={format}/{control}");
                Check(distance <= jump.HorizontalDistance * 0.87f + 0.001f,
                    $"wide leap respects prepared-jump safety margin row={i} mode={format}/{control}");
            }
            float gapHeight = toY - fromY;
            if (format == 1 && precisionPlaced)
                Check(gapHeight >= jump.Apex * (control == 1 ? 0.8f : 0.84f),
                    $"portrait precision landing needs limited air time row={i} control={control} gap={gapHeight:F2} apex={jump.Apex:F2}");
            if (!widePlaced && !precisionPlaced && sourceRange <= 0f && targetRange <= 0f)
                Check(gapHeight >= 1.25f,
                    $"failed wide leap must restore ordinary height row={i} mode={format}/{control} gap={gapHeight:F2}");
            float farThreshold = Mathf.Min(camera.orthographicSize * camera.aspect * 0.8f,
                (control == 1 ? 2.25f : 3.6f) * scale);
            sumGap += gapHeight;
            sumHorizontal += distance;
            if (distance >= farThreshold)
            {
                farRows++;
                if (gapHeight <= 1.5f) lowerFarRows++;
            }
            float stationaryWidth = (float)typeof(Spawner)
                .GetMethod("StationaryLandingWidth", Private).Invoke(spawner, null);
            if (precisionPlaced)
                Check(distance >= stationaryWidth * 1.05f - 0.001f,
                    $"precision landing requires steering beyond passive overlap row={i} mode={format}/{control}");
            bool stationaryLanding = sourceRange <= 0f && targetRange <= 0f &&
                distance < stationaryWidth;
            if (stationaryLanding) stationaryLandings++;
            stationaryStreak = stationaryLanding ? stationaryStreak + 1 : 0;
            longestStationaryStreak = Mathf.Max(longestStationaryStreak,
                stationaryStreak);
            Check(jump.CanLand, "route vertical reach " + i);
            Check(distance + sourceRange + targetRange <= jump.HorizontalDistance + 0.001f,
                $"route horizontal reach row={i} format={format} control={control} need={distance + sourceRange + targetRange} reach={jump.HorizontalDistance}");
            camera.transform.position = new Vector3(0f, toY - 18f, -10f);
            foreach (GameObject platform in GameObject.FindGameObjectsWithTag("Platform"))
                if (platform.transform.position.y < toY - 30f) Object.DestroyImmediate(platform);
            foreach (DoodlikHazard hazard in Object.FindObjectsByType<DoodlikHazard>())
                if (hazard.transform.position.y < toY - 30f) Object.DestroyImmediate(hazard.transform.root.gameObject);
        }
        Check(stationaryLandings <= 60 && longestStationaryStreak <= 2,
            $"route must not climb on its own format={format} control={control}: " +
            $"{stationaryLandings} passive landings, streak={longestStationaryStreak}");
        if (format == 0)
        {
            Check(farRows >= 100 && lowerFarRows >= 75,
                $"landscape route needs long lateral leaps control={control}: " +
                $"far={farRows}, low-and-far={lowerFarRows}/600");
            Check(preparedWideRows >= 12,
                $"landscape momentum-aligned leaps should recur control={control}: {preparedWideRows}/600");
        }
        else
            Check(farRows >= 50 && preparedWideRows == 0,
                $"portrait route needs sideways travel without low-and-wide steps control={control}: " +
                $"far={farRows}, wide={preparedWideRows}/600");
        Check(precisionRows >= (format == 1 ? 60 : 45),
            $"challenging landings should recur mode={format}/{control}: {precisionRows}/600");
        Check(sideTraversals >= (format == 0 && control == 1 ? 35 : 45),
            $"single route should move between screen sides format={format} control={control}: " +
            $"sideTraversals={sideTraversals}/600");
        foreach (int visits in horizontalBands)
            Check(visits > 10, "route visits every part of the screen: " +
                string.Join(",", horizontalBands));
        Check(GameplayPreferences.IsPortrait || clippedPlatforms == 0,
            "landscape route stays visible at screen edges");
        Check(earlyChallengeRows >= 7,
            $"early route includes narrow or moving landings: {earlyChallengeRows}/60 mode={format}/{control}");
        Debug.Log($"WORLD_SINGLE_ROUTE format={format} control={control} " +
            $"precision={precisionRows} reversals={precisionReversals} " +
            $"wide={preparedWideRows} far={farRows} lowerFar={lowerFarRows} " +
            $"sideTraversals={sideTraversals} passive={stationaryLandings} " +
            $"averageGap={sumGap / 600f:F2} averageHorizontal={sumHorizontal / 600f:F2}");

        // Explicitly stress rejection-sampling fallback at a very short reach.
        MethodInfo choose = typeof(Spawner).GetMethod("ChooseReachableX", Private);
        for (int i = 0; i < 200; i++)
        {
            float fromX = (float)x.GetValue(spawner);
            float candidate = (float)choose.Invoke(spawner, new object[] { 0.009f, 0.01f, true, 0.9f });
            Check(Mathf.Abs(TrapGeometry.WrappedDelta(fromX, candidate,
                camera.orthographicSize * camera.aspect * 2f)) <= 0.0101f, "fallback never exceeds reach");
        }

        FieldInfo minBound = typeof(Spawner).GetField("currentMinX", Private);
        FieldInfo maxBound = typeof(Spawner).GetField("currentMaxX", Private);
        float oldWidth = (float)maxBound.GetValue(spawner) - (float)minBound.GetValue(spawner);
        range.SetValue(spawner, 1f);
        camera.aspect *= 1.2f;
        typeof(Spawner).GetMethod("UpdateHorizontalBounds", Private).Invoke(spawner,
            new object[] { true });
        float newWidth = (float)maxBound.GetValue(spawner) - (float)minBound.GetValue(spawner);
        Check(Mathf.Abs((float)range.GetValue(spawner) - newWidth / oldWidth) < 0.001f,
            "resize scales moving-platform uncertainty with its patrol");
        HazardSpawner hazardsAfterResize = (HazardSpawner)typeof(Spawner)
            .GetField("hazardSpawner", Private).GetValue(spawner);
        IList reservedChoices = (IList)typeof(HazardSpawner)
            .GetField("alternativePlatforms", Private).GetValue(hazardsAfterResize);
        Check(reservedChoices.Count == 0,
            "single route has no side-platform danger reservations after resize");

        // The menu pauses Time.timeScale without ending the run. No route may
        // advance in that state, or HazardSpawner would skip its trap candidate.
        bool oldActive = RunFlow.RunActive, oldTransition = RunFlow.IsTransitioning;
        float oldTimeScale = Time.timeScale;
        try
        {
            RunFlow.RunActive = true;
            RunFlow.IsTransitioning = false;
            Time.timeScale = 0f;
            camera.transform.position = new Vector3(0f, (float)height.GetValue(spawner), -10f);
            float pausedHeight = (float)height.GetValue(spawner);
            typeof(Spawner).GetMethod("Update", Private).Invoke(spawner, null);
            Check(Mathf.Approximately((float)height.GetValue(spawner), pausedHeight),
                "pause cannot consume route or trap rows");
        }
        finally
        {
            RunFlow.RunActive = oldActive;
            RunFlow.IsTransitioning = oldTransition;
            Time.timeScale = oldTimeScale;
        }
    }

    private static Node[] ReadTier(Spawner spawner)
    {
        IList tier = (IList)typeof(Spawner).GetField("currentTier", Private).GetValue(spawner);
        Node[] nodes = new Node[tier.Count];
        for (int i = 0; i < tier.Count; i++)
        {
            object value = tier[i];
            Type type = value.GetType();
            nodes[i] = new Node {
                x = (float)type.GetField("x").GetValue(value),
                y = (float)type.GetField("y").GetValue(value),
                range = (float)type.GetField("movementRange").GetValue(value)
            };
        }
        return nodes;
    }

    private static void Restore(string key, bool existed, int value)
    { if (existed) PlayerPrefs.SetInt(key, value); else PlayerPrefs.DeleteKey(key); }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("World generation: " + message);
    }
}
#endif
