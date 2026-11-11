#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// A rendered frame may contain a descent, landing and rebound. Test the actual
// collision callback in local Box2D, not only a straight endpoint sweep.
public static class GameplaySweepValidation
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

    public static void ValidateHazardRemoval()
    {
        Require(Application.isPlaying, "Run hazard-removal validation in Play Mode.");
        var registry = (List<DoodlikHazard>)typeof(DoodlikHazard).GetField("Active", Static).GetValue(null);
        var saved = new List<DoodlikHazard>(registry);
        GameObject cameraObject = null, seed = null;
        try
        {
            registry.Clear();
            Player source = Object.FindAnyObjectByType<Player>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Platform.prefab");
            Require(source != null && prefab != null, "Scene player and platform prefab exist.");
            cameraObject = new GameObject("Hazard removal camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.aspect = 1f;
            camera.transform.position = new Vector3(8000f, 8000f, -10f);
            seed = new GameObject("Pending removal seed");
            Vector2 point = new Vector2(8000f, 8000f);
            seed.transform.position = point;
            DoodlikHazard hazard = seed.AddComponent<DoodlikHazard>();
            hazard.Initialize(DoodlikHazard.Kind.FlyingSeed, camera, 1f, 0f, 0f, 0f, default);
            var spawner = new HazardSpawner(camera, source, prefab, new TrapSettings());
            var owned = (List<DoodlikHazard>)typeof(HazardSpawner).GetField("hazards", Instance).GetValue(spawner);
            owned.Add(hazard);
            Require(DoodlikHazard.FindHit(point, point, Vector2.one * 0.1f, 10f) == hazard,
                "Removal control initially registers a damaging seed.");
            spawner.InvalidateLayout(8020f, 7997f, 8003f, 7996f, 8004f);
            Require(DoodlikHazard.FindHit(point, point, Vector2.one * 0.1f, 10f) == null,
                "Invalidated hazards stop damaging immediately, before deferred destruction.");
            Debug.Log("HAZARD_REMOVAL_PASS: layout invalidation removes damage before the next frame.");
        }
        finally
        {
            if (seed != null) Object.DestroyImmediate(seed);
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            registry.Clear();
            foreach (DoodlikHazard hazard in saved)
                if (hazard != null && hazard.isActiveAndEnabled) registry.Add(hazard);
        }
    }

    public static void Validate()
    {
        Require(Application.isPlaying, "Run gameplay-sweep validation in Play Mode.");
        bool run = RunFlow.RunActive, transition = RunFlow.IsTransitioning;
        float timeScale = Time.timeScale;
        FieldInfo owner = typeof(RunPowerups).GetField("instance", Static);
        object savedOwner = owner.GetValue(null);
        var hazards = (List<DoodlikHazard>)typeof(DoodlikHazard).GetField("Active", Static).GetValue(null);
        var savedHazards = new List<DoodlikHazard>(hazards);
        var pickups = (List<RunPickup>)typeof(RunPickup).GetField("Active", Static).GetValue(null);
        var savedPickups = new List<RunPickup>(pickups);
        Scene scene = default;
        GameObject root = null;
        Sprite sprite = null;
        try
        {
            RunFlow.RunActive = true;
            RunFlow.IsTransitioning = false;
            Time.timeScale = 1f;
            hazards.Clear();
            pickups.Clear();
            scene = SceneManager.CreateScene("Gameplay sweep " + Guid.NewGuid().ToString("N"),
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            PhysicsScene2D physics = scene.GetPhysicsScene2D();
            root = new GameObject("Gameplay sweep sandbox");
            SceneManager.MoveGameObjectToScene(root, scene);

            Camera camera = Child(root, "Camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.aspect = 1f;
            camera.transform.position = new Vector3(8000f, 8002f, -10f);
            GameObject platform = Child(root, "Normal platform");
            platform.tag = "Platform";
            platform.transform.position = new Vector2(8000f, 7999.9f);
            BoxCollider2D surface = platform.AddComponent<BoxCollider2D>();
            surface.size = new Vector2(3f, 0.2f);

            GameObject cat = Child(root, "Cat");
            Rigidbody2D body = cat.AddComponent<Rigidbody2D>();
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            BoxCollider2D feet = cat.AddComponent<BoxCollider2D>();
            feet.size = new Vector2(0.5f, 0.1f);
            feet.offset = new Vector2(0f, -0.45f);
            Texture2D texture = Texture2D.whiteTexture;
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                Vector2.one * 0.5f, texture.width);
            SpriteRenderer renderer = cat.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            Player player = cat.AddComponent<Player>();
            player.enabled = false;
            RunPowerups powers = cat.AddComponent<RunPowerups>();
            Set(player, "rb", body);
            Set(player, "playerCollider", feet);
            Set(player, "mainCamera", camera);
            Set(player, "spriteRenderer", renderer);
            Set(player, "powers", powers);
            Set(player, "hazardBodyHalfSize", Vector2.one * 0.325f);

            // Control proves that the actual callback bounced inside one frame
            // and the two rendered endpoints miss the intermediate excursion.
            Reset(player, body, renderer, feet);
            float minimum = Advance(player, body, physics);
            Require(minimum < 8000.8f && body.position.y > 8001.4f,
                "Control descends onto the platform and rebounds within 0.32 seconds.");
            Require(!player.IsDead, "A normal landing without hazards remains safe.");

            GameObject seed = Child(root, "Static damaging seed");
            seed.transform.position = new Vector2(8000f, 8000.55f);
            DoodlikHazard hazard = seed.AddComponent<DoodlikHazard>();
            hazard.Initialize(DoodlikHazard.Kind.FlyingSeed, camera, 1f, 0f, 0f, 0f, default);
            Reset(player, body, renderer, feet);
            Advance(player, body, physics);
            Invoke(player, "LateUpdate");
            Require(player.IsDead,
                "A hazard crossed before a same-frame normal rebound remains lethal.");

            Reset(player, body, renderer, feet);
            powers.Collect(RunPowerups.Kind.Shield);
            Advance(player, body, physics);
            Invoke(player, "LateUpdate");
            Require(!player.IsDead && !powers.HasShield && powers.IsProtected && body.linearVelocity.y > 0f,
                "The same excursion consumes one shield and preserves the normal rebound.");
            Object.DestroyImmediate(seed);

            GameObject bonus = Child(root, "Crossed magnet");
            bonus.transform.position = new Vector2(8000f, 8000.55f);
            RunPickup pickup = bonus.AddComponent<RunPickup>();
            Set(pickup, "kind", 2);
            Set(powers, "protectionTime", 0f);
            Reset(player, body, renderer, feet);
            Advance(player, body, physics);
            Invoke(player, "LateUpdate");
            Require(!player.IsDead && powers.MagnetTimeRemaining > 0f,
                "A bonus crossed before a same-frame normal rebound is collected.");

            // Bushes reverse travel in a trigger callback rather than a normal
            // platform collision. Exercise the real trigger with no bees/art.
            surface.enabled = false;
            Set(powers, "magnetTime", 0f);
            GameObject bush = MakeBush(root, "Control bush", 8000.1f);
            Reset(player, body, renderer, feet);
            Advance(player, body, physics, out int boosts);
            Invoke(player, "LateUpdate");
            Require(!player.IsDead && boosts == 1 && body.position.y > 8003f,
                "A real bush trigger reverses the descent and boosts within one rendered frame.");
            if (bush != null) Object.DestroyImmediate(bush);

            seed = Child(root, "Bush-path damaging seed");
            seed.transform.position = new Vector2(8000f, 8000.55f);
            hazard = seed.AddComponent<DoodlikHazard>();
            hazard.Initialize(DoodlikHazard.Kind.FlyingSeed, camera, 1f, 0f, 0f, 0f, default);
            bush = MakeBush(root, "Unprotected bush", 8000.1f);
            Reset(player, body, renderer, feet);
            Advance(player, body, physics);
            Invoke(player, "LateUpdate");
            Require(player.IsDead, "A bush cannot erase a hazard crossed before its boost.");
            if (bush != null) Object.DestroyImmediate(bush);

            bush = MakeBush(root, "Shielded bush", 8000.1f);
            Reset(player, body, renderer, feet);
            powers.Collect(RunPowerups.Kind.Shield);
            Advance(player, body, physics, out boosts);
            Invoke(player, "LateUpdate");
            Require(!player.IsDead && boosts == 1 && !powers.HasShield && powers.IsProtected &&
                body.position.y > 8003f, "A protected bush crossing consumes one shield and retains the boost.");
            if (bush != null) Object.DestroyImmediate(bush);
            Object.DestroyImmediate(seed);

            Set(powers, "protectionTime", 0f);
            MakeBush(root, "First chained bush", 8000.1f);
            MakeBush(root, "Second chained bush", 8001.9f);
            Reset(player, body, renderer, feet);
            Advance(player, body, physics, out boosts);
            Invoke(player, "LateUpdate");
            Require(!player.IsDead && boosts == 2 && body.linearVelocity.y > player.NormalJumpForce,
                "Two distinct bushes can boost the same cat in one rendered frame without losing the second boost.");

            // A callback must test current hazard geometry, not replay a move
            // which the preceding rendered frame has already checked.
            seed = Child(root, "Previously moving seed");
            Vector2 stationary = new Vector2(8000f, 8001f);
            seed.transform.position = stationary + Vector2.right * 2f;
            hazard = seed.AddComponent<DoodlikHazard>();
            hazard.Initialize(DoodlikHazard.Kind.FlyingSeed, camera, 1f, 0f, 0f, 0f, default);
            Set(hazard, "previousPosition", stationary - Vector2.right * 2f);
            Require(DoodlikHazard.FindHit(stationary, stationary, Vector2.one * 0.1f, 10f) == hazard,
                "Render-sweep control detects a moving hazard crossing a stationary cat.");
            Require(DoodlikHazard.FindHit(stationary, stationary, Vector2.one * 0.1f, 10f, false) == null,
                "Physics callback checks do not replay a hazard's prior rendered movement.");
            Debug.Log("GAMEPLAY_SWEEP_PASS: real Box2D down/up frame, lethal hazard, one-hit shield, crossed bonus, bush rebound, two-bush chain and no stale motion replay.");
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            if (sprite != null) Object.DestroyImmediate(sprite);
            if (scene.IsValid()) SceneManager.UnloadSceneAsync(scene);
            hazards.Clear();
            foreach (DoodlikHazard hazard in savedHazards)
                if (hazard != null && hazard.isActiveAndEnabled) hazards.Add(hazard);
            pickups.Clear();
            foreach (RunPickup pickup in savedPickups)
                if (pickup != null && pickup.isActiveAndEnabled) pickups.Add(pickup);
            owner.SetValue(null, savedOwner);
            RunFlow.RunActive = run;
            RunFlow.IsTransitioning = transition;
            Time.timeScale = timeScale;
        }
    }

    private static float Advance(Player player, Rigidbody2D body, PhysicsScene2D physics)
        => Advance(player, body, physics, out _);

    private static float Advance(Player player, Rigidbody2D body, PhysicsScene2D physics, out int boosts)
    {
        boosts = 0;
        float minimum = body.position.y;
        for (int step = 0; step < 16 && !player.IsDead; step++)
        {
            float previousVelocity = body.linearVelocity.y;
            Invoke(player, "ApplyGravityCurve");
            Require(physics.Simulate(0.02f), "Local physics advances.");
            if (!player.IsDead && body.linearVelocity.y > previousVelocity + 0.05f) boosts++;
            minimum = Mathf.Min(minimum, body.position.y);
        }
        return minimum;
    }

    private static GameObject MakeBush(GameObject root, string name, float height)
    {
        GameObject bush = Child(root, name);
        bush.transform.position = new Vector2(8000f, height);
        BoxCollider2D trigger = bush.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(0.6f, 0.3f);
        bush.AddComponent<Bush>();
        return bush;
    }

    private static void Reset(Player player, Rigidbody2D body, SpriteRenderer renderer, Collider2D feet)
    {
        Set(player, "isDead", false);
        Time.timeScale = 1f;
        body.position = new Vector2(8000f, 8001.5f);
        body.transform.position = body.position;
        body.linearVelocity = Vector2.down * 12f;
        body.WakeUp();
        Physics2D.SyncTransforms();
        Set(player, "previousHazardPosition", (Vector2)renderer.bounds.center);
        Set(player, "previousFeetPosition", new Vector2(feet.bounds.center.x, feet.bounds.min.y));
    }

    private static GameObject Child(GameObject parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        return child;
    }
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Instance).SetValue(target, value);
    private static void Invoke(object target, string name) =>
        target.GetType().GetMethod(name, Instance).Invoke(target, null);
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException("Gameplay sweep: " + message); }
}
#endif
