#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

// Deterministic migration/economy fixtures. Always two disposable namespaces;
// no real player wallet, wardrobe, record keys or build artifacts are edited.
public static class RogueEconomyValidation
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    [Serializable] private sealed class Envelope { public int version; public long revision; public string payload, checksum; }
    [Serializable] private sealed class Training { public string id; public int level; }
    [Serializable] private sealed class Mode { public string mode; public int magnet, duration; }
    [Serializable] private sealed class Stats { public int runs; public long totalCoins; }
    [Serializable] private sealed class Profile
    {
        public int coins;
        public string cat = "cat_cocoa", clothing = "clothing_original", trail = "trail_none";
        public List<string> owned = new List<string> { "cat_cocoa", "future_cosmetic" };
        public List<string> accessories = new List<string>();
        public List<string> redeemedCodes = new List<string> { "opaque-proof" };
        public List<string> completedChallenges = new List<string> { "run_coin10", "future_task" };
        public string selectedChallenge = "run_wide10";
        public List<Mode> upgrades = new List<Mode> { new Mode { mode = "portrait_precise", magnet = 2, duration = 1 } };
        public int challengeRocketLandings = 4, challengeGoldenFish = 2, challengeBeeDodges = 6;
        public Stats statistics = new Stats { runs = 4, totalCoins = 70 };
        public List<Training> expeditionTraining;
    }
    [Serializable] private sealed class Stock { public string id; public int count; }
    [Serializable] private sealed class Cargo
    {
        public List<Stock> stash = new List<Stock>();
        public List<string> settledRuns = new List<string>();
        public int preparedWards;
        public bool hunterCharmUnlocked, takeWardNextRun;
        public string lastWardRunId;
    }

    public static void Run() => Validate();
    public static int Validate()
    {
        Require(Application.isBatchMode && Application.dataPath.Replace('\\', '/').Contains("/.utmp/"),
            "Economy validation requires a disposable batch Editor.");
        checks = 0;
        bool active = RunFlow.RunActive, transitioning = RunFlow.IsTransitioning;
        float time = Time.timeScale;
        bool hadKind = PlayerPrefs.HasKey(GameplayPreferences.RunKindKey);
        int kind = PlayerPrefs.GetInt(GameplayPreferences.RunKindKey);
        ExpeditionRun originalRun = ExpeditionRun.Active;
        GameObject fixture = null;
        try
        {
            RunFlow.RunActive = RunFlow.IsTransitioning = false;
            Time.timeScale = 0f;
            PlayerPrefs.SetInt(GameplayPreferences.RunKindKey, (int)GameRunKind.Classic);
            using (PlayerProgress.UseIsolatedStorageForValidation(out string primary, out string backup))
            using (ExpeditionProgress.UseIsolatedStorageForValidation(out string cargoPrimary, out string cargoBackup))
            {
                Catalog();
                MigrationAndTraining(primary, backup);
                TrainingGuards(primary, backup);
                string normal = PlayerPrefs.GetString(primary), recovery = PlayerPrefs.GetString(backup);
                Workshop(cargoPrimary, cargoBackup);
                WardConsumption(cargoPrimary, cargoBackup, ref fixture);
                WorkshopGuards(cargoPrimary, cargoBackup);
                Check(PlayerPrefs.GetString(primary) == normal && PlayerPrefs.GetString(backup) == recovery,
                    "All workshop/cargo transactions preserve both ordinary wallet/wardrobe byte streams.");
                if (fixture != null) { SettleFixture(fixture); Object.DestroyImmediate(fixture); fixture = null; }
            }
        }
        finally
        {
            if (fixture != null) { SettleFixture(fixture); Object.DestroyImmediate(fixture); }
            typeof(ExpeditionRun).GetField("<Active>k__BackingField", PrivateStatic).SetValue(null, originalRun);
            RunFlow.RunActive = active; RunFlow.IsTransitioning = transitioning; Time.timeScale = time;
            if (hadKind) PlayerPrefs.SetInt(GameplayPreferences.RunKindKey, kind); else PlayerPrefs.DeleteKey(GameplayPreferences.RunKindKey);
            PlayerPrefs.Save();
        }
        Debug.Log("ROGUE_ECONOMY_PASS checks=" + checks +
            " normalSchema=9 cargoSchema=2 v8Migration=true v1Cargo=true atomicTraining=true atomicCraft=true" +
            " wardOncePerRun=true futureReadOnly=true adminBlocked=true ordinaryWalletIsolated=true");
        return checks;
    }

    private static void Catalog()
    {
        Check(ExpeditionTraining.All.Count == 3 && ExpeditionTraining.MaxLevel(ExpeditionTrainingKind.Vitality) == 2 &&
            ExpeditionTraining.MaxLevel(ExpeditionTrainingKind.Claws) == 1 && ExpeditionTraining.MaxLevel(ExpeditionTrainingKind.Fortune) == 2,
            "Three bounded training tracks, never a jump/speed multiplier.");
        var ids = new HashSet<string>();
        foreach (ExpeditionTrainingKind track in ExpeditionTraining.All)
        {
            Check(ids.Add(ExpeditionTraining.Id(track)) && !string.IsNullOrEmpty(ExpeditionTraining.Name(track)) &&
                !string.IsNullOrEmpty(ExpeditionTraining.Description(track)), "Training track has stable ID and readable text.");
            for (int level = 0; level < ExpeditionTraining.MaxLevel(track); level++)
                Check(ExpeditionTraining.Price(track, level) > 0, "Every actual level has a nonzero price.");
            Check(ExpeditionTraining.Price(track, -1) == 0 && ExpeditionTraining.Price(track, ExpeditionTraining.MaxLevel(track)) == 0,
                "Unknown/complete levels have no purchasable price.");
        }
        Check(!ExpeditionTraining.IsValid((ExpeditionTrainingKind)99) && ExpeditionTraining.Price((ExpeditionTrainingKind)99, 0) == 0,
            "Invalid enum cannot address a training purchase.");
    }

    private static void MigrationAndTraining(string primary, string backup)
    {
        var profile = new Profile { coins = 1000 };
        string legacy = Encode(8, 11, JsonUtility.ToJson(profile));
        PutNormal(primary, backup, legacy, legacy);
        Check(PlayerProgress.Coins == 1000 && PlayerProgress.IsEquipped("cat_cocoa") &&
            PlayerProgress.Statistics.Runs == 4 && PlayerProgress.Statistics.TotalCoins == 70,
            "Released v8 wallet, outfit and journal survive migration.");
        Envelope migrated = Read(primary);
        Check(migrated.version == 9 && migrated.revision == 12 && PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup) &&
            migrated.payload.Contains("future_cosmetic") && migrated.payload.Contains("future_task") && migrated.payload.Contains("opaque-proof"),
            "v8 migrates once to matching v9 copies without deleting unknown IDs/proofs.");
        Check(PlayerProgress.ChallengeProgressValue("mastery_rocket10") == 4 && PlayerProgress.ChallengeProgressValue("mastery_gold15") == 2 &&
            PlayerProgress.ChallengeProgressValue("mastery_bee20") == 6 && PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Magnet, "portrait_precise") == 2,
            "v8 event counters and existing per-control utility upgrades are not reset.");
        foreach (ExpeditionTrainingKind track in ExpeditionTraining.All)
            Check(PlayerProgress.GetExpeditionTrainingLevel(track) == 0, "Old saves receive no fabricated expedition training.");
        string once = PlayerPrefs.GetString(primary); PlayerProgress.Reload();
        Check(PlayerPrefs.GetString(primary) == once && PlayerProgress.Coins == 1000, "Reload does not remigrate or duplicate currency.");

        int expected = 1000;
        foreach (ExpeditionTrainingKind track in ExpeditionTraining.All)
        {
            for (int level = 0; level < ExpeditionTraining.MaxLevel(track); level++)
            {
                int price = ExpeditionTraining.Price(track, level);
                if (expected < price) { PlayerProgress.AddCoins(1000); expected += 1000; }
                Check(PlayerProgress.ExpeditionTrainingPrice(track) == price && PlayerProgress.TryUpgradeExpeditionTraining(track),
                    "A funded home purchase advances exactly one level.");
                expected -= price;
                Check(PlayerProgress.Coins == expected && PlayerProgress.GetExpeditionTrainingLevel(track) == level + 1 &&
                    PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup), "Cost and training level share both complete recovery copies.");
            }
            string capped = PlayerPrefs.GetString(primary);
            Check(!PlayerProgress.TryUpgradeExpeditionTraining(track) && PlayerProgress.Coins == expected && PlayerPrefs.GetString(primary) == capped,
                "A repeated click at the cap cannot spend another coin.");
        }
        PlayerPrefs.SetString(primary, "broken"); PlayerProgress.Reload();
        Check(PlayerProgress.Coins == expected && PlayerProgress.GetExpeditionTrainingLevel(ExpeditionTrainingKind.Claws) == 1 &&
            PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Magnet, "portrait_precise") == 2 && PlayerProgress.IsEquipped("cat_cocoa"),
            "Corrupt-primary recovery keeps both paid training and its wallet debit, plus classic upgrades/outfit.");

        profile = new Profile { coins = 333, expeditionTraining = new List<Training> {
            new Training { id = "vitality", level = 999 }, new Training { id = "vitality", level = 1 },
            new Training { id = "claws", level = -4 }, new Training { id = "future_branch", level = 7 } } };
        string malformed = Encode(9, 20, JsonUtility.ToJson(profile)); PutNormal(primary, backup, malformed, malformed);
        Check(PlayerProgress.GetExpeditionTrainingLevel(ExpeditionTrainingKind.Vitality) == 2 &&
            PlayerProgress.GetExpeditionTrainingLevel(ExpeditionTrainingKind.Claws) == 0 && PlayerProgress.TryUpgradeExpeditionTraining(ExpeditionTrainingKind.Claws),
            "Known levels normalize duplicate/negative/oversized entries without blocking a valid purchase.");
        Check(PlayerPrefs.GetString(primary).Contains("future_branch") && PlayerProgress.Coins == 113,
            "An unrelated unknown training ID survives the next valid wallet transaction.");
        string preTraining = Encode(8, 21, JsonUtility.ToJson(profile)); PutNormal(primary, backup, preTraining, preTraining);
        Check(PlayerProgress.GetExpeditionTrainingLevel(ExpeditionTrainingKind.Vitality) == 0 && PlayerProgress.Coins == 333,
            "A pre-training v8 fixture cannot invent new progression from similarly named extra fields.");
    }

    private static void TrainingGuards(string primary, string backup)
    {
        string empty = Encode(9, 29, JsonUtility.ToJson(new Profile { coins = 0 })); PutNormal(primary, backup, empty, empty);
        Check(!PlayerProgress.TryUpgradeExpeditionTraining(ExpeditionTrainingKind.Vitality) &&
            !PlayerProgress.TryUpgradeExpeditionTraining((ExpeditionTrainingKind)99) && PlayerPrefs.GetString(primary) == empty,
            "An unfunded or invalid purchase cannot spend currency or create a training level.");
        string normal = Encode(9, 30, JsonUtility.ToJson(new Profile { coins = 500 })); PutNormal(primary, backup, normal, normal);
        RunFlow.RunActive = true;
        Check(!PlayerProgress.TryUpgradeExpeditionTraining(ExpeditionTrainingKind.Vitality), "Training cannot be bought mid-attempt.");
        RunFlow.RunActive = false; RunFlow.IsTransitioning = true;
        Check(!PlayerProgress.TryUpgradeExpeditionTraining(ExpeditionTrainingKind.Vitality), "Training cannot be bought during scene transition.");
        RunFlow.IsTransitioning = false; PlayerProgress.BeginAdminSandbox();
        Check(!PlayerProgress.TryUpgradeExpeditionTraining(ExpeditionTrainingKind.Vitality), "Admin cannot purchase persistent training even with an inflated wallet.");
        PlayerProgress.EndAdminSandbox();
        Check(PlayerPrefs.GetString(primary) == normal && PlayerProgress.Coins == 500, "Blocked purchases preserve bytes and currency.");
        Field(typeof(PlayerProgress), "revision").SetValue(null, long.MaxValue);
        Check(!PlayerProgress.TryUpgradeExpeditionTraining(ExpeditionTrainingKind.Vitality) && PlayerProgress.Coins == 500 &&
            PlayerProgress.GetExpeditionTrainingLevel(ExpeditionTrainingKind.Vitality) == 0, "Unavailable revision cannot leave a half-spent cached wallet.");
        PlayerProgress.Reload();
        foreach (bool primaryFuture in new[] { true, false })
        {
            string future = normal.Replace("\"version\":9", "\"version\":10");
            PutNormal(primary, backup, primaryFuture ? future : normal, primaryFuture ? normal : future);
            string a = PlayerPrefs.GetString(primary), b = PlayerPrefs.GetString(backup);
            Check(!PlayerProgress.ExpeditionTrainingAvailable && !PlayerProgress.TryUpgradeExpeditionTraining(ExpeditionTrainingKind.Vitality),
                "Either future copy blocks training.");
            PlayerProgress.Flush(); PlayerProgress.Reload();
            Check(PlayerPrefs.GetString(primary) == a && PlayerPrefs.GetString(backup) == b, "Future ordinary byte streams survive purchases/reload unchanged.");
        }
        PutNormal(primary, backup, normal, normal);
    }

    private static void Workshop(string primary, string backup)
    {
        Cargo old = new Cargo { stash = new List<Stock> { new Stock { id = "firefly_jar", count = 6 },
            new Stock { id = "cloud_wool", count = 3 }, new Stock { id = "star_shard", count = 1 },
            new Stock { id = "spark_dust", count = 3 }, new Stock { id = "future_material", count = 7 } } };
        string legacy = Encode(1, 4, JsonUtility.ToJson(old)); PutCargo(primary, backup, legacy, legacy);
        Check(ExpeditionProgress.PreparedWards == 0 && !ExpeditionProgress.HunterCharmUnlocked &&
            ExpeditionProgress.Count(ExpeditionFindKind.FireflyJar) == 6, "Stage-one cargo v1 keeps materials and starts workshop ownership empty.");
        old.preparedWards = 9; old.hunterCharmUnlocked = old.takeWardNextRun = true;
        string preWorkshop = Encode(1, 4, JsonUtility.ToJson(old)); PutCargo(primary, backup, preWorkshop, preWorkshop);
        Check(ExpeditionProgress.PreparedWards == 0 && !ExpeditionProgress.HunterCharmUnlocked && !ExpeditionProgress.TakeWardNextRun,
            "Stage-one schema cannot fabricate equipment from similarly named optional fields.");
        Check(ExpeditionProgress.TryCraftWard() && ExpeditionProgress.PreparedWards == 1 && ExpeditionProgress.TakeWardNextRun &&
            ExpeditionProgress.Count(ExpeditionFindKind.FireflyJar) == 4 && ExpeditionProgress.Count(ExpeditionFindKind.CloudWool) == 2 &&
            Read(primary).version == 2 && PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup),
            "Ward craft migrates to v2 and atomically commits material debit, stock and preparation choice.");
        Check(ExpeditionProgress.TryUnlockHunterCharm() && ExpeditionProgress.HunterCharmUnlocked &&
            ExpeditionProgress.Count(ExpeditionFindKind.SparkDust) == 0 && ExpeditionProgress.Count(ExpeditionFindKind.StarShard) == 0,
            "Rare charm uses the dedicated enemy material and star shard in one ownership transaction.");
        string unlocked = PlayerPrefs.GetString(primary);
        Check(!ExpeditionProgress.TryUnlockHunterCharm() && PlayerPrefs.GetString(primary) == unlocked,
            "A second charm click cannot spend another rare material.");
        Check(ExpeditionProgress.SetTakeWardNextRun(false) && !ExpeditionProgress.TakeWardNextRun &&
            PlayerPrefs.GetString(primary).Contains("future_material"), "Preparation choice persists without deleting unknown materials.");
        PlayerPrefs.SetString(primary, "broken"); ExpeditionProgress.Reload();
        Check(ExpeditionProgress.PreparedWards == 1 && ExpeditionProgress.HunterCharmUnlocked && !ExpeditionProgress.TakeWardNextRun &&
            ExpeditionProgress.Count(ExpeditionFindKind.CloudWool) == 2, "Backup recovery does not reopen spent materials or lose crafted ownership.");

        old = new Cargo { stash = new List<Stock> { new Stock { id = "firefly_jar", count = 4 } } };
        string missing = Encode(2, 10, JsonUtility.ToJson(old)); PutCargo(primary, backup, missing, missing);
        Check(!ExpeditionProgress.TryCraftWard() && ExpeditionProgress.Count(ExpeditionFindKind.FireflyJar) == 4 &&
            ExpeditionProgress.PreparedWards == 0 && PlayerPrefs.GetString(primary) == missing,
            "Missing second ingredient does not debit the first ingredient or consume a revision.");
        var rareMissing = new Cargo { stash = new List<Stock> { new Stock { id = "spark_dust", count = 3 } } };
        string missingStar = Encode(2, 10, JsonUtility.ToJson(rareMissing)); PutCargo(primary, backup, missingStar, missingStar);
        Check(!ExpeditionProgress.TryUnlockHunterCharm() && ExpeditionProgress.Count(ExpeditionFindKind.SparkDust) == 3 &&
            !ExpeditionProgress.HunterCharmUnlocked && PlayerPrefs.GetString(primary) == missingStar,
            "Missing rare second ingredient cannot partially debit enemy materials.");
        old.stash.Add(new Stock { id = "cloud_wool", count = 2 });
        string funded = Encode(2, 11, JsonUtility.ToJson(old)); PutCargo(primary, backup, funded, funded);
        Field(typeof(ExpeditionProgress), "revision").SetValue(null, long.MaxValue);
        Check(!ExpeditionProgress.TryCraftWard() && ExpeditionProgress.Count(ExpeditionFindKind.FireflyJar) == 4 &&
            ExpeditionProgress.PreparedWards == 0, "Unavailable durable commit cannot leave partially spent cached cargo.");
        ExpeditionProgress.Reload();
        Check(ExpeditionProgress.TryCraftWard() && ExpeditionProgress.TryCraftWard() && ExpeditionProgress.PreparedWards == 2,
            "Two deliberate crafts produce exactly two consumable charges.");
    }

    private static void WardConsumption(string primary, string backup, ref GameObject fixture)
    {
        PlayerPrefs.SetInt(GameplayPreferences.RunKindKey, (int)GameRunKind.Expedition);
        fixture = new GameObject("Rogue economy eligible-run fixture"); fixture.hideFlags = HideFlags.DontSave;
        Player owner = fixture.AddComponent<Player>(); owner.enabled = false;
        typeof(Player).GetField("<RunKind>k__BackingField", PrivateInstance).SetValue(owner, GameRunKind.Expedition);
        ExpeditionRun run = ExpeditionRun.Attach(owner); run.enabled = false;
        var session = new ExpeditionSession("ward_eligible_" + Guid.NewGuid().ToString("N"));
        typeof(ExpeditionRun).GetField("<Session>k__BackingField", PrivateInstance).SetValue(run, session);
        RunFlow.RunActive = true; RunFlow.IsTransitioning = false; Time.timeScale = 1f;
        Check(ExpeditionProgress.TryConsumePreparedWard() && ExpeditionProgress.PreparedWards == 1,
            "An eligible live expedition consumes one prepared charge durably.");
        string consumed = PlayerPrefs.GetString(primary);
        Check(!ExpeditionProgress.TryConsumePreparedWard() && ExpeditionProgress.PreparedWards == 1 && PlayerPrefs.GetString(primary) == consumed,
            "Repeated begin/consume callbacks cannot spend two charges in the same run.");
        ExpeditionProgress.Reload();
        Check(!ExpeditionProgress.TryConsumePreparedWard() && ExpeditionProgress.PreparedWards == 1,
            "Reload in the same live run retains consumption idempotence.");
        var test = new ExpeditionSession("ward_ineligible_" + Guid.NewGuid().ToString("N"), false);
        typeof(ExpeditionRun).GetField("<Session>k__BackingField", PrivateInstance).SetValue(run, test);
        Check(!ExpeditionProgress.TryConsumePreparedWard(), "An ineligible test attempt cannot consume saved supplies.");
        var next = new ExpeditionSession("ward_next_" + Guid.NewGuid().ToString("N"));
        typeof(ExpeditionRun).GetField("<Session>k__BackingField", PrivateInstance).SetValue(run, next);
        Time.timeScale = 0f; Check(!ExpeditionProgress.TryConsumePreparedWard(), "A paused/home draft cannot consume a charge.");
        Time.timeScale = 1f; PlayerPrefs.SetInt(GameplayPreferences.RunKindKey, (int)GameRunKind.Classic);
        Check(!ExpeditionProgress.TryConsumePreparedWard(), "Classic mode cannot consume expedition equipment.");
        PlayerPrefs.SetInt(GameplayPreferences.RunKindKey, (int)GameRunKind.Expedition);
        Check(ExpeditionProgress.TryConsumePreparedWard() && ExpeditionProgress.PreparedWards == 0 && !ExpeditionProgress.TryConsumePreparedWard(),
            "The next eligible expedition consumes the last charge once, with no negative stock.");
        SettleFixture(fixture); Object.DestroyImmediate(fixture); fixture = null;
        RunFlow.RunActive = false; Time.timeScale = 0f;
    }

    private static void WorkshopGuards(string primary, string backup)
    {
        var cargo = new Cargo { stash = new List<Stock> { new Stock { id = "firefly_jar", count = 100 },
            new Stock { id = "cloud_wool", count = 100 }, new Stock { id = "spark_dust", count = 3 }, new Stock { id = "star_shard", count = 1 } } };
        string supported = Encode(2, 50, JsonUtility.ToJson(cargo)); PutCargo(primary, backup, supported, supported);
        RunFlow.RunActive = true;
        Check(!ExpeditionProgress.TryCraftWard() && !ExpeditionProgress.TryUnlockHunterCharm() && !ExpeditionProgress.SetTakeWardNextRun(true),
            "Workshop purchases and preparation choices cannot change mid-run.");
        RunFlow.RunActive = false; RunFlow.IsTransitioning = true;
        Check(!ExpeditionProgress.TryCraftWard(), "Workshop rejects a scene transition.");
        RunFlow.IsTransitioning = false; PlayerProgress.BeginAdminSandbox();
        Check(!ExpeditionProgress.TryCraftWard() && !ExpeditionProgress.TryUnlockHunterCharm(), "Admin cannot spend real cargo or manufacture permanent equipment.");
        PlayerProgress.EndAdminSandbox();
        Check(PlayerPrefs.GetString(primary) == supported && PlayerPrefs.GetString(backup) == supported,
            "All blocked workshop actions preserve both cargo copies exactly.");
        for (int i = 0; i < ExpeditionWorkshop.MaxPreparedWards; i++) Check(ExpeditionProgress.TryCraftWard(), "Ward storage accepts its bounded charges.");
        string cap = PlayerPrefs.GetString(primary);
        Check(!ExpeditionProgress.TryCraftWard() && ExpeditionProgress.PreparedWards == 9 && PlayerPrefs.GetString(primary) == cap,
            "Ward storage cap prevents further material debit.");
        foreach (bool primaryFuture in new[] { true, false })
        {
            string future = cap.Replace("\"version\":2", "\"version\":3");
            PutCargo(primary, backup, primaryFuture ? future : cap, primaryFuture ? cap : future);
            string a = PlayerPrefs.GetString(primary), b = PlayerPrefs.GetString(backup);
            Check(ExpeditionProgress.IsReadOnly && !ExpeditionProgress.TryCraftWard() && !ExpeditionProgress.TryUnlockHunterCharm() &&
                !ExpeditionProgress.SetTakeWardNextRun(false) && !ExpeditionProgress.TryConsumePreparedWard(), "Either future cargo copy blocks every workshop mutation.");
            ExpeditionProgress.Flush(); ExpeditionProgress.Reload();
            Check(PlayerPrefs.GetString(primary) == a && PlayerPrefs.GetString(backup) == b, "Future cargo copies remain byte-identical after workshop/reload.");
        }
    }

    private static void SettleFixture(GameObject root)
    {
        ExpeditionSession session = root.GetComponent<ExpeditionRun>()?.Session;
        session?.Invalidate(); session?.Settle(ExpeditionOutcome.Abandoned);
    }
    private static void PutNormal(string primary, string backup, string a, string b)
    {
        PlayerPrefs.SetString(primary, a); PlayerPrefs.SetString(backup, b); PlayerProgress.Reload();
    }
    private static void PutCargo(string primary, string backup, string a, string b)
    {
        PlayerPrefs.SetString(primary, a); PlayerPrefs.SetString(backup, b); ExpeditionProgress.Reload();
    }
    private static Envelope Read(string key) => JsonUtility.FromJson<Envelope>(PlayerPrefs.GetString(key));
    private static FieldInfo Field(Type type, string name) => type.GetField(name, PrivateStatic);
    private static string Encode(int version, long revision, string payload)
    {
        unchecked
        {
            uint hash = 2166136261;
            for (int i = 0; i < payload.Length; i++) hash = (hash ^ payload[i]) * 16777619;
            hash = (hash ^ (uint)revision) * 16777619; hash = (hash ^ (uint)(revision >> 32)) * 16777619;
            return JsonUtility.ToJson(new Envelope { version = version, revision = revision, payload = payload, checksum = hash.ToString("X8") });
        }
    }
    private static void Check(bool value, string message) { checks++; Require(value, message); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("Rogue economy: " + message); }
}
#endif
