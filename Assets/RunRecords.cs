using System;
using System.Collections.Generic;
using UnityEngine;

// Records belong to a format/control/difficulty combination. Currency and
// cosmetics deliberately remain outside this key set and its reset operations.
public static class RunRecords
{
    private static readonly string[] scoreKeys = BuildScoreKeys();
    private static readonly string[] heightKeys = BuildHeightKeys();
    public static IReadOnlyList<string> AllKeys { get; } = Array.AsReadOnly(BuildAllKeys());
    public static IReadOnlyList<string> AllExpeditionKeys { get; } = Array.AsReadOnly(BuildExpeditionKeys());

    public static string ScoreKey(bool portrait, bool inertia, GameDifficulty difficulty) =>
        scoreKeys[Index(portrait, inertia, difficulty)];

    public static string HeightKey(bool portrait, bool inertia, GameDifficulty difficulty) =>
        heightKeys[Index(portrait, inertia, difficulty)];

    public static string ScoreKey(bool portrait, bool inertia, GameDifficulty difficulty, GameRunKind kind) =>
        VariantKey(ScoreKey(portrait, inertia, difficulty), kind);

    public static string HeightKey(bool portrait, bool inertia, GameDifficulty difficulty, GameRunKind kind) =>
        VariantKey(HeightKey(portrait, inertia, difficulty), kind);

    public static string CurrentScoreKey => ScoreKey(GameplayPreferences.IsPortrait,
        GameplayPreferences.UseInertia, GameplayPreferences.Difficulty, GameplayPreferences.RunKind);

    public static string CurrentHeightKey => HeightKey(GameplayPreferences.IsPortrait,
        GameplayPreferences.UseInertia, GameplayPreferences.Difficulty, GameplayPreferences.RunKind);

    public static int BestHeight
    {
        get
        {
            // Before migration, the old single record is also an unmultiplied
            // Normal height, independent of the currently selected difficulty.
            int best = ReadNonnegative(Player.HighScoreKey);
            for (int mode = 0; mode < 4; mode++)
            for (int difficulty = 0; difficulty < 3; difficulty++)
            {
                int index = mode * 3 + difficulty;
                int height = ReadNonnegative(heightKeys[index]);
                if (difficulty == (int)GameDifficulty.Normal && !PlayerPrefs.HasKey(heightKeys[index]))
                    height = ReadNonnegative(scoreKeys[index]);
                best = Mathf.Max(best, height);
                best = Mathf.Max(best, ReadNonnegative(VariantKey(heightKeys[index], GameRunKind.Expedition)));
            }
            return best;
        }
    }

    public static void MigrateLegacy(bool portrait, bool inertia)
    {
        bool hasLegacy = PlayerPrefs.HasKey(Player.HighScoreKey);
        bool changed = false;
        if (hasLegacy)
        {
            string normalKey = ScoreKey(portrait, inertia, GameDifficulty.Normal);
            int legacy = ReadNonnegative(Player.HighScoreKey);
            PlayerPrefs.SetInt(normalKey, Mathf.Max(ReadNonnegative(normalKey), legacy));
            string normalHeight = HeightKey(portrait, inertia, GameDifficulty.Normal);
            if (PlayerPrefs.HasKey(normalHeight))
                PlayerPrefs.SetInt(normalHeight, Mathf.Max(ReadNonnegative(normalHeight), legacy));
            changed = true;
        }

        for (int mode = 0; mode < 4; mode++)
        {
            int normal = mode * 3 + (int)GameDifficulty.Normal;
            if (!PlayerPrefs.HasKey(scoreKeys[normal]) || PlayerPrefs.HasKey(heightKeys[normal])) continue;
            PlayerPrefs.SetInt(heightKeys[normal], ReadNonnegative(scoreKeys[normal]));
            changed = true;
        }

        // Persist the destination before retiring the only legacy copy. If the
        // write fails, it can be retried without losing or multiplying records.
        if (changed) PlayerPrefs.Save();
        if (!hasLegacy) return;
        PlayerPrefs.DeleteKey(Player.HighScoreKey);
        PlayerPrefs.Save();
    }

    public static void ResetCurrent()
    {
        // First place an unassigned legacy record in its Normal slot. Resetting
        // Easy/Hard must preserve that history; resetting Normal must remove it.
        if (GameplayPreferences.RunKind == GameRunKind.Classic)
            MigrateLegacy(GameplayPreferences.IsPortrait, GameplayPreferences.UseInertia);
        PlayerPrefs.DeleteKey(CurrentScoreKey);
        PlayerPrefs.DeleteKey(CurrentHeightKey);
        PlayerPrefs.Save();
    }

    public static void ResetAll()
    {
        foreach (string key in AllKeys) PlayerPrefs.DeleteKey(key);
        foreach (string key in AllExpeditionKeys) PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
    }

    private static string VariantKey(string classicKey, GameRunKind kind) =>
        kind == GameRunKind.Expedition ? "DoodlikExpeditionRecord_" + classicKey : classicKey;

    private static string[] BuildExpeditionKeys()
    {
        string[] keys = new string[24];
        for (int i = 0; i < scoreKeys.Length; i++)
        {
            keys[i] = VariantKey(scoreKeys[i], GameRunKind.Expedition);
            keys[i + scoreKeys.Length] = VariantKey(heightKeys[i], GameRunKind.Expedition);
        }
        return keys;
    }

    private static int ReadNonnegative(string key) => Mathf.Max(0, PlayerPrefs.GetInt(key, 0));

    private static int Index(bool portrait, bool inertia, GameDifficulty difficulty) =>
        ((portrait ? 2 : 0) + (inertia ? 1 : 0)) * 3 + (int)DifficultyRules.Sanitize(difficulty);

    private static string[] BuildScoreKeys()
    {
        string[] keys = new string[12];
        for (int mode = 0; mode < 4; mode++)
        {
            // Normal retains the shipped names, including records created
            // before difficulty or independent height metrics existed.
            string normal = Player.ModeHighScoreKeys[mode];
            keys[mode * 3 + (int)GameDifficulty.Easy] = normal + "_Easy";
            keys[mode * 3 + (int)GameDifficulty.Normal] = normal;
            keys[mode * 3 + (int)GameDifficulty.Hard] = normal + "_Hard";
        }
        return keys;
    }

    private static string[] BuildHeightKeys()
    {
        string[] keys = new string[12];
        string[] modes = { "Landscape_Precise", "Landscape_Inertia", "Portrait_Precise", "Portrait_Inertia" };
        string[] difficulties = { "Easy", "Normal", "Hard" };
        for (int mode = 0; mode < modes.Length; mode++)
        for (int difficulty = 0; difficulty < difficulties.Length; difficulty++)
            keys[mode * 3 + difficulty] = "DoodlikBestHeight_" + modes[mode] + "_" + difficulties[difficulty];
        return keys;
    }

    private static string[] BuildAllKeys()
    {
        string[] keys = new string[25];
        Array.Copy(scoreKeys, 0, keys, 0, scoreKeys.Length);
        Array.Copy(heightKeys, 0, keys, scoreKeys.Length, heightKeys.Length);
        keys[keys.Length - 1] = Player.HighScoreKey;
        return keys;
    }
}
