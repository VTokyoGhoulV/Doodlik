using UnityEngine;
using UnityEngine.UI;

// Cosmetics follow only the visual child. They never change colliders or jump
// parameters, and inherit the same portrait scaling and squash as the cat.
public sealed class CatAppearance : MonoBehaviour
{
    private static Material inkMaterial;
    private static int users;

    private SpriteRenderer catRenderer;
    private readonly SpriteRenderer[] accessoryRenderers = new SpriteRenderer[4];
    private readonly string[] accessoryIds = new string[4];
    private readonly int[] accessoryInstances = new int[4];
    private SpriteRenderer skinRenderer, clothingRenderer, helmetRenderer;
    private Material plainSpriteMaterial;
    private int equippedAccessoryCount;
    private TrailRenderer jumpTrail;
    private JumpTrailVisuals drawnTrail;
    private Player trailOwner;
    private Vector3 previousPosition;
    private Vector3 spriteCenter;
    private Vector3 spriteSize;
    private bool ownsResources;
    private string lastCat, lastClothing, lastAccessory, lastTrail;

    public static CatAppearance Attach(Transform visual, SpriteRenderer renderer)
    {
        if (visual == null || renderer == null || renderer.sprite == null) return null;
        CatAppearance appearance = visual.GetComponent<CatAppearance>();
        if (appearance == null) appearance = visual.gameObject.AddComponent<CatAppearance>();
        appearance.Initialize(renderer);
        return appearance;
    }

    public static Color GetCatTint(string id) => id == "cat_cocoa" || id == "cat_rose"
        ? ItemColor(id, Color.white) : Color.white;

    public static void ConfigurePreview(UnityEngine.UI.Image image, string catId, string clothingId)
    {
        if (image == null) return;
        CatWardrobePreview layers = image.GetComponent<CatWardrobePreview>();
        if (layers == null) layers = image.gameObject.AddComponent<CatWardrobePreview>();
        layers.Configure(image, catId, clothingId);
    }

    private void Initialize(SpriteRenderer renderer)
    {
        catRenderer = renderer;
        spriteCenter = renderer.sprite.bounds.center;
        spriteSize = renderer.sprite.bounds.size;
        previousPosition = transform.position;
        trailOwner = GetComponentInParent<Player>();
        if (!ownsResources)
        {
            plainSpriteMaterial = renderer.sharedMaterial;
            if (inkMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("HazardInk");
                if (shader == null)
                {
                    Debug.LogError("Doodlik: HazardInk shader is missing; accessories cannot be drawn.");
                    return;
                }
                inkMaterial = new Material(shader) { name = "Doodlik accessories (shared)" };
            }
            users++;
            ownsResources = true;

            skinRenderer = CreateVisualRenderer("Cat skin artwork", 0);
            clothingRenderer = CreateVisualRenderer("Cat clothing artwork", 10);
            helmetRenderer = CreateVisualRenderer("Cat helmet artwork", 15);

            for (int i = 0; i < accessoryRenderers.Length; i++)
            {
                GameObject accessory = new GameObject("Cat accessory " + i, typeof(SpriteRenderer));
                accessory.transform.SetParent(transform, false);
                accessoryRenderers[i] = accessory.GetComponent<SpriteRenderer>();
                accessoryRenderers[i].sharedMaterial = renderer.sharedMaterial;
                accessoryRenderers[i].color = Color.white;
            }

            GameObject trail = new GameObject("Jump ribbon", typeof(TrailRenderer));
            trail.transform.SetParent(transform, false);
            jumpTrail = trail.GetComponent<TrailRenderer>();
            jumpTrail.sharedMaterial = inkMaterial;
            jumpTrail.time = 0.23f;
            jumpTrail.numCapVertices = 2;
            jumpTrail.numCornerVertices = 1;
            jumpTrail.autodestruct = false;
            jumpTrail.emitting = false;
            jumpTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            jumpTrail.receiveShadows = false;
            jumpTrail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            drawnTrail = gameObject.AddComponent<JumpTrailVisuals>();
            drawnTrail.Initialize(renderer, inkMaterial);
        }
        foreach (SpriteRenderer accessory in accessoryRenderers)
        {
            accessory.sortingLayerID = renderer.sortingLayerID;
            accessory.sortingOrder = renderer.sortingOrder + 1;
        }
        jumpTrail.sortingLayerID = renderer.sortingLayerID;
        jumpTrail.sortingOrder = renderer.sortingOrder - 1;
        ApplyEquipped();
    }

    private SpriteRenderer CreateVisualRenderer(string objectName, int order)
    {
        GameObject child = new GameObject(objectName, typeof(SpriteRenderer));
        child.transform.SetParent(transform, false);
        SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
        renderer.sharedMaterial = plainSpriteMaterial;
        renderer.sortingLayerID = catRenderer.sortingLayerID;
        renderer.sortingOrder = catRenderer.sortingOrder + order;
        renderer.enabled = false;
        return renderer;
    }

    private void OnEnable()
    {
        PlayerProgress.Changed += ApplyEquipped;
        if (!ownsResources) return;
        previousPosition = transform.position;
        ApplyEquipped();
    }

    private void OnDisable()
    {
        PlayerProgress.Changed -= ApplyEquipped;
        if (jumpTrail != null)
        {
            jumpTrail.emitting = false;
            jumpTrail.Clear();
        }
        if (drawnTrail != null) drawnTrail.Clear();
    }

    public void ApplyEquipped()
    {
        if (catRenderer == null) return;
        string cat = PlayerProgress.EquippedId(PlayerProgress.Category.Cat);
        string clothing = PlayerProgress.EquippedId(PlayerProgress.Category.Clothing);
        string trail = PlayerProgress.EquippedId(PlayerProgress.Category.Trail);
        if (cat != lastCat || clothing != lastClothing)
        {
            catRenderer.sharedMaterial = CatClothingMaterials.For(catRenderer.sprite, cat, clothing, false);
            catRenderer.color = Color.white;
            Sprite skin = WardrobeArt.GetCatSprite(cat, catRenderer.sprite);
            skinRenderer.sprite = skin;
            skinRenderer.enabled = skin != null && skin != catRenderer.sprite;
            skinRenderer.sharedMaterial = CatClothingMaterials.For(skin, cat, clothing, false);
            // Keep source sprite and bounds intact: Player uses them for hazards.
            catRenderer.forceRenderingOff = skinRenderer.enabled;
            clothingRenderer.sprite = WardrobeArt.GetClothingSprite(clothing);
            clothingRenderer.enabled = clothingRenderer.sprite != null;
            helmetRenderer.sprite = WardrobeArt.GetSkinOverlay(cat);
            helmetRenderer.enabled = helmetRenderer.sprite != null;
            lastCat = cat;
            lastClothing = clothing;
        }
        System.Collections.Generic.IReadOnlyList<string> equipped = PlayerProgress.EquippedAccessories;
        equippedAccessoryCount = equipped.Count;
        lastAccessory = equipped.Count > 0 ? equipped[0] : "accessory_none";
        for (int i = 0; i < accessoryRenderers.Length; i++)
        {
            accessoryRenderers[i].enabled = false;
            accessoryIds[i] = null;
        }
        int visualIndex = 0;
        for (int item = 0; item < equipped.Count; item++)
        {
            string id = equipped[item];
            for (int instance = 0; instance < WardrobeArt.InstanceCount(id) && visualIndex < accessoryRenderers.Length; instance++)
            {
                Sprite artwork = WardrobeArt.GetAccessorySprite(id, instance);
                // Unknown owned IDs survive save migration, but must not take
                // one of the four drawable head/face/ear sprite slots.
                if (artwork == null) continue;
                SpriteRenderer visual = accessoryRenderers[visualIndex];
                visual.sprite = artwork;
                visual.enabled = true;
                visual.sortingOrder = catRenderer.sortingOrder + WardrobeArt.SortLayer(id, instance);
                accessoryIds[visualIndex] = id;
                accessoryInstances[visualIndex] = instance;
                visualIndex++;
            }
        }
        if (jumpTrail != null && trail != lastTrail)
        {
            Color color = ItemColor(trail, Color.clear);
            jumpTrail.startColor = color;
            jumpTrail.endColor = new Color(color.r, color.g, color.b, 0f);
            jumpTrail.enabled = trail == "trail_honey" || trail == "trail_petals";
            jumpTrail.Clear();
            if (drawnTrail != null) drawnTrail.Configure(trail);
            lastTrail = trail;
        }
        UpdateAccessoryTransform();
    }

    private void LateUpdate()
    {
        if (catRenderer == null || jumpTrail == null) return;
        UpdateAccessoryTransform();
        Vector3 current = transform.position;
        Vector3 movement = current - previousPosition;
        // Wrapping or a rescue teleport must never draw a line across the screen.
        float bodyWidth = Mathf.Max(0.1f, catRenderer.bounds.size.x);
        bool teleported = movement.sqrMagnitude > bodyWidth * bodyWidth * 2.25f;
        bool showTrail = RunFlow.IsPlaying && trailOwner != null && !trailOwner.IsDead;
        if (!showTrail || teleported || jumpTrail.positionCount > 48) jumpTrail.Clear();
        jumpTrail.emitting = jumpTrail.enabled && Time.timeScale > 0f && showTrail &&
            !teleported && movement.y > 0.001f;
        jumpTrail.minVertexDistance = bodyWidth * 0.15f;
        jumpTrail.widthMultiplier = bodyWidth * 0.15f;
        previousPosition = current;
    }

    private void UpdateAccessoryTransform()
    {
        float facing = catRenderer.flipX ? -1f : 1f;
        float upright = catRenderer.flipY ? -1f : 1f;
        FitVisual(skinRenderer, new Rect(-(Vector2)spriteSize * 0.5f, spriteSize), facing, upright);
        FitVisual(clothingRenderer, WardrobeArt.ClothingRect(lastClothing, spriteSize), facing, upright);
        FitVisual(helmetRenderer, WardrobeArt.SkinOverlayRect(lastCat, spriteSize), facing, upright);
        for (int i = 0; i < accessoryRenderers.Length; i++)
        {
            SpriteRenderer accessory = accessoryRenderers[i];
            if (accessory == null || !accessory.enabled || accessory.sprite == null) continue;
            string id = accessoryIds[i];
            int instance = accessoryInstances[i];
            // Preserve the original single-item fit contract used by editor probes.
            if (string.IsNullOrEmpty(id) || accessory.sprite != WardrobeArt.GetAccessorySprite(id, instance) ||
                equippedAccessoryCount <= 1 && id != lastAccessory)
            { id = lastAccessory; instance = i; }
            Rect fit = WardrobeArt.AccessoryRect(id, spriteSize, instance);
            Vector2 artSize = accessory.sprite.bounds.size;
            accessory.flipX = WardrobeArt.AccessoryFlipX(id, instance);
            accessory.transform.localPosition = new Vector3((spriteCenter.x + fit.center.x) * facing,
                (spriteCenter.y + fit.center.y) * upright, -0.01f);
            accessory.transform.localScale = new Vector3(fit.width / artSize.x * facing,
                fit.height / artSize.y * upright, 1f);
            accessory.transform.localRotation = Quaternion.Euler(0f, 0f,
                WardrobeArt.AccessoryAngle(id, instance) * facing * upright);
        }
        if (jumpTrail != null)
            jumpTrail.transform.localPosition = spriteCenter + Vector3.down * spriteSize.y * 0.36f;
    }

    private void FitVisual(SpriteRenderer visual, Rect fit, float facing, float upright)
    {
        if (visual == null || !visual.enabled || visual.sprite == null) return;
        Vector2 size = visual.sprite.bounds.size;
        visual.transform.localPosition = new Vector3((spriteCenter.x + fit.center.x) * facing,
            (spriteCenter.y + fit.center.y) * upright, -0.005f);
        visual.transform.localScale = new Vector3(fit.width / size.x * facing, fit.height / size.y * upright, 1f);
    }

    private void OnDestroy()
    {
        if (catRenderer != null) catRenderer.forceRenderingOff = false;
        if (!ownsResources || --users > 0) return;
        Release(inkMaterial);
        inkMaterial = null;
        users = 0;
    }

    private static void Release(Object resource)
    {
        if (resource == null) return;
        if (Application.isPlaying) Destroy(resource);
        else DestroyImmediate(resource);
    }

    private static Color ItemColor(string id, Color fallback)
    {
        foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
            if (item.Id == id) return item.Color;
        return fallback;
    }

}

// Cosmetic layers belong to an existing preview Image, so every UI consumer
// uses the same drawing rectangles without changing its prefab or presenter.
public sealed class CatWardrobePreview : MonoBehaviour
{
    private Image source, clothing, helmet;
    private Sprite originalSprite;
    private string clothingId, catId;
    private Rect previousRect;
    private bool previouslyVisible;

    public void Configure(Image image, string selectedCat, string selectedClothing)
    {
        source = image;
        if (originalSprite == null) originalSprite = image.sprite;
        catId = selectedCat;
        clothingId = selectedClothing;
        image.sprite = WardrobeArt.GetCatSprite(catId, originalSprite);
        image.material = CatClothingMaterials.For(image.sprite, catId, clothingId, true);
        image.color = Color.white;
        if (clothing == null) clothing = CreateLayer("Cat clothing preview");
        if (helmet == null) helmet = CreateLayer("Cat helmet preview");
        clothing.sprite = WardrobeArt.GetClothingSprite(clothingId);
        helmet.sprite = WardrobeArt.GetSkinOverlay(catId);
        UpdateLayout();
    }

    private Image CreateLayer(string layerName)
    {
        GameObject child = new GameObject(layerName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        child.transform.SetParent(transform, false);
        Image image = child.GetComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private void LateUpdate()
    {
        if (source == null) return;
        if (source.rectTransform.rect != previousRect || source.isActiveAndEnabled != previouslyVisible) UpdateLayout();
    }

    private void OnRectTransformDimensionsChange()
    {
        if (source != null && clothing != null && helmet != null) UpdateLayout();
    }

    private void UpdateLayout()
    {
        previousRect = source.rectTransform.rect;
        previouslyVisible = source.isActiveAndEnabled;
        bool visible = previouslyVisible && source.sprite != null;
        clothing.enabled = visible && clothing.sprite != null;
        helmet.enabled = visible && helmet.sprite != null;
        if (!visible) return;
        Rect draw = source.rectTransform.rect;
        if (source.preserveAspect && source.sprite != null)
        {
            Vector2 spriteSize = source.sprite.rect.size;
            float scale = Mathf.Min(draw.width / spriteSize.x, draw.height / spriteSize.y);
            Vector2 size = spriteSize * scale;
            draw = new Rect(draw.center - size * 0.5f, size);
        }
        Fit(clothing, WardrobeArt.ClothingRect(clothingId, draw.size), draw.center);
        Fit(helmet, WardrobeArt.SkinOverlayRect(catId, draw.size), draw.center);
    }

    private void Fit(Image layer, Rect fit, Vector2 drawingCenter)
    {
        RectTransform rect = layer.rectTransform;
        rect.anchorMin = rect.anchorMax = source.rectTransform.pivot;
        rect.pivot = Vector2.one * 0.5f;
        rect.anchoredPosition = drawingCenter + fit.center;
        rect.sizeDelta = fit.size;
    }
}
