#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Render actual pooled meshes in the game's URP camera, at gameplay and close
// inspection scale. All fixtures and progress are disposable validation data.
[InitializeOnLoad]
public static class JumpTrailRenderValidation
{
    private const string Key = "Doodlik.JumpTrailRenderValidation";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static double since;
    private static int checks;
    private static string failure;

    static JumpTrailRenderValidation()
    {
        if (!SessionState.GetBool(Key, false)) return;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    public static void Run()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-"))
            throw new InvalidOperationException("Trail rendering needs an isolated MenuValidation batch project.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikTrailRenderSandbox";
        GameplayPreferences.SetPortrait(true);
        AudioListener.pause = true;
        AudioListener.volume = 0f;
        SessionState.SetBool(Key, true);
        since = EditorApplication.timeSinceStartup;
        checks = 0;
        failure = null;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (failure != null) throw new InvalidOperationException(failure);
            if (EditorApplication.timeSinceStartup - since > 45f) throw new TimeoutException("Trail render startup timed out.");
            if (Time.frameCount < 20 || EditorApplication.timeSinceStartup - since < 0.8f) return;
            ValidateRenderers();
            Finish(0);
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    private static void ValidateRenderers()
    {
        Camera camera = Camera.main;
        CatAppearance source = Object.FindAnyObjectByType<CatAppearance>();
        Require(camera != null && source != null, "Real gameplay camera and source cat exist.");
        SpriteRenderer sourceSprite = source.GetComponent<SpriteRenderer>();
        Require(sourceSprite != null && sourceSprite.sprite != null, "Real original collision sprite exists.");
        Canvas[] canvases = Object.FindObjectsByType<Canvas>();
        bool[] canvasEnabled = new bool[canvases.Length];
        for (int i = 0; i < canvases.Length; i++) { canvasEnabled[i] = canvases[i].enabled; canvases[i].enabled = false; }
        Vector3 oldCameraPosition = camera.transform.position;
        float oldSize = camera.orthographicSize, oldAspect = camera.aspect, oldTime = Time.timeScale;
        CameraClearFlags oldClear = camera.clearFlags;
        Color oldBackground = camera.backgroundColor;
        bool oldRun = RunFlow.RunActive, oldTransition = RunFlow.IsTransitioning;
        GameObject fixture = null;
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/JumpTrailWorld"));
        Directory.CreateDirectory(output);
        using (PlayerProgress.UseIsolatedStorageForValidation(out _, out _))
        {
            try
            {
                fixture = new GameObject("World trail render validation");
                fixture.transform.position = new Vector3(8000f, 8000f, 0f);
                fixture.AddComponent<Player>().enabled = false;
                GameObject visual = new GameObject("Drawn cat", typeof(SpriteRenderer));
                visual.transform.SetParent(fixture.transform, false);
                visual.transform.localScale = source.transform.lossyScale;
                SpriteRenderer renderer = visual.GetComponent<SpriteRenderer>();
                renderer.sprite = sourceSprite.sprite;
                renderer.sharedMaterial = sourceSprite.sharedMaterial;
                CatAppearance.Attach(visual.transform, renderer);
                JumpTrailVisuals trail = visual.GetComponent<JumpTrailVisuals>();
                Require(trail != null, "Fixture uses the production pooled presenter.");
                PlayerProgress.AddCoins(10000);
                foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
                    if (item.Category == PlayerProgress.Category.Trail && item.Price >= 0 && JumpTrailArt.Get(item.Id) != null)
                        Require(PlayerProgress.TryPurchase(item.Id), "Purchasable trail still uses the shop: " + item.Id);
                // Earned illustrations are deliberately not purchasable. This
                // disposable art fixture grants visual access only; reward
                // eligibility and single-claim persistence have their own tests.
                PlayerProgress.BeginAdminSandbox();
                RunFlow.RunActive = true;
                RunFlow.IsTransitioning = false;
                Time.timeScale = 1f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(244, 230, 191, 255);
                foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
                {
                    if (item.Category != PlayerProgress.Category.Trail || JumpTrailArt.Get(item.Id) == null) continue;
                    Require(PlayerProgress.Equip(item.Id), "Owned or sandbox-granted trail can be selected: " + item.Id);
                    foreach (bool closeup in new[] { false, true })
                    {
                        trail.Clear();
                        float width = renderer.bounds.size.x;
                        for (int step = 0; step < 18; step++)
                        {
                            visual.transform.position += new Vector3(width * 0.015f, width * 0.065f, 0f);
                            Refresh(trail, 0.05f);
                        }
                        Require(trail.ActiveCount > 0, "Rising produced live world geometry.");
                        Transform pool = (Transform)typeof(JumpTrailVisuals).GetField("poolRoot", PrivateInstance).GetValue(trail);
                        Require(pool != null, "Production world pool exists.");
                        int active = 0;
                        foreach (MeshRenderer meshRenderer in pool.GetComponentsInChildren<MeshRenderer>())
                        {
                            if (!meshRenderer.gameObject.activeInHierarchy) continue;
                            active++;
                            Require(meshRenderer.sharedMaterial != null && meshRenderer.sharedMaterial.shader.isSupported,
                                "World trail has supported production material.");
                            Require(meshRenderer.GetComponent<MeshFilter>().sharedMesh.vertexCount > 0,
                                "World trail has nonempty render geometry.");
                        }
                        Require(active == trail.ActiveCount && active <= 12, "Active renderers match the bounded presenter.");
                        camera.orthographicSize = closeup ? Mathf.Max(0.7f, renderer.bounds.size.y * 1.15f) : oldSize;
                        camera.transform.position = new Vector3(renderer.bounds.center.x,
                            renderer.bounds.center.y - renderer.bounds.size.y * 0.32f, oldCameraPosition.z);
                        int height = closeup ? 720 : 1280;
                        pool.gameObject.SetActive(false);
                        Color32[] baseline = Capture(camera, 720, height, null);
                        pool.gameObject.SetActive(true);
                        string name = item.Id + (closeup ? "-Closeup" : "-Gameplay");
                        Color32[] visible = Capture(camera, 720, height, Path.Combine(output, name + ".png"));
                        long visibleEnergy = Difference(baseline, visible, out int changedPixels);
                        Require(changedPixels >= 8 && visibleEnergy > 200,
                            "Actual world trail changes rendered pixels: " + name + " pixels=" + changedPixels);
                        Refresh(trail, 0.18f);
                        Color32[] fading = Capture(camera, 720, height, null);
                        long fadeEnergy = Difference(baseline, fading, out _);
                        Require(fadeEnergy < visibleEnergy * 0.85f, "Actual alpha visibly fades: " + name);
                        Refresh(trail, 0.9f);
                        Color32[] expired = Capture(camera, 720, height, null);
                        long expiredEnergy = Difference(baseline, expired, out int expiredPixels);
                        Require(trail.ActiveCount == 0 && expiredEnergy <= 4 && expiredPixels == 0,
                            "Expired world trail leaves no rendered residue: " + name);
                        Debug.Log("JUMP_TRAIL_RENDER_CASE " + name + " pixels=" + changedPixels +
                            " energy=" + visibleEnergy + " fading=" + fadeEnergy + " expired=" + expiredEnergy);
                    }
                }
            }
            finally
            {
                if (fixture != null) Object.DestroyImmediate(fixture);
                if (PlayerProgress.AdminSandboxActive) PlayerProgress.EndAdminSandbox();
                camera.transform.position = oldCameraPosition;
                camera.orthographicSize = oldSize;
                camera.aspect = oldAspect;
                camera.clearFlags = oldClear;
                camera.backgroundColor = oldBackground;
                Time.timeScale = oldTime;
                RunFlow.RunActive = oldRun;
                RunFlow.IsTransitioning = oldTransition;
                for (int i = 0; i < canvases.Length; i++) if (canvases[i] != null) canvases[i].enabled = canvasEnabled[i];
            }
        }
        int renderedCases = 0;
        foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
            if (item.Category == PlayerProgress.Category.Trail && JumpTrailArt.Get(item.Id) != null) renderedCases += 2;
        Debug.Log("JUMP_TRAIL_RENDER_PASS checks=" + checks + " cases=" + renderedCases + " path=" + output);
    }

    private static void Refresh(JumpTrailVisuals trail, float dt) =>
        typeof(JumpTrailVisuals).GetMethod("Refresh", PrivateInstance).Invoke(trail, new object[] { dt });

    private static Color32[] Capture(Camera camera, int width, int height, string path)
    {
        RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
        float oldAspect = camera.aspect;
        RenderTexture target = new RenderTexture(width, height, 24);
        Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.aspect = (float)width / height;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply();
            if (path != null) File.WriteAllBytes(path, image.EncodeToPNG());
            return image.GetPixels32();
        }
        finally
        {
            camera.targetTexture = oldTarget;
            camera.aspect = oldAspect;
            RenderTexture.active = oldActive;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
        }
    }

    private static long Difference(Color32[] baseline, Color32[] current, out int changed)
    {
        long energy = 0;
        changed = 0;
        for (int i = 0; i < baseline.Length; i++)
        {
            int difference = Math.Abs(baseline[i].r - current[i].r) + Math.Abs(baseline[i].g - current[i].g) +
                Math.Abs(baseline[i].b - current[i].b);
            energy += difference;
            if (difference >= 8) changed++;
        }
        return energy;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Trail renderer: " + message);
        checks++;
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = message;
    }

    private static void Finish(int code)
    {
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        EditorApplication.Exit(code);
    }
}
#endif
