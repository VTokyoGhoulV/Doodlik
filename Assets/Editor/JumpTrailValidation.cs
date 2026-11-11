#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Synchronous isolated presenter checks. The root audit can invoke Validate in
// Play Mode; this does not move the real player or write the player's wallet.
public static class JumpTrailValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;

    public static int Validate()
    {
        Require(Application.isPlaying, "Trail checks need Play Mode.");
        checks = 0;
        bool oldRun = RunFlow.RunActive, oldTransition = RunFlow.IsTransitioning;
        float oldTimeScale = Time.timeScale;
        UnityEngine.Random.State oldRandom = UnityEngine.Random.state;
        GameObject owner = null, previewObject = null;
        Mesh previewMesh = new Mesh();
        using (PlayerProgress.UseIsolatedStorageForValidation(out _, out _))
        {
            try
            {
                owner = new GameObject("Trail presentation validation");
                owner.transform.position = new Vector3(8000f, 8000f, 0f);
                Player player = owner.AddComponent<Player>();
                player.enabled = false; // No Start/Update or gameplay side effects.
                GameObject visual = new GameObject("Validation cat", typeof(SpriteRenderer));
                visual.transform.SetParent(owner.transform, false);
                SpriteRenderer cat = visual.GetComponent<SpriteRenderer>();
                cat.sprite = WardrobeArt.GetCatSprite("cat_calico", null);
                Require(cat.sprite != null, "Fixture has a real imported cat sprite.");
                CatAppearance appearance = CatAppearance.Attach(visual.transform, cat);
                JumpTrailVisuals trail = visual.GetComponent<JumpTrailVisuals>();
                Require(appearance != null && trail != null && trail.PoolCapacity <= 12,
                    "World appearance owns a bounded trail pool.");
                previewObject = new GameObject("Trail card validation", typeof(RectTransform));
                DoodlikCosmeticPreview preview = previewObject.AddComponent<DoodlikCosmeticPreview>();
                preview.rectTransform.sizeDelta = new Vector2(200f, 180f);
                PlayerProgress.AddCoins(10000);
                foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
                    if (item.Category == PlayerProgress.Category.Trail && item.Price >= 0)
                        Require(PlayerProgress.Owns(item.Id) || PlayerProgress.TryPurchase(item.Id),
                            "Purchasable trails keep their shop path: " + item.Id);
                PlayerProgress.BeginAdminSandbox();
                RunFlow.RunActive = true;
                RunFlow.IsTransitioning = false;
                Time.timeScale = 1f;
                int drawnStyles = 0;
                HashSet<ulong> silhouettes = new HashSet<ulong>();
                foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
                {
                    if (item.Category != PlayerProgress.Category.Trail) continue;
                    Require(PlayerProgress.Owns(item.Id),
                        "Every trail is owned in the disposable art sandbox: " + item.Id);
                    Require(PlayerProgress.Equip(item.Id), "Every trail can be equipped: " + item.Id);
                    preview.Configure(item.Id, false);
                    Populate(preview, previewMesh);
                    Require((previewMesh.vertexCount > 0) == (item.Id != "trail_none"),
                        "Trail card has visible art exactly when expected: " + item.Id);
                    if (previewMesh.vertexCount > 0)
                    {
                        Bounds bounds = previewMesh.bounds;
                        Require(bounds.min.x >= -100f && bounds.max.x <= 100f &&
                            bounds.min.y >= -90f && bounds.max.y <= 90f,
                            "Trail card art remains inside its preview rectangle: " + item.Id);
                    }
                    JumpTrailArt.Drawing drawing = JumpTrailArt.Get(item.Id);
                    if (drawing == null) continue; // The two legacy ribbon items remain compatible.
                    drawnStyles++;
                    Require(drawing.Vertices.Length == drawing.Colors.Length && drawing.Triangles.Length > 0,
                        "Drawing has complete colored geometry: " + item.Id);
                    Require(ReferenceEquals(drawing, JumpTrailArt.Get(item.Id)), "Drawing is cached: " + item.Id);
                    Require(silhouettes.Add(GeometrySignature(drawing)), "New trail has its own silhouette: " + item.Id);
                    UnityEngine.Random.InitState(8711);
                    int expectedRandom = UnityEngine.Random.Range(0, int.MaxValue);
                    UnityEngine.Random.InitState(8711);
                    Rise(visual.transform, trail, 50);
                    Require(trail.ActiveCount > 0 && trail.ActiveCount <= trail.PoolCapacity,
                        "Rising emits visible motes without exceeding the pool: " + item.Id);
                    Require(UnityEngine.Random.Range(0, int.MaxValue) == expectedRandom,
                        "Trail does not consume gameplay generation RNG: " + item.Id);
                    Transform root = (Transform)typeof(JumpTrailVisuals).GetField("poolRoot", PrivateInstance).GetValue(trail);
                    Require(root != null && root.GetComponentsInChildren<Collider2D>(true).Length == 0,
                        "Trail introduces no collisions: " + item.Id);
                    Require(root.childCount == trail.PoolCapacity,
                        "Emission reuses the same fixed number of drawing objects: " + item.Id);
                    int activeBeforePause = trail.ActiveCount;
                    Time.timeScale = 0f;
                    Refresh(trail, 2f);
                    Require(trail.ActiveCount == activeBeforePause, "Pause freezes the trail fade: " + item.Id);
                    Time.timeScale = 1f;
                    for (int step = 0; step < 40; step++)
                    {
                        visual.transform.position += Vector3.down * 0.03f;
                        Refresh(trail, 0.025f);
                    }
                    Require(trail.ActiveCount == 0, "Falling does not emit and old motes expire: " + item.Id);
                    Rise(visual.transform, trail, 8);
                    visual.transform.position += Vector3.right * cat.bounds.size.x * 4f;
                    Refresh(trail, 0.025f);
                    Require(trail.ActiveCount == 0, "Wrap or rescue teleport clears old trail: " + item.Id);
                    Rise(visual.transform, trail, 8);
                    RunFlow.RunActive = false;
                    Refresh(trail, 0.025f);
                    Require(trail.ActiveCount == 0, "Menu/death screen cannot retain a live trail: " + item.Id);
                    RunFlow.RunActive = true;
                    Rise(visual.transform, trail, 8);
                    trail.enabled = false;
                    Require(trail.ActiveCount == 0, "Disabling the presenter clears its pool: " + item.Id);
                    trail.enabled = true;
                    Require(PlayerProgress.Equip("trail_none"), "New drawing can be removed: " + item.Id);
                    Require(!root.gameObject.activeSelf &&
                        typeof(JumpTrailVisuals).GetField("poolRoot", PrivateInstance).GetValue(trail) == null,
                        "Unequipping immediately hides and releases the drawing pool: " + item.Id);
                }
                Require(drawnStyles >= 3, "The wardrobe offers several distinct drawing trails.");
            }
            finally
            {
                RunFlow.RunActive = oldRun;
                RunFlow.IsTransitioning = oldTransition;
                Time.timeScale = oldTimeScale;
                UnityEngine.Random.state = oldRandom;
                if (owner != null) Object.DestroyImmediate(owner);
                if (previewObject != null) Object.DestroyImmediate(previewObject);
                Object.DestroyImmediate(previewMesh);
                if (PlayerProgress.AdminSandboxActive) PlayerProgress.EndAdminSandbox();
            }
        }
        Debug.Log("JUMP_TRAILS_PASS checks=" + checks + " (catalog/UI geometry, distinct silhouettes, ascent/fade/pause/wrap/disable, bounded pool, gameplay RNG unchanged).");
        return checks;
    }

    private static void Rise(Transform visual, JumpTrailVisuals trail, int steps)
    {
        for (int step = 0; step < steps; step++)
        {
            visual.position += new Vector3(0.04f, 0.06f, 0f);
            Refresh(trail, 0.025f);
        }
    }

    private static void Refresh(JumpTrailVisuals trail, float dt) =>
        typeof(JumpTrailVisuals).GetMethod("Refresh", PrivateInstance).Invoke(trail, new object[] { dt });

    private static void Populate(DoodlikCosmeticPreview preview, Mesh mesh)
    {
        using (VertexHelper helper = new VertexHelper())
        {
            typeof(DoodlikCosmeticPreview).GetMethod("OnPopulateMesh", PrivateInstance, null,
                new[] { typeof(VertexHelper) }, null).Invoke(preview, new object[] { helper });
            mesh.Clear();
            helper.FillMesh(mesh);
        }
    }

    private static ulong GeometrySignature(JumpTrailArt.Drawing art)
    {
        ulong hash = 14695981039346656037UL;
        foreach (Vector3 point in art.Vertices)
            unchecked { hash = (hash ^ (uint)Mathf.RoundToInt(point.x * 10000f)) * 1099511628211UL;
                hash = (hash ^ (uint)Mathf.RoundToInt(point.y * 10000f)) * 1099511628211UL; }
        return hash;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Jump trail validation: " + message);
        checks++;
    }
}
#endif
