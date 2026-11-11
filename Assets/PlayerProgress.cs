using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// Wallet and cosmetics are shared; utility upgrades belong to a specific mode.
// This save has its own keys and never changes records or gameplay preferences.
public static class PlayerProgress
{
    public const string SaveKey = "DoodlikProgress_v1";
    public const string BackupKey = "DoodlikProgress_v1_Backup";
    private const int CurrentVersion = 9;
    private const int MaximumCoins = 9999999;
    public static int AstronautHeight => WorldZones.StartScore(WorldZones.Zone.Moon);
    public static int AstronautScore => AstronautHeight; // Compatibility for existing tooling.
    private const string GhoulCodeDigest = "FE7471A498CDACE9D25B4FE22E9ED978EC35CF2D5B954A65BD4B4D6BC8873103";
    // A shareable developer code; keep the original digest valid for people
    // who already received it. Both variants grant the same one-time reward.
    private const string GhoulShareCodeDigest = "9FFF9627171B81BAED3677C85C27619B612E03050CEE92D8E4A5C56A124D784A";

    public enum Category { Cat = 0, Clothing = 1, Accessory = 2, Trail = 3 }
    public enum Upgrade { Magnet, Duration }

    public readonly struct Item
    {
        public readonly string Id, Name;
        public readonly Category Category;
        public readonly int Price;
        public readonly Color Color;

        public Item(string id, string name, Category category, int price, Color color)
        {
            Id = id;
            Name = name;
            Category = category;
            Price = price;
            Color = color;
        }
    }

    private static readonly Item[] Items =
    {
        new Item("cat_ginger", "Дудлик", Category.Cat, 0, Color.white),
        new Item("cat_cocoa", "Какао", Category.Cat, 120, new Color32(178, 150, 136, 255)),
        new Item("cat_rose", "Персик", Category.Cat, 160, new Color32(255, 175, 195, 255)),
        new Item("cat_calico", "Калико", Category.Cat, 250, Color.white),
        new Item("cat_siamese", "Сиамец", Category.Cat, 300, Color.white),
        new Item("cat_snow", "Снежок", Category.Cat, 280, Color.white),
        new Item("cat_tuxedo", "Фрак", Category.Cat, 360, Color.white),
        new Item("cat_ghoul", "Котик-гуль", Category.Cat, -1, Color.white),
        new Item("clothing_original", "Родная одёжка", Category.Clothing, 0, Color.white),
        new Item("clothing_ocean", "Морская одёжка", Category.Clothing, 80, new Color32(85, 161, 203, 255)),
        new Item("clothing_berry", "Ягодная одёжка", Category.Clothing, 100, new Color32(208, 86, 100, 255)),
        new Item("clothing_overalls", "Джинсовый комбинезон", Category.Clothing, 190, Color.white),
        new Item("clothing_starry", "Звёздная кофточка", Category.Clothing, 330, Color.white),
        new Item("clothing_raincoat", "Солнечный дождевик", Category.Clothing, 260, Color.white),
        new Item("clothing_hoodie", "Уютное худи", Category.Clothing, 310, Color.white),
        new Item("clothing_astronaut", "Белый скафандр", Category.Clothing, -1, Color.white),
        new Item("clothing_ghoul", "Тёмная одежда гуля", Category.Clothing, -1, Color.white),
        new Item("accessory_none", "Без аксессуара", Category.Accessory, 0, Color.clear),
        new Item("accessory_flower", "Цветочек", Category.Accessory, 70, new Color32(243, 158, 182, 255)),
        new Item("accessory_glasses", "Круглые очки", Category.Accessory, 90, new Color32(98, 66, 53, 255)),
        new Item("accessory_tufts", "Пушистые кисточки", Category.Accessory, 110, new Color32(145, 103, 75, 255)),
        new Item("accessory_star", "Звёздочка", Category.Accessory, 130, new Color32(255, 210, 85, 255)),
        new Item("accessory_headphones", "Наушники", Category.Accessory, 190, Color.white),
        new Item("accessory_bow", "Бантик", Category.Accessory, 145, Color.white),
        new Item("accessory_aviator", "Очки лётчика", Category.Accessory, 230, Color.white),
        new Item("accessory_coin_crown", "Корона смельчака", Category.Accessory, -1, Color.white),
        new Item("accessory_astronaut_helmet", "Шлем космонавта", Category.Accessory, -1, Color.white),
        new Item("accessory_ghoul_mask", "Маска гуля", Category.Accessory, -1, Color.white),
        new Item("accessory_ghoul_hair", "Причёска гуля", Category.Accessory, -1, Color.white),
        new Item("trail_none", "Без следа", Category.Trail, 0, Color.clear),
        new Item("trail_honey", "Медовая лента", Category.Trail, 100, new Color32(255, 185, 68, 220)),
        new Item("trail_petals", "Розовая лента", Category.Trail, 130, new Color32(226, 126, 173, 210)),
        new Item("trail_stardust", "Звёздная пыль", Category.Trail, 190, new Color32(255, 207, 95, 255)),
        new Item("trail_blossom", "Цветочный вихрь", Category.Trail, 220, new Color32(237, 164, 178, 255)),
        new Item("trail_fireflies", "Светлячки", Category.Trail, 280, new Color32(255, 218, 91, 255)),
        new Item("accessory_target", "Мишень на макушке", Category.Accessory, -1, Color.white),
        new Item("accessory_patch", "Боевой пластырь", Category.Accessory, -1, Color.white),
        new Item("trail_ninelives", "Девять жизней", Category.Trail, -1, Color.white),
        new Item("trail_meteor", "Метеоритный след", Category.Trail, -1, Color.white),
        new Item("clothing_explorer", "Дудлик-путешественник", Category.Clothing, -1, Color.white)
    };

    public static IReadOnlyList<Item> Catalog { get; } = Array.AsReadOnly(Items);
    public static event Action Changed;

    private static readonly string[] Modes =
    {
        "landscape_precise", "landscape_inertia", "portrait_precise", "portrait_inertia"
    };
    private static readonly int[] UpgradeCosts = { 60, 110, 180 };
    private static SaveData data;
    private static long revision;
    private static bool readOnlyFutureSave;
    private static string storageSaveKey = SaveKey;
    private static string storageBackupKey = BackupKey;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
    private sealed class AdminSandboxState
    {
        public readonly SaveData OriginalData;
        public readonly long OriginalRevision;
        public readonly bool OriginalReadOnly;

        public AdminSandboxState(SaveData originalData, long originalRevision, bool originalReadOnly)
        {
            OriginalData = originalData;
            OriginalRevision = originalRevision;
            OriginalReadOnly = originalReadOnly;
        }
    }
    private static AdminSandboxState adminSandbox;
#endif

    // The API is harmless in release players; no sandbox state or mutation code
    // is included unless this is the Editor, Development or explicit cheats build.
    public static bool AdminSandboxActive
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
            return adminSandbox != null;
#else
            return false;
#endif
        }
    }

    public static void BeginAdminSandbox()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
        if (adminSandbox != null) return;
        EnsureLoaded();
        SaveData sandbox = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(data));
        foreach (Item item in Items)
            if (!sandbox.owned.Contains(item.Id)) sandbox.owned.Add(item.Id);
        foreach (string mode in Modes)
        {
            ModeUpgrades upgrades = sandbox.upgrades.Find(entry => entry.mode == mode);
            if (upgrades == null)
            {
                upgrades = new ModeUpgrades { mode = mode };
                sandbox.upgrades.Add(upgrades);
            }
            upgrades.magnet = MaxUpgradeLevel(Upgrade.Magnet);
            upgrades.duration = MaxUpgradeLevel(Upgrade.Duration);
        }
        adminSandbox = new AdminSandboxState(data, revision, readOnlyFutureSave);
        data = sandbox;
        // A future-version normal save stays untouched and read-only on exit,
        // while the disposable copy can still be used to test the wardrobe.
        readOnlyFutureSave = false;
        Changed?.Invoke();
#endif
    }

    public static void EndAdminSandbox()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
        if (adminSandbox == null) return;
        AdminRuntime.ClearPowers();
        AdminRuntime.ResetTuning();
        AdminSandboxState original = adminSandbox;
        adminSandbox = null;
        data = original.OriginalData;
        revision = original.OriginalRevision;
        readOnlyFutureSave = original.OriginalReadOnly;
        Changed?.Invoke();
#endif
    }

    public static void AdjustAdminCoins(int signedDelta)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
        if (adminSandbox == null) return;
        int next = (int)Math.Max(0L, Math.Min(MaximumCoins, (long)data.coins + signedDelta));
        if (next == data.coins) return;
        data.coins = next;
        Changed?.Invoke();
#endif
    }

    // Utility levels are editable only in the disposable admin profile.
    // There is deliberately no Save call, even when a future save is loaded.
    public static bool SetAdminUpgradeLevel(Upgrade upgrade, string mode, int level)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
        if (adminSandbox == null || !IsMode(mode) ||
            (upgrade != Upgrade.Magnet && upgrade != Upgrade.Duration) ||
            level < 0 || level > MaxUpgradeLevel(upgrade)) return false;
        ModeUpgrades entry = GetMode(mode, true);
        if (upgrade == Upgrade.Magnet) entry.magnet = level;
        else entry.duration = level;
        Changed?.Invoke();
        return true;
#else
        return false;
#endif
    }

    [Serializable]
    private sealed class SaveEnvelope
    {
        public int version;
        public long revision;
        public string payload;
        public string checksum;
    }

    [Serializable]
    private sealed class SaveData
    {
        public int coins;
        public List<string> owned = new List<string>();
        public string cat = "cat_ginger";
        public string clothing = "clothing_original";
        // Kept only to import the one-accessory v1/v2 save format.
        public string accessory = "accessory_none";
        public List<string> accessories = new List<string>();
        public string trail = "trail_none";
        public List<ModeUpgrades> upgrades = new List<ModeUpgrades>();
        public List<string> redeemedCodes = new List<string>();
        // v5: completion and its wallet/wardrobe reward share one transaction.
        public string selectedChallenge = "run_coin10";
        public List<string> completedChallenges = new List<string>();
        public int centeredLandings, fallingRescues, preciseChallengeHeight, inertiaChallengeHeight;
        public int hardCleanChallengeHeight, hardChallengeHeight;
        // v6: only actual gameplay events increment these career totals.
        public int challengeCoinsCollected, challengeBushesUsed, challengeMovingLandings, challengeNarrowLandings;
        // v7: only finalized attempts from this version onwards are recorded.
        public StatisticsData statistics = new StatisticsData();
        // v8: certified new-content identities, never inferred from coin value
        // or old journal rows which did not record these proof events.
        public int challengeRocketLandings, challengeGoldenFish, challengeBeeDodges;
        // v9: expedition-only combat training shares its transaction with the
        // wallet debit. It never changes classic movement or utility upgrades.
        public List<ExpeditionTrainingLevel> expeditionTraining = new List<ExpeditionTrainingLevel>();
    }

    [Serializable]
    private sealed class ExpeditionTrainingLevel
    {
        public string id;
        public int level;
    }

    [Serializable]
    private sealed class StatisticsData
    {
        public int runs, bestRunHeight, bestRunScore, bestRunCoins;
        public long totalCoins, totalBushes, totalMovingLandings, totalNarrowLandings, totalWideJumps;
        public double activeSeconds;
        public string lastRunId;
        public List<RunEntryData> recentRuns = new List<RunEntryData>();
    }

    [Serializable]
    private sealed class RunEntryData
    {
        public string id, endReason;
        public bool portrait, inertia;
        public GameDifficulty difficulty;
        public int height, score, coins, bushes, movingLandings, narrowLandings, wideJumps;
        public double activeSeconds;

        public RunStatisticsEntry Snapshot() => new RunStatisticsEntry(id, portrait, inertia, difficulty,
            height, score, coins, bushes, movingLandings, narrowLandings, wideJumps, activeSeconds, endReason);

        public static RunEntryData From(RunStatisticsEntry entry) => new RunEntryData
        {
            id = entry.Id, portrait = entry.Portrait, inertia = entry.Inertia, difficulty = entry.Difficulty,
            height = entry.Height, score = entry.Score, coins = entry.Coins, bushes = entry.Bushes,
            movingLandings = entry.MovingLandings, narrowLandings = entry.NarrowLandings, wideJumps = entry.WideJumps,
            activeSeconds = entry.ActiveSeconds, endReason = CleanRunReason(entry.EndReason)
        };
    }

    [Serializable]
    private sealed class ModeUpgrades
    {
        public string mode;
        public int magnet;
        public int duration;
    }

    public static int Coins
    {
        get { EnsureLoaded(); return data.coins; }
    }

    public static CareerStatisticsSnapshot Statistics
    {
        get
        {
            EnsureLoaded();
            StatisticsData stats = data.statistics;
            RunStatisticsEntry[] recent = new RunStatisticsEntry[stats.recentRuns.Count];
            for (int i = 0; i < recent.Length; i++) recent[i] = stats.recentRuns[i].Snapshot();
            return new CareerStatisticsSnapshot(stats.runs, stats.totalCoins, stats.totalBushes,
                stats.totalMovingLandings, stats.totalNarrowLandings, stats.totalWideJumps, stats.activeSeconds,
                stats.bestRunHeight, stats.bestRunScore, stats.bestRunCoins, recent);
        }
    }

    // A finalized attempt is a journal transaction, never a wallet/challenge
    // event. The bounded recent-ID ledger protects repeated finalization of the
    // same live attempt without retaining an unbounded history of identifiers.
    public static bool RecordRun(RunStatisticsEntry entry)
    {
        EnsureLoaded();
        if (AdminSandboxActive || readOnlyFutureSave || !ValidRunEntry(entry)) return false;
        StatisticsData stats = data.statistics;
        if (stats.lastRunId == entry.Id || stats.recentRuns.Exists(run => run.id == entry.Id)) return false;
        stats.runs = stats.runs == int.MaxValue ? int.MaxValue : stats.runs + 1;
        stats.totalCoins = AddRunTotal(stats.totalCoins, entry.Coins);
        stats.totalBushes = AddRunTotal(stats.totalBushes, entry.Bushes);
        stats.totalMovingLandings = AddRunTotal(stats.totalMovingLandings, entry.MovingLandings);
        stats.totalNarrowLandings = AddRunTotal(stats.totalNarrowLandings, entry.NarrowLandings);
        stats.totalWideJumps = AddRunTotal(stats.totalWideJumps, entry.WideJumps);
        stats.activeSeconds = entry.ActiveSeconds > double.MaxValue - stats.activeSeconds
            ? double.MaxValue : stats.activeSeconds + entry.ActiveSeconds;
        stats.bestRunHeight = Math.Max(stats.bestRunHeight, entry.Height);
        stats.bestRunScore = Math.Max(stats.bestRunScore, entry.Score);
        stats.bestRunCoins = Math.Max(stats.bestRunCoins, entry.Coins);
        stats.lastRunId = entry.Id;
        stats.recentRuns.Insert(0, RunEntryData.From(entry));
        if (stats.recentRuns.Count > 5) stats.recentRuns.RemoveRange(5, stats.recentRuns.Count - 5);
        // Both durable copies contain the same totals and ID before returning;
        // recovering a damaged primary must not count the same attempt again.
        Save(true, true);
        Changed?.Invoke();
        return true;
    }

    private static long AddRunTotal(long total, int amount) => total > long.MaxValue - amount ? long.MaxValue : total + amount;

    private static bool ValidRunId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
        foreach (char c in id)
            if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '-' && c != '_')
                return false;
        return true;
    }

    private static bool ValidRunEntry(RunStatisticsEntry entry) => entry != null && ValidRunId(entry.Id) &&
        (int)entry.Difficulty >= (int)GameDifficulty.Easy && (int)entry.Difficulty <= (int)GameDifficulty.Hard &&
        entry.Height >= 0 && entry.Score >= 0 && entry.Coins >= 0 && entry.Bushes >= 0 &&
        entry.MovingLandings >= 0 && entry.NarrowLandings >= 0 && entry.WideJumps >= 0 &&
        !double.IsNaN(entry.ActiveSeconds) && !double.IsInfinity(entry.ActiveSeconds) && entry.ActiveSeconds >= 0;

    private static string CleanRunReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "Завершён";
        string trimmed = reason.Trim();
        int length = Math.Min(80, trimmed.Length);
        char[] characters = trimmed.Substring(0, length).ToCharArray();
        for (int i = 0; i < length; i++) if (char.IsControl(characters[i])) characters[i] = ' ';
        return new string(characters);
    }

    public static string SelectedChallengeId
    {
        get { EnsureLoaded(); return data.selectedChallenge; }
    }

    public static bool SelectChallenge(string id)
    {
        EnsureLoaded();
        if (AdminSandboxActive || readOnlyFutureSave || RunFlow.RunActive || RunFlow.IsTransitioning) return false;
        id = id ?? string.Empty;
        ChallengeDefinition challenge = ChallengeCatalog.Find(id);
        if (id.Length > 0 && (challenge == null || challenge.IsAchievement || data.completedChallenges.Contains(id))) return false;
        if (data.selectedChallenge == id) return true;
        data.selectedChallenge = id;
        Save(true);
        Changed?.Invoke();
        return true;
    }

    public static bool IsChallengeCompleted(string id)
    {
        EnsureLoaded();
        return ChallengeCatalog.Find(id) != null && data.completedChallenges.Contains(id);
    }

    public static int ChallengeProgressValue(string id)
    {
        EnsureLoaded();
        ChallengeDefinition challenge = ChallengeCatalog.Find(id);
        if (challenge == null) return 0;
        if (data.completedChallenges.Contains(id)) return challenge.Target;
        switch (id)
        {
            case "mastery_center50": return data.centeredLandings;
            case "mastery_rescue3": return data.fallingRescues;
            case "mastery_hard_clean1000": return data.hardCleanChallengeHeight;
            case "mastery_hard_space": return data.hardChallengeHeight;
            case "mastery_controls1000": return (data.preciseChallengeHeight >= 1000 ? 1 : 0) +
                (data.inertiaChallengeHeight >= 1000 ? 1 : 0);
            case "mastery_coins250": return data.challengeCoinsCollected;
            case "mastery_bush25": return data.challengeBushesUsed;
            case "mastery_moving50": return data.challengeMovingLandings;
            case "mastery_narrow75": return data.challengeNarrowLandings;
            case "mastery_tasks10": return Math.Min(10, CountCompletedRunChallenges());
            case "mastery_rocket10": return data.challengeRocketLandings;
            case "mastery_gold15": return data.challengeGoldenFish;
            case "mastery_bee20": return data.challengeBeeDodges;
            default: return 0; // Run-task counters restart with every attempt.
        }
    }

    internal static void ApplyChallengeProgress(ChallengeRunStats stats, string selectedId,
        bool centeredEvent, bool rescueEvent, bool selectedAllowed, int selectedProgress,
        bool coinEvent = false, bool bushEvent = false, bool movingEvent = false, bool narrowEvent = false,
        bool rocketLandingEvent = false, bool goldenFishEvent = false, bool beeDodgeEvent = false)
    {
        EnsureLoaded();
        if (AdminSandboxActive || readOnlyFutureSave) return;
        bool changed = false, rewarded = false;
        if (centeredEvent && data.centeredLandings < 50) { data.centeredLandings++; changed = true; }
        if (rescueEvent && data.fallingRescues < 3) { data.fallingRescues++; changed = true; }
        if (coinEvent && data.challengeCoinsCollected < 250) { data.challengeCoinsCollected++; changed = true; }
        if (bushEvent && data.challengeBushesUsed < 25) { data.challengeBushesUsed++; changed = true; }
        if (movingEvent && data.challengeMovingLandings < 50) { data.challengeMovingLandings++; changed = true; }
        if (narrowEvent && data.challengeNarrowLandings < 75) { data.challengeNarrowLandings++; changed = true; }
        if (rocketLandingEvent && data.challengeRocketLandings < 10) { data.challengeRocketLandings++; changed = true; }
        if (goldenFishEvent && data.challengeGoldenFish < 15) { data.challengeGoldenFish++; changed = true; }
        if (beeDodgeEvent && data.challengeBeeDodges < 20) { data.challengeBeeDodges++; changed = true; }
        if (stats.Inertia) changed |= Raise(ref data.inertiaChallengeHeight, stats.Height, 1000);
        else changed |= Raise(ref data.preciseChallengeHeight, stats.Height, 1000);
        if (stats.Difficulty == GameDifficulty.Hard)
        {
            changed |= Raise(ref data.hardChallengeHeight, stats.Height, WorldZones.GoalScore);
            if (!stats.UsedShield) changed |= Raise(ref data.hardCleanChallengeHeight, stats.Height, 1000);
        }
        ChallengeDefinition selected = ChallengeCatalog.Find(selectedId);
        if (selected != null && !selected.IsAchievement && selectedAllowed && selectedProgress >= selected.Target)
            rewarded |= GrantChallenge(selected);
        rewarded |= GrantEarnedChallengeAchievements();
        if (!changed && !rewarded) return;
        // Refresh both recovery copies for rewards so damaged-primary recovery
        // cannot reopen a completed task or split it from the granted item.
        Save(rewarded, rewarded);
        Changed?.Invoke();
    }

    private static bool GrantEarnedChallengeAchievements()
    {
        bool rewarded = false;
        for (int i = 0; i < ChallengeCatalog.All.Count; i++)
        {
            ChallengeDefinition achievement = ChallengeCatalog.All[i];
            if (achievement.IsAchievement && ChallengeProgressValue(achievement.Id) >= achievement.Target)
                rewarded |= GrantChallenge(achievement);
        }
        return rewarded;
    }

    private static int CountCompletedRunChallenges()
    {
        int completed = 0;
        for (int i = 0; i < data.completedChallenges.Count; i++)
        {
            ChallengeDefinition definition = ChallengeCatalog.Find(data.completedChallenges[i]);
            if (definition != null && !definition.IsAchievement) completed++;
        }
        return completed;
    }

    private static bool GrantChallenge(ChallengeDefinition challenge)
    {
        if (data.completedChallenges.Contains(challenge.Id) ||
            challenge.RewardItemId.Length > 0 && !FindItem(challenge.RewardItemId, out _)) return false;
        data.completedChallenges.Add(challenge.Id);
        if (data.selectedChallenge == challenge.Id) data.selectedChallenge = string.Empty;
        data.coins = (int)Math.Min(MaximumCoins, (long)data.coins + challenge.RewardCoins);
        if (challenge.RewardItemId.Length > 0) GrantOwned(challenge.RewardItemId);
        return true;
    }

    private static bool Raise(ref int saved, int value, int limit)
    {
        value = Mathf.Clamp(value, 0, Mathf.Max(0, limit));
        if (value <= saved) return false;
        saved = value;
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
        // SubsystemRegistration also runs when domain reload is disabled.
        // Drop both disposable data and the saved reference before reloading.
        adminSandbox = null;
#endif
        data = null;
        revision = 0;
        readOnlyFutureSave = false;
        Changed = null;
    }

    // Also useful to the editor's isolated save/recovery validation.
    public static void Reload()
    {
        // Scene/restart callers must not silently replace an active sandbox by
        // persistent progress. Only EndAdminSandbox or ResetSession exits it.
        if (AdminSandboxActive)
        {
            Changed?.Invoke();
            return;
        }
        data = null;
        revision = 0;
        readOnlyFutureSave = false;
        EnsureLoaded();
        Changed?.Invoke();
    }

    public static bool Owns(string id)
    {
        EnsureLoaded();
        return FindItem(id, out Item item) && (item.Price == 0 || data.owned.Contains(id));
    }

    public static bool IsEquipped(string id)
    {
        if (!FindItem(id, out Item item)) return false;
        EnsureLoaded();
        if (item.Category == Category.Accessory)
            return id == "accessory_none" ? data.accessories.Count == 0 : data.accessories.Contains(id);
        return EquippedId(item.Category) == id;
    }

    // Head, face and ears can each carry one illustration. The read-only
    // snapshot keeps callers from changing the saved selection by accident.
    public static IReadOnlyList<string> EquippedAccessories
    {
        get
        {
            EnsureLoaded();
            return Array.AsReadOnly(data.accessories.ToArray());
        }
    }

    public static string EquippedId(Category category)
    {
        EnsureLoaded();
        switch (category)
        {
            case Category.Cat: return data.cat;
            case Category.Clothing: return data.clothing;
            case Category.Accessory: return data.accessories.Count == 0 ? "accessory_none" : data.accessories[0];
            case Category.Trail: return data.trail;
            default: return string.Empty;
        }
    }

    public static bool TryPurchase(string id)
    {
        EnsureLoaded();
        if (readOnlyFutureSave || !FindItem(id, out Item item) || item.Price < 0) return false;
        if (Owns(id)) return true;
        if (data.coins < item.Price) return false;
        data.coins -= item.Price;
        data.owned.Add(id);
        Save(true);
        Changed?.Invoke();
        return true;
    }

    public static bool Equip(string id)
    {
        EnsureLoaded();
        if (readOnlyFutureSave || !FindItem(id, out Item item) || !Owns(id)) return false;
        if (item.Category == Category.Accessory)
        {
            if (id == "accessory_none")
            {
                if (data.accessories.Count == 0) return true;
                data.accessories.Clear();
            }
            else if (data.accessories.Contains(id))
                data.accessories.Remove(id);
            else
            {
                AccessorySlot slot = SlotFor(id);
                data.accessories.RemoveAll(selected =>
                    FindItem(selected, out Item known) && known.Category == Category.Accessory && SlotFor(selected) == slot);
                data.accessories.Insert(0, id);
            }
            data.accessory = data.accessories.Count == 0 ? "accessory_none" : data.accessories[0];
            Save(true);
            Changed?.Invoke();
            return true;
        }
        if (IsEquipped(id)) return true;
        switch (item.Category)
        {
            case Category.Cat: data.cat = id; break;
            case Category.Clothing: data.clothing = id; break;
            case Category.Trail: data.trail = id; break;
        }
        Save(true);
        Changed?.Invoke();
        return true;
    }

    public static string UnlockDescription(string id)
    {
        switch (id)
        {
            case "accessory_astronaut_helmet":
            case "clothing_astronaut": return $"Достигни высоты {AstronautHeight} · любая сложность";
            case "accessory_coin_crown": return "Собери 10 монет в воздухе за одну попытку";
            case "accessory_target": return "Достижение «Меткие лапки»: 50 точных приземлений";
            case "trail_ninelives": return "Достижение «Девять жизней»: 3 спасения щитом от падения";
            case "accessory_patch": return "Достижение «Колючий характер»: высота 1000 на сложной без срабатывания щита";
            case "trail_meteor": return "Достижение «Без скидок»: космос на сложной сложности";
            case "clothing_explorer": return "Достижение «Котик на все случаи»: высота 1000 с обоими управлениями";
            case "cat_ghoul":
            case "clothing_ghoul":
            case "accessory_ghoul_mask":
            case "accessory_ghoul_hair": return "Открывается промокодом";
            default: return string.Empty;
        }
    }

    public static bool AwardHeightUnlocks(int height, bool eligible)
    {
        EnsureLoaded();
        if (!eligible || AdminSandboxActive || readOnlyFutureSave || height < AstronautHeight) return false;
        bool changed = GrantOwned("accessory_astronaut_helmet") | GrantOwned("clothing_astronaut");
        if (changed) CommitUnlocks();
        return changed;
    }

    public static bool AwardAirCoinAchievement(bool eligible)
    {
        EnsureLoaded();
        if (!eligible || AdminSandboxActive || readOnlyFutureSave) return false;
        if (!GrantOwned("accessory_coin_crown")) return false;
        CommitUnlocks();
        return true;
    }

    public static bool TryRedeemCode(string code, out string message)
    {
        EnsureLoaded();
        if (AdminSandboxActive || readOnlyFutureSave)
        {
            message = "Сейчас промокод применить нельзя.";
            return false;
        }
        string normalized = NormalizeCode(code);
        string digest = normalized.Length > 0 ? CodeDigest(normalized) : string.Empty;
        if (digest != GhoulCodeDigest && digest != GhoulShareCodeDigest)
        {
            message = "Промокод не найден.";
            return false;
        }
        if (data.redeemedCodes.Contains(GhoulCodeDigest))
        {
            message = "Этот промокод уже использован.";
            return false;
        }
        data.redeemedCodes.Add(GhoulCodeDigest);
        GrantOwned("cat_ghoul");
        GrantOwned("clothing_ghoul");
        GrantOwned("accessory_ghoul_mask");
        GrantOwned("accessory_ghoul_hair");
        CommitUnlocks();
        message = "Образ котика-гуля открыт!";
        return true;
    }

    public static void AddCoins(int amount)
    {
        EnsureLoaded();
        if (readOnlyFutureSave || amount <= 0 || data.coins == MaximumCoins) return;
        data.coins = (int)Math.Min(MaximumCoins, (long)data.coins + amount);
        // PlayerPrefs.Save is performed at menu transactions, pause and death;
        // collecting a coin must not synchronously write to Android storage.
        Save(false);
        Changed?.Invoke();
    }

    public static int MaxUpgradeLevel(Upgrade upgrade)
    {
        return upgrade == Upgrade.Magnet || upgrade == Upgrade.Duration ? 3 : 0;
    }

    public static int GetUpgradeLevel(Upgrade upgrade, string mode)
    {
        EnsureLoaded();
        ModeUpgrades entry = GetMode(mode, false);
        if (entry == null) return 0;
        if (upgrade == Upgrade.Magnet) return entry.magnet;
        if (upgrade == Upgrade.Duration) return entry.duration;
        return 0;
    }

    public static int UpgradePrice(Upgrade upgrade, string mode)
    {
        int level = GetUpgradeLevel(upgrade, mode);
        return IsMode(mode) && level < MaxUpgradeLevel(upgrade) ? UpgradeCosts[level] : 0;
    }

    public static bool TryUpgrade(Upgrade upgrade, string mode)
    {
        EnsureLoaded();
        int price = UpgradePrice(upgrade, mode);
        if (readOnlyFutureSave || price <= 0 || data.coins < price) return false;
        ModeUpgrades entry = GetMode(mode, true);
        data.coins -= price;
        if (upgrade == Upgrade.Magnet) entry.magnet++;
        else entry.duration++;
        Save(true);
        Changed?.Invoke();
        return true;
    }

    public static float PickupRadiusBonus(string mode) => GetUpgradeLevel(Upgrade.Magnet, mode) * 0.12f;
    public static float PowerDurationBonus(string mode) => GetUpgradeLevel(Upgrade.Duration, mode) * 0.5f;

    public static bool ExpeditionTrainingAvailable
    {
        get
        {
            EnsureLoaded();
            return !readOnlyFutureSave && !AdminSandboxActive && !RunFlow.RunActive && !RunFlow.IsTransitioning;
        }
    }

    public static int GetExpeditionTrainingLevel(ExpeditionTrainingKind kind)
    {
        EnsureLoaded();
        if (!ExpeditionTraining.IsValid(kind)) return 0;
        string id = ExpeditionTraining.Id(kind);
        ExpeditionTrainingLevel entry = data.expeditionTraining.Find(item => item.id == id);
        return entry != null ? entry.level : 0;
    }

    public static int ExpeditionTrainingPrice(ExpeditionTrainingKind kind) =>
        ExpeditionTraining.Price(kind, GetExpeditionTrainingLevel(kind));

    public static bool TryUpgradeExpeditionTraining(ExpeditionTrainingKind kind)
    {
        EnsureLoaded();
        if (!ExpeditionTrainingAvailable || !ExpeditionTraining.IsValid(kind) || revision == long.MaxValue) return false;
        int price = ExpeditionTrainingPrice(kind);
        if (price <= 0 || data.coins < price) return false;
        SaveData next = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(data));
        Normalize(next);
        string id = ExpeditionTraining.Id(kind);
        ExpeditionTrainingLevel level = next.expeditionTraining.Find(item => item.id == id);
        if (level == null) { level = new ExpeditionTrainingLevel { id = id }; next.expeditionTraining.Add(level); }
        level.level++;
        next.coins -= price;
        long nextRevision = revision + 1;
        string payload = JsonUtility.ToJson(next);
        string json = JsonUtility.ToJson(new SaveEnvelope { version = CurrentVersion, revision = nextRevision,
            payload = payload, checksum = Checksum(payload, nextRevision) });
        try
        {
            // Recovery must contain both the level and its cost, not an older
            // wallet which could reopen the same purchase after corruption.
            PlayerPrefs.SetString(storageSaveKey, json);
            PlayerPrefs.SetString(storageBackupKey, json);
            PlayerPrefs.Save();
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Expedition training was not committed: " + exception.GetType().Name);
            return false;
        }
        data = next; revision = nextRevision;
        Changed?.Invoke();
        return true;
    }

    public static void Flush()
    {
        EnsureLoaded();
        if (!AdminSandboxActive && !readOnlyFutureSave) PlayerPrefs.Save();
    }

    private static bool FindItem(string id, out Item item)
    {
        for (int i = 0; i < Items.Length; i++)
        {
            if (Items[i].Id != id) continue;
            item = Items[i];
            return true;
        }
        item = default;
        return false;
    }

    private enum AccessorySlot { Head, Face, Ears }

    private static AccessorySlot SlotFor(string id)
    {
        switch (id)
        {
            case "accessory_glasses":
            case "accessory_aviator":
            case "accessory_ghoul_mask": return AccessorySlot.Face;
            case "accessory_tufts":
            case "accessory_patch":
            case "accessory_headphones": return AccessorySlot.Ears;
            case "accessory_astronaut_helmet": return AccessorySlot.Head;
            default: return AccessorySlot.Head;
        }
    }

    private static bool GrantOwned(string id)
    {
        if (!FindItem(id, out _) || data.owned.Contains(id)) return false;
        data.owned.Add(id);
        return true;
    }

    private static void CommitUnlocks()
    {
        Save(true);
        Changed?.Invoke();
    }

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64) return string.Empty;
        StringBuilder normalized = new StringBuilder(code.Length);
        foreach (char character in code)
        {
            if (character == '-' || character == '_' || char.IsWhiteSpace(character)) continue;
            normalized.Append(char.ToUpperInvariant(character));
        }
        return normalized.ToString();
    }

    private static string CodeDigest(string normalized)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }
    }

    private static bool IsMode(string mode) => Array.IndexOf(Modes, mode) >= 0;

    private static ModeUpgrades GetMode(string mode, bool create)
    {
        if (!IsMode(mode)) return null;
        for (int i = 0; i < data.upgrades.Count; i++)
            if (data.upgrades[i].mode == mode) return data.upgrades[i];
        if (!create) return null;
        ModeUpgrades result = new ModeUpgrades { mode = mode };
        data.upgrades.Add(result);
        return result;
    }

    private static void EnsureLoaded()
    {
        if (data != null) return;
        string primary = PlayerPrefs.GetString(storageSaveKey, string.Empty);
        string backup = PlayerPrefs.GetString(storageBackupKey, string.Empty);
        bool primaryValid = TryRead(primary, out SaveData primaryData, out long primaryRevision, out bool primaryFuture, out int primaryVersion);
        bool backupValid = TryRead(backup, out SaveData backupData, out long backupRevision, out bool backupFuture, out int backupVersion);
        readOnlyFutureSave = primaryFuture || backupFuture;
        int loadedVersion = CurrentVersion;
        if (primaryValid && (!backupValid || primaryRevision >= backupRevision))
        {
            data = primaryData;
            revision = primaryRevision;
            loadedVersion = primaryVersion;
        }
        else if (backupValid)
        {
            data = backupData;
            revision = backupRevision;
            loadedVersion = backupVersion;
            if (!readOnlyFutureSave) PlayerPrefs.SetString(storageSaveKey, backup);
        }
        else
        {
            data = new SaveData();
        }
        // Before v4, the helmet was a dedicated cat skin. Capture that
        // selection before normalization rejects its retired catalog ID.
        bool legacyAstronautEquipped = loadedVersion < 4 && data.cat == "cat_astronaut" &&
            data.owned != null && data.owned.Contains("cat_astronaut");
        // Historic records and challenge counters are not finished-attempt
        // statistics. Old save versions always start the journal empty.
        if (loadedVersion < 7) data.statistics = new StatisticsData();
        if (loadedVersion < 8)
            data.challengeRocketLandings = data.challengeGoldenFish = data.challengeBeeDodges = 0;
        if (loadedVersion < 9) data.expeditionTraining = new List<ExpeditionTrainingLevel>();
        Normalize(data);
        if (readOnlyFutureSave)
            Debug.LogWarning("Doodlik: найдено сохранение из более новой версии. Покупки отключены, чтобы сохранить прогресс.");
        else if (loadedVersion < CurrentVersion)
        {
            if (loadedVersion < 2) MigrateRetiredScarves(data);
            if (loadedVersion < 3) MigrateSingleAccessory(data);
            if (loadedVersion < 4) MigrateAstronautHelmet(data, legacyAstronautEquipped);
            // v5 already records proofs of the original ten tasks. Preserve
            // them and grant the new ten-tasks milestone once, without inventing
            // historic coin/bush/platform event counts that were never stored.
            if (loadedVersion < 6) GrantEarnedChallengeAchievements();
            // Commit both copies before returning the migrated wallet. Recovery
            // must not reopen an old refund or lose the previous accessory.
            Save(true, true);
        }
    }

    private static bool TryRead(string json, out SaveData value, out long savedRevision, out bool futureVersion, out int savedVersion)
    {
        value = null;
        savedRevision = 0;
        futureVersion = false;
        savedVersion = 0;
        if (string.IsNullOrEmpty(json) || json.Length > 65536) return false;
        try
        {
            SaveEnvelope envelope = JsonUtility.FromJson<SaveEnvelope>(json);
            if (envelope == null) return false;
            futureVersion = envelope.version > CurrentVersion;
            if (envelope.version < 1 || envelope.version > CurrentVersion || envelope.revision < 0 ||
                string.IsNullOrEmpty(envelope.payload) ||
                envelope.checksum != Checksum(envelope.payload, envelope.revision)) return false;
            value = JsonUtility.FromJson<SaveData>(envelope.payload);
            if (value == null || value.coins < 0 || value.coins > MaximumCoins) return false;
            savedRevision = envelope.revision;
            savedVersion = envelope.version;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void MigrateRetiredScarves(SaveData value)
    {
        // Contains is deliberate: damaged/old inventories may contain duplicate
        // IDs, but a removed item is refunded only once.
        int refund = (value.owned.Contains("scarf_berry") ? 70 : 0) +
            (value.owned.Contains("scarf_mint") ? 90 : 0);
        value.coins = (int)Math.Min(MaximumCoins, (long)value.coins + refund);
        value.owned.RemoveAll(id => id == "scarf_none" || id == "scarf_berry" || id == "scarf_mint");
    }

    private static void MigrateSingleAccessory(SaveData value)
    {
        if (value.accessories.Count > 0 || value.accessory == "accessory_none" ||
            !ValidEquipped(value.accessory, Category.Accessory, value)) return;
        value.accessories.Add(value.accessory);
    }

    private static void MigrateAstronautHelmet(SaveData value, bool wasEquipped)
    {
        // Preserve the retired ownership ID as unknown save data, while
        // exposing the earned reward as a head accessory in current versions.
        if (!value.owned.Contains("cat_astronaut")) return;
        if (!value.owned.Contains("accessory_astronaut_helmet"))
            value.owned.Add("accessory_astronaut_helmet");
        if (!wasEquipped) return;

        // Existing astronaut looks remain equipped, without replacing a
        // separately chosen face or ear accessory.
        value.cat = "cat_ginger";
        value.accessories.RemoveAll(id => SlotFor(id) == AccessorySlot.Head);
        value.accessories.Insert(0, "accessory_astronaut_helmet");
        value.accessory = "accessory_astronaut_helmet";
    }

    private static void Normalize(SaveData value)
    {
        if (value.owned == null) value.owned = new List<string>();
        value.owned.RemoveAll(string.IsNullOrEmpty);
        HashSet<string> unique = new HashSet<string>();
        value.owned.RemoveAll(id => !unique.Add(id));
        // Keep unknown owned IDs so a downgrade does not destroy future items.
        if (!ValidEquipped(value.cat, Category.Cat, value)) value.cat = "cat_ginger";
        if (!ValidEquipped(value.clothing, Category.Clothing, value)) value.clothing = "clothing_original";
        if (!ValidEquipped(value.accessory, Category.Accessory, value)) value.accessory = "accessory_none";
        if (!ValidEquipped(value.trail, Category.Trail, value)) value.trail = "trail_none";
        if (value.accessories == null) value.accessories = new List<string>();
        HashSet<string> seenAccessories = new HashSet<string>();
        HashSet<AccessorySlot> seenSlots = new HashSet<AccessorySlot>();
        value.accessories.RemoveAll(id =>
        {
            if (string.IsNullOrEmpty(id) || id == "accessory_none" || !seenAccessories.Add(id)) return true;
            if (!FindItem(id, out Item item)) return !value.owned.Contains(id);
            return item.Category != Category.Accessory || !ValidEquipped(id, Category.Accessory, value) ||
                !seenSlots.Add(SlotFor(id));
        });
        if (value.redeemedCodes == null) value.redeemedCodes = new List<string>();
        HashSet<string> seenCodes = new HashSet<string>();
        value.redeemedCodes.RemoveAll(code => string.IsNullOrEmpty(code) || !seenCodes.Add(code));
        if (value.completedChallenges == null) value.completedChallenges = new List<string>();
        HashSet<string> seenChallenges = new HashSet<string>();
        // Preserve unknown IDs for safe downgrades/future expansions.
        value.completedChallenges.RemoveAll(id => string.IsNullOrEmpty(id) || !seenChallenges.Add(id));
        ChallengeDefinition selectedChallenge = ChallengeCatalog.Find(value.selectedChallenge);
        if (selectedChallenge == null || selectedChallenge.IsAchievement) value.selectedChallenge = string.Empty;
        value.centeredLandings = Mathf.Clamp(value.centeredLandings, 0, 50);
        value.fallingRescues = Mathf.Clamp(value.fallingRescues, 0, 3);
        value.preciseChallengeHeight = Mathf.Clamp(value.preciseChallengeHeight, 0, 1000);
        value.inertiaChallengeHeight = Mathf.Clamp(value.inertiaChallengeHeight, 0, 1000);
        value.hardCleanChallengeHeight = Mathf.Clamp(value.hardCleanChallengeHeight, 0, 1000);
        value.hardChallengeHeight = Mathf.Clamp(value.hardChallengeHeight, 0, WorldZones.GoalScore);
        value.challengeCoinsCollected = Mathf.Clamp(value.challengeCoinsCollected, 0, 250);
        value.challengeBushesUsed = Mathf.Clamp(value.challengeBushesUsed, 0, 25);
        value.challengeMovingLandings = Mathf.Clamp(value.challengeMovingLandings, 0, 50);
        value.challengeNarrowLandings = Mathf.Clamp(value.challengeNarrowLandings, 0, 75);
        value.challengeRocketLandings = Mathf.Clamp(value.challengeRocketLandings, 0, 10);
        value.challengeGoldenFish = Mathf.Clamp(value.challengeGoldenFish, 0, 15);
        value.challengeBeeDodges = Mathf.Clamp(value.challengeBeeDodges, 0, 20);
        NormalizeStatistics(value);
        if (value.upgrades == null) value.upgrades = new List<ModeUpgrades>();
        HashSet<string> seenModes = new HashSet<string>();
        value.upgrades.RemoveAll(entry => entry == null || string.IsNullOrEmpty(entry.mode) || !seenModes.Add(entry.mode));
        foreach (ModeUpgrades entry in value.upgrades)
        {
            if (!IsMode(entry.mode)) continue;
            entry.magnet = Mathf.Clamp(entry.magnet, 0, MaxUpgradeLevel(Upgrade.Magnet));
            entry.duration = Mathf.Clamp(entry.duration, 0, MaxUpgradeLevel(Upgrade.Duration));
        }
        if (value.expeditionTraining == null) value.expeditionTraining = new List<ExpeditionTrainingLevel>();
        var training = new Dictionary<string, ExpeditionTrainingLevel>(StringComparer.Ordinal);
        foreach (ExpeditionTrainingLevel entry in value.expeditionTraining)
        {
            if (entry == null || !ValidRunId(entry.id)) continue;
            int maximum = MaximumCoins; // Preserve bounded unknown IDs for later versions.
            foreach (ExpeditionTrainingKind kind in ExpeditionTraining.All)
                if (entry.id == ExpeditionTraining.Id(kind)) { maximum = ExpeditionTraining.MaxLevel(kind); break; }
            int level = Math.Max(0, Math.Min(maximum, entry.level));
            if (training.TryGetValue(entry.id, out ExpeditionTrainingLevel prior)) prior.level = Math.Max(prior.level, level);
            else training.Add(entry.id, new ExpeditionTrainingLevel { id = entry.id, level = level });
        }
        value.expeditionTraining = new List<ExpeditionTrainingLevel>(training.Values);
    }

    private static void NormalizeStatistics(SaveData value)
    {
        if (value.statistics == null) value.statistics = new StatisticsData();
        StatisticsData stats = value.statistics;
        stats.runs = Math.Max(0, stats.runs);
        stats.totalCoins = Math.Max(0L, stats.totalCoins);
        stats.totalBushes = Math.Max(0L, stats.totalBushes);
        stats.totalMovingLandings = Math.Max(0L, stats.totalMovingLandings);
        stats.totalNarrowLandings = Math.Max(0L, stats.totalNarrowLandings);
        stats.totalWideJumps = Math.Max(0L, stats.totalWideJumps);
        if (double.IsNaN(stats.activeSeconds) || double.IsInfinity(stats.activeSeconds) || stats.activeSeconds < 0)
            stats.activeSeconds = 0;
        stats.bestRunHeight = Math.Max(0, stats.bestRunHeight);
        stats.bestRunScore = Math.Max(0, stats.bestRunScore);
        stats.bestRunCoins = Math.Max(0, stats.bestRunCoins);
        if (!ValidRunId(stats.lastRunId)) stats.lastRunId = string.Empty;
        if (stats.recentRuns == null) stats.recentRuns = new List<RunEntryData>();
        HashSet<string> seenRuns = new HashSet<string>();
        stats.recentRuns.RemoveAll(entry => entry == null || !ValidRunEntry(entry.Snapshot()) || !seenRuns.Add(entry.id));
        if (stats.recentRuns.Count > 5) stats.recentRuns.RemoveRange(5, stats.recentRuns.Count - 5);
        foreach (RunEntryData entry in stats.recentRuns) entry.endReason = CleanRunReason(entry.endReason);
    }

    private static bool ValidEquipped(string id, Category category, SaveData value)
    {
        return FindItem(id, out Item item) && item.Category == category &&
               (item.Price == 0 || value.owned.Contains(id));
    }

    private static void Save(bool flush, bool replaceRecoveryCopy = false)
    {
        // Check before even reading storage: sandbox transactions never update
        // either durable copy and never consume the normal save's revision.
        if (AdminSandboxActive || readOnlyFutureSave) return;
        string previous = PlayerPrefs.GetString(storageSaveKey, string.Empty);
        if (TryRead(previous, out _, out _, out _, out _)) PlayerPrefs.SetString(storageBackupKey, previous);
        revision = revision == long.MaxValue ? revision : revision + 1;
        string payload = JsonUtility.ToJson(data);
        SaveEnvelope envelope = new SaveEnvelope
        {
            version = CurrentVersion,
            revision = revision,
            payload = payload,
            checksum = Checksum(payload, revision)
        };
        string json = JsonUtility.ToJson(envelope);
        // First save gets a recovery copy too; later saves retain the previous
        // valid transaction, rather than copying a damaged primary over it.
        if (replaceRecoveryCopy || !TryRead(PlayerPrefs.GetString(storageBackupKey, string.Empty), out _, out _, out _, out _))
            PlayerPrefs.SetString(storageBackupKey, json);
        PlayerPrefs.SetString(storageSaveKey, json);
        if (flush) PlayerPrefs.Save();
    }

#if UNITY_EDITOR
    // Validation gets unique keys and its own cached data/events. Real progress
    // is never replaced, even temporarily, and is restored without loading it.
    public static IDisposable UseIsolatedStorageForValidation(out string primaryKey, out string recoveryKey)
    {
        if (storageSaveKey != SaveKey || storageBackupKey != BackupKey)
            throw new InvalidOperationException("A progression validation storage scope is already active.");
        primaryKey = "DoodlikValidation_" + Guid.NewGuid().ToString("N");
        recoveryKey = primaryKey + "_Backup";
        return new ValidationStorageScope(primaryKey, recoveryKey);
    }

    private sealed class ValidationStorageScope : IDisposable
    {
        private readonly SaveData originalData;
        private readonly long originalRevision;
        private readonly bool originalReadOnly;
        private readonly Action originalChanged;
        private readonly AdminSandboxState originalAdminSandbox;
        private readonly string primaryKey, recoveryKey;
        private bool disposed;

        public ValidationStorageScope(string primary, string recovery)
        {
            originalData = data;
            originalRevision = revision;
            originalReadOnly = readOnlyFutureSave;
            originalChanged = Changed;
            originalAdminSandbox = adminSandbox;
            adminSandbox = null;
            primaryKey = primary;
            recoveryKey = recovery;
            storageSaveKey = primary;
            storageBackupKey = recovery;
            data = null;
            revision = 0;
            readOnlyFutureSave = false;
            Changed = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                PlayerPrefs.DeleteKey(primaryKey);
                PlayerPrefs.DeleteKey(recoveryKey);
                PlayerPrefs.Save();
            }
            finally
            {
                storageSaveKey = SaveKey;
                storageBackupKey = BackupKey;
                data = originalData;
                revision = originalRevision;
                readOnlyFutureSave = originalReadOnly;
                Changed = originalChanged;
                adminSandbox = originalAdminSandbox;
            }
        }
    }
#endif

    private static string Checksum(string payload, long savedRevision)
    {
        unchecked
        {
            uint hash = 2166136261;
            for (int i = 0; i < payload.Length; i++) hash = (hash ^ payload[i]) * 16777619;
            hash = (hash ^ (uint)savedRevision) * 16777619;
            hash = (hash ^ (uint)(savedRevision >> 32)) * 16777619;
            return hash.ToString("X8");
        }
    }
}
