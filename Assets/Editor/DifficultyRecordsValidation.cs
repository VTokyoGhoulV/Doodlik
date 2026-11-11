using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// This destructive-fixture suite is intentionally restricted to the isolated
// difficulty QA namespace. It snapshots/restores even that disposable state.
public static class DifficultyRecordsValidation
{
    private static int checks;

    public static int Validate()
    {
        if (!Application.isBatchMode || PlayerSettings.companyName != "CodexValidation" ||
            PlayerSettings.productName != "DoodlikDifficultySandbox" ||
            !Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-"))
            throw new InvalidOperationException("Difficulty records validation requires the isolated difficulty QA project and preferences namespace.");

        checks = 0;
        List<string> keys = new List<string>(RunRecords.AllKeys)
        {
            GameplayPreferences.DifficultyKey, GameplayPreferences.OrientationKey, GameplayPreferences.InertiaKey
        };
        Dictionary<string, int> original = new Dictionary<string, int>();
        foreach (string key in keys)
            if (PlayerPrefs.HasKey(key)) original[key] = PlayerPrefs.GetInt(key);
        bool hadWallet = PlayerPrefs.HasKey(PlayerProgress.SaveKey), hadBackup = PlayerPrefs.HasKey(PlayerProgress.BackupKey);
        string wallet = PlayerPrefs.GetString(PlayerProgress.SaveKey), backup = PlayerPrefs.GetString(PlayerProgress.BackupKey);
        try
        {
            ValidateRules();
            ValidateKeysAndPreferences();
            ValidateMigration();
            ValidateBestHeight();
            ValidateResets();
            Check(PlayerPrefs.HasKey(PlayerProgress.SaveKey) == hadWallet &&
                PlayerPrefs.HasKey(PlayerProgress.BackupKey) == hadBackup &&
                PlayerPrefs.GetString(PlayerProgress.SaveKey) == wallet &&
                PlayerPrefs.GetString(PlayerProgress.BackupKey) == backup,
                "Record migration and resets leave both wallet/cosmetic copies untouched.");
            Debug.Log($"DIFFICULTY_RECORDS_PASS checks={checks}");
            return checks;
        }
        finally
        {
            foreach (string key in keys)
            {
                if (original.TryGetValue(key, out int value)) PlayerPrefs.SetInt(key, value);
                else PlayerPrefs.DeleteKey(key);
            }
            PlayerPrefs.Save();
        }
    }

    private static void ValidateRules()
    {
        Check((int)GameDifficulty.Easy == 0 && (int)GameDifficulty.Normal == 1 && (int)GameDifficulty.Hard == 2,
            "Persisted difficulty values are stable.");
        Check(DifficultyRules.Sanitize((GameDifficulty)(-1)) == GameDifficulty.Normal &&
            DifficultyRules.Sanitize((GameDifficulty)int.MaxValue) == GameDifficulty.Normal,
            "Invalid difficulties use Normal.");
        Check(DifficultyRules.Name(GameDifficulty.Easy) == "Лёгкая" &&
            DifficultyRules.Name(GameDifficulty.Normal) == "Обычная" && DifficultyRules.Name(GameDifficulty.Hard) == "Сложная",
            "All preset names are localized.");
        Check(DifficultyRules.MultiplierLabel(GameDifficulty.Easy) == "×0,85" &&
            DifficultyRules.MultiplierLabel(GameDifficulty.Normal) == "×1" &&
            DifficultyRules.MultiplierLabel(GameDifficulty.Hard) == "×1,25", "Multiplier labels match the rules.");
        foreach (GameDifficulty difficulty in new[] { GameDifficulty.Easy, GameDifficulty.Normal, GameDifficulty.Hard })
        {
            Check(DifficultyRules.ScoreForHeight(0, difficulty) == 0 && DifficultyRules.ScoreForHeight(int.MinValue, difficulty) == 0,
                "Nonpositive height cannot award points.");
            int previous = 0;
            for (int height = 1; height <= 10000; height++)
            {
                int score = DifficultyRules.ScoreForHeight(height, difficulty);
                Check(score >= previous, "Increasing height never decreases points.");
                previous = score;
            }
        }
        Check(DifficultyRules.ScoreForHeight(1, GameDifficulty.Easy) == 0 &&
            DifficultyRules.ScoreForHeight(4, GameDifficulty.Easy) == 3 &&
            DifficultyRules.ScoreForHeight(4, GameDifficulty.Hard) == 5, "Fractional points use floor, not rounding.");
        Check(DifficultyRules.ScoreForHeight(5000, GameDifficulty.Easy) == 4250 &&
            DifficultyRules.ScoreForHeight(5000, GameDifficulty.Normal) == 5000 &&
            DifficultyRules.ScoreForHeight(5000, GameDifficulty.Hard) == 6250, "All multipliers award the specified points.");
        Check(DifficultyRules.ScoreForHeight(int.MaxValue, GameDifficulty.Easy) == 1825361099 &&
            DifficultyRules.ScoreForHeight(int.MaxValue, GameDifficulty.Normal) == int.MaxValue &&
            DifficultyRules.ScoreForHeight(int.MaxValue, GameDifficulty.Hard) == int.MaxValue,
            "Full-range integer scoring never overflows.");
        Check(DifficultyRules.ScoreForHeight(123, (GameDifficulty)99) == 123, "Invalid rules use Normal scoring.");
    }

    private static void ValidateKeysAndPreferences()
    {
        HashSet<string> unique = new HashSet<string>(RunRecords.AllKeys);
        Check(unique.Count == 25 && RunRecords.AllKeys.Count == 25 && unique.Contains(Player.HighScoreKey),
            "There are 12 score keys, 12 height keys, and the legacy key without collisions.");
        for (int mode = 0; mode < 4; mode++)
        {
            bool portrait = mode >= 2, inertia = (mode & 1) != 0;
            Check(RunRecords.ScoreKey(portrait, inertia, GameDifficulty.Normal) == Player.ModeHighScoreKeys[mode],
                "Normal keys preserve shipped records.");
            foreach (GameDifficulty difficulty in new[] { GameDifficulty.Easy, GameDifficulty.Normal, GameDifficulty.Hard })
                Check(unique.Contains(RunRecords.ScoreKey(portrait, inertia, difficulty)) &&
                    unique.Contains(RunRecords.HeightKey(portrait, inertia, difficulty)), "Every preset exposes both resettable metrics.");
        }
        PlayerPrefs.DeleteKey(GameplayPreferences.DifficultyKey);
        Check(GameplayPreferences.Difficulty == GameDifficulty.Normal, "Fresh installations default to Normal.");
        PlayerPrefs.SetInt(GameplayPreferences.DifficultyKey, -10);
        Check(GameplayPreferences.Difficulty == GameDifficulty.Normal, "Invalid saved difficulty reads as Normal.");
        GameplayPreferences.SetDifficulty((GameDifficulty)99);
        Check(PlayerPrefs.GetInt(GameplayPreferences.DifficultyKey) == (int)GameDifficulty.Normal,
            "Invalid difficulty writes are sanitized.");
    }

    private static void ValidateMigration()
    {
        ClearRecords();
        GameplayPreferences.SetDifficulty(GameDifficulty.Hard);
        PlayerPrefs.SetInt(Player.HighScoreKey, 4200);
        string normalScore = RunRecords.ScoreKey(false, true, GameDifficulty.Normal);
        string normalHeight = RunRecords.HeightKey(false, true, GameDifficulty.Normal);
        string otherScore = RunRecords.ScoreKey(true, false, GameDifficulty.Normal);
        string otherHeight = RunRecords.HeightKey(true, false, GameDifficulty.Normal);
        PlayerPrefs.SetInt(otherScore, 6000);
        RunRecords.MigrateLegacy(false, true);
        Check(PlayerPrefs.GetInt(normalScore) == 4200 && PlayerPrefs.GetInt(normalHeight) == 4200 &&
            !PlayerPrefs.HasKey(Player.HighScoreKey) && !PlayerPrefs.HasKey(RunRecords.ScoreKey(false, true, GameDifficulty.Hard)),
            "A legacy record always migrates into Normal score and height, regardless of selection.");
        Check(PlayerPrefs.GetInt(otherHeight) == 6000, "Other missing Normal heights are backfilled from their own records.");
        PlayerPrefs.SetInt(otherScore, 7000);
        RunRecords.MigrateLegacy(false, true);
        Check(PlayerPrefs.GetInt(otherHeight) == 6000, "Existing heights are not re-derived from score on repeated migration.");
        PlayerPrefs.SetInt(normalScore, 8000);
        PlayerPrefs.SetInt(normalHeight, 9000);
        PlayerPrefs.SetInt(Player.HighScoreKey, 7000);
        RunRecords.MigrateLegacy(false, true);
        Check(PlayerPrefs.GetInt(normalScore) == 8000 && PlayerPrefs.GetInt(normalHeight) == 9000,
            "Legacy migration never reduces either existing metric.");
        PlayerPrefs.SetInt(Player.HighScoreKey, 10000);
        RunRecords.MigrateLegacy(false, true);
        Check(PlayerPrefs.GetInt(normalScore) == 10000 && PlayerPrefs.GetInt(normalHeight) == 10000,
            "A larger legacy height merges into both Normal metrics.");
    }

    private static void ValidateBestHeight()
    {
        ClearRecords();
        PlayerPrefs.SetInt(RunRecords.ScoreKey(false, false, GameDifficulty.Easy), int.MaxValue);
        PlayerPrefs.SetInt(RunRecords.ScoreKey(false, false, GameDifficulty.Hard), int.MaxValue);
        Check(RunRecords.BestHeight == 0, "Multiplied Easy/Hard points cannot grant height achievements.");
        PlayerPrefs.SetInt(RunRecords.ScoreKey(true, true, GameDifficulty.Normal), 5000);
        Check(RunRecords.BestHeight == 5000, "Unmigrated Normal points remain a safe raw-height fallback.");
        RunRecords.MigrateLegacy(false, false);
        Check(!PlayerPrefs.HasKey(RunRecords.HeightKey(false, false, GameDifficulty.Easy)) &&
            !PlayerPrefs.HasKey(RunRecords.HeightKey(false, false, GameDifficulty.Hard)), "Migration does not invent multiplied-preset heights.");
        PlayerPrefs.SetInt(RunRecords.HeightKey(false, false, GameDifficulty.Easy), 7000);
        PlayerPrefs.SetInt(RunRecords.HeightKey(true, false, GameDifficulty.Hard), 8000);
        Check(RunRecords.BestHeight == 8000, "Best height spans every independent difficulty and mode.");
        PlayerPrefs.SetInt(Player.HighScoreKey, 9000);
        Check(RunRecords.BestHeight == 9000, "The unmigrated old single record also represents raw height.");
    }

    private static void ValidateResets()
    {
        ClearRecords();
        PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, 0);
        PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, 0);
        GameplayPreferences.SetDifficulty(GameDifficulty.Hard);
        string hardScore = RunRecords.ScoreKey(false, false, GameDifficulty.Hard);
        string hardHeight = RunRecords.HeightKey(false, false, GameDifficulty.Hard);
        string normalScore = RunRecords.ScoreKey(false, false, GameDifficulty.Normal);
        string normalHeight = RunRecords.HeightKey(false, false, GameDifficulty.Normal);
        PlayerPrefs.SetInt(hardScore, 5000);
        PlayerPrefs.SetInt(hardHeight, 4000);
        PlayerPrefs.SetInt(Player.HighScoreKey, 1234);
        RunRecords.ResetCurrent();
        RunRecords.MigrateLegacy(false, false);
        Check(!PlayerPrefs.HasKey(hardScore) && !PlayerPrefs.HasKey(hardHeight) &&
            PlayerPrefs.GetInt(normalScore) == 1234 && PlayerPrefs.GetInt(normalHeight) == 1234,
            "Resetting Hard deletes its pair but preserves legacy Normal history.");
        GameplayPreferences.SetDifficulty(GameDifficulty.Normal);
        PlayerPrefs.SetInt(Player.HighScoreKey, 7000);
        RunRecords.ResetCurrent();
        RunRecords.MigrateLegacy(false, false);
        Check(!PlayerPrefs.HasKey(normalScore) && !PlayerPrefs.HasKey(normalHeight) && !PlayerPrefs.HasKey(Player.HighScoreKey),
            "Resetting Normal cannot resurrect either metric from the legacy key.");
        foreach (string key in RunRecords.AllKeys) PlayerPrefs.SetInt(key, 111);
        RunRecords.ResetAll();
        RunRecords.MigrateLegacy(false, false);
        foreach (string key in RunRecords.AllKeys) Check(!PlayerPrefs.HasKey(key), "Reset all removes all record keys without resurrection.");
        Check(GameplayPreferences.Difficulty == GameDifficulty.Normal && !GameplayPreferences.UseInertia,
            "Resetting records preserves selected gameplay preferences.");
    }

    private static void ClearRecords()
    {
        foreach (string key in RunRecords.AllKeys) PlayerPrefs.DeleteKey(key);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Difficulty records validation: " + message);
        checks++;
    }
}
