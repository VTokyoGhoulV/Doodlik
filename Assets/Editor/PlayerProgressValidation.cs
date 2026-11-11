using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class PlayerProgressValidation
{
    [MenuItem("Doodlik/Validate Wallet And Wardrobe")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Wallet validation must run outside Play Mode.");

        int assertions = 0;
        using (PlayerProgress.UseIsolatedStorageForValidation(out string primary, out string backup))
        {
            Fresh(primary, backup);
            Check(PlayerProgress.Coins == 0, "New wallet is empty", ref assertions);
            Check(PlayerProgress.Owns("cat_ginger") && PlayerProgress.Owns("accessory_none") && PlayerProgress.Owns("trail_none"), "Default cosmetics owned", ref assertions);
            Check(!PlayerProgress.TryPurchase("cat_cocoa"), "Insufficient funds rejected", ref assertions);
            Check(!PlayerProgress.Equip("cat_cocoa"), "Unowned cosmetic rejected", ref assertions);
            Check(!PlayerProgress.TryPurchase("unknown") && !PlayerProgress.Equip("unknown"), "Unknown IDs rejected", ref assertions);
            PlayerProgress.AddCoins(-10);
            Check(PlayerProgress.Coins == 0, "Negative grants ignored", ref assertions);
            PlayerProgress.AddCoins(1000);
            Check(PlayerProgress.TryPurchase("cat_cocoa") && PlayerProgress.Coins == 880, "Purchase charges exactly once", ref assertions);
            Check(PlayerProgress.TryPurchase("cat_cocoa") && PlayerProgress.Coins == 880, "Repeat purchase does not charge twice", ref assertions);
            Check(PlayerProgress.Equip("cat_cocoa") && PlayerProgress.IsEquipped("cat_cocoa"), "Equip owned cat", ref assertions);
            Check(PlayerProgress.TryPurchase("accessory_glasses") && PlayerProgress.Equip("accessory_glasses"), "Equip independently by category", ref assertions);
            PlayerProgress.Reload();
            Check(PlayerProgress.Coins == 790 && PlayerProgress.IsEquipped("cat_cocoa") && PlayerProgress.IsEquipped("accessory_glasses"), "Wallet and equipped items survive reload", ref assertions);
            Check(!PlayerProgress.TryUpgrade(PlayerProgress.Upgrade.Magnet, "invalid"), "Unknown upgrade mode rejected", ref assertions);
            string[] modes = { "landscape_precise", "landscape_inertia", "portrait_precise", "portrait_inertia" };
            foreach (string mode in modes)
            {
                Check(PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Magnet, mode) == 0, "Mode starts at level zero", ref assertions);
                PlayerProgress.AddCoins(1000);
                for (int level = 0; level < 3; level++)
                    Check(PlayerProgress.TryUpgrade(PlayerProgress.Upgrade.Magnet, mode), "Upgrade within cap", ref assertions);
                Check(!PlayerProgress.TryUpgrade(PlayerProgress.Upgrade.Magnet, mode), "Upgrade cap enforced", ref assertions);
                Check(Mathf.Approximately(PlayerProgress.PickupRadiusBonus(mode), 0.36f), "Pickup bonus bounded", ref assertions);
                for (int level = 0; level < 3; level++)
                    Check(PlayerProgress.TryUpgrade(PlayerProgress.Upgrade.Duration, mode), "Duration upgrade within cap", ref assertions);
                Check(Mathf.Approximately(PlayerProgress.PowerDurationBonus(mode), 1.5f), "Duration bonus bounded", ref assertions);
            }
            PlayerProgress.Reload();
            foreach (string mode in modes)
                Check(PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Duration, mode) == 3, "Mode upgrade persists", ref assertions);

            Fresh(primary, backup);
            PlayerProgress.AddCoins(300);
            PlayerProgress.TryPurchase("accessory_flower");
            PlayerProgress.Equip("accessory_flower");
            PlayerPrefs.SetString(primary, "{broken json");
            PlayerProgress.Reload();
            Check(PlayerProgress.Coins == 230 && PlayerProgress.Owns("accessory_flower"), "Corrupt primary recovers valid backup", ref assertions);
            Check(PlayerProgress.EquippedId(PlayerProgress.Category.Accessory) == "accessory_none", "Recovery restores complete previous transaction", ref assertions);

            PlayerProgress.AddCoins(int.MaxValue);
            PlayerProgress.AddCoins(int.MaxValue);
            Check(PlayerProgress.Coins == 9999999, "Coin grants never overflow", ref assertions);
            PlayerProgress.Reload();
            Check(PlayerProgress.Coins == 9999999, "Capped balance round trips", ref assertions);

            string current = PlayerPrefs.GetString(primary);
            int version = (int)typeof(PlayerProgress).GetField("CurrentVersion",
                BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
            string future = current.Replace("\"version\":" + version, "\"version\":" + (version + 1));
            Check(future != current, "Future-save fixture changes the current schema version", ref assertions);
            PlayerPrefs.SetString(primary, future);
            PlayerProgress.Reload();
            Check(!PlayerProgress.TryPurchase("cat_rose"), "Future schema cannot be overwritten by transactions", ref assertions);
            PlayerProgress.AddCoins(1);
            Check(PlayerPrefs.GetString(primary) == future, "Future save bytes preserved", ref assertions);

            PlayerPrefs.SetString(primary, "bad");
            PlayerPrefs.SetString(backup, "bad");
            PlayerProgress.Reload();
            Check(PlayerProgress.Coins == 0 && PlayerProgress.IsEquipped("cat_ginger"), "Both corrupted copies load safe defaults", ref assertions);
            PlayerProgress.AddCoins(5);
            PlayerPrefs.SetString(primary, "bad");
            PlayerProgress.Reload();
            Check(PlayerProgress.Coins == 5, "First repaired save creates working recovery copy", ref assertions);
            Debug.Log($"Doodlik wallet validation passed: {assertions} isolated checks. Original player save untouched.");
        }
    }

    private static void Fresh(string primary, string backup)
    {
        PlayerPrefs.DeleteKey(primary);
        PlayerPrefs.DeleteKey(backup);
        PlayerProgress.Reload();
    }

    private static void Check(bool passed, string label, ref int count)
    {
        if (!passed) throw new InvalidOperationException("Wallet validation failed: " + label);
        count++;
    }
}
