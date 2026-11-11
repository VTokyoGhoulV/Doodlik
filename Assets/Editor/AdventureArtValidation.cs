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

// Dedicated hidden, muted art fixture. Physical flight/exit and bee attack
// certification are validated by their gameplay owners, not faked by a render.
[InitializeOnLoad]
public static class AdventureArtValidation
{
    private const string Key = "Doodlik.AdventureArtValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] Cats = { "cat_ginger", "cat_cocoa", "cat_rose", "cat_calico", "cat_siamese", "cat_snow", "cat_tuxedo", "cat_ghoul" };
    private static readonly string[] Clothes = { "clothing_explorer", "clothing_raincoat", "clothing_hoodie", "clothing_overalls", "clothing_starry", "clothing_astronaut", "clothing_original", "clothing_ghoul" };
    private static readonly Dictionary<string, Color32[]> Large = new Dictionary<string, Color32[]>();
    private static readonly Dictionary<string, Color32[]> Native = new Dictionary<string, Color32[]>();
    private static double since;
    private static int checks, index;
    private static string failure, output;
    private static Camera camera;
    private static Player player;
    private static RunPowerups powers;
    private static RocketVisuals rocket;
    private static CatAppearance appearance;
    private static SpriteRenderer cat;
    private static Vector3 normalVisualScale;
    private static GameObject labels, iconOwner, support;
    private static TMP_Text header;
    private static Mesh oldCoin;
    private static IDisposable storage;
    private static bool cleanupTriggered;
    private static int cleanupFrame;

    static AdventureArtValidation()
    {
        if (!SessionState.GetBool(Key, false)) return;
        Register();
    }
    public static void Run()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-"))
            throw new InvalidOperationException("Adventure art requires a disposable MenuValidation Editor.");
        if (PlayerSettings.companyName != "CodexValidation") throw new InvalidOperationException("QA identity is not isolated.");
        EditorUtility.audioMasterMute = true; AudioListener.volume = 0; AudioListener.pause = true;
        PlayModeWindow.SetCustomRenderingResolution(1280, 720, "Doodlik adventure art QA");
        SessionState.SetBool(Key, true); Register();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }
    private static void Register()
    {
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (failure != null) throw new InvalidOperationException(failure);
            if (EditorApplication.timeSinceStartup - since > 240) throw new TimeoutException("Adventure art state " + index);
            if (Time.frameCount < 25) return;
            Require(EditorUtility.audioMasterMute && AudioListener.volume == 0, "Art QA remains completely silent.");
            if (player == null) Initialize();
            if (index >= 32)
            {
                if (!cleanupTriggered)
                { CheckExitAndCleanup(); cleanupTriggered = true; cleanupFrame = Time.frameCount + 2; return; }
                if (Time.frameCount < cleanupFrame) return;
                Require(player.transform.Find("Paper rocket seat") == null, "End-of-frame destruction removes the owned seat.");
                WriteSheets();
                Debug.Log("ADVENTURE_ART_PASS checks=" + checks + " icons=2 bees=1 rocketLooks=32 formats=2 facing=2 pool=12 rngPreserved=True output=" + output);
                Finish(0); return;
            }
            int scale = index / 16, catIndex = index % 16 / 2; bool flip = index % 2 != 0;
            if (index % 16 == 0) PrepareScale(scale);
            CaptureCat(catIndex, scale, flip);
            index++;
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    public static void Validate()
    {
        UnityEngine.Random.State state = UnityEngine.Random.state;
        UnityEngine.Random.InitState(91827); float expected = UnityEngine.Random.value;
        UnityEngine.Random.InitState(91827);
        try
        {
            Mesh[] meshes = { AdventureArt.PickupIcon(4), AdventureArt.PickupIcon(5), AdventureArt.RocketBody, AdventureArt.BeeBody, AdventureArt.Star };
            foreach (Mesh mesh in meshes)
            {
                Require(mesh != null && mesh.vertexCount > 20 && mesh.colors32.Length == mesh.vertexCount, "Drawing has real colored geometry.");
                Vector3[] vertices = mesh.vertices; int[] triangles = mesh.triangles;
                foreach (Vector3 vertex in vertices) Require(!float.IsNaN(vertex.x) && !float.IsInfinity(vertex.y), "Finite mesh point.");
                foreach (int triangle in triangles) Require(triangle >= 0 && triangle < vertices.Length, "Valid triangle index.");
                Require(mesh.bounds.size.z == 0 && mesh.bounds.size.x > .3f, "Artwork is a visible 2D drawing.");
            }
            Require(AdventureArt.PickupIcon(4) == meshes[0] && AdventureArt.PickupIcon(5) == meshes[1] &&
                AdventureArt.RocketBody == meshes[2] && AdventureArt.BeeBody == meshes[3] && AdventureArt.Star == meshes[4], "Templates are shared and cached.");
            Require(AdventureArt.PickupIcon(0) == null && AdventureArt.PickupIcon(3) == null, "Old pickup art is not replaced.");
            for (int kind = 4; kind <= 5; kind++)
                foreach (Vector3 vertex in AdventureArt.PickupIcon(kind).vertices)
                    Require(((Vector2)vertex).magnitude <= .2301f, "New pickup never exceeds old radius / route clearance.");
            Bounds rocketBounds = AdventureArt.RocketBody.bounds;
            Require(Vector3.Distance(rocketBounds.center, Vector3.zero) < .0001f &&
                Mathf.Abs(rocketBounds.size.x - 1f) < .0001f && Mathf.Abs(rocketBounds.size.y - .45f) < .0001f, "Rocket has its compact centred 1 x .45 contract.");
            Rect damage = AdventureArt.BeeDamageRect; Bounds beeBounds = AdventureArt.BeeBody.bounds;
            Require(damage.xMin > beeBounds.min.x && damage.xMax < beeBounds.max.x &&
                damage.yMin > beeBounds.min.y && damage.yMax < beeBounds.max.y, "Bee damage metadata excludes decorative extremities.");
            Require(Mathf.Abs(UnityEngine.Random.value - expected) < .000001f, "Building/caching all artwork preserves generator RNG.");
        }
        finally { UnityEngine.Random.state = state; }
    }

    private static void Initialize()
    {
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/AdventureArt")); Directory.CreateDirectory(output);
        Validate();
        player = Object.FindAnyObjectByType<Player>(); Require(player != null, "The real scene created Player.");
        powers = player.GetComponent<RunPowerups>(); appearance = player.GetComponentInChildren<CatAppearance>();
        cat = appearance.GetComponent<SpriteRenderer>(); normalVisualScale = cat.transform.localScale;
        Require(powers != null && cat.sprite != null, "Production power and cosmetic presenters exist.");
        camera = Camera.main;
        Object.FindAnyObjectByType<CameraFollow>().enabled = false;
        Object.FindAnyObjectByType<Spawner>().enabled = false;
        player.enabled = false;
        // Keep the real feet bounds available to TryLaunch while preventing
        // gravity/movement in this paused presentation fixture.
        player.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>()) canvas.enabled = false;
        storage = PlayerProgress.UseIsolatedStorageForValidation(out _, out _); PlayerProgress.BeginAdminSandbox();
        support = new GameObject("Art-only safe launch support", typeof(BoxCollider2D)); support.tag = "Platform";
        support.GetComponent<BoxCollider2D>().size = new Vector2(2f, .2f);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(244, 230, 191, 255); camera.cullingMask = 1 << 31;
        foreach (UnityEngine.Rendering.Universal.Light2D light in Object.FindObjectsByType<UnityEngine.Rendering.Universal.Light2D>())
            if (light.lightType == UnityEngine.Rendering.Universal.Light2D.LightType.Global) light.gameObject.layer = 31;
        camera.transform.position = new Vector3(8000, 8000, camera.transform.position.z);
        player.transform.position = new Vector3(8000, 8000, 0); Physics2D.SyncTransforms();
        labels = new GameObject("Art QA labels", typeof(Canvas), typeof(CanvasScaler));
        Canvas screen = labels.GetComponent<Canvas>(); screen.renderMode = RenderMode.ScreenSpaceCamera; screen.worldCamera = camera; screen.planeDistance = 1;
        labels.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        GameObject text = new GameObject("Art case title", typeof(RectTransform), typeof(TextMeshProUGUI)); text.transform.SetParent(labels.transform, false);
        header = text.GetComponent<TextMeshProUGUI>(); header.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF - Fallback");
        header.fontSize = 11; header.alignment = TextAlignmentOptions.Center; header.color = new Color32(94, 49, 24, 255); header.raycastTarget = false;
        header.rectTransform.sizeDelta = new Vector2(316, 20); header.rectTransform.anchoredPosition = new Vector2(0, 146);
        SetLayer(labels.transform);
        CaptureDrawings();
        RunFlow.RunActive = true; RunFlow.IsTransitioning = false;
    }

    private static void CaptureDrawings()
    {
        iconOwner = new GameObject("Adventure art comparison"); iconOwner.transform.position = new Vector3(8000, 8000, 0);
        oldCoin = (Mesh)typeof(RunPickup).GetMethod("BuildIcon", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { 0 });
        Mesh[] art = { oldCoin, AdventureArt.PickupIcon(4), AdventureArt.PickupIcon(5), AdventureArt.RocketBody, AdventureArt.BeeBody, AdventureArt.Star };
        string[] names = { "Обычная монетка", "Бумажная ракета", "Золотая рыбка", "Ракета под лапками", "Сердитый шмель", "Бумажная звезда" };
        Material ink = new Material(Resources.Load<Shader>("HazardInk")) { name = "Art contact ink" };
        for (int i = 0; i < art.Length; i++)
        {
            GameObject obj = new GameObject(names[i], typeof(MeshFilter), typeof(MeshRenderer)); obj.transform.SetParent(iconOwner.transform, false);
            obj.transform.localPosition = new Vector3(-2.6f + i * 1.04f, 0, 0); obj.GetComponent<MeshFilter>().sharedMesh = art[i]; obj.GetComponent<MeshRenderer>().sharedMaterial = ink;
        }
        SetLayer(iconOwner.transform); player.gameObject.SetActive(false); header.gameObject.SetActive(false);
        var captions = new List<GameObject>();
        for (int i = 0; i < names.Length; i++)
        {
            GameObject title = new GameObject("Drawing caption", typeof(RectTransform), typeof(TextMeshProUGUI)); title.transform.SetParent(labels.transform, false);
            TMP_Text text = title.GetComponent<TextMeshProUGUI>(); text.font = header.font; text.fontSize = 14; text.alignment = TextAlignmentOptions.Center;
            text.color = header.color; text.raycastTarget = false; text.text = names[i]; text.rectTransform.sizeDelta = new Vector2(310, 24);
            text.rectTransform.anchoredPosition = new Vector2(-800 + i * 320, -157); captions.Add(title);
        }
        SetLayer(labels.transform);
        camera.orthographicSize = .65f;
        Render(1920, 400, new Rect(0, 0, 1920, 400), "Adventure-art-comparison.png");
        foreach (GameObject caption in captions) Object.DestroyImmediate(caption);
        header.gameObject.SetActive(true); player.gameObject.SetActive(true); player.enabled = false;
        iconOwner.SetActive(false); Object.DestroyImmediate(ink);
    }

    private static void PrepareScale(int scale)
    {
        powers.GetComponent<PaperRocketFlight>()?.Cancel();
        RocketVisuals previous = powers.GetComponent<RocketVisuals>(); if (previous != null) Object.DestroyImmediate(previous);
        cat.transform.localScale = normalVisualScale * (scale == 0 ? 1f : .64f);
        PlayerBuffVisuals previousBuff = powers.GetComponent<PlayerBuffVisuals>(); if (previousBuff != null) Object.DestroyImmediate(previousBuff);
        PlayerBuffVisuals.Attach(powers);
        camera.orthographicSize = 1f;
        Bounds feet = player.GetComponent<Collider2D>().bounds;
        support.transform.position = new Vector3(feet.center.x, feet.min.y - .20f, 0); Physics2D.SyncTransforms();
        RunFlow.RunActive = true; RunFlow.IsTransitioning = false; Time.timeScale = 1;
        powers.Collect(RunPowerups.Kind.Rocket);
        Require(powers.IsRocketFlying, "A real collected rocket activates before its art fixture.");
        rocket = RocketVisuals.Attach(powers); Require(rocket != null && rocket.PoolCapacity == 12, "A fixed 12-mote presenter attaches idempotently.");
        Require(RocketVisuals.Attach(powers) == rocket, "Repeated Attach does not duplicate a pool.");
        Time.timeScale = 0;
    }

    private static void CaptureCat(int catIndex, int scale, bool flip)
    {
        string key = Cats[catIndex] + (flip ? "-L" : "-R") + (scale == 0 ? "-Landscape100" : "-Portrait64");
        PlayerProgress.Equip("accessory_none"); PlayerProgress.Equip("trail_none");
        Require(PlayerProgress.Equip(Cats[catIndex]) && PlayerProgress.Equip(Clothes[catIndex]), "Each representative wardrobe equips: " + key);
        if (catIndex == 5) Require(PlayerProgress.Equip("accessory_astronaut_helmet"), "Astronaut outfit keeps its separate helmet.");
        if (catIndex == 7) { PlayerProgress.Equip("accessory_ghoul_hair"); PlayerProgress.Equip("accessory_ghoul_mask"); }
        cat.flipX = flip; appearance.ApplyEquipped();
        Call(rocket, "ClearMotes");
        player.transform.position = new Vector3(8000, 8000, 0);
        UnityEngine.Random.State rng = UnityEngine.Random.state;
        UnityEngine.Random.InitState(51281); float expected = UnityEngine.Random.value; UnityEngine.Random.InitState(51281);
        for (int i = 0; i < 7; i++) { player.transform.position += Vector3.up * cat.bounds.size.x * .045f; TickVisual(.07f); }
        Call(player.GetComponent<PlayerBuffVisuals>(), "RefreshPresentation", 0f);
        Require(Mathf.Abs(UnityEngine.Random.value - expected) < .000001f, "Seat/mote animation consumes no gameplay RNG.");
        UnityEngine.Random.state = rng;
        SetLayer(player.transform); SetLayer((Transform)typeof(RocketVisuals).GetField("poolRoot", Private).GetValue(rocket));
        // An older pool may be queued for Unity's end-of-frame destruction.
        // Inspect this presenter's actual seat rather than a duplicate name.
        MeshRenderer seat = ((Transform)typeof(RocketVisuals).GetField("bodyRoot", Private).GetValue(rocket)).GetComponent<MeshRenderer>();
        Require(rocket.BodyVisible && rocket.ActiveMotes > 0 && rocket.ActiveMotes <= 12, "Flight shows its seat and bounded paper trail.");
        Require(seat.sortingOrder < cat.sortingOrder && seat.bounds.max.y < cat.bounds.center.y - cat.bounds.size.y * .2f,
            "The seat is behind and below the costume / face: seat=" + seat.bounds + " cat=" + cat.bounds);
        Require(Mathf.Abs(seat.bounds.size.x / cat.bounds.size.x - 1.1f) < .01f, "Both scales keep a compact 1.1-cat-width rocket.");
        Require(seat.GetComponentsInChildren<Collider2D>().Length == 0, "The paper rocket art never adds a hitbox.");
        camera.transform.position = new Vector3(cat.bounds.center.x, cat.bounds.center.y - .16f, camera.transform.position.z); camera.orthographicSize = .98f;
        labels.SetActive(true); header.text = Cats[catIndex] + " · " + Clothes[catIndex] + (flip ? " ←" : " →") + (scale == 0 ? " · 100%" : " · 64%");
        Large[key] = Render(320, 320, new Rect(0, 0, 320, 320), key + ".png");
        if (catIndex != 6) ValidateClothingLight(Large[key], key);
        labels.SetActive(false); camera.orthographicSize = 5f;
        Native[key] = Render(scale == 0 ? 1280 : 720, scale == 0 ? 720 : 1280,
            new Rect((scale == 0 ? 1280 : 720) / 2 - 90, (scale == 0 ? 720 : 1280) / 2 - 130, 180, 260), key + "-Native.png");
        if (catIndex == 0 && !flip)
        {
            int pausedCount = rocket.ActiveMotes;
            Call(rocket, "RefreshPresentation", 1f);
            Require(rocket.ActiveMotes == pausedCount, "Paused presentation does not emit or age motes.");
            Call(rocket, "ClearMotes");
            Color32[] withoutTrail = Render(scale == 0 ? 1280 : 720, scale == 0 ? 720 : 1280,
                new Rect((scale == 0 ? 1280 : 720) / 2 - 90, (scale == 0 ? 720 : 1280) / 2 - 130, 180, 260), key + "-Native-NoTrail.png");
            int changed = 0;
            for (int pixel = 0; pixel < withoutTrail.Length; pixel++)
            {
                Color32 a = withoutTrail[pixel], b = Native[key][pixel];
                if (Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) >= 16) changed++;
            }
            Require(changed >= 12, "Paper stars contribute visible native-gameplay pixels, not an invisible particle count.");
            Debug.Log("ADVENTURE_TRAIL_PIXELS scale=" + scale + " changed=" + changed);
        }
        Debug.Log("ADVENTURE_ART_CASE " + key + " activeMotes=" + rocket.ActiveMotes);
    }

    private static void ValidateClothingLight(Color32[] pixels, string key)
    {
        // A fully interior torso patch, never the pale background around the
        // clothing rectangle. This also catches a culled Global Light2D: fur
        // uses an unlit dye shader, but illustrated clothing is a lit sprite.
        float total = 0; int samples = 0;
        for (int y = 0; y < 5; y++) for (int x = 0; x < 7; x++)
        {
            Vector3 world = cat.bounds.center + new Vector3(Mathf.Lerp(-.20f, .10f, x / 6f) * cat.bounds.size.x,
                Mathf.Lerp(-.28f, -.18f, y / 4f) * cat.bounds.size.y, 0);
            float sxWorld = (world.x - camera.transform.position.x) / (camera.orthographicSize * 2f) + .5f;
            float syWorld = (world.y - camera.transform.position.y) / (camera.orthographicSize * 2f) + .5f;
            int sx = Mathf.Clamp(Mathf.RoundToInt(sxWorld * 320), 0, 319), sy = Mathf.Clamp(Mathf.RoundToInt(syWorld * 320), 0, 319);
            Color32 pixel = pixels[sy * 320 + sx]; total += Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)); samples++;
        }
        Require(total / samples > 18f, "Real illustrated clothing remains lit, not a black silhouette: " + key + " mean=" + total / samples);
    }

    private static void CheckExitAndCleanup()
    {
        PaperRocketFlight flight = powers.GetComponent<PaperRocketFlight>();
        float oldTime = Time.timeScale; Time.timeScale = 1;
        try { flight.Step(PaperRocketFlight.FlightDuration + .01f); } finally { Time.timeScale = oldTime; }
        Require(powers.IsRocketActive && !powers.IsRocketFlying, "A real flight step enters protected landing, not inactive idle.");
        Call(rocket, "ClearMotes");
        for (int i = 0; i < 4; i++) TickVisual(.1f);
        Require(rocket.BodyVisible && rocket.ActiveMotes == 0, "Landing retains its visible transport, without emitting powered-flight stars.");
        camera.orthographicSize = .98f;
        Render(320, 320, new Rect(0, 0, 320, 320), "Rocket-Protected-Landing.png");
        flight.Cancel(); TickVisual(.08f);
        Require(rocket.BodyVisible, "Flight exit has a short safe paper fade instead of a pop.");
        camera.orthographicSize = .98f;
        Render(320, 320, new Rect(0, 0, 320, 320), "Rocket-Exit-Fade.png");
        for (int i = 0; i < 9; i++) TickVisual(.1f);
        Require(!rocket.BodyVisible && rocket.ActiveMotes == 0, "Seat and motes expire completely.");
        int children = ((Transform)typeof(RocketVisuals).GetField("poolRoot", Private).GetValue(rocket)).childCount;
        Require(children == 12, "Animation never instantiated extra hot-path motes.");
        rocket.enabled = false; Require(!rocket.BodyVisible && rocket.ActiveMotes == 0, "Disable clears every visible effect.");
        Object.DestroyImmediate(rocket); rocket = null;
    }
    private static void TickVisual(float dt)
    {
        float previous = Time.timeScale; Time.timeScale = 1;
        try { Call(rocket, "RefreshPresentation", dt); } finally { Time.timeScale = previous; }
    }
    private static void Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Private).Invoke(owner, args);
    private static Color32[] Render(int width, int height, Rect crop, string name)
    {
        RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active; float oldAspect = camera.aspect;
        var target = new RenderTexture(width, height, 24); var image = new Texture2D((int)crop.width, (int)crop.height, TextureFormat.RGB24, false);
        try
        {
            camera.aspect = (float)width / height; camera.targetTexture = target; Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture.active = target; image.ReadPixels(crop, 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(output, name), image.EncodeToPNG()); return image.GetPixels32();
        }
        finally { camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = oldActive; Object.DestroyImmediate(image); Object.DestroyImmediate(target); }
    }
    private static void WriteSheets()
    {
        for (int scale = 0; scale < 2; scale++)
        {
            Texture2D large = new Texture2D(1280, 1280, TextureFormat.RGB24, false), small = new Texture2D(720, 1040, TextureFormat.RGB24, false);
            for (int catIndex = 0; catIndex < 8; catIndex++) for (int flip = 0; flip < 2; flip++)
            {
                int column = catIndex % 4, row = catIndex / 4 * 2 + flip;
                string key = Cats[catIndex] + (flip == 0 ? "-R" : "-L") + (scale == 0 ? "-Landscape100" : "-Portrait64");
                large.SetPixels32(column * 320, (3 - row) * 320, 320, 320, Large[key]);
                small.SetPixels32(column * 180, (3 - row) * 260, 180, 260, Native[key]);
            }
            large.Apply(); small.Apply();
            File.WriteAllBytes(Path.Combine(output, "Rocket-Cats-" + (scale == 0 ? "Landscape" : "Portrait") + ".png"), large.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(output, "Rocket-Native-" + (scale == 0 ? "Landscape" : "Portrait") + ".png"), small.EncodeToPNG());
            Object.DestroyImmediate(large); Object.DestroyImmediate(small);
        }
    }
    private static void SetLayer(Transform root) { foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31; }
    private static void Require(bool value, string message) { checks++; if (!value) throw new InvalidOperationException("Adventure art: " + message); }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search") && stack.Contains("IndexationOnStartup")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = message;
    }
    private static void Finish(int code)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
        if (PlayerProgress.AdminSandboxActive) PlayerProgress.EndAdminSandbox(); storage?.Dispose(); storage = null;
        if (labels != null) Object.DestroyImmediate(labels); if (iconOwner != null) Object.DestroyImmediate(iconOwner);
        if (support != null) Object.DestroyImmediate(support); if (oldCoin != null) Object.DestroyImmediate(oldCoin);
        EditorApplication.Exit(code);
    }
}
#endif
