#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Synchronous sandbox checks. No purchases, equip changes, preferences or live
// scene objects are mutated; the root agent can include this in Play Mode QA.
public static class WardrobeArtValidation
{
    [MenuItem("Doodlik/Validation/Wardrobe artwork")]
    public static void Validate()
    {
        Require(Application.isPlaying, "Wardrobe art validation requires Play Mode.");
        Texture2D atlas = Resources.Load<Texture2D>(WardrobeArt.ResourcePath);
        Require(atlas != null, "The illustrated wardrobe atlas is available.");
        string[] ids = { "upgrade_magnet", "upgrade_duration", "accessory_flower", "accessory_glasses", "accessory_tufts", "accessory_star" };
        Sprite[] art = new Sprite[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            art[i] = WardrobeArt.GetSprite(ids[i]);
            Require(art[i] != null && art[i].texture != null && art[i].rect.width > 0f && art[i].rect.height > 0f,
                "Every accessory and upgrade uses illustrated artwork.");
            Require(ReferenceEquals(art[i], WardrobeArt.GetSprite(ids[i])), "Runtime sprites are cached, not recreated every frame.");
            Require(art[i].rect.xMin >= 0f && art[i].rect.yMin >= 0f && art[i].rect.xMax <= art[i].texture.width && art[i].rect.yMax <= art[i].texture.height,
                "All sprite rectangles stay inside the texture.");
            for (int j = 0; j < i; j++)
                if (art[i].texture == art[j].texture) Require(!art[i].rect.Overlaps(art[j].rect), "Art rectangles never sample another item.");
        }
        Require(WardrobeArt.GetSprite("accessory_none") == null && WardrobeArt.GetSprite("trail_honey") == null &&
            WardrobeArt.GetSprite("scarf_berry") == null, "Empty slots, removed scarves and vector trails do not sample artwork.");

        GameObject sandbox = new GameObject("Wardrobe artwork validation");
        sandbox.hideFlags = HideFlags.HideAndDontSave;
        sandbox.transform.position = new Vector3(8000f, 8000f, 0f);
        Mesh mesh = new Mesh();
        try
        {
            GameObject previewObject = new GameObject("Preview", typeof(RectTransform));
            previewObject.transform.SetParent(sandbox.transform, false);
            previewObject.GetComponent<RectTransform>().sizeDelta = new Vector2(200f, 180f);
            DoodlikCosmeticPreview preview = previewObject.AddComponent<DoodlikCosmeticPreview>();
            MethodInfo populate = typeof(DoodlikCosmeticPreview).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(UnityEngine.UI.VertexHelper) }, null);
            for (int i = 0; i < ids.Length; i++)
            {
                preview.Configure(ids[i], false);
                Populate(preview, populate, mesh);
                Require(preview.mainTexture == art[i].texture && mesh.vertexCount == 4 && mesh.triangles.Length == 6,
                    "Accessory previews use a textured quad, not primitive placeholder art.");
                Require(Mathf.Abs(mesh.bounds.size.x / mesh.bounds.size.y - art[i].rect.width / art[i].rect.height) < 0.001f,
                    "Catalog art keeps its aspect ratio.");
                Vector2 first = mesh.uv[0];
                Vector2 last = mesh.uv[2];
                Require(last.x > first.x && last.y > first.y, "The preview samples the full cutout, not one UV pixel.");
            }
            preview.Configure("trail_honey", false);
            Populate(preview, populate, mesh);
            Require(preview.mainTexture != atlas && mesh.vertexCount > 0, "Switching to trails restores the white texture.");
            preview.Configure("accessory_flower", false);
            Populate(preview, populate, mesh);
            Require(preview.mainTexture == art[2].texture && mesh.vertexCount == 4, "Switching back restores the artwork texture.");
            preview.Configure("accessory_none", false);
            Populate(preview, populate, mesh);
            Require(mesh.vertexCount == 0, "An empty accessory slot has no visible accessory.");
            preview.Configure("accessory_tufts", true);
            Populate(preview, populate, mesh);
            Require(mesh.vertexCount == 8, "The cat preview places two ear tufts, not one stretched object.");

            Sprite catSprite = null;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Спрайт-0001.png"))
                if (asset is Sprite candidate) { catSprite = candidate; break; }
            Require(catSprite != null, "Original cat sprite is available for fit checks.");
            GameObject cat = new GameObject("Validation cat", typeof(SpriteRenderer));
            cat.transform.SetParent(sandbox.transform, false);
            SpriteRenderer catRenderer = cat.GetComponent<SpriteRenderer>();
            catRenderer.sprite = catSprite;
            CatAppearance appearance = CatAppearance.Attach(cat.transform, catRenderer);
            Transform accessory = cat.transform.Find("Cat accessory 0");
            Transform secondAccessory = cat.transform.Find("Cat accessory 1");
            Require(appearance != null && accessory != null && secondAccessory != null && accessory.GetComponent<SpriteRenderer>() != null,
                "Gameplay has two reusable accessory SpriteRenderers.");
            Require(cat.transform.Find("Hand drawn scarf") == null, "Gameplay does not create old scarf geometry.");
            SpriteRenderer accessoryRenderer = accessory.GetComponent<SpriteRenderer>();
            accessoryRenderer.sprite = art[3];
            accessoryRenderer.enabled = true;
            typeof(CatAppearance).GetField("lastAccessory", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(appearance, "accessory_glasses");
            MethodInfo updateFit = typeof(CatAppearance).GetMethod("UpdateAccessoryTransform", BindingFlags.Instance | BindingFlags.NonPublic);
            updateFit.Invoke(appearance, null);
            Rect fit = WardrobeArt.AccessoryRect("accessory_glasses", catSprite.bounds.size);
            Require(Mathf.Abs(accessory.localScale.x * art[3].bounds.size.x - fit.width) < 0.001f &&
                Mathf.Abs(accessory.localScale.y * art[3].bounds.size.y - fit.height) < 0.001f,
                "World glasses match home preview dimensions without distorting the art.");
            Require(fit.yMin > 0f && fit.yMax < catSprite.bounds.size.y * 0.21f,
                "Glasses sit on the eyes above the mouth.");
            float positionBefore = accessory.localPosition.x;
            catRenderer.flipX = true;
            updateFit.Invoke(appearance, null);
            Require(accessory.localScale.x < 0f && Mathf.Abs(accessory.localPosition.x + positionBefore) < 0.001f,
                "Accessories mirror and move with the cat.");
            ValidateTuftFit(sandbox.transform, catSprite, catRenderer, appearance, updateFit, populate, mesh);
            Require(cat.GetComponentsInChildren<Collider2D>().Length == 0, "Cosmetic art introduces no physics colliders.");
            ValidateClothing(catSprite);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(sandbox);
        }
        Debug.Log("WARDROBE_ART_VALIDATION PASS: accessories/upgrades, UVs, texture switches, two tuft fits across UI/world sizes and facing, fitted/mirrored accessories, independent fur/clothes materials, no physics changes.");
    }

    private static void ValidateTuftFit(Transform sandbox, Sprite catSprite, SpriteRenderer catRenderer,
        CatAppearance appearance, MethodInfo updateFit, MethodInfo populate, Mesh mesh)
    {
        const string item = "accessory_tufts";
        Sprite tuft = WardrobeArt.GetSprite(item);
        Require(WardrobeArt.InstanceCount(item) == 2, "Ear tufts always have one illustration for each ear.");
        Require(!WardrobeArt.AccessoryFlipX(item, 0) && WardrobeArt.AccessoryFlipX(item, 1),
            "The second tuft mirrors the artwork so the pair points in opposite directions.");
        Require(WardrobeArt.AccessoryAngle(item, 0) > 0f && WardrobeArt.AccessoryAngle(item, 1) < 0f &&
            Mathf.Abs(Mathf.DeltaAngle(WardrobeArt.AccessoryAngle(item, 0), -WardrobeArt.AccessoryAngle(item, 1))) < 0.001f,
            "The tufts use equal outward-facing tilt, not the same slant on both ears.");
        SpriteRenderer[] renderers = new SpriteRenderer[2];
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i] = catRenderer.transform.Find("Cat accessory " + i).GetComponent<SpriteRenderer>();
            renderers[i].sprite = tuft;
            renderers[i].enabled = true;
        }
        typeof(CatAppearance).GetField("lastAccessory", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(appearance, item);

        // A real sibling Image is essential: without it the UI silently falls
        // back to the preview rectangle's aspect instead of fitting the cat.
        GameObject group = new GameObject("Tuft fit preview", typeof(RectTransform));
        group.transform.SetParent(sandbox, false);
        GameObject imageObject = new GameObject("Cat", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        imageObject.transform.SetParent(group.transform, false);
        UnityEngine.UI.Image catImage = imageObject.GetComponent<UnityEngine.UI.Image>();
        catImage.sprite = catSprite;
        catImage.preserveAspect = true;
        GameObject previewObject = new GameObject("Accessories", typeof(RectTransform));
        previewObject.transform.SetParent(group.transform, false);
        DoodlikCosmeticPreview preview = previewObject.AddComponent<DoodlikCosmeticPreview>();
        preview.Configure(item, true);

        Vector2[] previewSizes = { new Vector2(200f, 180f), new Vector2(220f, 360f), new Vector2(320f, 140f) };
        Vector3[] modelScales = { Vector3.one, new Vector3(0.62f, 0.62f, 1f),
            new Vector3(1.16f, 0.84f, 1f), new Vector3(0.88f, 1.14f, 1f) };
        Vector4 uv = UnityEngine.Sprites.DataUtility.GetOuterUV(tuft);
        Vector2[] sourceUvs = { new Vector2(uv.x, uv.y), new Vector2(uv.x, uv.w),
            new Vector2(uv.z, uv.w), new Vector2(uv.z, uv.y) };
        Bounds artBounds = tuft.bounds;
        Vector3[] sourceCorners = { new Vector3(artBounds.min.x, artBounds.min.y),
            new Vector3(artBounds.min.x, artBounds.max.y), new Vector3(artBounds.max.x, artBounds.max.y),
            new Vector3(artBounds.max.x, artBounds.min.y) };
        Vector3 catSize = catSprite.bounds.size;
        Vector3 catCenter = catSprite.bounds.center;
        catRenderer.flipY = false;

        foreach (Vector2 previewSize in previewSizes)
        {
            catImage.rectTransform.sizeDelta = previewSize;
            preview.rectTransform.sizeDelta = previewSize;
            Populate(preview, populate, mesh);
            Require(mesh.vertexCount == 8 && mesh.triangles.Length == 12,
                "The fitted cat preview retains two complete tuft quads at every layout size.");
            Vector2 fittedCat = catSprite.rect.size * Mathf.Min(previewSize.x / catSprite.rect.width,
                previewSize.y / catSprite.rect.height);
            Vector3[] vertices = mesh.vertices;
            Vector2[] meshUvs = mesh.uv;
            for (int instance = 0; instance < renderers.Length; instance++)
            {
                Vector3 bottomLeft = VertexAtUv(vertices, meshUvs, instance * 4, sourceUvs[0]);
                Vector3 bottomRight = VertexAtUv(vertices, meshUvs, instance * 4, sourceUvs[3]);
                Require((bottomRight.x - bottomLeft.x) * (instance == 0 ? 1f : -1f) > 0f,
                    "The two UI tufts sample the illustration in opposite horizontal directions.");
                Vector3 root = PointAtSourceUv(vertices, meshUvs, instance * 4, sourceUvs, WardrobeArt.TuftRootUV);
                root -= (Vector3)preview.rectTransform.rect.center;
                Vector2 uiRoot = new Vector2(root.x / fittedCat.x * catSize.x, root.y / fittedCat.y * catSize.y);
                Require((uiRoot - WardrobeArt.TuftEarAnchor(catSize, instance)).magnitude < catSize.magnitude * 0.0001f,
                    "Each UI tuft is attached by its illustrated root, not its rectangle center, at every layout size.");
            }
            foreach (Vector3 scale in modelScales)
            for (int facingIndex = 0; facingIndex < 2; facingIndex++)
            {
                bool flipped = facingIndex != 0;
                float facing = flipped ? -1f : 1f;
                catRenderer.transform.localScale = scale;
                catRenderer.flipX = flipped;
                updateFit.Invoke(appearance, null);
                for (int instance = 0; instance < renderers.Length; instance++)
                {
                    SpriteRenderer renderer = renderers[instance];
                    Matrix4x4 accessoryToCat = Matrix4x4.TRS(renderer.transform.localPosition,
                        renderer.transform.localRotation, renderer.transform.localScale);
                    Vector2 ear = WardrobeArt.TuftEarAnchor(catSize, instance);
                    Vector3 expectedRoot = new Vector3((catCenter.x + ear.x) * facing, catCenter.y + ear.y, 0f);
                    Vector3 sourceRoot = new Vector3(artBounds.min.x + WardrobeArt.TuftRootUV.x * artBounds.size.x,
                        artBounds.min.y + WardrobeArt.TuftRootUV.y * artBounds.size.y, 0f);
                    if (renderer.flipX) sourceRoot.x = -sourceRoot.x;
                    if (renderer.flipY) sourceRoot.y = -sourceRoot.y;
                    Vector3 actualRoot = accessoryToCat.MultiplyPoint3x4(sourceRoot);
                    actualRoot.z = 0f;
                    Vector3 rootError = catRenderer.transform.localToWorldMatrix.MultiplyVector(actualRoot - expectedRoot);
                    float tolerance = Mathf.Max(catSize.x * Mathf.Abs(scale.x), catSize.y * Mathf.Abs(scale.y)) * 0.0001f;
                    Require(rootError.magnitude <= tolerance,
                        "The illustrated tuft roots remain on their own ear anchors through facing, portrait scaling, squash and stretch.");
                    for (int corner = 0; corner < sourceCorners.Length; corner++)
                    {
                        // Match sampled artwork pixels, not vertex ordering: an
                        // unmirrored second tuft must fail even at the right size.
                        Vector3 ui = VertexAtUv(vertices, meshUvs, instance * 4, sourceUvs[corner]);
                        ui -= (Vector3)preview.rectTransform.rect.center;
                        Vector3 expected = new Vector3((catCenter.x + ui.x / fittedCat.x * catSize.x) * facing,
                            catCenter.y + ui.y / fittedCat.y * catSize.y, 0f);
                        Vector3 source = sourceCorners[corner];
                        if (renderer.flipX) source.x = -source.x;
                        if (renderer.flipY) source.y = -source.y;
                        Vector3 actual = accessoryToCat.MultiplyPoint3x4(source);
                        actual.z = 0f;
                        Vector3 renderedError = catRenderer.transform.localToWorldMatrix.MultiplyVector(actual - expected);
                        Require(renderedError.magnitude <= tolerance,
                            "Each sampled tuft corner follows the same ear in UI/world, including facing and portrait/squash/stretch scales: " +
                            instance + ", flipX=" + flipped + ", preview=" + previewSize + ", scale=" + scale + ".");
                    }
                }
            }
        }
    }

    private static Vector3 PointAtSourceUv(Vector3[] vertices, Vector2[] meshUvs, int first, Vector2[] corners, Vector2 point)
    {
        Vector3 bottomLeft = VertexAtUv(vertices, meshUvs, first, corners[0]);
        Vector3 topLeft = VertexAtUv(vertices, meshUvs, first, corners[1]);
        Vector3 bottomRight = VertexAtUv(vertices, meshUvs, first, corners[3]);
        return bottomLeft + (bottomRight - bottomLeft) * point.x + (topLeft - bottomLeft) * point.y;
    }

    private static Vector3 VertexAtUv(Vector3[] vertices, Vector2[] uvs, int first, Vector2 requested)
    {
        for (int i = first; i < first + 4; i++)
            if ((uvs[i] - requested).sqrMagnitude < 0.00000001f) return vertices[i];
        throw new InvalidOperationException("Each tuft uses all four source UV corners, including mirrored instances.");
    }

    private static void ValidateClothing(Sprite catSprite)
    {
        string[] cats = { "cat_ginger", "cat_cocoa", "cat_rose" };
        string[] clothing = { "clothing_original", "clothing_ocean", "clothing_berry" };
        foreach (string cat in cats)
        foreach (string outfit in clothing)
        {
            Material world = CatClothingMaterials.For(catSprite, cat, outfit, false);
            Material ui = CatClothingMaterials.For(catSprite, cat, outfit, true);
            Require(world != null && ui != null && world.shader.isSupported && ui.shader.isSupported,
                "Independent clothing materials are available on the active graphics API.");
            Require(!ShaderUtil.ShaderHasError(world.shader) && !ShaderUtil.ShaderHasError(ui.shader),
                "Clothing shaders have no import/compiler errors.");
            Require(world == CatClothingMaterials.For(catSprite, cat, outfit, false), "Clothing materials are cached.");
            Require(world.GetColor("_BodyTint") == CatAppearance.GetCatTint(cat) &&
                world.GetColor("_ClothingColor") == CatClothingMaterials.GetClothingColor(outfit),
                "The selected fur and clothing colors have independent properties.");
            Require(world.GetColor("_BodyTint") == ui.GetColor("_BodyTint") &&
                world.GetColor("_ClothingColor") == ui.GetColor("_ClothingColor") &&
                world.GetVector("_SpriteUVRect") == ui.GetVector("_SpriteUVRect"),
                "UI and gameplay use the same color and texture region rules.");
            Require(Mathf.Approximately(world.GetFloat("_ClothingStrength"), outfit == "clothing_original" ? 0f : 1f),
                "Original clothes retain source pixels rather than receiving another cat's tint.");
        }
    }

    private static void Populate(DoodlikCosmeticPreview preview, MethodInfo method, Mesh mesh)
    {
        using (UnityEngine.UI.VertexHelper helper = new UnityEngine.UI.VertexHelper())
        {
            method.Invoke(preview, new object[] { helper });
            mesh.Clear();
            helper.FillMesh(mesh);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [MenuItem("Doodlik/Validation/Wardrobe artwork", true)]
    private static bool CanValidate() => EditorApplication.isPlaying;
}
#endif
