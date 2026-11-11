#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Scoped rules/placement and actual actor travel fixtures. No builds or live
// user data: the outer PlayMode runner must supply both isolated save stores.
public static class ExpeditionEnemyValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static int checks;

    public static void RunPlacementDiagnostic()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/")) throw new InvalidOperationException("Disposable QA only.");
        PlayerSettings.companyName = "CodexValidation"; PlayerSettings.productName = "DoodlikEnemyPlacement_20261102";
        RunFlow.RunActive = RunFlow.IsTransitioning = false; GameplayPreferences.SetRunKind(GameRunKind.Classic); GameplayPreferences.SetMode(false, false, false);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); ValidatePlacement(); CaptureEnemy();
    }

    public static int ValidateRules()
    {
        SafeBatch(); checks = 0;
        UnityEngine.Random.State original = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(1293); float expected = UnityEngine.Random.value; UnityEngine.Random.InitState(1293);
            foreach (Mesh mesh in new[] { ExpeditionEnemyArt.Body, ExpeditionEnemyArt.Warning, ExpeditionEnemyArt.Hurt,
                ExpeditionEnemyArt.Arrow, ExpeditionEnemyArt.Health(2), ExpeditionEnemyArt.Health(1) })
            {
                Check(mesh != null && mesh.vertexCount > 8 && mesh.colors32.Length == mesh.vertexCount, "Every actor pose has colored paper geometry.");
                foreach (Vector3 vertex in mesh.vertices) Check(Finite(vertex.x) && Finite(vertex.y) && vertex.z == 0f, "Enemy mesh vertices are finite and flat.");
                foreach (int index in mesh.triangles) Check(index >= 0 && index < mesh.vertexCount, "Enemy mesh indices are valid.");
            }
            Check(ReferenceEquals(ExpeditionEnemyArt.Body, ExpeditionEnemyArt.Body) && ExpeditionEnemyArt.Body != ExpeditionEnemyArt.Warning,
                "Pose drawings are cached, separate meshes.");
            for (uint seed = 1; seed <= 24; seed++)
            {
                var planner = new ExpeditionEnemySpawner(null, null, 0f, seed);
                Check(planner.NextBandHeight >= 100 && planner.NextBandHeight <= 160 && planner.SpawnedCount == 0,
                    "Opening enemies do not ambush the first jump.");
            }
            Check(UnityEngine.Random.value == expected, "Art and optional enemy bands preserve UnityEngine.Random.");
            foreach (float scale in new[] { 1f, .64f })
            {
                float feetOffset = .5f * scale, top = ExpeditionEnemy.BodyHalfHeight * scale;
                Check(ExpeditionEnemy.TryTopContact(new Vector2(0f, top + feetOffset + .4f),
                    new Vector2(0f, top + feetOffset - .1f), feetOffset, .2f * scale, scale, out float fraction) && fraction > 0f && fraction < 1f,
                    "A real descending paw crossing is an attack.");
                Check(!ExpeditionEnemy.TryTopContact(new Vector2(0f, top + feetOffset - .1f),
                    new Vector2(0f, top + feetOffset + .4f), feetOffset, .2f * scale, scale, out _), "An ascending overlap cannot stomp.");
                Check(!ExpeditionEnemy.TryTopContact(new Vector2(2f, top + feetOffset + .4f),
                    new Vector2(2f, top + feetOffset - .1f), feetOffset, .2f * scale, scale, out _), "Air beside wings cannot count as a top hit.");
                Check(!ExpeditionEnemy.TryTopContact(new Vector2(0f, top - .3f), new Vector2(0f, top - .4f),
                    feetOffset, .2f * scale, scale, out _), "An already-below body overlap cannot masquerade as a stomp.");
            }
            Check(!ExpeditionEnemy.TryTopContact(Vector2.zero, Vector2.down, float.NaN, .2f, 1f, out _), "Invalid contact geometry is rejected.");
            Debug.Log("EXPEDITION_ENEMY_RULES_PASS checks=" + checks + " poses=6 topPaws=true noRng=true"); return checks;
        }
        finally { UnityEngine.Random.state = original; }
    }

    public static int ValidatePlacement()
    {
        SafeBatch();
        if (Application.isPlaying || Object.FindAnyObjectByType<Player>() != null) throw new InvalidOperationException("Enemy placement needs an empty EditMode scene.");
        checks = 0;
        bool oldActive = RunFlow.RunActive, oldTransition = RunFlow.IsTransitioning;
        float oldTime = Time.timeScale, oldStart = WorldZones.RunStartHeight, oldRate = WorldZones.PointsPerUnit;
        int oldHeight = WorldZones.CurrentHeight;
        string[] keys = { GameplayPreferences.RunKindKey, GameplayPreferences.OrientationKey, GameplayPreferences.InertiaKey };
        var preferences = SnapshotPreferences(keys);
        UnityEngine.Random.State random = UnityEngine.Random.state;
        GameObject root = null; Sprite sprite = null;
        int modes = GameplayPreferences.IsDesktopBuild ? 2 : 4, spawned = 0;
        try
        {
            for (int mode = 0; mode < modes; mode++)
            {
                RunFlow.RunActive = RunFlow.IsTransitioning = false;
                GameplayPreferences.SetMode(mode >= 2, mode % 2 != 0, false); GameplayPreferences.SetRunKind(GameRunKind.Expedition, false);
                WorldZones.ConfigureRun(8000f, 10f);
                root = new GameObject("Enemy safe-route proof fixture");
                Camera camera = Child(root, "Camera").AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true;
                camera.orthographicSize = 5f; camera.aspect = GameplayPreferences.IsPortrait ? 720f / 1280f : 1280f / 720f;
                // Every seed's band centre (100..160) must be inside the
                // finalized offscreen window before expecting an attempt.
                camera.transform.position = new Vector3(8000f, 8007f, -10f);
                GameObject cat = Child(root, "Player"); cat.transform.position = new Vector3(8000f, 8000f); cat.transform.localScale = Vector3.one * GameplayPreferences.ModelScale;
                SpriteRenderer renderer = cat.AddComponent<SpriteRenderer>(); sprite = UnitSprite(); renderer.sprite = sprite;
                BoxCollider2D feet = cat.AddComponent<BoxCollider2D>(); feet.size = new Vector2(.5f, .1f); feet.offset = Vector2.down * .45f;
                Rigidbody2D body = cat.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
                Player player = cat.AddComponent<Player>(); player.enabled = false;
                var platforms = new List<GameObject>();
                for (int row = 0; row < 20; row++)
                {
                    GameObject platform = Child(root, "Safe platform " + row); platform.tag = "Platform";
                    platform.transform.position = new Vector2(8000f + (row % 2 == 0 ? -.8f : .8f) * GameplayPreferences.ModelScale, 8000f + row * 2f);
                    platform.AddComponent<BoxCollider2D>().size = new Vector2(2f * GameplayPreferences.ModelScale, .15f);
                    platforms.Add(platform);
                }
                Physics2D.SyncTransforms(); RunFlow.RunActive = true; Time.timeScale = 1f;
                for (uint seed = 1; seed <= 4; seed++)
                {
                    ExpeditionEnemy.RemoveUnclaimed(); var planner = new ExpeditionEnemySpawner(player, camera, 0f, seed);
                    foreach (GameObject platform in platforms) planner.RegisterPlatform(platform, 0f);
                    Check(WorldZones.ScoreAtHeight(camera.transform.position.y + camera.orthographicSize + 4f) >= planner.NextBandHeight,
                        "The diagnostic viewport has reached this seed's offscreen opportunity band.");
                    planner.Advance(8045f, false, false);
                    Check(planner.PlacementAttempts == 0, "Pending hazard planning postpones optional enemies.");
                    Time.timeScale = 0f; planner.Advance(8045f, false); Check(planner.PlacementAttempts == 0, "Paused generation performs no enemy work.");
                    Time.timeScale = 1f;
                    UnityEngine.Random.InitState(9981); float expected = UnityEngine.Random.value; UnityEngine.Random.InitState(9981);
                    planner.Advance(8045f, false);
                    Check(UnityEngine.Random.value == expected, "Successful or rejected enemy placement preserves base RNG.");
                    if (planner.SpawnedCount == 0) Debug.Log("ENEMY_PLACEMENT_PROBE mode=" + mode + " seed=" + seed + " band=" + planner.NextBandHeight + " " + planner.Diagnostics);
                    Check(planner.SpawnedCount == 1 && ExpeditionEnemy.ActiveCount == 1, "A moderate real route supports a certified optional enemy.");
                    ExpeditionEnemy enemy = Object.FindAnyObjectByType<ExpeditionEnemy>();
                    Check(enemy != null && enemy.GetComponent<Collider2D>() == null && enemy.Health == 2,
                        "Enemy contacts cannot turn into an extra solid route platform.");
                    Check(enemy.PatrolBounds.xMin >= camera.transform.position.x - camera.orthographicSize * camera.aspect &&
                        enemy.PatrolBounds.xMax <= camera.transform.position.x + camera.orthographicSize * camera.aspect,
                        "The whole patrol stays visibly inside the viewport.");
                    planner.Advance(8045f, false); Check(ExpeditionEnemy.ActiveCount == 1, "Early encounters obey the one-enemy cap."); spawned++;
                    planner.InvalidateLayout(); Check(ExpeditionEnemy.ActiveCount == 0, "Resizing removes stale patrols before recertification.");
                }
                RunFlow.RunActive = false; GameplayPreferences.SetRunKind(GameRunKind.Classic, false); RunFlow.RunActive = true;
                var classic = new ExpeditionEnemySpawner(player, camera, 0f, 6); foreach (GameObject platform in platforms) classic.RegisterPlatform(platform, 0f);
                classic.Advance(8045f, false); Check(classic.PlacementAttempts == 0 && ExpeditionEnemy.ActiveCount == 0, "Classic has no enemy generation or proof overhead.");
                Object.DestroyImmediate(root); root = null; Object.DestroyImmediate(sprite); sprite = null;
            }
            Debug.Log("EXPEDITION_ENEMY_PLACEMENT_PASS checks=" + checks + " modes=" + modes + " certifiedEnemies=" + spawned + " independentRng=true"); return checks;
        }
        finally
        {
            ExpeditionEnemy.RemoveUnclaimed(); if (root != null) Object.DestroyImmediate(root); if (sprite != null) Object.DestroyImmediate(sprite);
            WorldZones.ConfigureRun(oldStart, oldRate); WorldZones.SetScore(oldHeight);
            RestorePreferences(preferences); RunFlow.RunActive = oldActive; RunFlow.IsTransitioning = oldTransition; Time.timeScale = oldTime; UnityEngine.Random.state = random;
        }
    }

    public static int ValidateRuntime()
    {
        SafeBatch();
        Require(Application.isPlaying && !RunFlow.RunActive && !RunFlow.IsTransitioning && Time.timeScale == 0f && !ExpeditionUI.IsOpen && !AdminTools.IsOpen,
            "Actor runtime fixtures start at the isolated paused home.");
        string normalKey = (string)typeof(PlayerProgress).GetField("storageSaveKey", Static).GetValue(null);
        Require(normalKey.StartsWith("DoodlikValidation_", StringComparison.Ordinal), "Ordinary progression must be isolated too.");
        checks = 0;
        GameMenuUI menu = Object.FindAnyObjectByType<GameMenuUI>(); FrontEndUI front = menu.GetComponent<FrontEndUI>();
        ExpeditionUI ui = menu.GetComponent<ExpeditionUI>(); ExpeditionRun originalActive = ExpeditionRun.Active;
        object originalBound = Get(ui, "run"), originalClosed = typeof(ExpeditionUI).GetField("closedFrame", Static).GetValue(null);
        bool oldActive = RunFlow.RunActive, oldTransition = RunFlow.IsTransitioning; float oldTime = Time.timeScale;
        var preferences = SnapshotPreferences(new[] { GameplayPreferences.RunKindKey });
        int originalEnemies = ExpeditionEnemy.ActiveCount, originalFinds = ExpeditionPickup.ActiveCount;
        Require(originalEnemies == 0 && originalFinds == 0, "Home has no natural enemies or cargo to disturb.");
        var roots = new List<GameObject>(); Sprite sprite = null;
        string normalBytes = PlayerPrefs.GetString(normalKey);
        try
        {
            using (var snapshot = new CargoSnapshot())
            {
                GameplayPreferences.SetRunKind(GameRunKind.Expedition, false); front.Hide(); RunFlow.RunActive = true; Time.timeScale = 1f;
                Camera camera = Child(null, "Enemy contact camera").AddComponent<Camera>(); roots.Add(camera.gameObject);
                camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 5f; camera.aspect = 2f; camera.transform.position = new Vector3(8000f, 8002f, -10f);
                sprite = UnitSprite(); ExpeditionRun run = FixtureRun(camera, sprite, roots);
                Player player = run.Owner; Rigidbody2D body = player.GetComponent<Rigidbody2D>(); Collider2D paws = player.GetComponent<Collider2D>();
                float scale = GameplayPreferences.ModelScale;
                Vector2 center = new Vector2(8000f, 8000f), half = Vector2.one * .325f * scale;
                ExpeditionEnemy enemy = ExpeditionEnemy.Spawn(center, camera, 0f, 0f, 0f, 1); roots.Add(enemy.gameObject);
                GameObject staleSupport = new GameObject("Stale contacted platform"); roots.Add(staleSupport); staleSupport.tag = "Platform";
                staleSupport.transform.position = new Vector2(8000f, 7998f); Collider2D staleCollider = staleSupport.AddComponent<BoxCollider2D>();
                Set(player, "reachedHeight", 800); run.RecordLanding(staleCollider);
                Check(Get(run, "pendingLanding") == staleCollider, "A controlled real landing has a pending above-threshold rest candidate.");
                float offset = player.HazardBodyCenter.y - paws.bounds.min.y;
                Vector2 from = center + Vector2.up * (ExpeditionEnemy.BodyHalfHeight * scale + offset + .4f * scale);
                Vector2 to = center + Vector2.up * (ExpeditionEnemy.BodyHalfHeight * scale + offset - .1f * scale);
                MovePlayer(player, body, to); body.linearVelocity = Vector2.down * 4f;
                Time.timeScale = 0f; Check(!ExpeditionEnemy.ResolveTravel(run, from, to, half, 20f) && enemy.Health == 2, "Pause blocks a true geometric stomp.");
                Time.timeScale = 1f; RunFlow.IsTransitioning = true;
                Check(!ExpeditionEnemy.ResolveTravel(run, from, to, half, 20f) && enemy.Health == 2, "Transition blocks combat contacts."); RunFlow.IsTransitioning = false;
                Set(player, "isDead", true); Check(!ExpeditionEnemy.ResolveTravel(run, from, to, half, 20f) && enemy.Health == 2, "Dead players cannot attack."); Set(player, "isDead", false);
                Check(ExpeditionEnemy.ResolveTravel(run, from, to, half, 20f) && enemy.Health == 1 && body.linearVelocity.y == player.NormalJumpForce &&
                    paws.bounds.min.y >= center.y + ExpeditionEnemy.BodyHalfHeight * scale,
                    "A real swept top hit wounds and rebounds at normal force with paws above the enemy.");
                Check(Get(run, "pendingLanding") == null && !run.IsResting, "An enemy rebound cancels the stale platform's pending rest candidate.");
                Check(!ExpeditionEnemy.ResolveTravel(run, from, to, half, 20f) && enemy.Health == 1, "Same-frame callback re-entry cannot double stomp.");
                while (run.Session.Count < ExpeditionSession.Capacity) Check(run.TryCollect(ExpeditionFindKind.FireflyJar), "Cargo fixture fills its real runtime backpack.");
                Set(enemy, "contactAfter", 0f); Set(enemy, "lastContactFrame", -1); MovePlayer(player, body, to); body.linearVelocity = Vector2.down * 4f;
                Set(player, "previousHazardPosition", from);
                int beforeKills = run.EnemyKills;
                bool staleLandingAllowed = (bool)player.GetType().GetMethod("ResolveTravelBeforeBounce", Private).Invoke(player, new object[] { true });
                Check(!staleLandingAllowed && !player.IsDead && enemy.Health == 0 && run.EnemyKills == beforeKills + 1 && ExpeditionEnemy.ActiveCount == 0,
                    "Second actual stomp kills once and the real contact resolver refuses the now-stale platform bounce/rest callback.");
                Check(ExpeditionPickup.ActiveCount == 1 && run.Session.Count == ExpeditionSession.Capacity,
                    "A deterministic rare drop remains a world object when the bag is full.");
                ExpeditionPickup drop = Object.FindAnyObjectByType<ExpeditionPickup>(); roots.Add(drop.gameObject);
                Check(drop.Kind == ExpeditionFindKind.SparkDust && drop.transform.position.y > camera.transform.position.y - camera.orthographicSize &&
                    drop.transform.position.y < camera.transform.position.y + camera.orthographicSize, "Rare material is visible cargo, not a ballistic offscreen drop.");
                ExpeditionPickup.CollectNear(drop.transform.position, Vector2.one * .05f, 20f);
                Check(drop.gameObject.activeSelf && run.Session.Count == ExpeditionSession.Capacity, "Full-bag overlap never consumes rare loot.");
                Check(run.Session.DiscardSlot(1), "Controlled empty-slot fixture frees one cargo position."); Set(drop, "retryAfter", 0f);
                ExpeditionPickup.CollectNear(drop.transform.position, Vector2.one * .05f, 20f);
                Check(!drop.gameObject.activeSelf && run.Session.Slots[1] == ExpeditionFindKind.SparkDust, "The same dropped material is collectible after freeing space.");
                enemy = ExpeditionEnemy.Spawn(center + Vector2.right * 2f, camera, 0f, 0f, 1f, 5); roots.Add(enemy.gameObject);
                center = enemy.transform.position; from = center + Vector2.left; to = center + Vector2.right;
                MovePlayer(player, body, to); int beforeHealth = run.HitPoints;
                Check(!ExpeditionEnemy.ResolveTravel(run, from, to, half, 20f) && run.HitPoints == beforeHealth - 1 && enemy.Health == 2 && !player.IsDead,
                    "A real side sweep wounds the cat but cannot damage or bounce on the enemy.");
                Set(enemy, "lastContactFrame", -1); Set(enemy, "contactAfter", 0f);
                ExpeditionEnemy.ResolveTravel(run, from, to, half, 20f);
                Check(run.HitPoints == beforeHealth - 1, "Enemy-hit grace rejects another contact in the same damage interval.");
                Set(run, "enemyGraceUntil", 0f); Set(enemy, "lastContactFrame", -1); Set(enemy, "contactAfter", 0f);
                ExpeditionEnemy.ResolveTravel(run, from, to, half, 20f);
                Check(player.IsDead && run.HitPoints == 0 && run.IsFinished && run.Result.Outcome == ExpeditionOutcome.Lost && Time.timeScale == 0f,
                    "A lethal actual side contact uses Player death and the expedition loss settlement.");
                Hide(ui); ResetClosed(); Time.timeScale = 1f; ExpeditionEnemy.RemoveUnclaimed();
                run = FixtureRun(camera, sprite, roots); player = run.Owner; body = player.GetComponent<Rigidbody2D>(); paws = player.GetComponent<Collider2D>();
                center = new Vector2(7990.2f, 8000f); enemy = ExpeditionEnemy.Spawn(center, camera, 0f, 0f, 0f, 7); roots.Add(enemy.gameObject);
                from = new Vector2(8009.8f, 8000f); to = new Vector2(7990.4f, 8000f); MovePlayer(player, body, to);
                Check(!ExpeditionEnemy.ResolveTravel(run, from, to, half, 20f) && run.HitPoints == 1 && enemy.Health == 2,
                    "A real boundary-crossing sweep sees the nearby wrapped enemy once, not the whole screen.");
                Check(PlayerPrefs.GetString(normalKey) == normalBytes, "Enemy kills, loot and defeat never spend or grant ordinary coins/wardrobe data.");
            }
        }
        finally
        {
            Hide(ui);
            foreach (GameObject root in roots) if (root != null) { root.GetComponent<ExpeditionRun>()?.Session?.Invalidate(); Object.DestroyImmediate(root); }
            if (sprite != null) Object.DestroyImmediate(sprite);
            typeof(ExpeditionRun).GetField("<Active>k__BackingField", Static).SetValue(null, originalActive);
            Call(ui, "Bind", originalBound); RestorePreferences(preferences);
            RunFlow.RunActive = oldActive; RunFlow.IsTransitioning = oldTransition; front.ShowHome(); Time.timeScale = oldTime;
            typeof(ExpeditionUI).GetField("closedFrame", Static).SetValue(null, originalClosed);
        }
        Check(ExpeditionEnemy.ActiveCount == originalEnemies && ExpeditionPickup.ActiveCount == originalFinds, "Actor fixtures restore world registries.");
        Debug.Log("EXPEDITION_ENEMY_RUNTIME_PASS checks=" + checks + " actualTopWound=true actualKill=true sideDamage=true actualDeath=true wrappedContact=true fullBagRareDrop=true"); return checks;
    }

    public static void CaptureEnemy()
    {
        SafeBatch(); GameObject root = new GameObject("Storm beetle pose preview"); Material ink = null; RenderTexture target = null; Texture2D pixels = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            Camera camera = Child(root, "Camera").AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .65f; camera.aspect = 3f;
            camera.transform.position = new Vector3(8000f, 8000f, -10f); camera.backgroundColor = new Color32(255, 232, 179, 255);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.cullingMask = 1 << 30;
            ink = new Material(Resources.Load<Shader>("HazardInk"));
            Mesh[] meshes = { ExpeditionEnemyArt.Body, ExpeditionEnemyArt.Warning, ExpeditionEnemyArt.Hurt };
            for (int i = 0; i < 3; i++)
            {
                GameObject pose = Child(root, "Beetle pose " + i); pose.layer = 30; pose.transform.position = new Vector2(8000f + (i - 1) * 1.25f, 8000f);
                pose.AddComponent<MeshFilter>().sharedMesh = meshes[i]; pose.AddComponent<MeshRenderer>().sharedMaterial = ink;
            }
            target = new RenderTexture(1200, 400, 24); pixels = new Texture2D(1200, 400, TextureFormat.RGB24, false);
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1200, 400), 0, 0); pixels.Apply();
            Directory.CreateDirectory("Logs/RogueEnemy"); File.WriteAllBytes("Logs/RogueEnemy/StormBeetle.png", pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; Object.DestroyImmediate(root); if (target != null) Object.DestroyImmediate(target);
            if (pixels != null) Object.DestroyImmediate(pixels); if (ink != null) Object.DestroyImmediate(ink);
        }
    }

    private static ExpeditionRun FixtureRun(Camera camera, Sprite sprite, List<GameObject> roots)
    {
        GameObject root = new GameObject("Enemy controller fixture"); roots.Add(root); root.hideFlags = HideFlags.DontSave;
        root.transform.position = new Vector3(8000f, 8000f); root.transform.localScale = Vector3.one * GameplayPreferences.ModelScale;
        Rigidbody2D body = root.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
        BoxCollider2D paws = root.AddComponent<BoxCollider2D>(); paws.size = new Vector2(.4f, .1f); paws.offset = Vector2.down * .45f;
        SpriteRenderer renderer = root.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
        Player player = root.AddComponent<Player>(); player.enabled = false;
        Set(player, "rb", body); Set(player, "playerCollider", paws); Set(player, "spriteRenderer", renderer); Set(player, "mainCamera", camera);
        Set(player, "hazardBodyHalfSize", Vector2.one * .325f * GameplayPreferences.ModelScale); Auto(player, "RunKind", GameRunKind.Expedition);
        root.AddComponent<RunPowerups>(); Physics2D.SyncTransforms();
        ExpeditionRun run = ExpeditionRun.Attach(player); run.enabled = false; Auto(player, "Expedition", run); run.BeginRun();
        Auto(run, "HitPoints", 2); Auto(run, "MaximumHitPoints", 2); Auto(run, "HasWard", false);
        Set(run, "trainingClaws", 0); Set(run, "perkClaws", 0); Set(run, "baseDropChance", .25f); Set(run, "sceneCamera", camera);
        return run;
    }
    private static void MovePlayer(Player player, Rigidbody2D body, Vector2 position)
    { body.position = position; player.transform.position = position; Physics2D.SyncTransforms(); }
    private static Sprite UnitSprite() { Texture2D t = Texture2D.whiteTexture; return Sprite.Create(t, new Rect(0, 0, t.width, t.height), Vector2.one * .5f, t.width); }
    private static GameObject Child(GameObject root, string name) { GameObject child = new GameObject(name); if (root != null) child.transform.SetParent(root.transform, false); return child; }
    private static object Get(object owner, string field) => owner.GetType().GetField(field, Private).GetValue(owner);
    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
    private static void Auto(object owner, string property, object value) => Set(owner, "<" + property + ">k__BackingField", value);
    private static void Call(object owner, string method, params object[] values) => owner.GetType().GetMethod(method, Private).Invoke(owner, values);
    private static void Hide(ExpeditionUI ui) { if (ui != null && ExpeditionUI.IsOpen) Call(ui, "Hide", false); }
    private static void ResetClosed() => typeof(ExpeditionUI).GetField("closedFrame", Static).SetValue(null, -1);
    private static Dictionary<string, int?> SnapshotPreferences(string[] keys)
    { var result = new Dictionary<string, int?>(); foreach (string key in keys) result[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null; return result; }
    private static void RestorePreferences(Dictionary<string, int?> values)
    { foreach (var value in values) if (value.Value.HasValue) PlayerPrefs.SetInt(value.Key, value.Value.Value); else PlayerPrefs.DeleteKey(value.Key); }
    private sealed class CargoSnapshot : IDisposable
    {
        private readonly string primary, backup, a, b; private readonly bool hasA, hasB;
        private readonly string[] fields = { "data", "revision", "readOnly", "settledIds", "lastResult", "Changed", "liveRunId" };
        private readonly object[] values;
        public CargoSnapshot()
        {
            primary = (string)Field("primaryKey").GetValue(null); backup = (string)Field("backupKey").GetValue(null);
            Require(primary.StartsWith("DoodlikExpeditionValidation_", StringComparison.Ordinal), "Cargo state must be the outer runner's disposable namespace.");
            hasA = PlayerPrefs.HasKey(primary); hasB = PlayerPrefs.HasKey(backup); a = PlayerPrefs.GetString(primary); b = PlayerPrefs.GetString(backup);
            values = new object[fields.Length]; for (int i = 0; i < fields.Length; i++) values[i] = Field(fields[i]).GetValue(null);
            Field("Changed").SetValue(null, null); Field("liveRunId").SetValue(null, null); PlayerPrefs.DeleteKey(primary); PlayerPrefs.DeleteKey(backup); ExpeditionProgress.Reload();
        }
        public void Dispose()
        {
            if (hasA) PlayerPrefs.SetString(primary, a); else PlayerPrefs.DeleteKey(primary);
            if (hasB) PlayerPrefs.SetString(backup, b); else PlayerPrefs.DeleteKey(backup);
            for (int i = 0; i < fields.Length; i++) Field(fields[i]).SetValue(null, values[i]);
        }
        private static FieldInfo Field(string name) => typeof(ExpeditionProgress).GetField(name, Static);
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void SafeBatch() => Require(Application.isBatchMode && Application.dataPath.Replace('\\', '/').Contains("/.utmp/") && Application.companyName == "CodexValidation", "Disposable CodexValidation batch Editor only.");
    private static void Check(bool value, string message) { checks++; Require(value, message); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("Expedition enemy: " + message); }
}
#endif
