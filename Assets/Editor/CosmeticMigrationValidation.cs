using System;
using System.Collections.Generic;
using UnityEngine;

// Synchronous save-system checks. All writes use unique disposable validation
// keys; neither the player's wallet nor settings/records are read or replaced.
public static class CosmeticMigrationValidation
{
    private static int checks;
    private static readonly string[] Modes =
    {
        "landscape_precise", "landscape_inertia", "portrait_precise", "portrait_inertia"
    };

    [Serializable]
    private sealed class Envelope
    {
        public int version;
        public long revision;
        public string payload;
        public string checksum;
    }

    [Serializable]
    private sealed class Fixture
    {
        public int coins;
        public List<string> owned = new List<string>();
        public string cat = "cat_ginger";
        public string scarf = "scarf_none";
        public string clothing;
        public string accessory;
        public string trail = "trail_none";
        public List<UpgradeFixture> upgrades = new List<UpgradeFixture>();
    }

    [Serializable]
    private sealed class UpgradeFixture
    {
        public string mode;
        public int magnet;
        public int duration;
    }

    public static int Validate()
    {
        checks = 0;
        using (PlayerProgress.UseIsolatedStorageForValidation(out string primary, out string backup))
        {
            ValidateDefaults(primary, backup);
            ValidateMigration(primary, backup);
            ValidateRecovery(primary, backup);
            ValidateNewSlots(primary, backup);
            ValidateFutureProtection(primary, backup);
        }
        Debug.Log($"[CosmeticMigrationValidation] PASS: {checks} isolated checks.");
        return checks;
    }

    private static void ValidateDefaults(string primary, string backup)
    {
        ResetFixture(primary, backup);
        Check((int)PlayerProgress.Category.Cat == 0 && (int)PlayerProgress.Category.Clothing == 1 &&
            (int)PlayerProgress.Category.Accessory == 2 && (int)PlayerProgress.Category.Trail == 3,
            "Category values are stable");
        Check(PlayerProgress.Coins == 0, "New wallet is empty");
        HashSet<string> catalogIds = new HashSet<string>();
        HashSet<PlayerProgress.Category> categories = new HashSet<PlayerProgress.Category>();
        foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
        {
            Check(!string.IsNullOrEmpty(item.Id) && catalogIds.Add(item.Id), "Catalog IDs remain unique and stable");
            categories.Add(item.Category);
        }
        Check(categories.Count == Enum.GetValues(typeof(PlayerProgress.Category)).Length,
            "Catalog includes every supported cosmetic category");
        Check(PlayerProgress.EquippedId(PlayerProgress.Category.Cat) == "cat_ginger", "Default cat");
        Check(PlayerProgress.EquippedId(PlayerProgress.Category.Clothing) == "clothing_original", "Default clothing");
        Check(PlayerProgress.EquippedId(PlayerProgress.Category.Accessory) == "accessory_none", "Default accessory");
        Check(PlayerProgress.EquippedId(PlayerProgress.Category.Trail) == "trail_none", "Default trail");
        foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
        {
            Check(!item.Id.StartsWith("scarf_"), "No retired scarf in catalog");
            if (item.Price == 0) Check(PlayerProgress.Owns(item.Id), "Free defaults are owned");
        }
        Check(!PlayerProgress.TryPurchase("scarf_berry") && !PlayerProgress.Equip("scarf_mint"),
            "Retired scarves cannot be purchased or equipped");
    }

    private static Fixture OldSave()
    {
        Fixture old = new Fixture
        {
            coins = 41,
            cat = "cat_cocoa",
            scarf = "scarf_mint",
            trail = "trail_petals",
            owned = new List<string>
            {
                "cat_cocoa", "trail_petals", "scarf_none", "scarf_berry", "scarf_berry",
                "scarf_mint", "scarf_mint", "unknown_kept", "scarf_future_unknown"
            }
        };
        for (int i = 0; i < Modes.Length; i++)
            old.upgrades.Add(new UpgradeFixture { mode = Modes[i], magnet = i, duration = 3 - i });
        old.upgrades.Add(new UpgradeFixture { mode = "future_mode", magnet = 5, duration = 8 });
        return old;
    }

    private static void ValidateMigration(string primary, string backup)
    {
        Put(primary, backup, Encode(1, 17, OldSave()), null);
        Check(PlayerProgress.Coins == 201, "Each purchased scarf refunded once despite duplicate IDs");
        Check(PlayerProgress.EquippedId(PlayerProgress.Category.Cat) == "cat_cocoa", "Old cat retained");
        Check(PlayerProgress.EquippedId(PlayerProgress.Category.Trail) == "trail_petals", "Old trail retained");
        Check(PlayerProgress.EquippedId(PlayerProgress.Category.Clothing) == "clothing_original", "Migration gives default clothing");
        Check(PlayerProgress.EquippedId(PlayerProgress.Category.Accessory) == "accessory_none", "Migration gives no accessory");
        Envelope saved = Read(primary);
        Fixture migrated = JsonUtility.FromJson<Fixture>(saved.payload);
        Check(saved.version == 9 && Read(backup).version == 9, "Both durable copies migrate immediately to v9");
        Check(saved.revision == 18, "Migration advances revision once");
        Check(!saved.payload.Contains("\"scarf\":"), "Retired scarf selection no longer serialized");
        Check(!migrated.owned.Contains("scarf_berry") && !migrated.owned.Contains("scarf_mint") &&
            !migrated.owned.Contains("scarf_none"), "Retired owned IDs removed");
        Check(migrated.owned.Contains("unknown_kept") && migrated.owned.Contains("scarf_future_unknown"),
            "Unknown owned IDs preserved");
        Check(migrated.upgrades.Exists(value => value.mode == "future_mode" && value.magnet == 5 && value.duration == 8),
            "Unknown mode upgrades preserved");
        for (int i = 0; i < Modes.Length; i++)
        {
            Check(PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Magnet, Modes[i]) == i,
                "Existing magnet upgrade preserved for " + Modes[i]);
            Check(PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Duration, Modes[i]) == 3 - i,
                "Existing duration upgrade preserved for " + Modes[i]);
        }
        string migratedJson = PlayerPrefs.GetString(primary);
        PlayerProgress.Reload();
        PlayerProgress.Reload();
        Check(PlayerProgress.Coins == 201 && PlayerPrefs.GetString(primary) == migratedJson,
            "Repeated reload does not refund or rewrite migration");

        foreach (string scarf in new[] { "scarf_berry", "scarf_mint" })
        {
            Fixture single = new Fixture { coins = 8, scarf = scarf, owned = new List<string> { scarf, scarf } };
            Put(primary, backup, Encode(1, 1, single), null);
            Check(PlayerProgress.Coins == 8 + (scarf == "scarf_berry" ? 70 : 90), "Single unique scarf refund: " + scarf);
        }
        Put(primary, backup, Encode(1, 1, new Fixture { coins = 12, scarf = "scarf_mint" }), null);
        Check(PlayerProgress.Coins == 12, "Unowned equipped scarf does not create a refund");
        Put(primary, backup, Encode(1, 1, new Fixture { coins = 9999990, owned = new List<string> { "scarf_berry" } }), null);
        Check(PlayerProgress.Coins == 9999999, "Migration respects wallet upper bound");
    }

    private static void ValidateRecovery(string primary, string backup)
    {
        Put(primary, backup, "{broken", Encode(1, 4, OldSave()));
        Check(PlayerProgress.Coins == 201 && Read(primary).version == 9 && Read(backup).version == 9,
            "Corrupt primary recovers and migrates valid old backup");
        string migrated = PlayerPrefs.GetString(backup);
        PlayerPrefs.SetString(primary, "corrupt after migration");
        PlayerProgress.Reload();
        Check(PlayerProgress.Coins == 201 && PlayerPrefs.GetString(primary) == migrated,
            "Recovery from migrated backup never adds another refund");
        PlayerProgress.Reload();
        Check(PlayerProgress.Coins == 201, "Recovery followed by reload remains idempotent");

        Fixture newer = new Fixture { coins = 500, clothing = "clothing_original", accessory = "accessory_none" };
        Put(primary, backup, Encode(1, 2, OldSave()), Encode(2, 3, newer));
        Check(PlayerProgress.Coins == 500, "Newer valid backup wins without obsolete refunds");
        string brokenChecksum = Encode(1, 7, OldSave()).Replace("\"checksum\":\"", "\"checksum\":\"BAD");
        Put(primary, backup, brokenChecksum, Encode(2, 6, newer));
        Check(PlayerProgress.Coins == 500, "Invalid checksum cannot override valid backup");
        Put(primary, backup, "bad", "also bad");
        Check(PlayerProgress.Coins == 0 && PlayerProgress.EquippedId(PlayerProgress.Category.Accessory) == "accessory_none",
            "Two invalid copies use safe defaults");
        PlayerProgress.AddCoins(3);
        Check(Read(primary).version == 9 && Read(backup).version == 9, "Next valid write recreates recovery copy");
    }

    private static void ValidateNewSlots(string primary, string backup)
    {
        ResetFixture(primary, backup);
        PlayerProgress.AddCoins(10000);
        string[] equipped = { "cat_rose", "clothing_ocean", "accessory_flower", "trail_honey" };
        for (int i = 0; i < equipped.Length; i++)
        {
            Check(PlayerProgress.TryPurchase(equipped[i]) && PlayerProgress.Equip(equipped[i]), "Purchase/equip slot " + i);
            for (int previous = 0; previous <= i; previous++)
                Check(PlayerProgress.EquippedId((PlayerProgress.Category)previous) == equipped[previous],
                    "Changing one cosmetic slot preserves the others");
        }
        PlayerProgress.Reload();
        for (int i = 0; i < equipped.Length; i++)
            Check(PlayerProgress.EquippedId((PlayerProgress.Category)i) == equipped[i], "Four equipped slots survive reload");
        int coins = PlayerProgress.Coins;
        Check(PlayerProgress.TryPurchase("clothing_ocean") && PlayerProgress.Coins == coins,
            "Owned item cannot be charged twice");

        for (int mode = 0; mode < Modes.Length; mode++)
        {
            for (int level = 0; level < mode; level++)
                Check(PlayerProgress.TryUpgrade(PlayerProgress.Upgrade.Magnet, Modes[mode]), "Upgrade isolated mode");
        }
        PlayerProgress.Reload();
        for (int mode = 0; mode < Modes.Length; mode++)
        {
            Check(PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Magnet, Modes[mode]) == mode,
                "Four mode upgrade slots remain independent");
            Check(PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Duration, Modes[mode]) == 0,
                "Upgrade type remains independent");
        }
    }

    private static void ValidateFutureProtection(string primary, string backup)
    {
        string future = Encode(10, 40, OldSave());
        string legacy = Encode(1, 3, OldSave());
        foreach (bool futureInPrimary in new[] { true, false })
        {
            Put(primary, backup, futureInPrimary ? future : legacy, futureInPrimary ? legacy : future);
            string beforePrimary = PlayerPrefs.GetString(primary);
            string beforeBackup = PlayerPrefs.GetString(backup);
            int beforeCoins = PlayerProgress.Coins;
            PlayerProgress.AddCoins(100);
            Check(!PlayerProgress.TryPurchase("clothing_ocean") && !PlayerProgress.Equip("clothing_original") &&
                !PlayerProgress.TryUpgrade(PlayerProgress.Upgrade.Magnet, Modes[0]), "Future save remains read-only");
            PlayerProgress.Flush();
            PlayerProgress.Reload();
            Check(PlayerProgress.Coins == beforeCoins && PlayerPrefs.GetString(primary) == beforePrimary &&
                PlayerPrefs.GetString(backup) == beforeBackup, "Future primary/backup are never replaced or migrated");
        }
    }

    private static void ResetFixture(string primary, string backup) => Put(primary, backup, null, null);

    private static void Put(string primary, string backup, string primaryJson, string backupJson)
    {
        if (primaryJson == null) PlayerPrefs.DeleteKey(primary); else PlayerPrefs.SetString(primary, primaryJson);
        if (backupJson == null) PlayerPrefs.DeleteKey(backup); else PlayerPrefs.SetString(backup, backupJson);
        PlayerProgress.Reload();
    }

    private static Envelope Read(string key) => JsonUtility.FromJson<Envelope>(PlayerPrefs.GetString(key));

    private static string Encode(int version, long revision, Fixture value)
    {
        string payload = JsonUtility.ToJson(value);
        return JsonUtility.ToJson(new Envelope { version = version, revision = revision, payload = payload,
            checksum = Checksum(payload, revision) });
    }

    private static string Checksum(string payload, long revision)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in payload) hash = (hash ^ character) * 16777619;
            hash = (hash ^ (uint)revision) * 16777619;
            hash = (hash ^ (uint)(revision >> 32)) * 16777619;
            return hash.ToString("X8");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Cosmetic migration validation failed: " + message);
        checks++;
    }
}
