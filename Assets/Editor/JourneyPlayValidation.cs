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

// Run with -executeMethod JourneyPlayValidation.Run, without -quit. No player build.
[InitializeOnLoad]
public static class JourneyPlayValidation
{
    private const string Key = "Doodlik.JourneySmoke";
    private static int phase;
    private static double since;
    private static Player player, oldPlayer;
    private static GameMenuUI menu;
    private static DoodlikHazard thorn;
    private static string error;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] private sealed class Preference { public string key, text; public int type, integer; public float number; public bool exists; }
    [Serializable] private sealed class Snapshot { public List<Preference> values = new List<Preference>(); }

    static JourneyPlayValidation()
    {
        if (!SessionState.GetBool(Key, false)) return;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Application.logMessageReceived += Log;
    }

    public static void Run()
    {
        RunInternal(false);
    }

    public static void RunPortrait() => RunInternal(true);

    private static void RunInternal(bool portrait)
    {
        Snapshot snapshot = new Snapshot();
        foreach (string key in Player.ModeHighScoreKeys) Save(snapshot, key, 0);
        foreach (string key in new[] { Player.HighScoreKey, GameplayPreferences.OrientationKey, GameplayPreferences.InertiaKey, "DoodlikMobileFrameRate" }) Save(snapshot, key, 0);
        Save(snapshot, PlayerProgress.SaveKey, 1); Save(snapshot, PlayerProgress.BackupKey, 1);
        Save(snapshot, GameAudio.MusicVolumeKey, 2); Save(snapshot, GameAudio.EffectsVolumeKey, 2);
        SessionState.SetString(Key + ".prefs", JsonUtility.ToJson(snapshot));
        PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, portrait ? 1 : 0);
        SessionState.SetBool(Key, true);
        phase = 0;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Application.logMessageReceived -= Log; Application.logMessageReceived += Log;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void Save(Snapshot snapshot, string key, int type) => snapshot.values.Add(new Preference
    { key = key, exists = PlayerPrefs.HasKey(key), type = type, integer = type == 0 ? PlayerPrefs.GetInt(key) : 0,
      text = type == 1 ? PlayerPrefs.GetString(key) : "", number = type == 2 ? PlayerPrefs.GetFloat(key) : 0f });
    private static void Log(string message, string stack, LogType type)
    {
        // Unity 6000.5 sometimes fails its own Search startup indexing in a
        // fresh batch Editor. Keep this separate from actual game exceptions.
        if (stack.Contains("UnityEditor.Search.SearchDatabase") &&
            stack.Contains("IndexationOnStartup")) return;
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            error = message;
    }
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    private static void Check(bool value, string message) { if (!value) throw new Exception("Journey check: " + message); }
    private static void Advance() { phase++; since = EditorApplication.timeSinceStartup; }
    private static void Freeze()
    {
        player.GetComponent<Rigidbody2D>().simulated = false;
        Object.FindAnyObjectByType<Spawner>().enabled = false;
        Object.FindAnyObjectByType<CameraFollow>().enabled = false;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (error != null) throw new Exception("Runtime error: " + error);
            double elapsed = EditorApplication.timeSinceStartup - since;
            if (elapsed > 40) throw new Exception("Journey timeout phase " + phase);
            if (elapsed < 0.5) return;
            switch (phase)
            {
                case 0:
                    if (Time.frameCount < 20) return;
                    menu = Object.FindAnyObjectByType<GameMenuUI>();
                    player = Object.FindAnyObjectByType<Player>();
                    Check(menu != null && player != null, "scene references");
                    Check(menu.GetComponent<FrontEndUI>().IsVisible && !RunFlow.IsPlaying && Time.timeScale == 0f, "home screen pauses fresh run");
                    EditorActualJumpValidation.Run();
                    DoodlikAudioValidation.Validate();
                    Capture("Home");
                    Invoke(menu.GetComponent<FrontEndUI>(), "ShowShop");
                    Advance(); break;
                case 1:
                    Capture("Wardrobe");
                    Invoke(menu.GetComponent<FrontEndUI>(), "SelectTab", 3);
                    menu.StartRunFromHome();
                    Freeze();
                    Check(RunFlow.IsPlaying && !GameMenuUI.BlocksGameplayInput && Time.timeScale == 1f, "play begins from home");
                    // The extension checker preserves the wallet itself; the outer snapshot is an extra safeguard.
                    Type extensions = typeof(JourneyPlayValidation).Assembly.GetType("RunExtensionValidation");
                    Check(extensions != null, "powerup validator exists");
                    extensions.GetMethod("ValidatePickupsAndPowers").Invoke(null, null);
                    CheckCrumblingPlatform();
                    Invoke(menu, "ToggleMenu");
                    Advance(); break;
                case 2:
                    Check(Time.timeScale == 0f && GameMenuUI.BlocksGameplayInput, "pause stops game");
                    menu.OpenSettingsFromHome();
                    Capture("Settings");
                    Invoke(menu, "CloseSettings");
                    menu.StartRunFromHome();
                    GameObject obstacle = new GameObject("Journey test thorn");
                    obstacle.transform.position = player.transform.position;
                    thorn = obstacle.AddComponent<DoodlikHazard>();
                    thorn.Initialize(DoodlikHazard.Kind.Thorns, Camera.main, 1f, 0f, 1f, 0f,
                        new Rect((Vector2)obstacle.transform.position - Vector2.one, Vector2.one * 2f));
                    Advance(); break;
                case 3:
                    Check(player.IsDead && Time.timeScale == 0f && player.gameOverPanel.activeSelf, "hazard defeat");
                    Capture("Defeat");
                    oldPlayer = player;
                    player.RestartGame();
                    Check(Time.timeScale == 0f && RunFlow.IsTransitioning, "restart remains frozen behind fade");
                    Advance(); break;
                case 4:
                    if (RunFlow.IsTransitioning) return;
                    player = Object.FindAnyObjectByType<Player>();
                    Check(player != oldPlayer && !player.IsDead && thorn == null, "clean restart");
                    Check(Object.FindObjectsByType<GameMenuUI>().Length == 1 && Object.FindObjectsByType<GameAudio>().Length == 1 &&
                        Object.FindObjectsByType<ScrollingBackground>().Length == 1, "no duplicate persistent systems");
                    Freeze();
                    float nightHeight = WorldZones.HeightAtScore(WorldZones.StartScore(WorldZones.Zone.Night) + 10);
                    typeof(Player).GetField("maximumHeight", Private).SetValue(player, nightHeight);
                    WorldZones.SetHeight(nightHeight);
                    Advance(); break;
                case 5:
                    if (elapsed < 2) return;
                    Check(WorldZones.Current == WorldZones.Zone.Night, "night zone reached");
                    Capture("Night");
                    menu.ReturnToHome();
                    Advance(); break;
                case 6:
                    if (RunFlow.IsTransitioning) return;
                    Check(!RunFlow.RunActive && menu.GetComponent<FrontEndUI>().IsVisible && Time.timeScale == 0f, "return home stops fresh attempt");
                    Check(WorldZones.Current == WorldZones.Zone.Meadow, "zone resets on retry");
                    menu.GetComponent<FrontEndUI>().ShowModes();
                    Check(menu.SelectGameMode(GameplayPreferences.IsPortrait, !GameplayPreferences.UseInertia),
                        "mode selection accepted from home");
                    Advance(); break;
                case 7:
                    if (RunFlow.IsTransitioning) return;
                    Check(!RunFlow.RunActive && Time.timeScale == 0f && GameMenuUI.BlocksGameplayInput, "mode restart stays in front end");
                    FrontEndUI frontEnd = menu.GetComponent<FrontEndUI>();
                    Check(frontEnd.IsModeScreenVisible, "mode tab remains open after reload");
                    bool hasModeLabel = false;
                    foreach (TMP_Text label in frontEnd.GetComponentsInChildren<TMP_Text>())
                        hasModeLabel |= label.text.Contains(GameplayPreferences.CurrentModeDisplayName);
                    Check(hasModeLabel, "selected mode label survives reload");
                    Check(!((GameObject)typeof(GameMenuUI).GetField("settingsPanel", Private).GetValue(menu)).activeSelf, "mode change does not reopen general settings");
                    frontEnd.GoBack();
                    Debug.Log("JOURNEY_PLAYMODE_PASS: startup, shop, audio, powers, pickups, pause, defeat, restart, persistent cleanup, zones, settings, modes");
                    Finish(0); break;
            }
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    private static void Capture(string name)
    {
        int width = GameplayPreferences.IsPortrait ? 720 : 1280;
        int height = GameplayPreferences.IsPortrait ? 1280 : 720;
        Camera camera = Camera.main;
        float oldAspect = camera.aspect;
        camera.aspect = (float)width / height;
        var background = Object.FindAnyObjectByType<ScrollingBackground>();
        if (background != null) Invoke(background, "LateUpdate");
        var changed = new List<Canvas>();
        var priorCameras = new List<Camera>();
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>())
        {
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            changed.Add(canvas); priorCameras.Add(canvas.worldCamera);
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
        }
        RenderTexture previous = RenderTexture.active;
        RenderTexture target = new RenderTexture(width, height, 24);
        RenderTexture oldTarget = camera.targetTexture;
        camera.targetTexture = target;
        Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
        Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
        File.WriteAllBytes("Logs/Journey-" + EditorUserBuildSettings.activeBuildTarget + "-" + name + ".png", image.EncodeToPNG());
        camera.targetTexture = oldTarget; RenderTexture.active = previous;
        camera.aspect = oldAspect;
        for (int i=0; i<changed.Count; i++) { changed[i].renderMode=RenderMode.ScreenSpaceOverlay; changed[i].worldCamera=priorCameras[i]; }
        Object.DestroyImmediate(image); Object.DestroyImmediate(target);
    }

    private static void CheckCrumblingPlatform()
    {
        GameObject root = new GameObject("Isolated crumble probe");
        root.transform.position = new Vector3(9000f, 9000f, 0f);
        try
        {
            SpriteRenderer sprite = root.AddComponent<SpriteRenderer>();
            GameObject[] platforms = GameObject.FindGameObjectsWithTag("Platform");
            sprite.sprite = platforms[0].GetComponent<SpriteRenderer>().sprite;
            BoxCollider2D surface = root.AddComponent<BoxCollider2D>();
            CrumblingPlatform crumble = root.AddComponent<CrumblingPlatform>();
            crumble.OnLanded();
            Check(surface.enabled, "crumbling platform allows first bounce");
            RunFlow.IsTransitioning = true;
            Invoke(crumble, "Update");
            Check((float)typeof(CrumblingPlatform).GetField("timeAfterLanding", Private).GetValue(crumble) == 0f,
                "transition freezes crumble countdown");
            RunFlow.IsTransitioning = false;
            typeof(CrumblingPlatform).GetField("timeAfterLanding", Private).SetValue(crumble, 0.33f);
            Invoke(crumble, "Update");
            Check(!surface.enabled, "crumbling platform disables surface after delay");
        }
        finally { RunFlow.IsTransitioning = false; Object.DestroyImmediate(root); }
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        SessionState.SetBool(Key, false);
        Snapshot snapshot = JsonUtility.FromJson<Snapshot>(SessionState.GetString(Key + ".prefs", ""));
        foreach (Preference pref in snapshot.values)
        {
            if (!pref.exists) PlayerPrefs.DeleteKey(pref.key);
            else if (pref.type == 0) PlayerPrefs.SetInt(pref.key, pref.integer);
            else if (pref.type == 1) PlayerPrefs.SetString(pref.key, pref.text);
            else PlayerPrefs.SetFloat(pref.key, pref.number);
        }
        PlayerPrefs.Save();
        Time.timeScale = 1f;
        EditorApplication.Exit(code);
    }
}
#endif
