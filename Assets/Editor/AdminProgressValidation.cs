using System;
using System.Reflection;
using UnityEngine;

// Uses disposable validation keys only. Never reads, snapshots, or overwrites
// the player's actual PlayerPrefs wallet; the outer cached state is opaque.
public static class AdminProgressValidation
{
    private const BindingFlags Private = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly string[] Modes =
    {
        "landscape_precise", "landscape_inertia", "portrait_precise", "portrait_inertia"
    };
    private static int checks;

    public static int Validate()
    {
        checks = 0;
        object externalData = Field("data").GetValue(null);
        object externalSandbox = Field("adminSandbox").GetValue(null);
        object externalChanged = Field("Changed").GetValue(null);
        using (PlayerProgress.UseIsolatedStorageForValidation(out string primary, out string backup))
            ValidateTransactions(primary, backup);
        Check(ReferenceEquals(externalData, Field("data").GetValue(null)) &&
            ReferenceEquals(externalSandbox, Field("adminSandbox").GetValue(null)) &&
            ReferenceEquals(externalChanged, Field("Changed").GetValue(null)),
            "Validation scope restores opaque outer data, sandbox and event subscriptions");
        using (PlayerProgress.UseIsolatedStorageForValidation(out string primary, out string backup))
            ValidateFutureSave(primary, backup);
        using (PlayerProgress.UseIsolatedStorageForValidation(out string primary, out string backup))
            ValidateSessionReset(primary, backup);
        // Deliberately leave an inner sandbox open: disposing isolated storage
        // must discard it, not let its test currency escape into the outer cache.
        using (PlayerProgress.UseIsolatedStorageForValidation(out _, out _))
        {
            Check(!PlayerProgress.AdminSandboxActive, "Validation starts outside an outer sandbox");
            PlayerProgress.AddCoins(11);
            PlayerProgress.BeginAdminSandbox();
            PlayerProgress.AdjustAdminCoins(int.MaxValue);
        }
        Check(ReferenceEquals(externalData, Field("data").GetValue(null)) &&
            ReferenceEquals(externalSandbox, Field("adminSandbox").GetValue(null)) &&
            ReferenceEquals(externalChanged, Field("Changed").GetValue(null)),
            "Disposing an active inner sandbox cannot leak it into normal progress");
        Debug.Log($"ADMIN_PROGRESS_PASS isolatedChecks={checks}");
        return checks;
    }

    private static void ValidateTransactions(string primary, string backup)
    {
        PlayerProgress.Reload();
        PlayerProgress.AddCoins(5000);
        Check(PlayerProgress.TryPurchase("cat_cocoa") && PlayerProgress.Equip("cat_cocoa"), "Fixture owns an equipped cat");
        Check(PlayerProgress.TryPurchase("clothing_ocean") && PlayerProgress.Equip("clothing_ocean"), "Fixture owns equipped clothing");
        Check(PlayerProgress.TryUpgrade(PlayerProgress.Upgrade.Magnet, Modes[0]) &&
            PlayerProgress.TryUpgrade(PlayerProgress.Upgrade.Duration, Modes[3]), "Fixture has independent non-max upgrades");
        PlayerProgress.Flush();
        string originalPrimary = PlayerPrefs.GetString(primary), originalBackup = PlayerPrefs.GetString(backup);
        object originalData = Field("data").GetValue(null);
        long originalRevision = (long)Field("revision").GetValue(null);
        int originalCoins = PlayerProgress.Coins;
        int changed = 0;
        Action onChanged = () => changed++;
        PlayerProgress.Changed += onChanged;
        try
        {
            PlayerProgress.BeginAdminSandbox();
            Check(PlayerProgress.AdminSandboxActive && changed == 1, "Begin activates sandbox and refreshes listeners once");
            Check(!ReferenceEquals(originalData, Field("data").GetValue(null)), "Sandbox data is a separate deep copy");
            Check(PlayerProgress.Coins == originalCoins, "Initial sandbox wallet copies current normal balance");
            foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
            {
                Check(PlayerProgress.Owns(item.Id), "Sandbox unlocks " + item.Id);
                Check((item.Price < 0 || PlayerProgress.TryPurchase(item.Id)) && PlayerProgress.Equip(item.Id),
                    "Unlocked sandbox item can be equipped: " + item.Id);
            }
            foreach (string mode in Modes)
            foreach (PlayerProgress.Upgrade upgrade in new[] { PlayerProgress.Upgrade.Magnet, PlayerProgress.Upgrade.Duration })
            {
                Check(PlayerProgress.GetUpgradeLevel(upgrade, mode) == 3, "Sandbox max upgrade for " + mode + "/" + upgrade);
                Check(!PlayerProgress.TryUpgrade(upgrade, mode), "Already-max sandbox upgrade keeps its normal cap");
                for (int level = 0; level <= 3; level++)
                {
                    Check(PlayerProgress.SetAdminUpgradeLevel(upgrade, mode, level) &&
                        PlayerProgress.GetUpgradeLevel(upgrade, mode) == level,
                        "Sandbox upgrade level can be explicitly set: " + mode + "/" + upgrade + "/" + level);
                }
            }
            Check(!PlayerProgress.SetAdminUpgradeLevel(PlayerProgress.Upgrade.Magnet, Modes[0], -1) &&
                !PlayerProgress.SetAdminUpgradeLevel(PlayerProgress.Upgrade.Magnet, Modes[0], 4) &&
                !PlayerProgress.SetAdminUpgradeLevel(PlayerProgress.Upgrade.Magnet, "invalid", 1) &&
                !PlayerProgress.SetAdminUpgradeLevel((PlayerProgress.Upgrade)99, Modes[0], 1),
                "Invalid admin upgrade levels, modes and kinds are rejected");
            PlayerProgress.AdjustAdminCoins(int.MaxValue);
            Check(PlayerProgress.Coins == 9999999, "Positive extreme clamps without integer overflow");
            PlayerProgress.AddCoins(int.MaxValue);
            Check(PlayerProgress.Coins == 9999999, "Normal coin collection respects sandbox cap");
            PlayerProgress.AdjustAdminCoins(-1);
            Check(PlayerProgress.Coins == 9999998, "Signed negative delta subtracts coins");
            PlayerProgress.AdjustAdminCoins(int.MinValue);
            Check(PlayerProgress.Coins == 0, "Negative extreme clamps at zero");
            PlayerProgress.AdjustAdminCoins(-123);
            PlayerProgress.AddCoins(-10);
            Check(PlayerProgress.Coins == 0, "Negative adjustment cannot underflow and normal negative grants stay ignored");
            PlayerProgress.AddCoins(12);
            Check(PlayerProgress.Coins == 12, "Normal coin collection changes only sandbox memory");
            object sandbox = Field("adminSandbox").GetValue(null);
            int beforeRepeat = changed;
            PlayerProgress.BeginAdminSandbox();
            Check(ReferenceEquals(sandbox, Field("adminSandbox").GetValue(null)) &&
                PlayerProgress.Coins == 12 && changed == beforeRepeat, "Repeated Begin is idempotent and does not re-snapshot fake progress");
            // The catalog can grow with new same-slot rewards. Explicitly form
            // this three-slot fixture instead of depending on catalog order.
            PlayerProgress.Equip("accessory_none");
            Check(PlayerProgress.Equip("accessory_ghoul_hair") &&
                PlayerProgress.Equip("accessory_ghoul_mask") &&
                PlayerProgress.Equip("accessory_headphones"), "Fixture explicitly equips three compatible accessories");
            PlayerProgress.Reload();
            Check(PlayerProgress.AdminSandboxActive && PlayerProgress.Coins == 12 &&
                PlayerProgress.IsEquipped("accessory_ghoul_hair") &&
                PlayerProgress.IsEquipped("accessory_ghoul_mask") &&
                PlayerProgress.IsEquipped("accessory_headphones"),
                "Reload preserves all three accessory slots in the active in-memory sandbox");
            PlayerProgress.Flush();
            Unchanged(primary, backup, originalPrimary, originalBackup, "All sandbox transactions/Reload/Flush preserve durable bytes");
            Check((long)Field("revision").GetValue(null) == originalRevision, "Sandbox transactions do not consume normal save revisions");
            int beforeEnd = changed;
            PlayerProgress.EndAdminSandbox();
            Check(!PlayerProgress.AdminSandboxActive && changed == beforeEnd + 1, "End refreshes listeners once");
            Check(ReferenceEquals(originalData, Field("data").GetValue(null)) && PlayerProgress.Coins == originalCoins,
                "End restores the original cache and wallet exactly");
            Check(PlayerProgress.IsEquipped("cat_cocoa") && PlayerProgress.IsEquipped("clothing_ocean") &&
                PlayerProgress.IsEquipped("accessory_none") && !PlayerProgress.Owns("cat_rose") && !PlayerProgress.Owns("accessory_star"),
                "Sandbox equipment and unlocks cannot enter normal inventory");
            foreach (string mode in Modes)
            {
                Check(PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Magnet, mode) == (mode == Modes[0] ? 1 : 0),
                    "Original per-mode magnet upgrade restored");
                Check(PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Duration, mode) == (mode == Modes[3] ? 1 : 0),
                    "Original per-mode duration upgrade restored");
            }
            beforeEnd = changed;
            PlayerProgress.EndAdminSandbox();
            PlayerProgress.AdjustAdminCoins(int.MaxValue);
            Check(!PlayerProgress.SetAdminUpgradeLevel(PlayerProgress.Upgrade.Magnet, Modes[0], 3),
                "Upgrade mutation outside sandbox is rejected");
            Check(changed == beforeEnd && PlayerProgress.Coins == originalCoins, "End/adjust outside sandbox cannot grant currency or emit changes");
            Unchanged(primary, backup, originalPrimary, originalBackup, "Exiting sandbox keeps both saved copies byte-identical");
            Check(PlayerProgress.TryPurchase("cat_rose"), "Normal purchases resume after exit");
            Check((long)Field("revision").GetValue(null) == originalRevision + 1 &&
                PlayerProgress.Coins == originalCoins - 160, "The next normal transaction uses the restored revision and balance");
        }
        finally
        {
            PlayerProgress.Changed -= onChanged;
            PlayerProgress.EndAdminSandbox();
        }
    }

    private static void ValidateFutureSave(string primary, string backup)
    {
        PlayerProgress.Reload();
        PlayerProgress.AddCoins(91);
        string current = PlayerPrefs.GetString(primary);
        int version = (int)Field("CurrentVersion").GetRawConstantValue();
        string future = current.Replace("\"version\":" + version, "\"version\":" + (version + 1));
        Check(future != current, "Future-save fixture changes the current schema version");
        PlayerPrefs.SetString(primary, future);
        PlayerProgress.Reload();
        string originalBackup = PlayerPrefs.GetString(backup);
        int originalCoins = PlayerProgress.Coins;
        Check((bool)Field("readOnlyFutureSave").GetValue(null), "Fixture detects future-version protection");
        PlayerProgress.BeginAdminSandbox();
        PlayerProgress.AdjustAdminCoins(10000);
        Check(PlayerProgress.Equip("accessory_glasses"), "Disposable future-save copy is usable in sandbox");
        PlayerProgress.Flush();
        PlayerProgress.EndAdminSandbox();
        Check((bool)Field("readOnlyFutureSave").GetValue(null), "Original future-version protection is restored");
        PlayerProgress.AddCoins(999);
        Check(!PlayerProgress.Equip("cat_ginger") && PlayerProgress.Coins == originalCoins,
            "Leaving sandbox cannot bypass read-only future-save restrictions");
        PlayerProgress.Flush();
        Unchanged(primary, backup, future, originalBackup, "Future primary and backup are preserved exactly");
    }

    private static void ValidateSessionReset(string primary, string backup)
    {
        PlayerProgress.Reload();
        PlayerProgress.AddCoins(43);
        string originalPrimary = PlayerPrefs.GetString(primary), originalBackup = PlayerPrefs.GetString(backup);
        PlayerProgress.BeginAdminSandbox();
        PlayerProgress.AdjustAdminCoins(500000);
        typeof(PlayerProgress).GetMethod("ResetSession", Private).Invoke(null, null);
        Check(!PlayerProgress.AdminSandboxActive, "Subsystem session reset always leaves admin mode");
        Check(PlayerProgress.Coins == 43 && !PlayerProgress.Owns("accessory_star"),
            "Reset discards cheat data before the next normal load");
        Unchanged(primary, backup, originalPrimary, originalBackup, "Session reset does not persist sandbox data");
        PlayerProgress.AddCoins(1);
        Check(PlayerProgress.Coins == 44, "Normal progress remains writable after a session reset");
    }

    private static FieldInfo Field(string name) => typeof(PlayerProgress).GetField(name, Private);

    private static void Unchanged(string primary, string backup, string expectedPrimary, string expectedBackup, string message)
    {
        Check(PlayerPrefs.GetString(primary) == expectedPrimary && PlayerPrefs.GetString(backup) == expectedBackup, message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Admin progress validation: " + message);
        checks++;
    }
}
