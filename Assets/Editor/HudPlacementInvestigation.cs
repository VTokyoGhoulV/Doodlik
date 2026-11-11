#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Read-only production investigation; mutations are limited to disposable
// runtime UI and isolated test preferences. No scene or prefab is saved.
[InitializeOnLoad]
public static class HudPlacementInvestigation
{
    private const string Key = "Doodlik.HudPlacementInvestigation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static double since;
    private static int phase;
    private static int requestedWidth, requestedHeight;
    private static string failure;
    private static readonly Vector3[] Corners = new Vector3[4];

    static HudPlacementInvestigation()
    {
        if (!SessionState.GetBool(Key, false)) return;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    public static void Run()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-"))
            throw new InvalidOperationException("HUD investigation needs an isolated MenuValidation batch copy.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikHudPlacementSandbox";
        GameplayPreferences.SetMode(false, false, false);
        GameplayPreferences.SetDifficulty(GameDifficulty.Hard);
        GameplayPreferences.SetScorePanelTransparency(0f);
        SessionState.SetBool(Key, true);
        phase = 0;
        since = EditorApplication.timeSinceStartup;
        AudioListener.pause = true;
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
            if (EditorApplication.timeSinceStartup - since > 30f) throw new TimeoutException("HUD phase " + phase);
            if (Time.frameCount < 20 || EditorApplication.timeSinceStartup - since < 0.6f) return;
            switch (phase)
            {
                case 0: BeginRun(); SetResolution(1280, 720); break;
                case 1: Inspect("Landscape-16x9", Rect.zero); SetResolution(2400, 1080); break;
                case 2: Inspect("Landscape-20x9", Rect.zero); Advance(); break;
                case 3: Inspect("Landscape-20x9-NotchLeft", new Rect(90f, 0f, Screen.width - 90f, Screen.height)); Advance(); break;
                case 4:
                    SceneManager.LoadScene("SampleScene"); Advance(); break;
                case 5:
                    BeginRun(); Inspect("Landscape-20x9-Reload", Rect.zero); Advance(); break;
                case 6:
                    Inspect("Landscape-20x9-Reload-NotchLeft", new Rect(90f, 0f, Screen.width - 90f, Screen.height));
                    SetResolution(1280, 800); break;
                case 7:
                    Inspect("Landscape-16x10", Rect.zero);
                    GameplayPreferences.SetPortrait(true, false); SetResolution(720, 1280); break;
                case 8:
                    Inspect("Portrait-AfterRotate", Rect.zero);
                    GameplayPreferences.SetPortrait(false, false); SetResolution(2400, 1080); break;
                case 9:
                    Inspect("Landscape-20x9-AfterRotate", Rect.zero);
                    Debug.Log("HUD_PLACEMENT_INVESTIGATION_PASS captures=8 hard/precise, 16:9,20:9,16:10, notch simulation, reload and rotate");
                    Finish(0); break;
            }
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    private static void BeginRun()
    {
        GameMenuUI menu = Object.FindAnyObjectByType<GameMenuUI>();
        Player player = Object.FindAnyObjectByType<Player>();
        Require(menu != null && player != null, "Menu/player exist.");
        menu.StartRunFromHome();
        player.GetComponent<Rigidbody2D>().simulated = false;
        Object.FindAnyObjectByType<Spawner>().enabled = false;
        Object.FindAnyObjectByType<CameraFollow>().enabled = false;
        Time.timeScale = 0f;
    }

    private static void SetResolution(int width, int height)
    {
        requestedWidth = width; requestedHeight = height;
        PlayModeWindow.SetCustomRenderingResolution((uint)width, (uint)height, "Doodlik isolated HUD probe");
        Debug.Log("HUD_REQUEST_RESOLUTION " + width + "x" + height);
        Advance();
    }

    private static void Advance() { phase++; since = EditorApplication.timeSinceStartup; }

    private static void Inspect(string name, Rect syntheticSafeArea)
    {
        Require(Screen.width == requestedWidth && Screen.height == requestedHeight,
            "The actual GameView must match the requested viewport, got " + Screen.width + "x" + Screen.height);
        GameObject scoreObject = GameObject.Find("Score UI");
        Canvas canvas = scoreObject.GetComponent<Canvas>();
        CanvasScaler scaler = scoreObject.GetComponent<CanvasScaler>();
        ResponsiveDisplay responsive = scoreObject.GetComponent<ResponsiveDisplay>();
        Require(responsive != null, "ResponsiveDisplay exists after lifecycle.");
        typeof(ResponsiveDisplay).GetMethod("ApplySafeArea", Private).Invoke(responsive, new object[] { true });
        RectTransform safe = scoreObject.transform.Find("Safe Area").GetComponent<RectTransform>();
        Rect safePixels = syntheticSafeArea.width > 0f ? syntheticSafeArea : Screen.safeArea;
        if (syntheticSafeArea.width > 0f)
        {
            safe.anchorMin = new Vector2(safePixels.xMin / Screen.width, safePixels.yMin / Screen.height);
            safe.anchorMax = new Vector2(safePixels.xMax / Screen.width, safePixels.yMax / Screen.height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;
        }
        Canvas.ForceUpdateCanvases();
        GameHudFrame frame = scoreObject.GetComponentInChildren<GameHudFrame>(true);
        Require(frame != null && frame.transform.parent == safe, "HUD remains a direct child of the safe-area root.");
        Require(scoreObject.GetComponentsInChildren<GameHudFrame>(true).Length == 1, "Exactly one score panel exists.");
        typeof(GameHudFrame).GetMethod("LateUpdate", Private).Invoke(frame, null);
        Canvas.ForceUpdateCanvases();
        RectTransform panel = frame.GetComponent<RectTransform>();
        Rect paper = ScreenRect(panel, canvas);
        Rect safeRect = ScreenRect(safe, canvas);
        float left = paper.xMin - safeRect.xMin;
        float top = safeRect.yMax - paper.yMax;
        Require(Mathf.Abs(left - 14f * canvas.scaleFactor) < 1f && Mathf.Abs(top - 14f * canvas.scaleFactor) < 1f,
            "Panel top/left padding equals the fixed 14 UI units.");
        Require(panel.anchorMin == new Vector2(0f, 1f) && panel.anchorMax == new Vector2(0f, 1f) &&
            panel.pivot == new Vector2(0f, 1f), "Panel is pinned by its upper-left corner.");
        Require(panel.localScale == Vector3.one && panel.localRotation == Quaternion.identity,
            "Panel has no inherited local scaling or rotation offset.");
        Debug.Log("HUD_GEOMETRY " + name + " screen=" + Screen.width + "x" + Screen.height +
            " screenSafe=" + Screen.safeArea + " testSafe=" + safePixels + " scale=" + canvas.scaleFactor.ToString("F5") +
            " reference=" + scaler.referenceResolution + " safeRect=" + safeRect.ToString("F3") +
            " paperRect=" + paper.ToString("F3") + " padding=" + left.ToString("F3") + "," + top.ToString("F3") +
            " panelUI=" + panel.rect.size + " anchor=" + panel.anchorMin + " pivot=" + panel.pivot);
        Player player = Object.FindAnyObjectByType<Player>();
        Debug.Log("HUD_TEXT " + name + " font=" + player.scoreText.fontSize + " rect=" +
            ScreenRect(player.scoreText.rectTransform, canvas).ToString("F3") + " text=" + player.scoreText.text.Replace('\n', '|'));
        Capture(name);
    }

    private static Rect ScreenRect(RectTransform rect, Canvas canvas)
    {
        rect.GetWorldCorners(Corners);
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, Corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, Corners[2]);
        return new Rect(min, max - min);
    }

    private static void Capture(string name)
    {
        Camera camera = Camera.main;
        RenderTexture previous = camera.targetTexture, oldActive = RenderTexture.active;
        RenderTexture target = new RenderTexture(Screen.width, Screen.height, 24);
        Texture2D image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        List<Canvas> changed = new List<Canvas>();
        List<Camera> oldCameras = new List<Camera>();
        try
        {
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>())
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                changed.Add(canvas); oldCameras.Add(canvas.worldCamera);
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
            }
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0f, 0f, Screen.width, Screen.height), 0, 0); image.Apply();
            Directory.CreateDirectory("Logs/HudPlacement");
            File.WriteAllBytes("Logs/HudPlacement/" + name + ".png", image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previous; RenderTexture.active = oldActive;
            for (int i = 0; i < changed.Count; i++) { changed[i].renderMode = RenderMode.ScreenSpaceOverlay; changed[i].worldCamera = oldCameras[i]; }
            Object.DestroyImmediate(image); Object.DestroyImmediate(target);
        }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = message;
    }
    private static void Finish(int code)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
        EditorApplication.Exit(code);
    }
}
#endif
