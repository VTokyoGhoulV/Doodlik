#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class HazardPlayValidation
{
    private const string ActiveKey = "Doodlik.HazardSmoke.Active";
    private const string SaveKey = "Doodlik.HazardSmoke.Preferences";
    private static int phase;
    private static double since;
    private static Player player;
    private static DoodlikHazard obstacle;
    private static Vector3 pausedPosition;
    private static Player oldPlayer;

    [Serializable] private class Preference
    {
        public string key;
        public bool exists;
        public int value;
    }
    [Serializable] private class SavedPreferences { public List<Preference> values = new List<Preference>(); }

    static HazardPlayValidation()
    {
        if (SessionState.GetBool(ActiveKey, false))
        {
            since = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }
    }

    public static void Run()
    {
        SavedPreferences saved = new SavedPreferences();
        List<string> keys = new List<string>(Player.ModeHighScoreKeys)
        {
            Player.HighScoreKey, GameplayPreferences.OrientationKey,
            GameplayPreferences.InertiaKey, "DoodlikMobileFrameRate"
        };
        foreach (string key in keys)
            saved.values.Add(new Preference { key = key, exists = PlayerPrefs.HasKey(key), value = PlayerPrefs.GetInt(key) });
        SessionState.SetString(SaveKey, JsonUtility.ToJson(saved));
        SessionState.SetBool(ActiveKey, true);
        since = EditorApplication.timeSinceStartup;
        phase = 0;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling)
            return;
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - since;
            if (elapsed > 35) throw new Exception("Smoke test timeout in phase " + phase);
            switch (phase)
            {
                case 0:
                    if (elapsed < 1) return;
                    ShieldThornValidation.Validate();
                    player = Object.FindAnyObjectByType<Player>();
                    Require(player != null && !player.IsDead, "scene starts alive");
                    Object.FindAnyObjectByType<GameMenuUI>().StartRunFromHome();
                    FreezeWorld();
                    obstacle = Create(DoodlikHazard.Kind.Thorns, player.transform.position);
                    Advance();
                    break;
                case 1:
                    if (elapsed < 0.4) return;
                    Require(player.IsDead && Time.timeScale == 0f, "thorn contact opens defeat and pauses");
                    TMP_Text title = player.gameOverPanel.transform.Find("Game Over Card/GameOverText").GetComponent<TMP_Text>();
                    Require(title.text == "Ой! Колючки!", "hazard defeat message");
                    oldPlayer = player;
                    player.RestartGame();
                    Advance();
                    break;
                case 2:
                    // A batch Editor can load the new scene before the
                    // unscaled fade-in finishes. Wait for the actual playable
                    // state; otherwise ToggleMenu is correctly ignored while
                    // the restart transition still owns the pause.
                    if (elapsed < 1 || RunFlow.IsTransitioning ||
                        !RunFlow.RunActive || Time.timeScale <= 0f) return;
                    player = Object.FindAnyObjectByType<Player>();
                    Require(player != null && player != oldPlayer && !player.IsDead,
                        "smooth restart creates a living player");
                    Require(obstacle == null, "restart destroys old hazards");
                    FreezeWorld();
                    obstacle = Create(DoodlikHazard.Kind.FlyingSeed, player.transform.position + Vector3.right * 3f);
                    Object menu = Object.FindAnyObjectByType<GameMenuUI>();
                    typeof(GameMenuUI).GetMethod("ToggleMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, null);
                    pausedPosition = obstacle.transform.position;
                    Advance();
                    break;
                case 3:
                    if (elapsed < 0.5) return;
                    Require(Time.timeScale == 0f && obstacle.transform.position == pausedPosition, "menu freezes seed");
                    Object nextMenu = Object.FindAnyObjectByType<GameMenuUI>();
                    typeof(GameMenuUI).GetMethod("ToggleMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(nextMenu, null);
                    Advance();
                    break;
                case 4:
                    if (elapsed < 0.4) return;
                    Require(Vector3.Distance(obstacle.transform.position, pausedPosition) > 0.01f, "seed resumes patrol");
                    // Start above the seed, then cross it entirely in one frame.
                    player.transform.position = obstacle.transform.position + Vector3.up * 1.8f;
                    Advance();
                    break;
                case 5:
                    if (elapsed < 0.1) return;
                    Require(!player.IsDead, "nearby seed does not kill before contact");
                    player.transform.position = obstacle.transform.position - Vector3.up * 1.8f;
                    Advance();
                    break;
                case 6:
                    if (elapsed < 0.2) return;
                    Require(player.IsDead, "swept body catches seed crossed between frames");
                    Debug.Log("HAZARD_PLAYMODE_PASS: death, defeat text, smooth restart, cleanup, menu pause, patrol, swept contact");
                    Finish(0);
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void Advance() { phase++; since = EditorApplication.timeSinceStartup; }
    private static void Require(bool value, string description)
    {
        if (!value) throw new Exception("Hazard PlayMode check failed: " + description);
    }

    private static void FreezeWorld()
    {
        player.GetComponent<Rigidbody2D>().simulated = false;
        Object.FindAnyObjectByType<Spawner>().enabled = false;
        Object.FindAnyObjectByType<CameraFollow>().enabled = false;
    }

    private static DoodlikHazard Create(DoodlikHazard.Kind kind, Vector3 position)
    {
        GameObject target = new GameObject("Smoke test hazard");
        target.transform.position = position;
        DoodlikHazard hazard = target.AddComponent<DoodlikHazard>();
        hazard.Initialize(kind, Camera.main, 1f, kind == DoodlikHazard.Kind.FlyingSeed ? 0.42f : 0f,
            1f, 0f, new Rect((Vector2)position - Vector2.one, Vector2.one * 2f));
        return hazard;
    }

    private static void Finish(int exitCode)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(ActiveKey, false);
        SavedPreferences saved = JsonUtility.FromJson<SavedPreferences>(SessionState.GetString(SaveKey, ""));
        foreach (Preference pref in saved.values)
        {
            if (pref.exists) PlayerPrefs.SetInt(pref.key, pref.value);
            else PlayerPrefs.DeleteKey(pref.key);
        }
        PlayerPrefs.Save();
        Time.timeScale = 1f;
        EditorApplication.Exit(exitCode);
    }
}
#endif
