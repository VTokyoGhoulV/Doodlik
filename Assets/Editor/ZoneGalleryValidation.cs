#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Camera renders of the six CURRENT zone looks, not proposed location artwork.
// Uses one frozen platform layout so only score/HUD/background atmosphere differ.
[InitializeOnLoad]
public static class ZoneGalleryValidation
{
    private const string Key = "Doodlik.ZoneGalleryValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] Names = { "Meadow", "Forest", "Clouds", "Sunset", "Night", "Moon" };
    private static readonly int[] Scores = new int[WorldZones.ZoneCount];
    private static Player player;
    private static readonly Dictionary<Transform, Vector3> Layout = new Dictionary<Transform, Vector3>();
    private static int index = -1, captured;
    private static double since;
    private static string failure;

    static ZoneGalleryValidation()
    {
        if (!SessionState.GetBool(Key, false)) return;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Application.logMessageReceived += Log;
    }

    public static void Run()
    {
        if (!Application.isBatchMode ||
            !Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-6e879ab6/Assets"))
            throw new InvalidOperationException("Zone gallery is restricted to the designated isolated QA clone.");

        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "MenuPolishSandbox";
        // Only the sandbox namespace is touched: render the phone gallery in
        // portrait and the desktop gallery in its supported landscape format.
        PlayerPrefs.SetInt(GameplayPreferences.OrientationKey,
            EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ? 1 : 0);
        PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, 1);
        PlayerPrefs.Save();
        CosmeticTextureSetup.Ensure();
        JourneySettingsSetup.EnsureAsset();
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Logs");
        SessionState.SetBool(Key, true);
        index = -1;
        captured = 0;
        failure = null;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= Log;
        Application.logMessageReceived += Log;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.EnterPlaymode();
    }

    private static void Log(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup"))
        {
            Debug.LogWarning("GALLERY_ENVIRONMENT: known Unity Search startup indexer exception: " + message);
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
            if (failure != null) throw new Exception("Gallery runtime error: " + failure);
            double elapsed = EditorApplication.timeSinceStartup - since;
            if (elapsed > 40) throw new Exception("Gallery phase timeout: " + index);
            if (index < 0)
            {
                if (elapsed < 1.0 || Time.frameCount < 20) return;
                GameMenuUI menu = Object.FindAnyObjectByType<GameMenuUI>();
                player = Object.FindAnyObjectByType<Player>();
                Check(menu != null && player != null && Camera.main != null, "Runtime scene initialized");
                menu.StartRunFromHome();
                Time.timeScale = 0f;
                // Freeze gameplay after explicitly making the score visible;
                // its production visibility update normally runs in LateUpdate.
                if (player.scoreText != null) player.scoreText.enabled = true;
                player.enabled = false;
                player.GetComponent<Rigidbody2D>().simulated = false;
                Object.FindAnyObjectByType<Spawner>().enabled = false;
                Object.FindAnyObjectByType<CameraFollow>().enabled = false;
                Layout.Clear();
                foreach (GameObject platform in GameObject.FindGameObjectsWithTag("Platform"))
                    Layout.Add(platform.transform, platform.transform.position);
                Check(Layout.Count > 0, "One real generated platform layout retained");
                for (int i = 0; i < Scores.Length; i++)
                {
                    int start = WorldZones.StartScore((WorldZones.Zone)i);
                    int end = i + 1 < Scores.Length ? WorldZones.StartScore((WorldZones.Zone)(i + 1)) : WorldZones.GoalScore;
                    Scores[i] = start + (end - start) / 2;
                }
                SetZone(0);
                return;
            }

            // Use the production panorama scroll and cloud/star opacity
            // transitions. Never force UV positions or final colours.
            if (elapsed < 4.0) return;
            Check(WorldZones.Current == (WorldZones.Zone)index && player.CurrentScore == Scores[index], "Score and current zone agree");
            foreach (KeyValuePair<Transform, Vector3> item in Layout)
                Check(item.Key != null && item.Key.position == item.Value, "Platform layout remains identical");
            Check(Time.timeScale == 0f && !player.enabled, "Only the visual atmosphere is progressing");

            string name = "Zone-" + (index + 1).ToString("00") + "-" + Names[index];
            typeof(JourneyPlayValidation).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { name });
            string path = "Logs/Journey-" + EditorUserBuildSettings.activeBuildTarget + "-" + name + ".png";
            Check(File.Exists(path) && new FileInfo(path).Length > 10000, "Rendered PNG exists: " + path);
            captured++;
            Debug.Log($"ZONE_GALLERY_CAPTURE: {captured}/6 {WorldZones.Name((WorldZones.Zone)index)}, score {Scores[index]}, {path}");

            if (captured < Names.Length) SetZone(index + 1);
            else
            {
                Check(captured == 6, "All six rendered locations produced");
                Debug.Log("ZONE_GALLERY_PASS: 6 actual camera renders; same platform layout; 4-second production atmosphere transitions; no player build.");
                Finish(0);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void SetZone(int next)
    {
        index = next;
        typeof(Player).GetField("score", Private).SetValue(player, Scores[index]);
        typeof(Player).GetField("highScore", Private).SetValue(player, Scores[index]);
        typeof(Player).GetMethod("UpdateScoreText", Private).Invoke(player, null);
        WorldZones.SetScore(Scores[index]);
        since = EditorApplication.timeSinceStartup;
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException("Zone gallery check failed: " + message);
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= Log;
        SessionState.SetBool(Key, false);
        EditorApplication.Exit(code);
    }
}
#endif
