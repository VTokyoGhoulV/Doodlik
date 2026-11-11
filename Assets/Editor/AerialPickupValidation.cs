using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Isolated Box2D replay of every accepted witness using the real Player methods.
public static class AerialPickupValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Validate()
    {
        Require(Application.isPlaying, "Aerial pickup validation requires Play Mode.");
        Player source = Object.FindAnyObjectByType<Player>();
        Require(source != null, "A source player exists.");
        bool hadFormat = PlayerPrefs.HasKey(GameplayPreferences.OrientationKey);
        bool hadControl = PlayerPrefs.HasKey(GameplayPreferences.InertiaKey);
        int format = PlayerPrefs.GetInt(GameplayPreferences.OrientationKey);
        int control = PlayerPrefs.GetInt(GameplayPreferences.InertiaKey);
        float fixedStep = Time.fixedDeltaTime, timeScale = Time.timeScale;
        SimulationMode2D simulationMode = Physics2D.simulationMode;
        bool autoSync = Physics2D.autoSyncTransforms;
        Scene scene = default;
        GameObject probe = null;
        int accepted = 0, replays = 0, skipped = 0;
        try
        {
            scene = SceneManager.CreateScene("Aerial pickup witness " + Guid.NewGuid().ToString("N"),
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            PhysicsScene2D physics = scene.GetPhysicsScene2D();
            Require(physics.IsValid() && physics != Physics2D.defaultPhysicsScene, "Physics scene is isolated.");
            probe = new GameObject("Air pickup controller probe");
            SceneManager.MoveGameObjectToScene(probe, scene);
            Rigidbody2D body = probe.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            body.linearDamping = source.GetComponent<Rigidbody2D>().linearDamping;
            CircleCollider2D probeFeet = probe.AddComponent<CircleCollider2D>();
            probeFeet.radius = 0.1f;
            probeFeet.offset = Vector2.up * 0.1f;
            Player player = probe.AddComponent<Player>();
            EditorUtility.CopySerialized(source, player);
            player.enabled = false;
            typeof(Player).GetField("rb", Private).SetValue(player, body);
            typeof(Player).GetField("playerCollider", Private).SetValue(player, probeFeet);
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.autoSyncTransforms = false;
            Time.timeScale = 1f;
            int formats = GameplayPreferences.IsDesktopBuild ? 1 : 2;
            foreach (float step in new[] { fixedStep, 1f / 60f, 1f / 120f })
            for (int f = 0; f < formats; f++)
            for (int c = 0; c < 2; c++)
            {
                Time.fixedDeltaTime = step;
                GameplayPreferences.SetMode(f == 1, c == 1, false);
                float scale = GameplayPreferences.HorizontalMovementScale;
                float width = f == 1 ? 5.625f : 17.7778f;
                float halfWidth = 0.8685f * scale, bodyAboveFeet = 0.38f * scale;
                int acceptedInMode = 0;
                foreach (float gap in new[] { 1.35f, 2f, 2.8f, 3.2f })
                foreach (float distance in new[] { 1.1f, 1.8f, 2.5f, 3.2f })
                for (int wrap = 0; wrap < 2; wrap++)
                {
                    Vector2 from = new Vector2(wrap == 0 ? 0f : width * 0.45f, 0f);
                    Vector2 to = new Vector2(from.x + distance * scale, gap);
                    if (wrap != 0) to.x -= width;
                    Vector2 bodyHalfSize = new Vector2(0.28f, 0.30f) * scale;
                    bool placed = AerialPickupPlacement.TryDetour(player, from, to, halfWidth,
                        width, bodyAboveFeet, bodyHalfSize, 5, out Vector2 point);
                    bool repeated = AerialPickupPlacement.TryDetour(player, from, to, halfWidth,
                        width, bodyAboveFeet, bodyHalfSize, 5, out Vector2 again);
                    Require(placed == repeated && point == again, "Placement is deterministic.");
                    if (!placed) { skipped++; continue; }
                    accepted++; acceptedInMode++;
                    float delta = TrapGeometry.WrappedDelta(from.x, to.x, width);
                    Vector2 relativePoint = point - from;
                    Require(relativePoint.x < Mathf.Min(0f, delta) - bodyHalfSize.x - 0.18f ||
                        relativePoint.x > Mathf.Max(0f, delta) + bodyHalfSize.x + 0.18f,
                        "Optional coin lies outside the direct landing corridor and needs a deliberate detour.");
                    for (int start = 0; start <= (c == 1 ? 1 : 0); start++)
                    {
                        float initialVx = start * Mathf.Sign(relativePoint.x) * player.moveSpeed * scale;
                        Require(Replay(player, body, physics, gap, delta, halfWidth, bodyAboveFeet,
                            relativePoint, initialVx, c == 1, scale),
                            $"Actual controller collects and lands: format={f} inertia={c} dt={step} gap={gap} dx={delta} vx={initialVx}.");
                        replays++;
                    }
                }
                Require(acceptedInMode > 0, $"Airborne rewards exist for format={f}, inertia={c}, dt={step}.");
                float apex = JumpReachability.Simulate(player, 0f, c == 1, scale).Apex;
                Require(!AerialPickupPlacement.TryDetour(player, Vector2.zero, new Vector2(2f * scale, apex - 0.3f),
                    halfWidth, width, bodyAboveFeet, Vector2.one * 0.28f * scale, 5, out _),
                    "Near-limit mandatory jumps never demand another maneuver for a coin.");
            }
            ValidateClearAirAndOwnership();
            ValidateEarlyLanding(player, body, physics, scene);
            Debug.Log($"AERIAL_PICKUP_PASS accepted={accepted} actualPhysicsReplays={replays} skippedWithoutPlatformFallback={skipped} formats={formats}");
        }
        finally
        {
            if (probe != null) Object.DestroyImmediate(probe);
            if (scene.IsValid()) SceneManager.UnloadSceneAsync(scene);
            Physics2D.simulationMode = simulationMode;
            Physics2D.autoSyncTransforms = autoSync;
            Time.fixedDeltaTime = fixedStep;
            Time.timeScale = timeScale;
            Restore(GameplayPreferences.OrientationKey, hadFormat, format);
            Restore(GameplayPreferences.InertiaKey, hadControl, control);
            PlayerPrefs.Save();
        }
    }

    private static void ValidateClearAirAndOwnership()
    {
        var surfaces = new System.Collections.Generic.List<Rect> { new Rect(-1f, -0.2f, 2f, 0.2f) };
        var dangers = new System.Collections.Generic.List<Rect>();
        Require(!AirCoinSpawner.IsAirClear(Vector2.zero, 1f, 10f, surfaces, dangers), "Coins never overlap platform surfaces.");
        Require(!AirCoinSpawner.IsAirClear(new Vector2(0f, 0.6f), 1f, 10f, surfaces, dangers),
            "Coins are separated from platform tops by visible free air.");
        Require(AirCoinSpawner.IsAirClear(new Vector2(2f, 2f), 1f, 10f, surfaces, dangers), "A free-air detour remains available.");
        Require(!AirCoinSpawner.IsAirClear(new Vector2(10f, 0f), 1f, 10f, surfaces, dangers),
            "Surface clearance uses screen-wrap copies too.");
        dangers.Add(new Rect(1.7f, 1.7f, 0.6f, 0.6f));
        Require(!AirCoinSpawner.IsAirClear(new Vector2(2f, 2f), 1f, 10f, surfaces, dangers),
            "Coins cannot sit inside a thorn/flyer patrol reservation.");
        var path = new System.Collections.Generic.List<Vector2> { Vector2.zero, new Vector2(2f, 2f), new Vector2(3f, 1f) };
        Require(!AirCoinSpawner.IsWitnessClear(path, Vector2.zero, Vector2.one * 0.25f, 10f, 10f, dangers),
            "An otherwise reachable detour crossing a hazard is rejected.");
        var fallingPath = new System.Collections.Generic.List<Vector2> { new Vector2(0f, 3.4f), new Vector2(0f, 1.4f) };
        var intermediate = new System.Collections.Generic.List<Rect> { new Rect(-1f, 1.8f, 2f, 0.2f) };
        Require(!AirCoinSpawner.IsLandingPathClear(fallingPath, Vector2.zero, 0.4f, 0.15f, 10f, intermediate),
            "A descent through an intermediate platform is not treated as an uninterrupted return path.");
        var risingPath = new System.Collections.Generic.List<Vector2> { new Vector2(0f, 1.4f), new Vector2(0f, 3.4f) };
        Require(AirCoinSpawner.IsLandingPathClear(risingPath, Vector2.zero, 0.4f, 0.15f, 10f, intermediate),
            "Passing through a one-way platform while rising remains valid.");
        RunPickup coin = RunPickup.SpawnAirCoin(new Vector2(8000f, 8000f), null, 0f);
        try
        {
            Require(coin != null && coin.transform.parent == null, "Coins are created as independent world objects, not platform children.");
            Vector3 position = coin.transform.position;
            Require(position.x == 8000f && position.y == 8000f, "Coin creation respects the approved free-air point.");
        }
        finally { if (coin != null) Object.DestroyImmediate(coin.gameObject); }
        GameObject reservationCamera = new GameObject("Temporary coin reservation camera", typeof(Camera));
        try
        {
            Camera camera = reservationCamera.GetComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.transform.position = new Vector3(8000f, 8000f, 0f);
            CoinRouteReservations.Reserve(camera, path, new Vector2(8000f, 8000f), Vector2.one * 0.25f);
            Require(CoinRouteReservations.Overlaps(new Rect(8001.7f, 8001.7f, 0.6f, 0.6f), 10f),
                "Late hazards cannot overwrite a published coin path.");
            Require(CoinRouteReservations.Overlaps(new Rect(8011.7f, 8001.7f, 0.6f, 0.6f), 10f),
                "Coin path reservations also protect screen-wrap copies.");
            CoinRouteReservations.ClearForCamera(camera);
            Require(!CoinRouteReservations.Overlaps(new Rect(8001.7f, 8001.7f, 0.6f, 0.6f), 10f),
                "Layout reset releases only this camera's old reservation.");
        }
        finally { Object.DestroyImmediate(reservationCamera); }
    }

    private static void ValidateEarlyLanding(Player player, Rigidbody2D body, PhysicsScene2D physics, Scene scene)
    {
        bool active = RunFlow.RunActive, transition = RunFlow.IsTransitioning;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        FieldInfo owner = typeof(RunPowerups).GetField("instance", statics);
        object savedOwner = owner.GetValue(null);
        var hazards = (List<DoodlikHazard>)typeof(DoodlikHazard).GetField("Active", statics).GetValue(null);
        var savedHazards = new List<DoodlikHazard>(hazards);
        var pickups = (List<RunPickup>)typeof(RunPickup).GetField("Active", statics).GetValue(null);
        var savedPickups = new List<RunPickup>(pickups);
        GameObject cameraObject = null;
        SpriteRenderer renderer = null;
        RunPowerups powers = null;
        Sprite sprite = null;
        GameObject surface = new GameObject("Intermediate automatic-bounce platform");
        SceneManager.MoveGameObjectToScene(surface, scene);
        try
        {
            RunFlow.RunActive = true;
            RunFlow.IsTransitioning = false;
            // The replay deliberately bypasses Player.Start. A real landing
            // also resolves travelled hazards/pickups, so supply its complete
            // dependencies without exposing the live scene's global registries.
            hazards.Clear();
            pickups.Clear();
            cameraObject = new GameObject("Early landing probe camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.aspect = 1f;
            renderer = player.gameObject.AddComponent<SpriteRenderer>();
            Texture2D texture = Texture2D.whiteTexture;
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                Vector2.one * 0.5f, texture.width);
            renderer.sprite = sprite;
            powers = player.gameObject.AddComponent<RunPowerups>();
            typeof(Player).GetField("mainCamera", Private).SetValue(player, camera);
            typeof(Player).GetField("spriteRenderer", Private).SetValue(player, renderer);
            typeof(Player).GetField("powers", Private).SetValue(player, powers);
            typeof(Player).GetField("hazardBodyHalfSize", Private).SetValue(player, Vector2.one * 0.325f);
            surface.tag = "Platform";
            surface.transform.position = new Vector3(0f, 1.9f, 0f);
            surface.AddComponent<BoxCollider2D>().size = new Vector2(2f, 0.2f);
            body.position = new Vector2(0f, 3f);
            body.linearVelocity = new Vector2(0f, -2f);
            body.WakeUp();
            Physics2D.SyncTransforms();
            typeof(Player).GetField("previousHazardPosition", Private).SetValue(player, (Vector2)renderer.bounds.center);
            Collider2D feet = player.GetComponent<Collider2D>();
            typeof(Player).GetField("previousFeetPosition", Private).SetValue(player,
                new Vector2(feet.bounds.center.x, feet.bounds.min.y));
            MethodInfo gravity = typeof(Player).GetMethod("ApplyGravityCurve", Private);
            bool bounced = false;
            for (int i = 0; i < 160; i++)
            {
                gravity.Invoke(player, null);
                Require(physics.Simulate(Time.fixedDeltaTime), "Early-landing Box2D simulation advances.");
                if (body.linearVelocity.y > 1f) { bounced = true; break; }
                if (body.position.y < 1f) break;
            }
            Require(bounced && body.position.y >= 1.9f && body.position.y <= 2.3f,
                "The real Player automatically bounces on the intermediate surface: rejecting that witness is necessary.");
        }
        finally
        {
            RunFlow.RunActive = active;
            RunFlow.IsTransitioning = transition;
            Object.DestroyImmediate(surface);
            if (powers != null) Object.DestroyImmediate(powers);
            if (renderer != null) Object.DestroyImmediate(renderer);
            if (sprite != null) Object.DestroyImmediate(sprite);
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            owner.SetValue(null, savedOwner);
            hazards.Clear();
            foreach (DoodlikHazard hazard in savedHazards)
                if (hazard != null && hazard.isActiveAndEnabled) hazards.Add(hazard);
            pickups.Clear();
            foreach (RunPickup pickup in savedPickups)
                if (pickup != null && pickup.isActiveAndEnabled) pickups.Add(pickup);
        }
    }

    private static bool Replay(Player player, Rigidbody2D body, PhysicsScene2D physics,
        float gap, float landingX, float halfWidth, float aboveFeet, Vector2 pickup,
        float initialVx, bool inertia, float scale)
    {
        MethodInfo horizontal = typeof(Player).GetMethod("ApplyHorizontalMovement", Private);
        MethodInfo gravity = typeof(Player).GetMethod("ApplyGravityCurve", Private);
        FieldInfo input = typeof(Player).GetField("horizontalInput", Private);
        body.position = Vector2.zero;
        body.linearVelocity = new Vector2(initialVx, player.NormalJumpForce);
        body.WakeUp();
        Physics2D.SyncTransforms();
        bool collected = false, aboveTarget = false;
        float collectedAt = -1f, step = Time.fixedDeltaTime;
        Rect collectible = new Rect(pickup - Vector2.one * 0.14f, Vector2.one * 0.28f);
        for (float time = 0f; time < 6f; time += step)
        {
            Vector2 previous = body.position;
            float targetX = collected && time - collectedAt >= 0.06f ? landingX : pickup.x;
            float control = body.linearVelocity.y < 0f ? player.fallingControl : player.risingControl;
            input.SetValue(player, AerialPickupPlacement.SteeringInput(player, targetX - body.position.x,
                body.linearVelocity.x, control, scale, inertia, step));
            horizontal.Invoke(player, null);
            gravity.Invoke(player, null);
            Require(physics.Simulate(step), "Isolated Box2D simulation advances.");
            Vector2 current = body.position;
            if (!collected && TrapGeometry.SweptBox(previous + Vector2.up * aboveFeet,
                current + Vector2.up * aboveFeet, Vector2.zero, collectible))
            {
                collected = true;
                collectedAt = time;
            }
            aboveTarget |= current.y >= gap;
            if (body.linearVelocity.y < 0f && current.y <= gap && aboveTarget)
            {
                float fraction = Mathf.InverseLerp(previous.y, current.y, gap);
                return collected && Mathf.Abs(Mathf.Lerp(previous.x, current.x, fraction) - landingX) <= halfWidth * 0.6f + 0.01f;
            }
            if (body.linearVelocity.y < 0f && current.y < 0f) return false;
        }
        return false;
    }

    private static void Restore(string key, bool hadKey, int value)
    {
        if (hadKey) PlayerPrefs.SetInt(key, value);
        else PlayerPrefs.DeleteKey(key);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Aerial pickup validation: " + message);
    }
}
