#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Journal tests use disposable PlayerProgress keys, never the real wallet,
// records or saved preferences. No scene, prefab or build is created.
public static class RunStatisticsValidation
{
    private static int checks;

    [Serializable] private sealed class Envelope
    {
        public int version;
        public long revision;
        public string payload, checksum;
    }

    [Serializable] private class LegacyV6
    {
        public int coins = 123;
        public List<string> owned = new List<string> { "cat_rose", "accessory_glasses", "future_cosmetic_keep" };
        public string cat = "cat_rose", clothing = "clothing_original", accessory = "accessory_glasses", trail = "trail_none";
        public List<string> accessories = new List<string> { "accessory_glasses" };
        public List<UpgradeData> upgrades = new List<UpgradeData> { new UpgradeData { mode = "portrait_precise", magnet = 2, duration = 1 } };
        public List<string> redeemedCodes = new List<string> { "keep-redeemed" };
        public string selectedChallenge = "run_wide10";
        public List<string> completedChallenges = new List<string> { "run_coin10", "future_challenge_keep" };
        public int centeredLandings = 17, fallingRescues = 1, preciseChallengeHeight = 950, inertiaChallengeHeight = 600;
        public int hardCleanChallengeHeight = 350, hardChallengeHeight = 1100;
        public int challengeCoinsCollected = 51, challengeBushesUsed = 7, challengeMovingLandings = 11, challengeNarrowLandings = 9;
    }

    [Serializable] private sealed class UpgradeData { public string mode; public int magnet, duration; }
    [Serializable] private sealed class Fixture : LegacyV6 { public StatsData statistics = new StatsData(); }
    [Serializable] private sealed class StatsData
    {
        public int runs, bestRunHeight, bestRunScore, bestRunCoins;
        public long totalCoins, totalBushes, totalMovingLandings, totalNarrowLandings, totalWideJumps;
        public double activeSeconds;
        public string lastRunId;
        public List<EntryData> recentRuns = new List<EntryData>();
    }

    [Serializable] private sealed class EntryData
    {
        // Match the production DTO: Unity inline serialization can replace a
        // null class element with a default instance. It must not gain an ID.
        public string id, endReason = "Поражение";
        public bool portrait, inertia;
        public GameDifficulty difficulty = GameDifficulty.Normal;
        public int height, score, coins, bushes, movingLandings, narrowLandings, wideJumps;
        public double activeSeconds;
    }

    // One-shot evidence for the original fixture failure, not user save data.
    [Serializable] private sealed class PreviousNullEntryProbe { public string id = "entry"; }
    [Serializable] private sealed class PreviousNullListProbe
    {
        public List<PreviousNullEntryProbe> recentRuns = new List<PreviousNullEntryProbe> { null };
    }

    public static void RunAll()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/"))
            throw new InvalidOperationException("Run journal validation requires an isolated batch-mode QA project.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikRunStatisticsSandbox";
        EditorUtility.audioMasterMute = true;
        AudioListener.volume = 0f;
        AudioListener.pause = true;
        Validate();
        ChallengeProgressValidation.Validate();
        CosmeticsProgressValidation.Validate();
        CosmeticMigrationValidation.Validate();
        Debug.Log("RUN_STATISTICS_AND_MIGRATION_PASS: isolated journal domain, v1-v6 migration, challenge/cosmetics regression.");
    }

    public static int Validate()
    {
        checks = 0;
        using (PlayerProgress.UseIsolatedStorageForValidation(out string primary, out string backup))
        {
            NewProfileAndRoundTrip(primary, backup);
            RecentHistoryAndDeduplication(primary, backup);
            Migration(primary, backup);
            ValidationAndBounds(primary, backup);
            FutureAndAdminProtection(primary, backup);
        }
        Debug.Log($"RUN_STATISTICS_PASS checks={checks} isolatedStorage=True schema=9 historyLimit=5");
        return checks;
    }

    private static RunStatisticsEntry Entry(string id, int height = 100, int score = 120, int coins = 3,
        int bushes = 2, int moving = 4, int narrow = 5, int wide = 6, double seconds = 12.5,
        bool portrait = false, bool inertia = false, GameDifficulty difficulty = GameDifficulty.Normal,
        string reason = "Поражение") => new RunStatisticsEntry(id, portrait, inertia, difficulty,
            height, score, coins, bushes, moving, narrow, wide, seconds, reason);

    private static void NewProfileAndRoundTrip(string primary, string backup)
    {
        Reset(primary, backup);
        CareerStatisticsSnapshot empty = PlayerProgress.Statistics;
        Check(empty.Runs == 0 && empty.RecentRuns.Count == 0 && empty.ActiveSeconds == 0 &&
            empty.TotalCoins == 0 && empty.TotalBushes == 0 && empty.TotalMovingLandings == 0 &&
            empty.TotalNarrowLandings == 0 && empty.TotalWideJumps == 0 && empty.BestRunHeight == 0 &&
            empty.BestRunScore == 0 && empty.BestRunCoins == 0, "New journal is genuinely empty.");
        PlayerProgress.AddCoins(97);
        int changed = 0;
        Action handler = () => changed++;
        PlayerProgress.Changed += handler;
        try
        {
            Check(PlayerProgress.RecordRun(Entry("run_first", portrait: true, inertia: true, difficulty: GameDifficulty.Hard)),
                "A valid finalized run is recorded.");
            Check(changed == 1, "One journal transaction sends one notification.");
            CareerStatisticsSnapshot stats = PlayerProgress.Statistics;
            Check(stats.Runs == 1 && stats.TotalCoins == 3 && stats.TotalBushes == 2 && stats.TotalMovingLandings == 4 &&
                stats.TotalNarrowLandings == 5 && stats.TotalWideJumps == 6 && stats.ActiveSeconds == 12.5 &&
                stats.BestRunHeight == 100 && stats.BestRunScore == 120 && stats.BestRunCoins == 3,
                "Every career total and best value comes from the finalized attempt.");
            RunStatisticsEntry entry = stats.RecentRuns[0];
            Check(entry.Id == "run_first" && entry.Portrait && entry.Inertia && entry.Difficulty == GameDifficulty.Hard &&
                entry.Height == 100 && entry.Score == 120 && entry.Coins == 3 && entry.Bushes == 2 &&
                entry.MovingLandings == 4 && entry.NarrowLandings == 5 && entry.WideJumps == 6 &&
                entry.ActiveSeconds == 12.5 && entry.EndReason == "Поражение", "Journal entry preserves all mode and gameplay fields.");
            Check(empty.Runs == 0 && empty.RecentRuns.Count == 0, "Previously returned snapshots remain immutable after writes.");
            bool readOnly = false;
            try { ((IList<RunStatisticsEntry>)stats.RecentRuns).Add(Entry("mutate")); }
            catch (NotSupportedException) { readOnly = true; }
            Check(readOnly && PlayerProgress.Statistics.RecentRuns.Count == 1, "Public history cannot mutate the saved list.");
            Check(PlayerProgress.Coins == 97 && !PlayerProgress.IsChallengeCompleted("run_coin10") &&
                PlayerProgress.ChallengeProgressValue("mastery_coins250") == 0 &&
                PlayerProgress.ChallengeProgressValue("mastery_bush25") == 0,
                "Journal counters never mint currency or replay challenge events.");
            Check(Read(primary).version == 9 && PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup),
                "Journal, totals and deduplication ID commit to both current recovery copies.");
            PlayerProgress.Reload();
            CareerStatisticsSnapshot reloaded = PlayerProgress.Statistics;
            Check(reloaded.Runs == 1 && reloaded.RecentRuns[0].Id == "run_first" && reloaded.TotalNarrowLandings == 5 &&
                reloaded.ActiveSeconds == 12.5 && reloaded.RecentRuns[0].Portrait && reloaded.RecentRuns[0].Inertia &&
                reloaded.RecentRuns[0].Difficulty == GameDifficulty.Hard && PlayerProgress.Coins == 97,
                "Mode fields, lifetime totals and wallet round-trip without change.");
        }
        finally { PlayerProgress.Changed -= handler; }
    }

    private static void RecentHistoryAndDeduplication(string primary, string backup)
    {
        Reset(primary, backup);
        double seconds = 0;
        for (int i = 1; i <= 8; i++)
        {
            Check(PlayerProgress.RecordRun(Entry("history_" + i, height: i * 100, score: 900 - i * 10,
                coins: i, seconds: i, portrait: i % 2 == 0, inertia: i % 3 == 0,
                difficulty: (GameDifficulty)(i % 3))), "Distinct attempt accepted: " + i);
            seconds += i;
        }
        CareerStatisticsSnapshot stats = PlayerProgress.Statistics;
        Check(stats.Runs == 8 && stats.RecentRuns.Count == 5 && stats.TotalCoins == 36 && stats.TotalBushes == 16 &&
            stats.TotalMovingLandings == 32 && stats.TotalNarrowLandings == 40 && stats.TotalWideJumps == 48 &&
            stats.ActiveSeconds == seconds && stats.BestRunHeight == 800 && stats.BestRunScore == 890 && stats.BestRunCoins == 8,
            "Dropping old history entries never drops lifetime totals or old best values.");
        for (int i = 0; i < 5; i++) Check(stats.RecentRuns[i].Id == "history_" + (8 - i), "History is newest first.");
        string durable = PlayerPrefs.GetString(primary);
        Check(!PlayerProgress.RecordRun(Entry("history_8")) && !PlayerProgress.RecordRun(Entry("history_4")) &&
            PlayerPrefs.GetString(primary) == durable && PlayerProgress.Statistics.Runs == 8,
            "Last and older recent IDs are both idempotent without writing.");
        PlayerProgress.Reload();
        Check(!PlayerProgress.RecordRun(Entry("history_7")) && PlayerProgress.Statistics.Runs == 8,
            "Deduplication survives reload.");
        PlayerPrefs.SetString(primary, "{broken");
        PlayerProgress.Reload();
        Check(PlayerProgress.Statistics.Runs == 8 && PlayerProgress.Statistics.RecentRuns[0].Id == "history_8" &&
            !PlayerProgress.RecordRun(Entry("history_8")) && PlayerPrefs.GetString(primary) == durable,
            "Recovery preserves totals, latest entry and its no-duplicate guarantee together.");
        Check(stats.RecentRuns[0].Id == "history_8" && stats.Runs == 8, "Older snapshots do not alias mutable save data.");
    }

    private static void Migration(string primary, string backup)
    {
        string legacy = Encode(6, new LegacyV6());
        Check(!legacy.Contains("statistics"), "The real v6 fixture has no statistics fields.");
        Put(primary, backup, legacy, legacy);
        Check(PlayerProgress.Coins == 123 && PlayerProgress.EquippedId(PlayerProgress.Category.Cat) == "cat_rose" &&
            PlayerProgress.IsEquipped("accessory_glasses") && PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Magnet, "portrait_precise") == 2 &&
            PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Duration, "portrait_precise") == 1,
            "v6 wallet, outfit and per-mode upgrades survive current migration.");
        Check(PlayerProgress.IsChallengeCompleted("run_coin10") && PlayerProgress.SelectedChallengeId == "run_wide10" &&
            PlayerProgress.ChallengeProgressValue("mastery_center50") == 17 && PlayerProgress.ChallengeProgressValue("mastery_rescue3") == 1 &&
            PlayerProgress.ChallengeProgressValue("mastery_coins250") == 51 && PlayerProgress.ChallengeProgressValue("mastery_bush25") == 7 &&
            PlayerProgress.ChallengeProgressValue("mastery_moving50") == 11 && PlayerProgress.ChallengeProgressValue("mastery_narrow75") == 9,
            "v6 completed trials, selection and all existing career event counters survive.");
        Envelope current = Read(primary);
        Check(current.version == 9 && current.revision == 13 && current.payload.Contains("future_cosmetic_keep") &&
            current.payload.Contains("future_challenge_keep") && current.payload.Contains("keep-redeemed") &&
            PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup), "Migration retains unknown IDs and code proofs in both durable copies.");
        Check(PlayerProgress.Statistics.Runs == 0 && PlayerProgress.Statistics.TotalCoins == 0 &&
            PlayerProgress.Statistics.RecentRuns.Count == 0, "Old balance and event counts never fabricate historic finished runs.");
        string once = PlayerPrefs.GetString(primary);
        PlayerProgress.Reload();
        Check(PlayerPrefs.GetString(primary) == once && PlayerProgress.Coins == 123, "Current load does not remigrate, grant or rewrite.");
        PlayerPrefs.SetString(primary, "broken");
        PlayerProgress.Reload();
        Check(PlayerProgress.Coins == 123 && PlayerProgress.Statistics.Runs == 0 && PlayerPrefs.GetString(primary) == once,
            "Migrated current backup preserves old progression and the empty journal.");
        for (int version = 1; version <= 6; version++)
        {
            Fixture fixture = new Fixture();
            fixture.statistics.runs = 999;
            fixture.statistics.totalCoins = 999;
            fixture.statistics.recentRuns.Add(new EntryData());
            Put(primary, backup, Encode(version, fixture), null);
            Check(PlayerProgress.Statistics.Runs == 0 && PlayerProgress.Statistics.TotalCoins == 0 &&
                PlayerProgress.Statistics.RecentRuns.Count == 0 && Read(primary).version == 9,
                "Version " + version + " starts statistics empty even if unrelated old fields resemble a journal.");
        }
    }

    private static void ValidationAndBounds(string primary, string backup)
    {
        Reset(primary, backup);
        PlayerProgress.AddCoins(42);
        string durable = PlayerPrefs.GetString(primary);
        int notifications = 0;
        Action handler = () => notifications++;
        PlayerProgress.Changed += handler;
        try
        {
            Check(!PlayerProgress.RecordRun(null), "Null entry is rejected.");
            foreach (string id in new[] { null, "", " ", "bad/id", "x:y", "котик", new string('a', 65) })
                Check(!PlayerProgress.RecordRun(Entry(id)), "Unsafe or oversized identifier is rejected.");
            foreach (double seconds in new[] { -1d, double.NaN, double.NegativeInfinity, double.PositiveInfinity })
                Check(!PlayerProgress.RecordRun(Entry("bad_seconds", seconds: seconds)), "Negative or non-finite duration is rejected.");
            Check(!PlayerProgress.RecordRun(Entry("bad_difficulty", difficulty: (GameDifficulty)9)), "Unknown difficulty is rejected.");
            for (int index = 0; index < 7; index++)
            {
                int[] numbers = { 1, 1, 1, 1, 1, 1, 1 };
                numbers[index] = -1;
                Check(!PlayerProgress.RecordRun(new RunStatisticsEntry("negative_" + index, false, false, GameDifficulty.Easy,
                    numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5], numbers[6], 1, "Поражение")),
                    "Negative per-run field is rejected: " + index);
            }
            Check(notifications == 0 && PlayerProgress.Statistics.Runs == 0 && PlayerPrefs.GetString(primary) == durable &&
                PlayerProgress.Coins == 42, "Rejected input does not mutate, notify or write any progression.");
        }
        finally { PlayerProgress.Changed -= handler; }
        Check(PlayerProgress.RecordRun(Entry(new string('a', 64), height: 0, score: 0, coins: 0, bushes: 0,
            moving: 0, narrow: 0, wide: 0, seconds: 0, reason: "  Выход\nиз меню " + new string('x', 100))),
            "Zero-valued aborted attempt and boundary-length ID are valid.");
        string reason = PlayerProgress.Statistics.RecentRuns[0].EndReason;
        Check(reason.Length == 80 && !reason.Contains("\n") && reason.StartsWith("Выход"), "Reasons are bounded, trimmed and control-character safe.");
        Check(PlayerProgress.RecordRun(Entry("blank_reason", reason: null)) &&
            PlayerProgress.Statistics.RecentRuns[0].EndReason == "Завершён", "Missing reason has a safe visible fallback.");

        Debug.Log("RUN_STATISTICS_NULL_SERIALIZATION previousFixture=" + JsonUtility.ToJson(new PreviousNullListProbe()));

        Fixture malformed = new Fixture { statistics = new StatsData { runs = -1, totalCoins = -1, totalBushes = -1,
            totalMovingLandings = -1, totalNarrowLandings = -1, totalWideJumps = -1, activeSeconds = -1,
            bestRunHeight = -1, bestRunScore = -1, bestRunCoins = -1, lastRunId = "bad/id" } };
        malformed.statistics.recentRuns.Add(null);
        malformed.statistics.recentRuns.Add(new EntryData { id = "bad/id" });
        malformed.statistics.recentRuns.Add(new EntryData { id = "bad_amount", coins = -1 });
        for (int i = 0; i < 8; i++) malformed.statistics.recentRuns.Add(new EntryData { id = "keep_" + i });
        malformed.statistics.recentRuns.Insert(5, new EntryData { id = "keep_0" });
        string malformedJson = Encode(8, malformed);
        Fixture encodedFixture = JsonUtility.FromJson<Fixture>(JsonUtility.FromJson<Envelope>(malformedJson).payload);
        Check(encodedFixture.statistics.recentRuns[0] == null || string.IsNullOrEmpty(encodedFixture.statistics.recentRuns[0].id),
            "A null fixture history element remains ID-less through the real Unity serializer.");
        Put(primary, backup, malformedJson, null);
        CareerStatisticsSnapshot repaired = PlayerProgress.Statistics;
        Check(repaired.Runs == 0 && repaired.TotalCoins == 0 && repaired.TotalBushes == 0 && repaired.TotalMovingLandings == 0 &&
            repaired.TotalNarrowLandings == 0 && repaired.TotalWideJumps == 0 && repaired.ActiveSeconds == 0 &&
            repaired.BestRunHeight == 0 && repaired.BestRunScore == 0 && repaired.BestRunCoins == 0,
            "Malformed negative aggregate fields normalize without integer wraparound.");
        Check(repaired.RecentRuns.Count == 5 && repaired.RecentRuns[0].Id == "keep_0" && repaired.RecentRuns[4].Id == "keep_4",
            "Malformed, duplicate and excess history entries are excluded, preserving newest valid entries. Actual=" +
            string.Join(",", new List<RunStatisticsEntry>(repaired.RecentRuns).ConvertAll(entry => entry.Id)));
        Fixture maximum = new Fixture { statistics = new StatsData { runs = int.MaxValue, totalCoins = long.MaxValue,
            totalBushes = long.MaxValue, totalMovingLandings = long.MaxValue, totalNarrowLandings = long.MaxValue,
            totalWideJumps = long.MaxValue, activeSeconds = double.MaxValue,
            bestRunHeight = int.MaxValue, bestRunScore = int.MaxValue, bestRunCoins = int.MaxValue } };
        Put(primary, backup, Encode(8, maximum), null);
        Check(PlayerProgress.RecordRun(Entry("overflow", seconds: double.MaxValue)), "Finite maximum input remains recordable.");
        CareerStatisticsSnapshot saturated = PlayerProgress.Statistics;
        Check(saturated.Runs == int.MaxValue && saturated.TotalCoins == long.MaxValue && saturated.TotalBushes == long.MaxValue &&
            saturated.TotalMovingLandings == long.MaxValue && saturated.TotalNarrowLandings == long.MaxValue &&
            saturated.TotalWideJumps == long.MaxValue && saturated.ActiveSeconds == double.MaxValue &&
            saturated.BestRunHeight == int.MaxValue && saturated.BestRunScore == int.MaxValue && saturated.BestRunCoins == int.MaxValue,
            "Lifetime counters saturate instead of overflowing or becoming infinite.");
        PlayerProgress.Reload();
        Check(PlayerProgress.Statistics.TotalCoins == long.MaxValue && PlayerProgress.Statistics.ActiveSeconds == double.MaxValue &&
            !PlayerProgress.RecordRun(Entry("overflow")), "Saturated totals and duplicate protection round-trip.");
        Fixture missing = new Fixture { statistics = null };
        Put(primary, backup, Encode(8, missing), null);
        Check(PlayerProgress.Statistics.Runs == 0 && PlayerProgress.RecordRun(Entry("missing")), "Missing optional journal gets safe defaults.");
    }

    private static void FutureAndAdminProtection(string primary, string backup)
    {
        Reset(primary, backup);
        PlayerProgress.RecordRun(Entry("normal"));
        string normal = PlayerPrefs.GetString(primary);
        int coins = PlayerProgress.Coins;
        PlayerProgress.BeginAdminSandbox();
        try
        {
            Check(!PlayerProgress.RecordRun(Entry("admin")) && PlayerProgress.Statistics.Runs == 1 &&
                PlayerPrefs.GetString(primary) == normal && PlayerPrefs.GetString(backup) == normal,
                "Admin cannot mutate even the sandbox journal or either durable copy.");
        }
        finally { PlayerProgress.EndAdminSandbox(); }
        Check(PlayerProgress.Statistics.Runs == 1 && PlayerProgress.Coins == coins, "Leaving admin restores untouched ordinary statistics.");
        foreach (bool futureInPrimary in new[] { true, false })
        {
            string future = Encode(10, new Fixture { statistics = new StatsData { runs = 999, lastRunId = "future" } });
            Put(primary, backup, futureInPrimary ? future : normal, futureInPrimary ? normal : future);
            string beforePrimary = PlayerPrefs.GetString(primary), beforeBackup = PlayerPrefs.GetString(backup);
            Check(!PlayerProgress.RecordRun(Entry("future_blocked")) && PlayerProgress.Statistics.Runs == 1,
                "Future save in either copy blocks journal writes without corrupting the supported fallback.");
            PlayerProgress.Flush();
            PlayerProgress.Reload();
            Check(PlayerPrefs.GetString(primary) == beforePrimary && PlayerPrefs.GetString(backup) == beforeBackup &&
                PlayerProgress.Statistics.Runs == 1, "Future v10 bytes and current backup remain unchanged after reload/flush.");
        }
        Put(primary, backup, "broken", "broken");
        Check(PlayerProgress.Statistics.Runs == 0 && PlayerProgress.Statistics.RecentRuns.Count == 0 &&
            PlayerProgress.RecordRun(Entry("recover_empty")), "Both damaged copies produce safe writable defaults.");
    }

    private static void Reset(string primary, string backup) => Put(primary, backup, null, null);
    private static void Put(string primary, string backup, string first, string second)
    {
        PlayerProgress.EndAdminSandbox();
        if (first == null) PlayerPrefs.DeleteKey(primary); else PlayerPrefs.SetString(primary, first);
        if (second == null) PlayerPrefs.DeleteKey(backup); else PlayerPrefs.SetString(backup, second);
        PlayerProgress.Reload();
    }
    private static Envelope Read(string key) => JsonUtility.FromJson<Envelope>(PlayerPrefs.GetString(key));
    private static string Encode(int version, object fixture)
    {
        string payload = JsonUtility.ToJson(fixture);
        const long revision = 12;
        uint hash = 2166136261;
        unchecked
        {
            foreach (char c in payload) hash = (hash ^ c) * 16777619;
            hash = (hash ^ (uint)revision) * 16777619;
            hash = (hash ^ (uint)(revision >> 32)) * 16777619;
        }
        return JsonUtility.ToJson(new Envelope { version = version, revision = revision, payload = payload, checksum = hash.ToString("X8") });
    }
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("Run statistics: " + message);
    }
}
#endif
