using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Synchronous checks: do not reload a scene, move the device or spend currency.
public static class ModeSelectionValidation
{
    [MenuItem("Doodlik/Validate Game Mode Preferences")]
    public static void ValidatePreferences()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Validate mode preferences outside Play Mode.");

        bool hadOrientation = PlayerPrefs.HasKey(GameplayPreferences.OrientationKey);
        bool hadInertia = PlayerPrefs.HasKey(GameplayPreferences.InertiaKey);
        int orientation = PlayerPrefs.GetInt(GameplayPreferences.OrientationKey);
        int inertia = PlayerPrefs.GetInt(GameplayPreferences.InertiaKey);
        string progress = PlayerPrefs.GetString(PlayerProgress.SaveKey, string.Empty);
        string backup = PlayerPrefs.GetString(PlayerProgress.BackupKey, string.Empty);
        int[] records = new int[Player.ModeHighScoreKeys.Length];
        for (int i = 0; i < records.Length; i++)
            records[i] = PlayerPrefs.GetInt(Player.ModeHighScoreKeys[i], 0);

        int checks = 0;
        try
        {
            for (int mode = 0; mode < 4; mode++)
            {
                bool portrait = (mode & 2) != 0;
                bool useInertia = (mode & 1) != 0;
                bool expectedPortrait = portrait && !GameplayPreferences.IsDesktopBuild;
                GameplayPreferences.SetMode(portrait, useInertia, false);
                Require(GameplayPreferences.IsPortrait == expectedPortrait &&
                    GameplayPreferences.UseInertia == useInertia, "Both mode preferences update together."); checks++;
                Require(PlayerPrefs.GetInt(GameplayPreferences.OrientationKey) == (expectedPortrait ? 1 : 0) &&
                    PlayerPrefs.GetInt(GameplayPreferences.InertiaKey) == (useInertia ? 1 : 0),
                    "Existing preference keys retain the selected mode."); checks++;
                Require(RunFlow.ModeKey == (expectedPortrait ? "portrait" : "landscape") +
                    (useInertia ? "_inertia" : "_precise"), "Existing upgrade mode key is unchanged."); checks++;
                Require(Player.CurrentHighScoreKey == RunRecords.ScoreKey(expectedPortrait,
                    useInertia, GameplayPreferences.Difficulty),
                    "Record key matches the selected format, control and difficulty."); checks++;
                Require(!GameplayPreferences.IsDesktopBuild ||
                    (!GameplayPreferences.CurrentModeDisplayName.Contains("Книжный") &&
                     !GameplayPreferences.CurrentModeDisplayName.Contains("Альбомный")),
                    "Desktop names do not expose mobile orientation."); checks++;
            }

            for (int i = 0; i < records.Length; i++)
            {
                Require(PlayerPrefs.GetInt(Player.ModeHighScoreKeys[i], 0) == records[i],
                    "Switching mode preserves every record."); checks++;
            }
            Require(PlayerPrefs.GetString(PlayerProgress.SaveKey, string.Empty) == progress &&
                PlayerPrefs.GetString(PlayerProgress.BackupKey, string.Empty) == backup,
                "Switching mode preserves wallet, cosmetics and upgrades."); checks++;
            Debug.Log($"MODE_PREFERENCES_PASS: {checks} checks; original preferences restored in finally.");
        }
        finally
        {
            Restore(GameplayPreferences.OrientationKey, hadOrientation, orientation);
            Restore(GameplayPreferences.InertiaKey, hadInertia, inertia);
            PlayerPrefs.Save();
        }
    }

    public static void ValidateRuntimeGuards()
    {
        Require(Application.isPlaying, "Runtime mode guards require Play Mode.");
        GameMenuUI menu = UnityEngine.Object.FindFirstObjectByType<GameMenuUI>();
        Require(menu != null, "Runtime menu exists.");
        FieldInfo localTransition = typeof(GameMenuUI).GetField("transitionInProgress",
            BindingFlags.NonPublic | BindingFlags.Instance);
        bool runActive = RunFlow.RunActive, transitioning = RunFlow.IsTransitioning;
        bool local = (bool)localTransition.GetValue(menu);
        int orientation = PlayerPrefs.GetInt(GameplayPreferences.OrientationKey, 0);
        int inertia = PlayerPrefs.GetInt(GameplayPreferences.InertiaKey, 1);
        int checks = 0;
        try
        {
            // Active, paused and defeated attempts all keep RunActive until the
            // player explicitly returns to the start screen.
            RunFlow.RunActive = true;
            RunFlow.IsTransitioning = false;
            localTransition.SetValue(menu, false);
            Require(!menu.CanSelectGameMode && !menu.SelectGameMode(true, !GameplayPreferences.UseInertia),
                "An active attempt cannot change mode."); checks++;
            RunFlow.RunActive = false;
            RunFlow.IsTransitioning = true;
            Require(!menu.CanSelectGameMode && !menu.SelectGameMode(true, !GameplayPreferences.UseInertia),
                "A scene transition cannot change mode."); checks++;
            RunFlow.IsTransitioning = false;
            localTransition.SetValue(menu, true);
            Require(!menu.CanSelectGameMode && !menu.SelectGameMode(true, !GameplayPreferences.UseInertia),
                "A pending local transition cannot be duplicated."); checks++;
            Require(PlayerPrefs.GetInt(GameplayPreferences.OrientationKey, 0) == orientation &&
                PlayerPrefs.GetInt(GameplayPreferences.InertiaKey, 1) == inertia,
                "Rejected requests do not write preferences."); checks++;
            Debug.Log($"MODE_RUNTIME_GUARDS_PASS: {checks} checks.");
        }
        finally
        {
            RunFlow.RunActive = runActive;
            RunFlow.IsTransitioning = transitioning;
            localTransition.SetValue(menu, local);
        }
    }

    private static void Restore(string key, bool existed, int value)
    {
        if (existed) PlayerPrefs.SetInt(key, value);
        else PlayerPrefs.DeleteKey(key);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Mode selection validation: " + message);
    }
}
