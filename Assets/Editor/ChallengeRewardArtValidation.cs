#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

// The broader cosmetics runner also captures every new catalog item. These
// checks protect the new illustrations' transparency, cache, fit and shared UI.
public static class ChallengeRewardArtValidation
{
    public static int Validate()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Reward art validation requires Play Mode.");
        int checks = 0;
        UnityEngine.Random.State random = UnityEngine.Random.state;
        string randomJson = JsonUtility.ToJson(random);
        GameObject previewObject = null;
        Mesh mesh = new Mesh();
        var textures = new HashSet<Texture>();
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/ChallengeRewardArt"));
        bool capture = Application.isBatchMode && Application.dataPath.Replace('\\', '/').Contains("/.utmp/");
        if (capture) Directory.CreateDirectory(output);
        try
        {
            previewObject = new GameObject("Earned reward card validation", typeof(RectTransform));
            var preview = previewObject.AddComponent<DoodlikCosmeticPreview>();
            preview.rectTransform.sizeDelta = new Vector2(200f, 180f);
            MethodInfo populate = typeof(DoodlikCosmeticPreview).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(UnityEngine.UI.VertexHelper) }, null);
            Require(populate != null, "Production preview draw method is available.", ref checks);
            foreach (string id in new[] { "accessory_target", "accessory_patch", "clothing_explorer" })
            {
                Sprite sprite = WardrobeArt.GetSprite(id);
                Require(sprite != null && ReferenceEquals(sprite, WardrobeArt.GetSprite(id)), "Shared cached illustrated sprite: " + id, ref checks);
                Require(textures.Add(sprite.texture), "Different rewards are separate illustrations: " + id, ref checks);
                Color32[] pixels = sprite.texture.GetPixels32();
                int opaque = 0, transparent = 0;
                var colors = new HashSet<uint>();
                foreach (Color32 pixel in pixels)
                {
                    if (pixel.a < 10) transparent++;
                    if (pixel.a > 200) { opaque++; colors.Add((uint)(pixel.r << 16 | pixel.g << 8 | pixel.b)); }
                }
                Require(opaque > 500 && transparent > 500 && colors.Count > 20,
                    "Cutout has substantial multicolour drawing and a transparent margin: " + id, ref checks);
                preview.Configure(id, false);
                using (var vertices = new UnityEngine.UI.VertexHelper())
                {
                    populate.Invoke(preview, new object[] { vertices });
                    mesh.Clear(); vertices.FillMesh(mesh);
                }
                Require(preview.mainTexture == sprite.texture && mesh.vertexCount == 4,
                    "Wardrobe card samples the real shared drawing: " + id, ref checks);
                Require(mesh.bounds.size.x <= 200.1f && mesh.bounds.size.y <= 180.1f,
                    "Reward stays inside the item-card rectangle: " + id, ref checks);
                if (id == "clothing_explorer")
                {
                    Require(WardrobeArt.GetClothingSprite(id) == sprite, "Explorer uses the production clothing layer.", ref checks);
                    Rect fit = WardrobeArt.ClothingRect(id, Vector2.one);
                    Require(fit.yMin >= -0.36f && fit.yMax < 0.08f && fit.width <= 0.72f,
                        "Vest fits the existing torso without sleeves or a neck.", ref checks);
                }
                else
                {
                    Rect fit = WardrobeArt.AccessoryRect(id, Vector2.one);
                    Require(fit.yMin > 0.24f && fit.yMax < 0.58f && fit.xMin > -0.40f && fit.xMax < 0.20f,
                        "Head/ear reward stays clear of eyes and mouth: " + id, ref checks);
                }
                if (capture) File.WriteAllBytes(Path.Combine(output, id + ".png"), sprite.texture.EncodeToPNG());
            }
            foreach (string id in new[] { "trail_ninelives", "trail_meteor" })
            {
                JumpTrailArt.Drawing drawing = JumpTrailArt.Get(id);
                Require(drawing != null && ReferenceEquals(drawing, JumpTrailArt.Get(id)), "Earned motif is cached: " + id, ref checks);
                Require(drawing.Vertices.Length == drawing.Colors.Length && drawing.Vertices.Length > 24 && drawing.Triangles.Length > 50,
                    "Earned trail has complete coloured artwork: " + id, ref checks);
                foreach (int triangle in drawing.Triangles)
                    Require(triangle >= 0 && triangle < drawing.Vertices.Length, "All motif triangles reference valid vertices.", ref checks);
                preview.Configure(id, false);
                using (var vertices = new UnityEngine.UI.VertexHelper())
                {
                    populate.Invoke(preview, new object[] { vertices });
                    mesh.Clear(); vertices.FillMesh(mesh);
                }
                Require(mesh.vertexCount > 0 && mesh.bounds.size.x <= 200.1f && mesh.bounds.size.y <= 180.1f,
                    "Earned motif is visible within the shared wardrobe card: " + id, ref checks);
            }
            Require(JsonUtility.ToJson(UnityEngine.Random.state) == randomJson,
                "Reward art never consumes the procedural route RNG.", ref checks);
            if (capture) CaptureWorldRewards(output, ref checks);
            Debug.Log("CHALLENGE_REWARD_ART_PASS checks=" + checks + " sprites=3 trails=2 output=" + output);
            return checks;
        }
        finally
        {
            UnityEngine.Random.state = random;
            if (previewObject != null) Object.DestroyImmediate(previewObject);
            Object.DestroyImmediate(mesh);
        }
    }

    private static void CaptureWorldRewards(string output, ref int checks)
    {
        CatAppearance source = Object.FindAnyObjectByType<CatAppearance>();
        Camera camera = Camera.main;
        Require(source != null && camera != null, "Production appearance and URP camera are available.", ref checks);
        SpriteRenderer original = source.GetComponent<SpriteRenderer>();
        Require(original != null && original.sprite != null, "The original Doodlik sprite is available.", ref checks);
        Vector3 oldPosition = camera.transform.position;
        float oldSize = camera.orthographicSize, oldAspect = camera.aspect;
        CameraClearFlags oldClear = camera.clearFlags;
        Color oldBackground = camera.backgroundColor;
        Canvas[] canvases = Object.FindObjectsByType<Canvas>();
        bool[] enabled = new bool[canvases.Length];
        for (int i = 0; i < canvases.Length; i++) { enabled[i] = canvases[i].enabled; canvases[i].enabled = false; }
        GameObject owner = null;
        using (PlayerProgress.UseIsolatedStorageForValidation(out _, out _))
        {
            try
            {
                PlayerProgress.BeginAdminSandbox();
                owner = new GameObject("Earned artwork world fixture");
                owner.transform.position = new Vector3(8000f, 8000f, 0f);
                owner.AddComponent<Player>().enabled = false;
                GameObject visual = new GameObject("Actual reward cat", typeof(SpriteRenderer));
                visual.transform.SetParent(owner.transform, false);
                visual.transform.localScale = source.transform.lossyScale;
                SpriteRenderer renderer = visual.GetComponent<SpriteRenderer>();
                renderer.sprite = original.sprite;
                renderer.sharedMaterial = original.sharedMaterial;
                CatAppearance appearance = CatAppearance.Attach(visual.transform, renderer);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(244, 230, 191, 255);
                camera.orthographicSize = Mathf.Max(0.7f, renderer.bounds.size.y * 0.85f);
                camera.transform.position = new Vector3(renderer.bounds.center.x, renderer.bounds.center.y, oldPosition.z);
                PlayerProgress.Equip("cat_ginger");
                PlayerProgress.Equip("accessory_none");
                PlayerProgress.Equip("clothing_original");
                PlayerProgress.Equip("trail_none");
                appearance.ApplyEquipped();
                Color32[] baseline = Render(camera, null);
                foreach (string id in new[] { "accessory_target", "accessory_patch", "clothing_explorer" })
                {
                    PlayerProgress.Equip("accessory_none");
                    PlayerProgress.Equip("clothing_original");
                    Require(PlayerProgress.Equip(id), "Reward can be equipped in the visual sandbox: " + id, ref checks);
                    appearance.ApplyEquipped();
                    Color32[] captured = Render(camera, Path.Combine(output, "World-" + id + ".png"));
                    int pixels = 0;
                    for (int i = 0; i < baseline.Length; i++)
                        if (Math.Abs(baseline[i].r - captured[i].r) + Math.Abs(baseline[i].g - captured[i].g) + Math.Abs(baseline[i].b - captured[i].b) > 12) pixels++;
                    Require(pixels > 30, "Reward changes real world-rendered pixels: " + id + " pixels=" + pixels, ref checks);
                    SpriteRenderer layer = id == "clothing_explorer" ? visual.transform.Find("Cat clothing artwork").GetComponent<SpriteRenderer>()
                        : visual.transform.Find("Cat accessory 0").GetComponent<SpriteRenderer>();
                    Require(layer.enabled && layer.sprite == WardrobeArt.GetSprite(id) && layer.sharedMaterial != null,
                        "Production sprite layer uses the shared illustration: " + id, ref checks);
                    Debug.Log("CHALLENGE_REWARD_WORLD_CASE id=" + id + " pixels=" + pixels);
                }
                PlayerProgress.Equip("accessory_none");
                PlayerProgress.Equip("clothing_explorer");
                PlayerProgress.Equip("accessory_target");
                PlayerProgress.Equip("accessory_patch");
                PlayerProgress.Equip("accessory_glasses");
                appearance.ApplyEquipped();
                Render(camera, Path.Combine(output, "World-explorer-three-accessories.png"));
                Require(PlayerProgress.EquippedAccessories.Count == 3, "Target, patch and glasses use three separate compatible slots.", ref checks);
            }
            finally
            {
                if (owner != null) Object.DestroyImmediate(owner);
                if (PlayerProgress.AdminSandboxActive) PlayerProgress.EndAdminSandbox();
                camera.transform.position = oldPosition;
                camera.orthographicSize = oldSize;
                camera.aspect = oldAspect;
                camera.clearFlags = oldClear;
                camera.backgroundColor = oldBackground;
                for (int i = 0; i < canvases.Length; i++) if (canvases[i] != null) canvases[i].enabled = enabled[i];
            }
        }
    }

    private static Color32[] Render(Camera camera, string file)
    {
        RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
        float aspect = camera.aspect;
        RenderTexture target = new RenderTexture(512, 512, 24);
        Texture2D image = new Texture2D(512, 512, TextureFormat.RGB24, false);
        try
        {
            camera.aspect = 1f;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            image.Apply();
            if (file != null) File.WriteAllBytes(file, image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally
        {
            camera.targetTexture = previousTarget;
            camera.aspect = aspect;
            RenderTexture.active = previousActive;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
        }
    }

    private static void Require(bool condition, string message, ref int checks)
    {
        if (!condition) throw new InvalidOperationException("Challenge reward artwork: " + message);
        checks++;
    }
}
#endif
