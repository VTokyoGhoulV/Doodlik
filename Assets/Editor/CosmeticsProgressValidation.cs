using System;
using System.Collections.Generic;
using UnityEngine;

// Isolated progression checks: no real wallet, records, or player settings are touched.
public static class CosmeticsProgressValidation
{
    public const string PromoCodeEnvironmentVariable = "DOODLIK_TEST_PROMO_CODE";
    private static int checks;

    public static void Run()
    {
        Validate();
    }

    [Serializable]
    private sealed class Envelope
    {
        public int version;
        public long revision;
        public string payload;
        public string checksum;
    }

    [Serializable]
    private sealed class LegacySave
    {
        public int coins;
        public List<string> owned = new List<string>();
        public string cat = "cat_ginger";
        public string clothing = "clothing_original";
        public string accessory = "accessory_none";
        public List<string> accessories = new List<string>();
        public string trail = "trail_none";
    }

    public static int Validate()
    {
        checks = 0;
        string promoCode = Environment.GetEnvironmentVariable(PromoCodeEnvironmentVariable);
        using (PlayerProgress.UseIsolatedStorageForValidation(out string primary, out string backup))
        {
            ValidateCatalog();
            ValidateAccessorySlots();
            ValidateAchievements(primary);
            ValidatePromo(primary, promoCode);
            ValidateSandbox(primary, backup, promoCode);
            ValidateV2Migration(primary, backup);
            ValidateV3AstronautMigration(primary, backup);
        }
        Debug.Log($"[CosmeticsProgressValidation] PASS: {checks} isolated checks; promoSuccessTested={!string.IsNullOrWhiteSpace(promoCode)}.");
        return checks;
    }

    private static void ValidateCatalog()
    {
        Dictionary<string, PlayerProgress.Item> catalog = new Dictionary<string, PlayerProgress.Item>();
        foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
        {
            Require(!catalog.ContainsKey(item.Id), "Catalog IDs are unique: " + item.Id);
            catalog.Add(item.Id, item);
        }
        Require(catalog["cat_ginger"].Name == "Дудлик", "The original cat is named Doodlik without changing its ID.");
        foreach (string id in new[]
        {
            "cat_calico", "cat_siamese", "cat_snow", "cat_tuxedo",
            "clothing_overalls", "clothing_starry", "clothing_raincoat", "clothing_hoodie",
            "accessory_headphones", "accessory_bow", "accessory_aviator"
        }) Require(catalog[id].Price > 0, "Additional store cosmetic has a price: " + id);
        foreach (string id in new[]
        {
            "accessory_astronaut_helmet", "clothing_astronaut", "cat_ghoul", "clothing_ghoul",
            "accessory_coin_crown", "accessory_ghoul_mask", "accessory_ghoul_hair"
        })
        {
            Require(catalog[id].Price < 0, "Reward cannot be bought: " + id);
            Require(!PlayerProgress.Owns(id), "New player has not earned reward: " + id);
            Require(!string.IsNullOrEmpty(PlayerProgress.UnlockDescription(id)), "Reward shows unlock requirement: " + id);
        }
        PlayerProgress.AddCoins(100000);
        Require(!catalog.ContainsKey("cat_astronaut") && !PlayerProgress.TryPurchase("cat_astronaut") &&
            !PlayerProgress.TryPurchase("accessory_astronaut_helmet") && !PlayerProgress.TryPurchase("accessory_coin_crown") &&
            !PlayerProgress.TryPurchase("accessory_ghoul_hair"), "Locked rewards reject purchases despite a rich wallet.");
    }

    private static void ValidateAccessorySlots()
    {
        foreach (string id in new[]
        {
            "accessory_flower", "accessory_glasses", "accessory_tufts", "accessory_star",
            "accessory_headphones", "accessory_bow", "accessory_aviator"
        }) Require(PlayerProgress.TryPurchase(id), "Bought slot test accessory: " + id);

        Require(PlayerProgress.Equip("accessory_bow") && PlayerProgress.Equip("accessory_glasses") &&
            PlayerProgress.Equip("accessory_tufts"), "Head, face, and ears equip together.");
        IReadOnlyList<string> chosen = PlayerProgress.EquippedAccessories;
        Require(chosen.Count == 3 && PlayerProgress.IsEquipped("accessory_bow") &&
            PlayerProgress.IsEquipped("accessory_glasses") && PlayerProgress.IsEquipped("accessory_tufts"),
            "Three distinct slots coexist.");
        Require(PlayerProgress.Equip("accessory_aviator") && !PlayerProgress.IsEquipped("accessory_glasses") &&
            PlayerProgress.IsEquipped("accessory_bow") && PlayerProgress.IsEquipped("accessory_tufts"),
            "New face accessory replaces only the old face accessory.");
        Require(PlayerProgress.Equip("accessory_headphones") && !PlayerProgress.IsEquipped("accessory_tufts") &&
            PlayerProgress.IsEquipped("accessory_bow") && PlayerProgress.IsEquipped("accessory_aviator"),
            "Headphones replace only ear tufts.");
        Require(PlayerProgress.Equip("accessory_bow") && !PlayerProgress.IsEquipped("accessory_bow") &&
            PlayerProgress.EquippedAccessories.Count == 2, "Equipping a selected accessory toggles it off.");
        PlayerProgress.Reload();
        Require(PlayerProgress.IsEquipped("accessory_headphones") && PlayerProgress.IsEquipped("accessory_aviator") &&
            PlayerProgress.EquippedAccessories.Count == 2, "Accessory combination survives save reload.");
        Require(PlayerProgress.Equip("accessory_none") && PlayerProgress.IsEquipped("accessory_none") &&
            PlayerProgress.EquippedAccessories.Count == 0, "None clears every slot.");
    }

    private static void ValidateAchievements(string primary)
    {
        int coins = PlayerProgress.Coins;
        int arrival = PlayerProgress.AstronautScore;
        Require(arrival == WorldZones.StartScore(WorldZones.Zone.Moon),
            "Astronaut threshold is the entrance to space.");
        Require(!PlayerProgress.AwardHeightUnlocks(arrival - 1, true) && !PlayerProgress.AwardHeightUnlocks(arrival, false),
            "Astronaut does not unlock below threshold or in an ineligible attempt.");
        long before = Read(primary).revision;
        Require(PlayerProgress.AwardHeightUnlocks(PlayerProgress.AstronautScore, true) &&
            PlayerProgress.Owns("accessory_astronaut_helmet") && PlayerProgress.Owns("clothing_astronaut"),
            "Reaching space unlocks helmet accessory and white suit together.");
        Require(Read(primary).revision == before + 1 && !PlayerProgress.AwardHeightUnlocks(7000, true),
            "Achievement bundle commits once and cannot be awarded repeatedly.");
        Require(PlayerProgress.TryPurchase("cat_cocoa") && PlayerProgress.Equip("cat_cocoa") &&
            PlayerProgress.Equip("clothing_astronaut") && PlayerProgress.Equip("accessory_astronaut_helmet") &&
            PlayerProgress.IsEquipped("cat_cocoa") && PlayerProgress.IsEquipped("accessory_astronaut_helmet"),
            "Helmet and spacesuit equip independently over another cat skin.");
        coins = PlayerProgress.Coins;
        Require(!PlayerProgress.AwardAirCoinAchievement(false) && !PlayerProgress.Owns("accessory_coin_crown"),
            "Ineligible air-coin attempts cannot unlock the crown.");
        Require(PlayerProgress.AwardAirCoinAchievement(true) && PlayerProgress.Owns("accessory_coin_crown") &&
            !PlayerProgress.AwardAirCoinAchievement(true), "Air-coin crown unlocks once.");
        Require(PlayerProgress.Coins == coins, "Achievements do not change currency.");
        PlayerProgress.Reload();
        Require(PlayerProgress.Owns("accessory_astronaut_helmet") && PlayerProgress.Owns("clothing_astronaut") &&
            PlayerProgress.IsEquipped("cat_cocoa") && PlayerProgress.IsEquipped("accessory_astronaut_helmet") &&
            PlayerProgress.Owns("accessory_coin_crown"), "Achievement items and selected look survive reload.");
    }

    private static void ValidatePromo(string primary, string promoCode)
    {
        int coins = PlayerProgress.Coins;
        Require(!PlayerProgress.TryRedeemCode("wrong", out string failure) && !string.IsNullOrEmpty(failure),
            "Unknown code gives feedback and no reward.");
        if (string.IsNullOrWhiteSpace(promoCode))
        {
            Debug.Log("[CosmeticsProgressValidation] PROMO_SUCCESS_SKIPPED: set DOODLIK_TEST_PROMO_CODE locally to test redemption.");
            return;
        }

        long before = Read(primary).revision;
        string variant = " " + promoCode.Trim().ToLowerInvariant().Replace('-', '_') + " ";
        Require(PlayerProgress.TryRedeemCode(variant, out string success) &&
            !string.IsNullOrEmpty(success), "Known code accepts case and separator variations.");
        Require(PlayerProgress.Owns("cat_ghoul") && PlayerProgress.Owns("clothing_ghoul") &&
            PlayerProgress.Owns("accessory_ghoul_mask") && PlayerProgress.Owns("accessory_ghoul_hair"),
            "Redeeming unlocks complete Ghoul set.");
        Require(Read(primary).revision == before + 1 &&
            !PlayerProgress.TryRedeemCode(promoCode, out string repeated) &&
            !string.IsNullOrEmpty(repeated), "Promo redemption is durable and one-time.");
        Require(PlayerProgress.Equip("accessory_ghoul_hair") && PlayerProgress.Equip("accessory_ghoul_mask") &&
            PlayerProgress.IsEquipped("accessory_ghoul_hair") && PlayerProgress.IsEquipped("accessory_ghoul_mask"),
            "Ghoul hair and mask fit in distinct accessory slots.");
        Require(PlayerProgress.Coins == coins, "Promo does not change currency.");
        PlayerProgress.Reload();
        Require(PlayerProgress.IsEquipped("accessory_ghoul_hair") && PlayerProgress.IsEquipped("accessory_ghoul_mask") &&
            !PlayerProgress.TryRedeemCode(promoCode, out _),
            "Ghoul combination and redeemed marker survive reload.");
    }

    private static void ValidateSandbox(string primary, string backup, string promoCode)
    {
        string oldPrimary = PlayerPrefs.GetString(primary);
        string oldBackup = PlayerPrefs.GetString(backup);
        PlayerProgress.BeginAdminSandbox();
        Require(PlayerProgress.AdminSandboxActive, "Admin sandbox starts.");
        foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
            Require(PlayerProgress.Owns(item.Id), "Admin sandbox owns every catalog item: " + item.Id);
        Require(!PlayerProgress.AwardHeightUnlocks(PlayerProgress.AstronautScore, true) &&
            !PlayerProgress.AwardAirCoinAchievement(true),
            "Sandbox cannot write achievement progress to the ordinary save.");
        if (!string.IsNullOrWhiteSpace(promoCode))
            Require(!PlayerProgress.TryRedeemCode(promoCode, out _),
                "Sandbox cannot redeem the reward code.");
        PlayerProgress.EndAdminSandbox();
        Require(PlayerPrefs.GetString(primary) == oldPrimary && PlayerPrefs.GetString(backup) == oldBackup,
            "Sandbox leaves both durable saves byte-for-byte unchanged.");
    }

    private static void ValidateV2Migration(string primary, string backup)
    {
        LegacySave legacy = new LegacySave { coins = 37, accessory = "accessory_glasses" };
        legacy.owned.Add("accessory_glasses");
        legacy.owned.Add("future_item_retained");
        string payload = JsonUtility.ToJson(legacy);
        PlayerPrefs.SetString(primary, JsonUtility.ToJson(new Envelope
        {
            version = 2,
            revision = 9,
            payload = payload,
            checksum = Checksum(payload, 9)
        }));
        PlayerPrefs.DeleteKey(backup);
        PlayerProgress.Reload();
        Require(PlayerProgress.Coins == 37 && PlayerProgress.EquippedAccessories.Count == 1 &&
            PlayerProgress.IsEquipped("accessory_glasses"), "v2 single accessory becomes the v3 accessory list without spending coins.");
        Envelope migrated = Read(primary);
        Require(migrated.version == 9 && migrated.revision == 10 && Read(backup).version == 9,
            "Both recovery copies migrate to v9 with one revision increment.");
        Require(migrated.payload.Contains("future_item_retained"), "Migration retains unknown owned item IDs.");
        string migratedPrimary = PlayerPrefs.GetString(primary);
        string migratedBackup = PlayerPrefs.GetString(backup);
        PlayerProgress.Reload();
        PlayerProgress.Reload();
        Require(PlayerProgress.Coins == 37 && PlayerProgress.IsEquipped("accessory_glasses") &&
            PlayerPrefs.GetString(primary) == migratedPrimary && PlayerPrefs.GetString(backup) == migratedBackup,
            "Repeated v4 loading does not remigrate or rewrite the previous accessory.");
    }

    private static void ValidateV3AstronautMigration(string primary, string backup)
    {
        LegacySave legacy = new LegacySave
        {
            coins = 123,
            cat = "cat_astronaut",
            clothing = "clothing_astronaut",
            accessory = "accessory_flower",
            accessories = new List<string> { "accessory_flower", "accessory_glasses", "accessory_tufts" },
            owned = new List<string>
            {
                "cat_astronaut", "clothing_astronaut", "accessory_flower", "accessory_glasses", "accessory_tufts"
            }
        };
        string payload = JsonUtility.ToJson(legacy);
        PlayerPrefs.SetString(primary, JsonUtility.ToJson(new Envelope
        {
            version = 3,
            revision = 21,
            payload = payload,
            checksum = Checksum(payload, 21)
        }));
        PlayerPrefs.DeleteKey(backup);
        PlayerProgress.Reload();
        Require(PlayerProgress.Coins == 123 && PlayerProgress.Owns("accessory_astronaut_helmet") &&
            PlayerProgress.IsEquipped("cat_ginger") && PlayerProgress.IsEquipped("clothing_astronaut") &&
            PlayerProgress.IsEquipped("accessory_astronaut_helmet") &&
            !PlayerProgress.IsEquipped("accessory_flower") && PlayerProgress.IsEquipped("accessory_glasses") &&
            PlayerProgress.IsEquipped("accessory_tufts"),
            "Equipped v3 astronaut cat becomes Doodlik with helmet; suit, face, ears, and coins survive.");
        Envelope migrated = Read(primary);
        Require(migrated.version == 9 && migrated.revision == 22 && Read(backup).version == 9 &&
            migrated.payload.Contains("cat_astronaut"),
            "v3 helmet migration writes both recovery copies without discarding retired ownership data.");
        string savedPrimary = PlayerPrefs.GetString(primary);
        string savedBackup = PlayerPrefs.GetString(backup);
        PlayerProgress.Reload();
        Require(PlayerProgress.IsEquipped("accessory_astronaut_helmet") &&
            PlayerPrefs.GetString(primary) == savedPrimary && PlayerPrefs.GetString(backup) == savedBackup,
            "v4 reload does not repeat helmet migration or rewrite the save.");

        legacy.cat = "cat_cocoa";
        legacy.owned.Add("cat_cocoa");
        payload = JsonUtility.ToJson(legacy);
        PlayerPrefs.SetString(primary, JsonUtility.ToJson(new Envelope
        {
            version = 3,
            revision = 31,
            payload = payload,
            checksum = Checksum(payload, 31)
        }));
        PlayerPrefs.DeleteKey(backup);
        PlayerProgress.Reload();
        Require(PlayerProgress.Owns("accessory_astronaut_helmet") && PlayerProgress.IsEquipped("cat_cocoa") &&
            PlayerProgress.IsEquipped("accessory_flower") && !PlayerProgress.IsEquipped("accessory_astronaut_helmet"),
            "Owned but unequipped v3 astronaut reward transfers without changing the selected cat or accessory.");
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

    private static Envelope Read(string key) => JsonUtility.FromJson<Envelope>(PlayerPrefs.GetString(key));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Cosmetics progression validation failed: " + message);
        checks++;
    }
}
