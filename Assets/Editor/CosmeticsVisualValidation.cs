#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Batch-only visual sweep. Run only in an isolated .utmp/MenuValidation-* copy:
// -executeMethod CosmeticsVisualValidation.Run (or RunPortrait on Android), without -quit.
[InitializeOnLoad]
public static class CosmeticsVisualValidation
{
    private const string SessionKey = "Doodlik.CosmeticsVisualValidation";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int LandscapeWidth = 1280, LandscapeHeight = 720;
    private const int PortraitWidth = 720, PortraitHeight = 1280;

    private struct Look
    {
        public string Name, Cat, Clothing, Trail;
        public string[] Accessories;
        public string ItemId;
    }

    private static readonly List<Look> looks = new List<Look>();
    private static double since;
    private static int index, checks;
    private static bool portrait, pendingCapture;
    private static string failure, outputDirectory;
    private static FrontEndUI frontEnd;
    private static RectTransform previewRoot;
    private static Image previewCat;
    private static TextMeshProUGUI title, details;
    private static CatAppearance appearance;
    private static ulong baseSignature;

    static CosmeticsVisualValidation()
    {
        if (!SessionState.GetBool(SessionKey, false)) return;
        portrait = SessionState.GetBool(SessionKey + ".portrait", false);
        outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/CosmeticsVisual"));
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
        since = EditorApplication.timeSinceStartup;
    }

    public static void Run() => Start(false);
    public static void RunPortrait() => Start(true);

    private static void Start(bool wantPortrait)
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-"))
            throw new InvalidOperationException("Cosmetic screenshots require an isolated .utmp/MenuValidation-* batch project.");
        if (wantPortrait && EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new InvalidOperationException("Portrait cosmetic screenshots require the Android build target.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "CosmeticsVisualSandbox";
        CosmeticTextureSetup.Ensure();
        JourneySettingsSetup.EnsureAsset();
        AssetDatabase.SaveAssets();
        PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, wantPortrait ? 1 : 0);
        PlayerPrefs.Save();
        portrait = wantPortrait;
        SessionState.SetBool(SessionKey + ".portrait", portrait);
        SessionState.SetBool(SessionKey, true);
        index = checks = 0;
        pendingCapture = false;
        failure = null;
        since = EditorApplication.timeSinceStartup;
        outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/CosmeticsVisual"));
        Directory.CreateDirectory(outputDirectory);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup"))
        {
            Debug.LogWarning("VALIDATION_ENVIRONMENT: Unity Search startup indexer exception: " + message);
            return;
        }
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            failure = message;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (failure != null) throw new InvalidOperationException("Runtime error: " + failure);
            if (EditorApplication.timeSinceStartup - since > 25)
                throw new TimeoutException("Cosmetics screenshot stalled at item " + index);
            if (Time.frameCount < 20) return;
            if (frontEnd == null)
            {
                InitializeRuntime();
                since = EditorApplication.timeSinceStartup;
                return;
            }
            if (index >= looks.Count)
            {
                Debug.Log("COSMETICS_VISUAL_PASS checks=" + checks + " previews=" + looks.Count +
                    " worldCloseups=3" +
                    " orientation=" + (portrait ? "portrait" : "landscape") + " path=" + outputDirectory);
                Finish(0);
                return;
            }
            if (EditorApplication.timeSinceStartup - since < 0.08) return;
            Look look = looks[index];
            if (!pendingCapture)
            {
                Apply(look);
                pendingCapture = true;
                since = EditorApplication.timeSinceStartup;
                return;
            }
            Validate(look);
            ulong signature = Capture(look, index);
            if (index == 0) baseSignature = signature;
            else if (look.ItemId != "cat_ginger" && look.ItemId != "clothing_original" &&
                     look.ItemId != "accessory_none" && look.ItemId != "trail_none" && signature == baseSignature)
                throw new InvalidOperationException("Preview looks identical to the original cat: " + look.Name);
            index++;
            pendingCapture = false;
            since = EditorApplication.timeSinceStartup;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void InitializeRuntime()
    {
        GameMenuUI menu = Object.FindAnyObjectByType<GameMenuUI>();
        Player player = Object.FindAnyObjectByType<Player>();
        Require(menu != null && player != null, "menu and player exist");
        frontEnd = menu.GetComponent<FrontEndUI>();
        Require(frontEnd != null && frontEnd.IsVisible && !RunFlow.RunActive && Time.timeScale == 0f,
            "home preview is active before a run");
        Require(GameplayPreferences.IsPortrait == portrait, "requested orientation is active");
        Screen.SetResolution(portrait ? PortraitWidth : LandscapeWidth,
            portrait ? PortraitHeight : LandscapeHeight, false);
        Debug.Log("COSMETICS_VISUAL_DISPLAY requested=" + (portrait ? "720x1280" : "1280x720") +
            " initial=" + Screen.width + "x" + Screen.height);
        appearance = player.GetComponentInChildren<CatAppearance>(true);
        Require(appearance != null, "gameplay cat has appearance component");
        PlayerProgress.BeginAdminSandbox();
        Require(PlayerProgress.AdminSandboxActive, "all cosmetics available in disposable admin sandbox");
        PlayerProgress.Equip("trail_none");
        BuildLooks();
        ValidateDistinctArt();
        PrepareBoard();
    }

    private static void BuildLooks()
    {
        looks.Clear();
        foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
        {
            if (item.Category == PlayerProgress.Category.Cat)
                looks.Add(new Look { Name = "Котик: " + item.Name, Cat = item.Id,
                    Clothing = "clothing_original", Accessories = Array.Empty<string>(), ItemId = item.Id });
            else if (item.Category == PlayerProgress.Category.Clothing)
                looks.Add(new Look { Name = "Одежда: " + item.Name, Cat = "cat_ginger",
                    Clothing = item.Id, Accessories = Array.Empty<string>(), ItemId = item.Id });
            else if (item.Category == PlayerProgress.Category.Accessory)
                looks.Add(new Look { Name = "Аксессуар: " + item.Name, Cat = "cat_ginger",
                    Clothing = "clothing_original", Accessories = item.Id == "accessory_none"
                        ? Array.Empty<string>() : new[] { item.Id }, ItemId = item.Id });
            else if (item.Category == PlayerProgress.Category.Trail)
                looks.Add(new Look { Name = "След прыжка: " + item.Name, Cat = "cat_ginger",
                    Clothing = "clothing_original", Accessories = Array.Empty<string>(), Trail = item.Id,
                    ItemId = item.Id });
        }
        looks.Add(new Look { Name = "Награда: полный скафандр", Cat = "cat_ginger",
            Clothing = "clothing_astronaut", Accessories = new[] { "accessory_astronaut_helmet" }, ItemId = "astronaut_set" });
        looks.Add(new Look { Name = "Скафандр на другом котике", Cat = "cat_siamese",
            Clothing = "clothing_astronaut", Accessories = new[] { "accessory_astronaut_helmet" }, ItemId = "astronaut_siamese" });
        looks.Add(new Look { Name = "Промокод: котик-гуль", Cat = "cat_ghoul",
            Clothing = "clothing_ghoul", Accessories = new[] { "accessory_ghoul_hair", "accessory_ghoul_mask" },
            ItemId = "ghoul_set" });
        looks.Add(new Look { Name = "Три аксессуара вместе", Cat = "cat_calico",
            Clothing = "clothing_overalls", Accessories = new[] { "accessory_bow", "accessory_aviator", "accessory_headphones" },
            ItemId = "three_slots" });
    }

    private static void ValidateDistinctArt()
    {
        string[] illustrated =
        {
            "cat_calico", "cat_siamese", "cat_snow", "cat_tuxedo", "cat_ghoul",
            "clothing_overalls", "clothing_starry", "clothing_raincoat", "clothing_hoodie",
            "clothing_astronaut", "clothing_ghoul", "accessory_headphones", "accessory_bow",
            "accessory_aviator", "accessory_coin_crown", "accessory_ghoul_mask", "accessory_ghoul_hair",
            "accessory_astronaut_helmet"
        };
        HashSet<Texture> unique = new HashSet<Texture>();
        foreach (string id in illustrated)
        {
            Sprite sprite = WardrobeArt.GetSprite(id);
            Require(sprite != null && sprite.texture != null && unique.Add(sprite.texture),
                "every new illustrated item has its own imported artwork: " + id);
        }
    }

    private static void PrepareBoard()
    {
        GameObject home = (GameObject)typeof(FrontEndUI).GetField("home", Fields).GetValue(frontEnd);
        object preview = typeof(FrontEndUI).GetField("homePreview", Fields).GetValue(frontEnd);
        previewCat = (Image)preview.GetType().GetField("Cat", Fields).GetValue(preview);
        Require(home != null && previewCat != null, "home cat preview exists");
        previewRoot = previewCat.transform.parent.GetComponent<RectTransform>();
        Require(previewRoot != null, "home preview has a rect transform");
        foreach (Transform child in home.transform)
            if (child != previewRoot.transform) child.gameObject.SetActive(false);
        previewRoot.sizeDelta = new Vector2(550f, 550f);
        previewRoot.anchoredPosition = new Vector2(0f, 0f);
        title = MakeLabel(home.transform, "Look title", new Vector2(700f, 84f), new Vector2(0f, 356f), 40f);
        details = MakeLabel(home.transform, "Look IDs", new Vector2(700f, 62f), new Vector2(0f, -358f), 21f);
        frontEnd.Refresh();
        Canvas.ForceUpdateCanvases();
    }

    private static TextMeshProUGUI MakeLabel(Transform parent, string name, Vector2 size, Vector2 position, float fontSize)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        TextMeshProUGUI label = gameObject.AddComponent<TextMeshProUGUI>();
        label.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF - Fallback");
        label.fontSize = fontSize;
        label.color = new Color32(94, 49, 24, 255);
        label.alignment = TextAlignmentOptions.Center;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    private static void Apply(Look look)
    {
        Require(PlayerProgress.Equip("accessory_none"), "clear prior accessories");
        Require(PlayerProgress.Equip(look.Cat), "equip cat " + look.Cat);
        Require(PlayerProgress.Equip(look.Clothing), "equip clothing " + look.Clothing);
        Require(PlayerProgress.Equip(look.Trail ?? "trail_none"), "equip trail " + (look.Trail ?? "trail_none"));
        foreach (string id in look.Accessories)
            Require(PlayerProgress.Equip(id), "equip accessory " + id);
        title.text = look.Name;
        details.text = look.ItemId + "    •    " + look.Cat + " / " + look.Clothing +
            (look.Accessories.Length > 0 ? " / " + string.Join(" + ", look.Accessories) : "");
        frontEnd.Refresh();
        Canvas.ForceUpdateCanvases();
    }

    private static void Validate(Look look)
    {
        Require(previewCat.enabled && previewCat.sprite != null && previewCat.sprite.texture != null &&
            previewCat.material != null, "cat preview has drawable sprite and material: " + look.ItemId);
        Require(appearance != null && appearance.gameObject.activeInHierarchy, "gameplay appearance stays active");
        SpriteRenderer body = appearance.GetComponent<SpriteRenderer>();
        Require(body != null && body.enabled && body.sprite != null && body.sprite.texture != null &&
            body.sharedMaterial != null, "gameplay cat has drawable sprite and material: " + look.ItemId);
        Sprite selectedSkin = WardrobeArt.GetCatSprite(look.Cat, body.sprite);
        Sprite selectedClothing = WardrobeArt.GetClothingSprite(look.Clothing);
        Sprite selectedHelmet = WardrobeArt.GetSkinOverlay(look.Cat);
        Require(previewCat.sprite == selectedSkin, "home preview uses the selected illustrated cat: " + look.ItemId);
        SpriteRenderer worldSkin = WorldLayer("Cat skin artwork");
        SpriteRenderer worldClothing = WorldLayer("Cat clothing artwork");
        SpriteRenderer worldHelmet = WorldLayer("Cat helmet artwork");
        bool paintedSkin = selectedSkin != body.sprite;
        Require(worldSkin.enabled == paintedSkin && body.forceRenderingOff == paintedSkin &&
            (!paintedSkin || worldSkin.sprite == selectedSkin && FitsBody(worldSkin, body)),
            "illustrated world cat is visible and fits its original collision sprite: " + look.ItemId);
        Require(worldClothing.enabled == (selectedClothing != null) &&
            (selectedClothing == null || worldClothing.sprite == selectedClothing && FitsBody(worldClothing, body)),
            "world outfit is visible and placed on the body: " + look.ItemId);
        Require(worldHelmet.enabled == (selectedHelmet != null) &&
            (selectedHelmet == null || worldHelmet.sprite == selectedHelmet && FitsBody(worldHelmet, body)),
            "world helmet is visible and placed on the head: " + look.ItemId);
        Image uiClothing = UiLayer("Cat clothing preview");
        Image uiHelmet = UiLayer("Cat helmet preview");
        Require(uiClothing.enabled == (selectedClothing != null) && uiClothing.sprite == selectedClothing &&
            uiHelmet.enabled == (selectedHelmet != null) && uiHelmet.sprite == selectedHelmet,
            "preview outfit and helmet match equipped slots: " + look.ItemId);
        Require(PlayerProgress.EquippedId(PlayerProgress.Category.Cat) == look.Cat &&
            PlayerProgress.EquippedId(PlayerProgress.Category.Clothing) == look.Clothing,
            "preview and gameplay cat use intended equipped IDs: " + look.ItemId);
        Require(PlayerProgress.EquippedId(PlayerProgress.Category.Trail) == (look.Trail ?? "trail_none"),
            "trail selection matches the catalog look: " + look.ItemId);
        if (!string.IsNullOrEmpty(look.Trail) && look.Trail != "trail_none")
        {
            bool foundTrail = false;
            foreach (DoodlikCosmeticPreview graphic in previewRoot.GetComponentsInChildren<DoodlikCosmeticPreview>(true))
                if (graphic.ItemId == look.Trail && graphic.enabled) foundTrail = true;
            Require(foundTrail, "selected trail is present on the home preview: " + look.Trail);
            Require(appearance.GetComponent<JumpTrailVisuals>() != null,
                "gameplay cat has its bounded drawing trail presenter");
        }
        IReadOnlyList<string> chosen = PlayerProgress.EquippedAccessories;
        Require(chosen.Count == look.Accessories.Length, "accessory count matches selection: " + look.ItemId);
        DoodlikCosmeticPreview[] graphics = previewRoot.GetComponentsInChildren<DoodlikCosmeticPreview>(true);
        SpriteRenderer[] renderers = appearance.GetComponentsInChildren<SpriteRenderer>(true);
        foreach (string id in look.Accessories)
        {
            Require(PlayerProgress.IsEquipped(id), "accessory remained equipped: " + id);
            Sprite art = WardrobeArt.GetSprite(id);
            Require(art != null && art.texture != null && WardrobeArt.InstanceCount(id) > 0,
                "accessory illustration is imported: " + id);
            int uiCount = 0, worldCount = 0;
            foreach (DoodlikCosmeticPreview graphic in graphics)
                if (graphic.ItemId == id && graphic.enabled && graphic.mainTexture == art.texture) uiCount++;
            for (int instance = 0; instance < WardrobeArt.InstanceCount(id); instance++)
            {
                Sprite part = WardrobeArt.GetAccessorySprite(id, instance);
                foreach (SpriteRenderer renderer in renderers)
                    if (renderer != body && renderer.enabled && renderer.sprite == part && FitsBody(renderer, body))
                        worldCount++;
            }
            Require(uiCount >= 1,
                "accessory graphic is on the large home preview: " + id);
            Require(worldCount >= WardrobeArt.InstanceCount(id), "accessory sprites are on gameplay cat: " + id);
        }
    }

    private static SpriteRenderer WorldLayer(string name)
    {
        Transform child = appearance.transform.Find(name);
        Require(child != null && child.gameObject.activeInHierarchy, "world cosmetic layer exists: " + name);
        SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
        Require(renderer != null && renderer.sharedMaterial != null, "world cosmetic renderer is valid: " + name);
        return renderer;
    }

    private static Image UiLayer(string name)
    {
        Transform child = previewCat.transform.Find(name);
        Require(child != null && child.gameObject.activeInHierarchy, "UI cosmetic layer exists: " + name);
        Image image = child.GetComponent<Image>();
        Require(image != null, "UI cosmetic image exists: " + name);
        return image;
    }

    private static bool FitsBody(SpriteRenderer layer, SpriteRenderer body)
    {
        if (!layer.gameObject.activeInHierarchy || layer.sprite == null || layer.bounds.size.sqrMagnitude <= 0f)
            return false;
        Bounds envelope = body.bounds;
        envelope.Expand(Mathf.Max(envelope.size.x, envelope.size.y) * 0.4f);
        return envelope.Intersects(layer.bounds) &&
            layer.bounds.size.x <= body.bounds.size.x * 1.35f &&
            layer.bounds.size.y <= body.bounds.size.y * 1.35f;
    }

    private static ulong Capture(Look look, int number)
    {
        Camera camera = Camera.main;
        Require(camera != null, "main camera exists");
        if (number == 0)
            Debug.Log("COSMETICS_VISUAL_DISPLAY capture=" + Screen.width + "x" + Screen.height +
                " safe=" + Screen.safeArea);
        int width = portrait ? PortraitWidth : LandscapeWidth;
        int height = portrait ? PortraitHeight : LandscapeHeight;
        float oldAspect = camera.aspect;
        RenderTexture oldTarget = camera.targetTexture;
        RenderTexture oldActive = RenderTexture.active;
        List<Canvas> changed = new List<Canvas>();
        List<Camera> oldCameras = new List<Camera>();
        RenderTexture target = null;
        Texture2D image = null;
        try
        {
            camera.aspect = (float)width / height;
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>())
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                changed.Add(canvas);
                oldCameras.Add(canvas.worldCamera);
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
            }
            target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply();
            string name = number.ToString("D2") + "-" + look.ItemId + "-" + (portrait ? "Portrait" : "Landscape") + ".png";
            File.WriteAllBytes(Path.Combine(outputDirectory, name), image.EncodeToPNG());
            ulong signature = PreviewSignature(image, width, height);
            if (look.ItemId == "ghoul_set" || look.ItemId == "astronaut_set" || look.ItemId == "three_slots")
                CaptureWorldCloseup(camera, target, image, look, number, width, height);
            return signature;
        }
        finally
        {
            camera.targetTexture = oldTarget;
            RenderTexture.active = oldActive;
            camera.aspect = oldAspect;
            for (int i = 0; i < changed.Count; i++)
            {
                changed[i].renderMode = RenderMode.ScreenSpaceOverlay;
                changed[i].worldCamera = oldCameras[i];
            }
            if (image != null) Object.DestroyImmediate(image);
            if (target != null) Object.DestroyImmediate(target);
        }
    }

    private static void CaptureWorldCloseup(Camera camera, RenderTexture target, Texture2D image,
        Look look, int number, int width, int height)
    {
        GameObject home = (GameObject)typeof(FrontEndUI).GetField("home", Fields).GetValue(frontEnd);
        HomeScreenBackdrop backdrop = Object.FindAnyObjectByType<HomeScreenBackdrop>();
        Transform backdropRoot = backdrop != null ? backdrop.transform.Find("Home backdrop") : null;
        Vector3 oldCameraPosition = camera.transform.position;
        float oldOrthographicSize = camera.orthographicSize;
        bool homeActive = home.activeSelf;
        bool backdropActive = backdropRoot != null && backdropRoot.gameObject.activeSelf;
        try
        {
            home.SetActive(false);
            if (backdropRoot != null) backdropRoot.gameObject.SetActive(false);
            Vector3 cat = appearance.transform.position;
            camera.transform.position = new Vector3(cat.x, cat.y, oldCameraPosition.z);
            camera.orthographicSize = 1.7f;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply();
            string name = number.ToString("D2") + "-" + look.ItemId + "-World-" +
                (portrait ? "Portrait" : "Landscape") + ".png";
            File.WriteAllBytes(Path.Combine(outputDirectory, name), image.EncodeToPNG());
        }
        finally
        {
            camera.transform.position = oldCameraPosition;
            camera.orthographicSize = oldOrthographicSize;
            if (backdropRoot != null) backdropRoot.gameObject.SetActive(backdropActive);
            home.SetActive(homeActive);
        }
    }

    // Sample only the large cat area; labels and animated background are outside it.
    private static ulong PreviewSignature(Texture2D image, int width, int height)
    {
        ulong hash = 14695981039346656037UL;
        int left = Mathf.FloorToInt(width * 0.30f), right = Mathf.CeilToInt(width * 0.70f);
        int bottom = Mathf.FloorToInt(height * (portrait ? 0.30f : 0.25f));
        int top = Mathf.CeilToInt(height * (portrait ? 0.69f : 0.75f));
        for (int y = bottom; y < top; y += 6)
        for (int x = left; x < right; x += 6)
        {
            Color32 color = image.GetPixel(x, y);
            unchecked
            {
                hash = (hash ^ color.r) * 1099511628211UL;
                hash = (hash ^ color.g) * 1099511628211UL;
                hash = (hash ^ color.b) * 1099511628211UL;
            }
        }
        return hash;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Cosmetics visual validation: " + message);
        checks++;
    }

    private static void Finish(int exitCode)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        SessionState.SetBool(SessionKey, false);
        if (PlayerProgress.AdminSandboxActive) PlayerProgress.EndAdminSandbox();
        Time.timeScale = 1f;
        EditorApplication.Exit(exitCode);
    }
}
#endif
