#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Batch-only adapter: Screen.SetResolution is inert in an Editor GameView.
// Keep the real GameView surface aligned with the selected mobile format while
// the existing feature suites perform scene reloads and mode changes.
[InitializeOnLoad]
public static class ActualViewportValidation
{
    private const string Key = "Doodlik.ActualViewportValidation";
    private static int lastWidth, lastHeight;
    private static string lastMode;
    private static double mismatchSince;

    static ActualViewportValidation()
    {
        if (!SessionState.GetBool(Key, false)) return;
        EditorApplication.update += WatchViewport;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public static void RunFocused() => Start(false, FullGameAuditValidation.RunFocused);
    public static void RunLandscape() => Start(false, DifficultyFeatureValidation.RunLandscape);
    public static void RunPortrait() => Start(true, DifficultyFeatureValidation.RunPortrait);
    public static void RunCosmeticsPortrait() => Start(true, CosmeticsVisualValidation.RunPortrait);

    private static void Start(bool portrait, Action suite)
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-"))
            throw new InvalidOperationException("Actual viewport validation requires a disposable MenuValidation batch copy.");
        SessionState.SetBool(Key, true);
        AudioListener.volume = 0f;
        Request(portrait);
        EditorApplication.update -= WatchViewport;
        EditorApplication.update += WatchViewport;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        suite();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BeforeSceneLoad()
    {
        if (!SessionState.GetBool(Key, false)) return;
        AudioListener.volume = 0f;
        Request(GameplayPreferences.IsPortrait);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        AudioListener.volume = 0f;
        Request(GameplayPreferences.IsPortrait);
    }

    private static void Request(bool portrait)
    {
        int width = portrait ? 720 : 1280;
        int height = portrait ? 1280 : 720;
        if (lastWidth == width && lastHeight == height) return;
        PlayModeWindow.SetCustomRenderingResolution((uint)width, (uint)height, "Doodlik isolated viewport validation");
        lastWidth = width;
        lastHeight = height;
        mismatchSince = EditorApplication.timeSinceStartup;
        Debug.Log("ACTUAL_VIEWPORT_REQUEST " + width + "x" + height);
    }

    private static void WatchViewport()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        // -noaudio does not guarantee silence when a suite exercises audio
        // settings; the release task additionally mutes test sessions in Windows.
        AudioListener.volume = 0f;
        Request(GameplayPreferences.IsPortrait);
        bool matches = Screen.width == lastWidth && Screen.height == lastHeight;
        if (!matches)
        {
            if (EditorApplication.timeSinceStartup - mismatchSince > 5f)
            {
                Debug.LogError("ACTUAL_VIEWPORT_FAIL actual=" + Screen.width + "x" + Screen.height +
                    " expected=" + lastWidth + "x" + lastHeight);
                SessionState.SetBool(Key, false);
                EditorApplication.Exit(1);
            }
            return;
        }
        string mode = GameplayPreferences.IsPortrait + "/" + GameplayPreferences.UseInertia + "/" + GameplayPreferences.Difficulty;
        if (mode == lastMode) return;
        lastMode = mode;
        Debug.Log("ACTUAL_VIEWPORT_PASS screen=" + Screen.width + "x" + Screen.height +
            " portrait=" + GameplayPreferences.IsPortrait + " inertia=" + GameplayPreferences.UseInertia +
            " difficulty=" + GameplayPreferences.Difficulty + " safe=" + Screen.safeArea);
    }
}
#endif
