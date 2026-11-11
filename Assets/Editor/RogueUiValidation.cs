#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Called by the existing isolated presenter runner once per actual viewport.
// Only its disposable save namespaces are ever snapshotted or mutated here.
public static class RogueUiValidation
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static int checks;

    private sealed class Snapshot : IDisposable
    {
        private readonly Type owner;
        private readonly string[] names;
        private readonly object[] values;
        private readonly string primary, backup, primaryBytes, backupBytes;
        private readonly bool hadPrimary, hadBackup;
        public Snapshot(Type type, string primaryField, string backupField, string expectedPrefix, params string[] fields)
        {
            owner = type; names = fields; values = new object[names.Length];
            primary = (string)Field(primaryField).GetValue(null); backup = (string)Field(backupField).GetValue(null);
            Check(primary.StartsWith(expectedPrefix, StringComparison.Ordinal) && backup == primary + "_Backup",
                "Workshop UI uses only the outer runner's disposable progression namespaces.");
            hadPrimary = PlayerPrefs.HasKey(primary); hadBackup = PlayerPrefs.HasKey(backup);
            primaryBytes = PlayerPrefs.GetString(primary); backupBytes = PlayerPrefs.GetString(backup);
            for (int i = 0; i < names.Length; i++)
            {
                object value = Field(names[i]).GetValue(null);
                // Some ordinary profile methods mutate their data in place.
                values[i] = names[i] == "data" && value != null ? JsonUtility.FromJson(JsonUtility.ToJson(value), value.GetType()) : value;
            }
        }
        private FieldInfo Field(string field) => owner.GetField(field, Private) ?? throw new InvalidOperationException("Missing QA field " + field);
        public void Dispose()
        {
            if (hadPrimary) PlayerPrefs.SetString(primary, primaryBytes); else PlayerPrefs.DeleteKey(primary);
            if (hadBackup) PlayerPrefs.SetString(backup, backupBytes); else PlayerPrefs.DeleteKey(backup);
            for (int i = 0; i < names.Length; i++) Field(names[i]).SetValue(null, values[i]);
            PlayerPrefs.Save();
        }
    }

    public static int ValidateHome(FrontEndUI front, GameMenuUI menu, Action<RectTransform> inspect,
        Action<string, RectTransform> capture, string viewport)
    {
        Check(Application.isPlaying && Application.isBatchMode && Application.dataPath.Replace('\\', '/').Contains("/.utmp/"),
            "Workshop interactions require disposable batch PlayMode.");
        Check(front != null && menu != null && menu.CanSelectGameMode && !RunFlow.RunActive && Time.timeScale == 0f &&
            AudioListener.volume == 0f && front.IsExpeditionStashVisible, "Start from a silent real home stash.");
        checks = 0;
        string primary = (string)typeof(PlayerProgress).GetField("storageSaveKey", Private).GetValue(null);
        string backup = (string)typeof(PlayerProgress).GetField("storageBackupKey", Private).GetValue(null);
        string before = PlayerPrefs.GetString(primary), beforeBackup = PlayerPrefs.GetString(backup);
        int coins = PlayerProgress.Coins;
        using (var ordinary = new Snapshot(typeof(PlayerProgress), "storageSaveKey", "storageBackupKey", "DoodlikValidation_",
            "data", "revision", "readOnlyFutureSave", "adminSandbox", "Changed"))
        using (var cargo = new Snapshot(typeof(ExpeditionProgress), "primaryKey", "backupKey", "DoodlikExpeditionValidation_",
            "data", "revision", "readOnly", "settledIds", "lastResult", "Changed", "liveRunId"))
        {
            Seed(ExpeditionFindKind.FireflyJar, ExpeditionFindKind.FireflyJar, ExpeditionFindKind.CloudWool);
            Seed(ExpeditionFindKind.SparkDust, ExpeditionFindKind.SparkDust, ExpeditionFindKind.SparkDust);
            Seed(ExpeditionFindKind.SparkDust, ExpeditionFindKind.SparkDust, ExpeditionFindKind.SparkDust);
            Seed(ExpeditionFindKind.StarShard, ExpeditionFindKind.StarShard);
            Click(front.transform, "Мастерская");
            Check(front.IsExpeditionWorkshopVisible && !front.IsExpeditionStashVisible, "The actual stash button opens the workshop.");
            ExpeditionWorkshopUI workshop = (ExpeditionWorkshopUI)typeof(FrontEndUI).GetField("expeditionWorkshop", Instance).GetValue(front);
            Check(workshop != null && workshop.IsTrainingVisible, "Workshop opens its training tab.");
            int startCoins = PlayerProgress.Coins;
            foreach (ExpeditionTrainingKind kind in new[] { ExpeditionTrainingKind.Vitality, ExpeditionTrainingKind.Claws, ExpeditionTrainingKind.Fortune })
            {
                int price = PlayerProgress.ExpeditionTrainingPrice(kind), level = PlayerProgress.GetExpeditionTrainingLevel(kind);
                Check(price > 0 && startCoins >= price, "Disposable profile can afford the first training purchase.");
                Click(workshop.transform, "Purchase training " + kind);
                Check(PlayerProgress.GetExpeditionTrainingLevel(kind) == level + 1 && PlayerProgress.Coins == startCoins - price,
                    "Actual training action purchases exactly one level for its displayed price.");
                startCoins -= price;
            }
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(FindButton(workshop.transform, "Purchase training Vitality").gameObject);
            Click(workshop.transform, "Purchase training Vitality");
            Check(PlayerProgress.GetExpeditionTrainingLevel(ExpeditionTrainingKind.Vitality) == ExpeditionTraining.MaxLevel(ExpeditionTrainingKind.Vitality),
                "Vitality reaches its bounded permanent cap.");
            UnityEngine.UI.Button capped = FindButton(workshop.transform, "Purchase training Vitality");
            int cappedCoins = PlayerProgress.Coins; string cappedBytes = PlayerPrefs.GetString(primary);
            Check(!capped.interactable, "A max-level training button is disabled.");
            GameObject selection = UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            Check(!GameplayPreferences.IsDesktopBuild || selection != null && selection != capped.gameObject && selection.GetComponent<UnityEngine.UI.Selectable>().IsInteractable(),
                "Desktop focus leaves a newly capped button for an available action or Back.");
            capped.onClick.Invoke();
            Check(PlayerProgress.Coins == cappedCoins && PlayerPrefs.GetString(primary) == cappedBytes,
                "Even a synthetic repeated callback cannot charge for a capped level.");
            Check(!FindButton(workshop.transform, "Purchase training Fortune").interactable,
                "Unaffordable next level is not presented as purchasable.");
            workshop.ShowTraining(); // Clear feedback from the synthetic disabled callback before the visual capture.
            inspect(workshop.Card); capture("Workshop-Training-" + viewport, workshop.Card);
            Click(workshop.transform, "Рецепты");
            int jars = ExpeditionProgress.Count(ExpeditionFindKind.FireflyJar), wool = ExpeditionProgress.Count(ExpeditionFindKind.CloudWool);
            Click(workshop.transform, "Craft expedition ward");
            Check(ExpeditionProgress.PreparedWards == 1 && ExpeditionProgress.TakeWardNextRun &&
                ExpeditionProgress.Count(ExpeditionFindKind.FireflyJar) == jars - 2 && ExpeditionProgress.Count(ExpeditionFindKind.CloudWool) == wool - 1,
                "Actual recipe atomically trades the shown materials for one prepared ward.");
            Click(workshop.transform, "Toggle prepared ward");
            Check(!ExpeditionProgress.TakeWardNextRun && ExpeditionProgress.PreparedWards == 1, "Leaving a ward at home does not consume it.");
            Click(workshop.transform, "Toggle prepared ward");
            Check(ExpeditionProgress.TakeWardNextRun, "Ward preparation can be re-enabled.");
            int sparks = ExpeditionProgress.Count(ExpeditionFindKind.SparkDust), stars = ExpeditionProgress.Count(ExpeditionFindKind.StarShard);
            Click(workshop.transform, "Unlock hunter charm");
            Check(ExpeditionProgress.HunterCharmUnlocked && ExpeditionProgress.Count(ExpeditionFindKind.SparkDust) == sparks - 3 &&
                ExpeditionProgress.Count(ExpeditionFindKind.StarShard) == stars - 1, "Actual hunter recipe grants one permanent unlock at its shown cost.");
            UnityEngine.UI.Button unlocked = FindButton(workshop.transform, "Unlock hunter charm");
            Check(!unlocked.interactable, "An owned material unlock is disabled.");
            unlocked.onClick.Invoke();
            Check(ExpeditionProgress.Count(ExpeditionFindKind.SparkDust) == sparks - 3 && ExpeditionProgress.Count(ExpeditionFindKind.StarShard) == stars - 1,
                "Repeated unlock callbacks cannot spend surplus materials twice.");
            ExpeditionProgress.Reload(); workshop.ShowRecipes();
            Check(ExpeditionProgress.HunterCharmUnlocked && ExpeditionProgress.PreparedWards == 1 && ExpeditionProgress.TakeWardNextRun,
                "Real recipe results and preparation survive durable reload.");
            TMPro.TMP_Text policy = (TMPro.TMP_Text)typeof(ExpeditionWorkshopUI).GetField("hint", Instance).GetValue(workshop);
            Check(policy.text.Contains("на старте") && policy.text.Contains("не возвращается"),
                "The recipe page explains charge consumption at start and the absence of restart/extraction refunds.");
            inspect(workshop.Card); capture("Workshop-Recipes-" + viewport, workshop.Card);
            string cargoPrimary = (string)typeof(ExpeditionProgress).GetField("primaryKey", Private).GetValue(null);
            string cargoBytes = PlayerPrefs.GetString(cargoPrimary); int pausedCoins = PlayerProgress.Coins;
            RunFlow.RunActive = true;
            try
            {
                workshop.Refresh();
                Check(!FindButton(workshop.transform, "Toggle prepared ward").interactable, "Preparation is locked during an active attempt.");
                FindButton(workshop.transform, "Toggle prepared ward").onClick.Invoke();
                Check(PlayerPrefs.GetString(cargoPrimary) == cargoBytes && PlayerProgress.Coins == pausedCoins,
                    "A hidden active-run callback cannot spend materials or change next-run preparation.");
            }
            finally { RunFlow.RunActive = false; }
            Check(front.GoBack() && front.IsExpeditionStashVisible && !front.IsExpeditionWorkshopVisible,
                "Back from workshop returns to the stash, not through gameplay.");
        }
        front.ShowExpeditionStash();
        Check(PlayerPrefs.GetString(primary) == before && PlayerPrefs.GetString(backup) == beforeBackup && PlayerProgress.Coins == coins,
            "Workshop fixtures restore the outer isolated wallet/wardrobe cache and both exact byte streams.");
        Debug.Log("ROGUE_UI_HOME_PASS checks=" + checks + " viewport=" + viewport + " purchaseButtons=true boundedLevels=true recipes=true preparation=true reload=true profileRestored=true");
        return checks;
    }

    private static void Seed(params ExpeditionFindKind[] kinds)
    {
        var session = new ExpeditionSession("workshop_ui_" + Guid.NewGuid().ToString("N"));
        foreach (ExpeditionFindKind kind in kinds) Check(session.TryCollect(kind), "Known material fits a disposable seed receipt.");
        Check(ExpeditionProgress.TryRecord(session.Settle(ExpeditionOutcome.Extracted)), "Only disposable cargo stash is seeded.");
    }
    private static UnityEngine.UI.Button FindButton(Transform root, string name)
    {
        foreach (UnityEngine.UI.Button button in root.GetComponentsInChildren<UnityEngine.UI.Button>())
            if (button.gameObject.name == name) return button;
        throw new InvalidOperationException("Missing visible rogue UI button " + name);
    }
    private static void Click(Transform root, string name)
    {
        UnityEngine.UI.Button button = FindButton(root, name); Check(button.interactable, "Button is interactable: " + name); button.onClick.Invoke();
    }
    private static void Check(bool condition, string message)
    {
        checks++; if (!condition) throw new InvalidOperationException("Rogue UI: " + message);
    }
}
#endif
