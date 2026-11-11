using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Invoked only by the isolated batch menu runner; no user profile is used.
public static class AdminToolsValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Validate()
    {
        Require(Application.isBatchMode && Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-"), "Isolated QA project only");
        AdminTools tools = UnityEngine.Object.FindAnyObjectByType<AdminTools>();
        GameMenuUI menu = UnityEngine.Object.FindAnyObjectByType<GameMenuUI>();
        Require(AdminTools.IsAvailable && tools != null && !AdminTools.IsOpen, "Admin component exists only as an available closed panel");
        float previousScale = Time.timeScale;
        int normalCoins = PlayerProgress.Coins;
        string normalPrimary = PlayerPrefs.GetString(PlayerProgress.SaveKey), normalBackup = PlayerPrefs.GetString(PlayerProgress.BackupKey);
        try
        {
            tools.Open();
            Require(AdminTools.IsOpen && PlayerProgress.AdminSandboxActive && GameMenuUI.BlocksGameplayInput && Time.timeScale == 0f,
                "Opening pauses gameplay, blocks controls and enters sandbox");
            foreach (PlayerProgress.Item item in PlayerProgress.Catalog) Require(PlayerProgress.Owns(item.Id), "All wardrobe items unlocked");
            TMP_InputField field = (TMP_InputField)typeof(AdminTools).GetField("amount", Private).GetValue(tools);
            field.text = "123";
            Require(tools.ChangeCoins(1) && PlayerProgress.Coins == normalCoins + 123, "Coin amount grants correctly");
            Require(tools.ChangeCoins(-1) && PlayerProgress.Coins == normalCoins, "Coin amount subtracts correctly");
            field.text = "0";
            Require(!tools.ChangeCoins(1) && PlayerProgress.Coins == normalCoins, "Zero input cannot mutate wallet");
            field.text = "-12";
            Require(!tools.ChangeCoins(-1) && PlayerProgress.Coins == normalCoins, "Negative input cannot invert withdrawal");
            field.text = "5000";
            tools.ChangeCoins(1);
            AdminTools.SetCheatMode(true);
            Require(AdminTools.CheatMode, "Cheat switch enabled in sandbox");
            Canvas.ForceUpdateCanvases();
            RectTransform card = (RectTransform)typeof(AdminTools).GetField("card", Private).GetValue(tools);
            CheckVisible(card);
            foreach (TMP_Text label in card.GetComponentsInChildren<TMP_Text>())
                Require(!label.isTextOverflowing, "Admin label fits: " + label.text);
            Capture("Admin-Panel");
            tools.Close();
            Require(!AdminTools.IsOpen && Time.timeScale == previousScale && PlayerProgress.AdminSandboxActive,
                "Closing restores the previous pause state and retains sandbox");
            PlayerProgress.Equip("cat_ginger");
            PlayerProgress.Equip("clothing_original");
            PlayerProgress.Equip("accessory_tufts");
            PlayerProgress.Equip("trail_none");
            FrontEndUI home = menu.GetComponent<FrontEndUI>();
            home.ShowHome(); home.Refresh();
            Capture("Tufts-Home");
            tools.Open();
            Require(PlayerProgress.Coins == normalCoins + 5000, "Repeated open keeps the test balance");
            tools.EndSandbox();
            Require(!PlayerProgress.AdminSandboxActive && !AdminTools.CheatMode && !AdminTools.IsOpen && PlayerProgress.Coins == normalCoins,
                "Exit restores normal progress and turns cheats off");
            Require(PlayerPrefs.GetString(PlayerProgress.SaveKey) == normalPrimary && PlayerPrefs.GetString(PlayerProgress.BackupKey) == normalBackup,
                "UI operations keep both normal saves byte-identical");
            Debug.Log("ADMIN_UI_PASS: opening/pause/input, coin grants/withdrawals/invalid input, all unlocked, cheat toggle, reopen, exact normal-save restoration.");
        }
        finally
        {
            tools.EndSandbox();
            Time.timeScale = previousScale;
        }
    }

    private static void CheckVisible(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Rect safe = Screen.safeArea;
        foreach (Vector3 corner in corners)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, corner);
            Require(point.x >= safe.xMin - 2f && point.x <= safe.xMax + 2f && point.y >= safe.yMin - 2f && point.y <= safe.yMax + 2f,
                "Admin card fits the current safe area");
        }
    }

    private static void Capture(string name) => typeof(JourneyPlayValidation).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { name });
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Admin UI validation: " + message);
    }
}
