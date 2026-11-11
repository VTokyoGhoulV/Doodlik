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

// Broad presentation matrix. The scene, gameplay actor and saved inventory are
// never edited. Captures use real production UI/world presenters in a sandbox.
[InitializeOnLoad]
public static class ClothingFitValidation
{
    private const string Key = "Doodlik.ClothingFitValidation";
    private const int Width = 480, Height = 260, CropWidth = 120, CropHeight = 160;
    private static readonly List<PlayerProgress.Item> Cats = new List<PlayerProgress.Item>();
    private static readonly List<PlayerProgress.Item> Clothes = new List<PlayerProgress.Item>();
    private static readonly List<string> Issues = new List<string>();
    private static readonly Dictionary<string, Color32[]> Baselines = new Dictionary<string, Color32[]>();
    private static readonly Dictionary<string, Color32[]> Captures = new Dictionary<string, Color32[]>();
    private static readonly Dictionary<string, Color32[]> SmallCaptures = new Dictionary<string, Color32[]>();
    private static double started;
    private static int index, checks;
    private static string output, runtimeFailure;
    private static Camera camera;
    private static Vector3 originalCameraPosition;
    private static float originalSize, originalAspect, originalTime;
    private static int originalCulling;
    private static CameraClearFlags originalClear;
    private static Color originalBackground;
    private static Canvas[] originalCanvases;
    private static bool[] originalCanvasEnabled;
    private static GameObject owner, canvasObject;
    private static SpriteRenderer body;
    private static CatAppearance appearance;
    private static Image uiCat;
    private static DoodlikCosmeticPreview uiHelmet;
    private static TMP_Text header;
    private static IDisposable storage;
    private static Vector2 pixelBody;

    static ClothingFitValidation()
    {
        if (!SessionState.GetBool(Key, false)) return;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
        started = EditorApplication.timeSinceStartup;
    }

    public static void Run()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-"))
            throw new InvalidOperationException("Clothing matrix requires a disposable MenuValidation batch copy.");
        if (PlayerSettings.companyName != "CodexValidation")
            throw new InvalidOperationException("QA copy must already have the isolated CodexValidation identity.");
        EditorUtility.audioMasterMute = true; AudioListener.volume = 0; AudioListener.pause = true;
        SessionState.SetBool(Key, true);
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
        PlayModeWindow.SetCustomRenderingResolution(720, 1280, "Doodlik clothing fit matrix");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        started = EditorApplication.timeSinceStartup;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (runtimeFailure != null) throw new InvalidOperationException(runtimeFailure);
            if (EditorApplication.timeSinceStartup - started > 300) throw new TimeoutException("Clothing matrix timed out at " + index);
            if (Time.frameCount < 25 || EditorApplication.timeSinceStartup - started < 0.6) return;
            EditorUtility.audioMasterMute = true; AudioListener.volume = 0; AudioListener.pause = true;
            if (owner == null) Initialize();
            int normalCount = Cats.Count * Clothes.Count * 2;
            int total = normalCount + Cats.Count * 2;
            if (index >= total)
            {
                WriteSheets();
                Debug.Log("CLOTHING_FIT_MATRIX_" + (Issues.Count == 0 ? "PASS" : "FAIL") + " states=" + total +
                    " cats=" + Cats.Count + " clothing=" + Clothes.Count + " checks=" + checks + " issues=" + Issues.Count + " output=" + output);
                Finish(Issues.Count == 0 ? 0 : 1); return;
            }
            bool helmet = index >= normalCount;
            int local = helmet ? index - normalCount : index;
            bool flipped = local % 2 == 1;
            int catIndex = helmet ? local / 2 : local / (Clothes.Count * 2);
            int clothingIndex = helmet ? -1 : local / 2 % Clothes.Count;
            Capture(Cats[catIndex], helmet ? "clothing_astronaut" : Clothes[clothingIndex].Id, flipped, helmet);
            index++;
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    private static void Initialize()
    {
        Require(EditorUtility.audioMasterMute && AudioListener.volume == 0, "The whole Editor is muted.");
        Require(Screen.width == 720 && Screen.height == 1280, "Actual GameView has the portrait surface.");
        foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
            if (item.Category == PlayerProgress.Category.Cat) Cats.Add(item);
            else if (item.Category == PlayerProgress.Category.Clothing) Clothes.Add(item);
        Require(Cats.Count > 0 && Clothes.Count > 0, "Every current cat and clothing catalog entry is included.");
        CatAppearance source = Object.FindAnyObjectByType<CatAppearance>();
        Require(source != null, "The natural scene created its production appearance.");
        SpriteRenderer original = source.GetComponent<SpriteRenderer>();
        camera = Camera.main;
        originalCameraPosition = camera.transform.position; originalSize = camera.orthographicSize; originalAspect = camera.aspect;
        originalCulling = camera.cullingMask; originalClear = camera.clearFlags; originalBackground = camera.backgroundColor; originalTime = Time.timeScale;
        originalCanvases = Object.FindObjectsByType<Canvas>(); originalCanvasEnabled = new bool[originalCanvases.Length];
        for (int i = 0; i < originalCanvases.Length; i++) { originalCanvasEnabled[i] = originalCanvases[i].enabled; originalCanvases[i].enabled = false; }
        storage = PlayerProgress.UseIsolatedStorageForValidation(out _, out _);
        PlayerProgress.BeginAdminSandbox();
        Time.timeScale = 0;
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/ClothingFitMatrix")); Directory.CreateDirectory(output);
        camera.transform.position = new Vector3(8000, 8000, originalCameraPosition.z);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(244, 230, 191, 255); camera.cullingMask = 1 << 31;
        owner = new GameObject("Clothing matrix owner"); owner.AddComponent<Player>().enabled = false;
        GameObject visual = new GameObject("Production cat", typeof(SpriteRenderer)); visual.transform.SetParent(owner.transform, false);
        visual.transform.localScale = Vector3.one * 0.1f;
        body = visual.GetComponent<SpriteRenderer>(); body.sprite = original.sprite; body.sharedMaterial = original.sharedMaterial;
        appearance = CatAppearance.Attach(visual.transform, body);
        canvasObject = new GameObject("Production preview canvas", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
        canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        GameObject imageObject = new GameObject("Fitted UI cat", typeof(RectTransform), typeof(Image)); imageObject.transform.SetParent(canvasObject.transform, false);
        uiCat = imageObject.GetComponent<Image>(); uiCat.sprite = original.sprite; uiCat.preserveAspect = true; uiCat.raycastTarget = false;
        uiCat.rectTransform.anchoredPosition = new Vector2(120, 0);
        GameObject helmetObject = new GameObject("Production UI helmet", typeof(RectTransform)); helmetObject.transform.SetParent(imageObject.transform, false);
        uiHelmet = helmetObject.AddComponent<DoodlikCosmeticPreview>(); uiHelmet.rectTransform.anchorMin = Vector2.zero; uiHelmet.rectTransform.anchorMax = Vector2.one;
        uiHelmet.rectTransform.offsetMin = uiHelmet.rectTransform.offsetMax = Vector2.zero; uiHelmet.raycastTarget = false;
        typeof(DoodlikCosmeticPreview).GetField("catImage", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(uiHelmet, uiCat);
        header = Label("matrix header", new Vector2(0, 116), new Vector2(470, 22), 13);
        Label("World · игровая модель", new Vector2(-120, 98), new Vector2(225, 16), 11);
        Label("UI · гардероб", new Vector2(120, 98), new Vector2(225, 16), 11);
        SetLayer(owner.transform); SetLayer(canvasObject.transform);
    }

    private static TMP_Text Label(string text, Vector2 position, Vector2 size, float fontSize)
    {
        GameObject root = new GameObject(text, typeof(RectTransform), typeof(TextMeshProUGUI)); root.transform.SetParent(canvasObject.transform, false);
        var label = root.GetComponent<TextMeshProUGUI>(); label.text = text; label.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF - Fallback");
        label.fontSize = fontSize; label.alignment = TextAlignmentOptions.Center; label.color = new Color32(94, 49, 24, 255); label.raycastTarget = false;
        label.rectTransform.anchoredPosition = position; label.rectTransform.sizeDelta = size; return label;
    }

    private static void Capture(PlayerProgress.Item cat, string clothing, bool flipped, bool helmet)
    {
        string key = cat.Id + "__" + clothing + (flipped ? "__L" : "__R") + (helmet ? "__helmet" : "");
        PlayerProgress.Equip("accessory_none"); PlayerProgress.Equip("trail_none");
        Require(PlayerProgress.Equip(cat.Id) && PlayerProgress.Equip(clothing), "All owned matrix selections can equip: " + key);
        if (helmet) Require(PlayerProgress.Equip("accessory_astronaut_helmet"), "Helmet can equip on " + cat.Id);
        body.transform.localScale = Vector3.one * 0.1f;
        camera.orthographicSize = 0.6f;
        owner.transform.position = camera.transform.position + new Vector3(-120f * 1.2f / Height, 0, -camera.transform.position.z);
        body.flipX = flipped; appearance.ApplyEquipped();
        pixelBody = (Vector2)body.sprite.bounds.size * 0.1f * Height / 1.2f;
        canvasObject.SetActive(true);
        uiCat.rectTransform.sizeDelta = pixelBody;
        uiCat.rectTransform.localScale = new Vector3(flipped ? -1 : 1, 1, 1);
        CatAppearance.ConfigurePreview(uiCat, cat.Id, clothing);
        uiHelmet.Configure(helmet ? "accessory_astronaut_helmet" : "accessory_none", true);
        header.text = cat.Name + " · " + clothing + (helmet ? " + шлем" : "") + (flipped ? " · ←" : " · →");
        SetLayer(owner.transform); SetLayer(canvasObject.transform);
        canvasObject.SetActive(true);
        Color32[] pixels = Render(Width, Height, new Rect(0, 0, Width, Height), Path.Combine(output, key + ".png"));
        Captures[key] = pixels;
        ValidateAttachments(key, clothing, flipped);
        string baselineKey = cat.Id + (flipped ? "L" : "R");
        if (clothing == "clothing_original" && !helmet) Baselines[baselineKey] = pixels;
        else if (!helmet && Baselines.TryGetValue(baselineKey, out Color32[] baseline)) CheckPixels(key, clothing, pixels, baseline);
        canvasObject.SetActive(false);
        // Native portrait-gameplay pixels: not an enlarged thumbnail. The
        // 0.64 model scale and 5-unit camera match the phone presentation.
        body.transform.localScale = Vector3.one * 0.064f; owner.transform.position = new Vector3(8000, 8000, 0); appearance.ApplyEquipped();
        camera.orthographicSize = 5f;
        SmallCaptures[key] = Render(720, 1280, new Rect(300, 560, CropWidth, CropHeight), Path.Combine(output, key + "__GameplayPortrait.png"));
        Debug.Log("CLOTHING_FIT_CASE " + key);
    }

    private static void ValidateAttachments(string key, string clothing, bool flipped)
    {
        Sprite expected = WardrobeArt.GetClothingSprite(clothing);
        SpriteRenderer world = body.transform.Find("Cat clothing artwork").GetComponent<SpriteRenderer>();
        Image ui = uiCat.transform.Find("Cat clothing preview").GetComponent<Image>();
        Require(world.enabled == (expected != null) && ui.enabled == (expected != null), "Correct optional clothing layers: " + key);
        if (expected == null) return;
        Require(world.sprite == expected && ui.sprite == expected, "Both presenters use the same clothing illustration: " + key);
        Vector3 local = body.transform.InverseTransformPoint(world.bounds.center) - body.sprite.bounds.center;
        Vector2 normalizedWorld = new Vector2(local.x / body.sprite.bounds.size.x, local.y / body.sprite.bounds.size.y);
        Vector2 normalizedUi = ui.rectTransform.anchoredPosition / pixelBody;
        if (flipped) normalizedWorld.x = -normalizedWorld.x;
        Require(Vector2.Distance(normalizedWorld, normalizedUi) < 0.003f, "UI/world garment attachment centres agree, including mirroring: " + key);
        Require(world.GetComponent<Collider2D>() == null, "A visual clothing layer adds no collider: " + key);
    }

    private static void CheckPixels(string key, string clothing, Color32[] pixels, Color32[] baseline)
    {
        for (int side = 0; side < 2; side++)
        {
            int center = side == 0 ? 120 : 360, changes = 0, face = 0, tail = 0, feet = 0;
            for (int y = 25; y < 219; y++) for (int x = side * 240 + 5; x < side * 240 + 235; x++)
            {
                int p = y * Width + x;
                int difference = Math.Abs(pixels[p].r - baseline[p].r) + Math.Abs(pixels[p].g - baseline[p].g) + Math.Abs(pixels[p].b - baseline[p].b);
                if (difference < 48) continue;
                float u = (x - center) / pixelBody.x + 0.5f, v = (y - Height * 0.5f) / pixelBody.y + 0.5f;
                if (u < 0 || u > 1 || v < 0 || v > 1) continue;
                changes++;
                if (v > 0.57f && u > 0.18f && u < 0.74f) face++;
                if (u > 0.85f && v > 0.12f && v < 0.60f) tail++;
                if (v < 0.08f && u > 0.12f && u < 0.80f) feet++;
            }
            string tag = key + (side == 0 ? " world" : " UI");
            if (changes < 12) Issue(tag + " selected clothing has no meaningful pixel change (" + changes + ").");
            if (face > 3) Issue(tag + " garment changes facial/ear pixels (" + face + ").");
            // Tail mirrors with the model: only test the unflipped canonical tail.
            if (!key.Contains("__L") && tail > 3) Issue(tag + " garment changes tail pixels (" + tail + ").");
            if (clothing != "clothing_astronaut" && feet > 3) Issue(tag + " non-boot garment changes paw pixels (" + feet + ").");
            checks += 4;
        }
    }

    private static Color32[] Render(int width, int height, Rect crop, string path)
    {
        RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
        float aspect = camera.aspect;
        RenderTexture target = new RenderTexture(width, height, 24);
        Texture2D image = new Texture2D((int)crop.width, (int)crop.height, TextureFormat.RGB24, false);
        try
        {
            camera.aspect = (float)width / height; camera.targetTexture = target; Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture.active = target; image.ReadPixels(crop, 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG()); return image.GetPixels32();
        }
        finally { camera.targetTexture = previousTarget; camera.aspect = aspect; RenderTexture.active = previousActive; Object.DestroyImmediate(image); Object.DestroyImmediate(target); }
    }

    private static void WriteSheets()
    {
        foreach (PlayerProgress.Item cat in Cats)
        {
            int rows = (Clothes.Count + 4) / 5 * 2;
            Texture2D sheet = NewSheet(Width * 5, Height * rows), small = NewSheet(CropWidth * 5, CropHeight * rows);
            for (int item = 0; item < Clothes.Count; item++) for (int facing = 0; facing < 2; facing++)
            {
                string key = cat.Id + "__" + Clothes[item].Id + (facing == 0 ? "__R" : "__L");
                int column = item % 5, row = item / 5 * 2 + facing;
                sheet.SetPixels32(column * Width, (rows - 1 - row) * Height, Width, Height, Captures[key]);
                small.SetPixels32(column * CropWidth, (rows - 1 - row) * CropHeight, CropWidth, CropHeight, SmallCaptures[key]);
            }
            SaveSheet(sheet, Path.Combine(output, "Contact-" + cat.Id + ".png"));
            SaveSheet(small, Path.Combine(output, "GameplayPortrait-" + cat.Id + ".png"));
        }
        int helmetRows = (Cats.Count + 3) / 4 * 2;
        Texture2D helmets = NewSheet(Width * 4, Height * helmetRows), smallHelmets = NewSheet(CropWidth * 4, CropHeight * helmetRows);
        for (int cat = 0; cat < Cats.Count; cat++) for (int facing = 0; facing < 2; facing++)
        {
            string key = Cats[cat].Id + "__clothing_astronaut" + (facing == 0 ? "__R" : "__L") + "__helmet";
            int column = cat % 4, row = cat / 4 * 2 + facing;
            helmets.SetPixels32(column * Width, (helmetRows - 1 - row) * Height, Width, Height, Captures[key]);
            smallHelmets.SetPixels32(column * CropWidth, (helmetRows - 1 - row) * CropHeight, CropWidth, CropHeight, SmallCaptures[key]);
        }
        SaveSheet(helmets, Path.Combine(output, "Contact-AstronautHelmets.png")); SaveSheet(smallHelmets, Path.Combine(output, "GameplayPortrait-AstronautHelmets.png"));
        File.WriteAllText(Path.Combine(output, "Findings.txt"), string.Join("\n", Issues));
    }

    private static Texture2D NewSheet(int width, int height)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        Color32[] fill = new Color32[width * height]; for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(244, 230, 191, 255);
        texture.SetPixels32(fill); return texture;
    }
    private static void SaveSheet(Texture2D sheet, string path) { sheet.Apply(); File.WriteAllBytes(path, sheet.EncodeToPNG()); Object.DestroyImmediate(sheet); }
    private static void SetLayer(Transform root) { foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Clothing fit: " + message); checks++; }
    private static void Issue(string message) { Issues.Add(message); Debug.LogWarning("CLOTHING_FIT_FINDING " + message); }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup")) return;
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) runtimeFailure = message;
    }
    private static void Finish(int code)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog; SessionState.SetBool(Key, false);
        if (owner != null) Object.DestroyImmediate(owner); if (canvasObject != null) Object.DestroyImmediate(canvasObject);
        if (PlayerProgress.AdminSandboxActive) PlayerProgress.EndAdminSandbox(); storage?.Dispose(); storage = null;
        if (camera != null) { camera.transform.position = originalCameraPosition; camera.orthographicSize = originalSize; camera.aspect = originalAspect;
            camera.cullingMask = originalCulling; camera.clearFlags = originalClear; camera.backgroundColor = originalBackground; }
        if (originalCanvases != null) for (int i = 0; i < originalCanvases.Length; i++) if (originalCanvases[i] != null) originalCanvases[i].enabled = originalCanvasEnabled[i];
        Time.timeScale = originalTime;
        EditorApplication.Exit(code);
    }
}
#endif
