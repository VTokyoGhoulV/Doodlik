#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

// Narrow, disposable checks for the new drawings and optional collect-return
// planner. Natural Player/session/UI integration is owned by the run tests.
public static class ExpeditionWorldValidation
{
    private static int checks;
    public static int ValidateRules()
    {
        RequireSafeEditor(); checks = 0;
        UnityEngine.Random.State state = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(78123); float expected = UnityEngine.Random.value;
            UnityEngine.Random.InitState(78123);
            foreach (ExpeditionCatalog.Definition definition in ExpeditionCatalog.All)
            {
                Mesh mesh = ExpeditionArt.FindMesh(definition.Kind);
                Check(mesh != null && mesh == ExpeditionArt.FindMesh(definition.Kind), "Each find owns a cached shared drawing.");
                Check(mesh.vertexCount > 20 && mesh.colors32.Length == mesh.vertexCount, "Paper drawing has actual per-vertex colors.");
                Check(mesh.bounds.size.x <= .461f && mesh.bounds.size.y <= .461f && mesh.bounds.size.z == 0f,
                    "The same compact silhouette fits the world and bag UI.");
                foreach (Vector3 vertex in mesh.vertices)
                    Check(Finite(vertex.x) && Finite(vertex.y), "Drawing coordinates are finite.");
                foreach (int triangle in mesh.triangles)
                    Check(triangle >= 0 && triangle < mesh.vertexCount, "Drawing indices reference real vertices.");
                Check(definition.EnemyOnly ? ExpeditionFindSpawner.KindAtHeight(int.MaxValue) != definition.Kind :
                    ExpeditionFindSpawner.KindAtHeight(definition.MinimumHeight) == definition.Kind,
                    "World gates open world finds, never the enemy-only material.");
            }
            Check(ExpeditionArt.FindMesh(ExpeditionFindKind.None) == null && ExpeditionArt.FindMesh((ExpeditionFindKind)99) == null,
                "Empty/unknown kinds cannot create a placeholder pickup.");
            Check(ExpeditionFindSpawner.KindAtHeight(-1) == ExpeditionFindKind.None, "A negative height opens no find.");
            for (uint seed = 1; seed <= 24; seed++)
            {
                ExpeditionFindSpawner planner = new ExpeditionFindSpawner(null, null, 0f, seed);
                Check(planner.NextBandHeight >= 60 && planner.NextBandHeight <= 100 && planner.SpawnedCount == 0,
                    "Opening band stays in a useful early raw-height range.");
            }
            Check(UnityEngine.Random.value == expected, "Drawing/cache/planner construction preserves UnityEngine.Random.");
            Debug.Log("EXPEDITION_WORLD_RULES_PASS checks=" + checks + " meshes=" + ExpeditionCatalog.All.Count + " sharedWithUI=true rngUnchanged=true");
            return checks;
        }
        finally { UnityEngine.Random.state = state; }
    }

    public static int ValidatePlacement()
    {
        RequireSafeEditor();
        if (Application.isPlaying || Object.FindAnyObjectByType<Player>() != null)
            throw new InvalidOperationException("Expedition placement fixture needs an empty EditMode QA scene.");
        checks = 0;
        bool oldActive = RunFlow.RunActive, oldTransition = RunFlow.IsTransitioning;
        float oldTime = Time.timeScale, oldStart = WorldZones.RunStartHeight, oldRate = WorldZones.PointsPerUnit;
        int oldHeight = WorldZones.CurrentHeight;
        string[] keys = { GameplayPreferences.RunKindKey, GameplayPreferences.OrientationKey, GameplayPreferences.InertiaKey };
        bool[] existed = new bool[keys.Length]; int[] values = new int[keys.Length];
        for (int i = 0; i < keys.Length; i++) { existed[i] = PlayerPrefs.HasKey(keys[i]); values[i] = PlayerPrefs.GetInt(keys[i]); }
        UnityEngine.Random.State random = UnityEngine.Random.state;
        GameObject root = null; Sprite sprite = null;
        int modes = GameplayPreferences.IsDesktopBuild ? 2 : 4, spawned = 0;
        try
        {
            for (int mode = 0; mode < modes; mode++)
            {
                RunFlow.RunActive = RunFlow.IsTransitioning = false;
                GameplayPreferences.SetMode(mode >= 2, mode % 2 != 0, false);
                Check(GameplayPreferences.SetRunKind(GameRunKind.Expedition, false), "Fixture can select expedition before its run.");
                WorldZones.ConfigureRun(8000f, 10f);
                root = new GameObject("Expedition optional-route fixture");
                GameObject cameraObject = Child(root, "Camera");
                Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                camera.orthographic = true; camera.orthographicSize = 5f;
                camera.aspect = GameplayPreferences.IsPortrait ? 720f / 1280f : 1280f / 720f;
                camera.transform.position = new Vector3(8000f, 8001f, -10f);
                GameObject cat = Child(root, "Player");
                cat.transform.position = new Vector3(8000f, 8000f, 0f);
                cat.transform.localScale = Vector3.one * GameplayPreferences.ModelScale;
                SpriteRenderer body = cat.AddComponent<SpriteRenderer>();
                Texture2D texture = Texture2D.whiteTexture;
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * .5f, texture.width);
                body.sprite = sprite;
                BoxCollider2D feet = cat.AddComponent<BoxCollider2D>(); feet.size = new Vector2(.5f, .1f); feet.offset = Vector2.down * .45f;
                Rigidbody2D rigidbody = cat.AddComponent<Rigidbody2D>(); rigidbody.bodyType = RigidbodyType2D.Kinematic;
                Player player = cat.AddComponent<Player>(); player.enabled = false;
                var platforms = new List<GameObject>();
                for (int row = 0; row < 18; row++)
                {
                    GameObject surface = Child(root, "Safe platform " + row); surface.tag = "Platform";
                    surface.transform.position = new Vector2(8000f + (row % 2 == 0 ? -.8f : .8f) * GameplayPreferences.ModelScale, 8000f + row * 2f);
                    BoxCollider2D collider = surface.AddComponent<BoxCollider2D>();
                    collider.size = new Vector2(2f * GameplayPreferences.ModelScale, .15f);
                    platforms.Add(surface);
                }
                Physics2D.SyncTransforms();
                RunFlow.RunActive = true; Time.timeScale = 1f;
                for (uint seed = 1; seed <= 4; seed++)
                {
                    ExpeditionPickup.RemoveUnclaimed();
                    ExpeditionFindSpawner planner = Planner(player, camera, platforms, seed);
                    planner.Advance(8040f, false, false);
                    Check(planner.PlacementAttempts == 0 && planner.SpawnedCount == 0, "Pending hazard proofs postpone cargo, not hazards.");
                    Time.timeScale = 0f; planner.Advance(8040f, false);
                    Check(planner.PlacementAttempts == 0, "Pause does not advance cargo opportunities.");
                    Time.timeScale = 1f;
                    UnityEngine.Random.InitState(1901); float expected = UnityEngine.Random.value;
                    UnityEngine.Random.InitState(1901);
                    planner.Advance(8040f, false);
                    Check(UnityEngine.Random.value == expected, "Successful/rejected placement does not reroll the base generator.");
                    Check(planner.SpawnedCount == 1 && ExpeditionPickup.ActiveCount == 1,
                        "A real certified moderate opening route provides one optional find.");
                    ExpeditionPickup pickup = Object.FindAnyObjectByType<ExpeditionPickup>();
                    int height = pickup != null ? WorldZones.ScoreAtHeight(pickup.transform.position.y) : -1;
                    Check(height >= 30 && height <= 120 && pickup.Kind == ExpeditionFindKind.FireflyJar,
                        "First visible find arrives early, behind its type gate, not an unreachable late reward.");
                    Check(pickup.transform.parent == null, "Cargo remains independent of platform transforms.");
                    spawned++;
                }
                ExpeditionPickup.Spawn(ExpeditionFindKind.FireflyJar, new Vector2(8001f, 8010f), camera);
                Check(ExpeditionPickup.ActiveCount == ExpeditionFindSpawner.MaximumAlive, "Fixture fills the explicit live-item cap.");
                ExpeditionFindSpawner capped = Planner(player, camera, platforms, 74u);
                capped.Advance(8040f, false);
                Check(capped.SpawnedCount == 0 && capped.PlacementAttempts == 0, "The live cap prevents screen clutter and redundant proofs.");
                ExpeditionPickup.RemoveUnclaimed();
                Check(ExpeditionPickup.ActiveCount == 0, "Layout cleanup removes and unregisters every unclaimed cargo.");
                RunFlow.RunActive = false;
                Check(GameplayPreferences.SetRunKind(GameRunKind.Classic, false), "Fixture restores classic outside a run.");
                RunFlow.RunActive = true;
                ExpeditionFindSpawner classic = Planner(player, camera, platforms, 14u); classic.Advance(8040f, false);
                Check(classic.PlacementAttempts == 0 && ExpeditionPickup.ActiveCount == 0,
                    "Classic mode contains neither cargo nor expedition proof work.");
                RunFlow.RunActive = false;
                Object.DestroyImmediate(root); root = null; Object.DestroyImmediate(sprite); sprite = null;
            }
            Debug.Log("EXPEDITION_PLACEMENT_PASS checks=" + checks + " modes=" + modes + " certifiedOpeningFinds=" + spawned +
                " pendingWait=true unchangedRng=true noClassicCargo=true cap=2");
            return checks;
        }
        finally
        {
            ExpeditionPickup.RemoveUnclaimed();
            if (root != null) Object.DestroyImmediate(root);
            if (sprite != null) Object.DestroyImmediate(sprite);
            RunFlow.RunActive = false;
            WorldZones.ConfigureRun(oldStart, oldRate); WorldZones.SetScore(oldHeight);
            for (int i = 0; i < keys.Length; i++) { if (existed[i]) PlayerPrefs.SetInt(keys[i], values[i]); else PlayerPrefs.DeleteKey(keys[i]); }
            RunFlow.RunActive = oldActive; RunFlow.IsTransitioning = oldTransition; Time.timeScale = oldTime;
            UnityEngine.Random.state = random;
        }
    }

    public static void CaptureFinds()
    {
        RequireSafeEditor();
        GameObject root = new GameObject("Expedition paper finds preview");
        Material ink = null; RenderTexture target = null; Texture2D pixels = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            GameObject cameraObject = Child(root, "Camera"); Camera camera = cameraObject.AddComponent<Camera>();
            int count = ExpeditionCatalog.All.Count, imageWidth = count * 300;
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = .35f; camera.aspect = count;
            camera.transform.position = new Vector3(8000f, 8000f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(255, 232, 179, 255); camera.cullingMask = 1 << 30;
            ink = new Material(Resources.Load<Shader>("HazardInk"));
            for (int i = 0; i < ExpeditionCatalog.All.Count; i++)
            {
                GameObject icon = Child(root, ExpeditionCatalog.All[i].Name); icon.layer = 30;
                icon.transform.position = new Vector3(8000f + (i - (count - 1) * .5f) * .62f, 8000f, 0f);
                icon.AddComponent<MeshFilter>().sharedMesh = ExpeditionArt.FindMesh(ExpeditionCatalog.All[i].Kind);
                icon.AddComponent<MeshRenderer>().sharedMaterial = ink;
            }
            target = new RenderTexture(imageWidth, 300, 24); pixels = new Texture2D(imageWidth, 300, TextureFormat.RGB24, false);
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, imageWidth, 300), 0, 0); pixels.Apply();
            Directory.CreateDirectory("Logs/ExpeditionFinds");
            File.WriteAllBytes("Logs/ExpeditionFinds/Finds.png", pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(root);
            if (target != null) Object.DestroyImmediate(target);
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (ink != null) Object.DestroyImmediate(ink);
        }
    }

    private static ExpeditionFindSpawner Planner(Player player, Camera camera, List<GameObject> platforms, uint seed)
    {
        ExpeditionFindSpawner result = new ExpeditionFindSpawner(player, camera, 0f, seed);
        foreach (GameObject platform in platforms) result.RegisterPlatform(platform, 0f);
        return result;
    }
    private static GameObject Child(GameObject root, string name)
    { GameObject result = new GameObject(name); result.transform.SetParent(root.transform, false); return result; }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException("Expedition world: " + message); }
    private static void RequireSafeEditor()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/") || Application.companyName != "CodexValidation")
            throw new InvalidOperationException("Expedition world checks require an isolated CodexValidation batch project.");
    }
}
#endif
