#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

// Synchronous PlayMode contract fixtures. The outer UI runner supplies a real
// initialized menu and two disposable progression namespaces. No simulation
// frame advances here. The outer runner tests genuine startup/menu/reload,
// but supplies controlled landing callbacks rather than a physical playthrough.
// Natural generation/placement is covered by the separate world QA runner.
public static class ExpeditionRuntimeValidation
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static int checks;

    private sealed class IntPreference
    {
        public readonly string Key;
        public readonly bool Existed;
        public readonly int Value;
        public IntPreference(string key) { Key = key; Existed = PlayerPrefs.HasKey(key); Value = PlayerPrefs.GetInt(key); }
        public void Restore() { if (Existed) PlayerPrefs.SetInt(Key, Value); else PlayerPrefs.DeleteKey(Key); }
        public bool Unchanged => PlayerPrefs.HasKey(Key) == Existed && (!Existed || PlayerPrefs.GetInt(Key) == Value);
    }

    private sealed class CargoSnapshot : IDisposable
    {
        private readonly string primary, backup, primaryBytes, backupBytes;
        private readonly bool hasPrimary, hasBackup;
        private readonly string[] fields = { "data", "revision", "readOnly", "settledIds", "lastResult", "Changed", "liveRunId" };
        private readonly object[] values;
        public CargoSnapshot()
        {
            primary = (string)ProgressField("primaryKey").GetValue(null);
            backup = (string)ProgressField("backupKey").GetValue(null);
            Require(primary.StartsWith("DoodlikExpeditionValidation_", StringComparison.Ordinal) && backup == primary + "_Backup",
                "Only the UI runner's disposable cargo namespace may be used.");
            hasPrimary = PlayerPrefs.HasKey(primary); hasBackup = PlayerPrefs.HasKey(backup);
            primaryBytes = PlayerPrefs.GetString(primary); backupBytes = PlayerPrefs.GetString(backup);
            values = new object[fields.Length];
            for (int i = 0; i < fields.Length; i++) values[i] = ProgressField(fields[i]).GetValue(null);
            ProgressField("Changed").SetValue(null, null);
            ProgressField("liveRunId").SetValue(null, null);
            PlayerPrefs.DeleteKey(primary); PlayerPrefs.DeleteKey(backup);
            ExpeditionProgress.Reload();
        }
        public void Dispose()
        {
            if (hasPrimary) PlayerPrefs.SetString(primary, primaryBytes); else PlayerPrefs.DeleteKey(primary);
            if (hasBackup) PlayerPrefs.SetString(backup, backupBytes); else PlayerPrefs.DeleteKey(backup);
            for (int i = 0; i < fields.Length; i++) ProgressField(fields[i]).SetValue(null, values[i]);
        }
    }

    public static int Validate()
    {
        Require(Application.isPlaying && Application.isBatchMode &&
            Application.dataPath.Replace('\\', '/').Contains("/.utmp/"), "Disposable batch PlayMode only.");
        Require(!RunFlow.RunActive && !RunFlow.IsTransitioning && Time.timeScale == 0f &&
            !ExpeditionUI.IsOpen && !AdminTools.IsOpen && !PlayerProgress.AdminSandboxActive,
            "Run before the UI runner starts its natural attempt.");
        Require(AudioListener.volume == 0f, "Runtime fixtures must be silent.");
        checks = 0;
        GameMenuUI menu = Object.FindAnyObjectByType<GameMenuUI>();
        ExpeditionUI ui = Object.FindAnyObjectByType<ExpeditionUI>();
        FrontEndUI home = menu != null ? menu.GetComponent<FrontEndUI>() : null;
        Require(menu != null && ui != null && home != null && home.IsVisible &&
            Object.FindAnyObjectByType<Player>() != null, "Natural home, menu, Player and expedition UI must exist.");
        RunPowerups naturalPowers = Object.FindAnyObjectByType<Player>().GetComponent<RunPowerups>();
        int originalPickupCount = ExpeditionPickup.ActiveCount;
        string normalPrimary = (string)typeof(PlayerProgress).GetField("storageSaveKey", Static).GetValue(null);
        string normalBackup = (string)typeof(PlayerProgress).GetField("storageBackupKey", Static).GetValue(null);
        Require(normalPrimary.StartsWith("DoodlikValidation_", StringComparison.Ordinal), "Ordinary profile must also be isolated.");
        string originalNormal = PlayerPrefs.GetString(normalPrimary), originalRecovery = PlayerPrefs.GetString(normalBackup);
        bool originalNormalExists = PlayerPrefs.HasKey(normalPrimary), originalRecoveryExists = PlayerPrefs.HasKey(normalBackup);
        var preferences = new List<IntPreference>();
        foreach (string key in RunRecords.AllKeys) preferences.Add(new IntPreference(key));
        foreach (string key in RunRecords.AllExpeditionKeys) preferences.Add(new IntPreference(key));
        preferences.Add(new IntPreference(GameplayPreferences.RunKindKey));
        ExpeditionRun originalActive = ExpeditionRun.Active;
        object originalBoundRun = Get(ui, "run");
        object originalCloseFrame = typeof(ExpeditionUI).GetField("closedFrame", Static).GetValue(null);
        GameObject originalFocus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        var roots = new List<GameObject>();
        float originalTime = Time.timeScale;
        bool originalActiveFlag = RunFlow.RunActive, originalTransition = RunFlow.IsTransitioning;
        try
        {
            using (var cargo = new CargoSnapshot())
            {
                ValidateRecordsAndPreferenceRules();
                RunFlow.RunActive = false; RunFlow.IsTransitioning = false; Time.timeScale = 0f;
                GameplayPreferences.SetRunKind(GameRunKind.Expedition, false);
                home.Hide();
                Player player = NewPlayer(roots, GameRunKind.Expedition);
                ExpeditionRun run = ExpeditionRun.Attach(player); run.enabled = false;
                SetAuto(player, "Expedition", run);
                Check(run != null && run.Owner == player && !run.HasStarted && ExpeditionRun.Active == run,
                    "Expedition component attaches only to a captured expedition Player.");
                run.BeginRun();
                Check(!run.HasStarted && run.EndRun(ExpeditionOutcome.Abandoned) == null && ExpeditionProgress.TotalCount == 0,
                    "An unopened home draft cannot start, settle or create rewards.");
                Player classic = NewPlayer(roots, GameRunKind.Classic);
                Check(ExpeditionRun.Attach(classic) == null && ExpeditionRun.Active == run, "Classic Player cannot acquire expedition gameplay.");
                RunFlow.RunActive = true; Time.timeScale = 1f;
                run.BeginRun();
                ExpeditionSession session = run.Session;
                Check(session != null && session.IsEligible && run.CanCollect && player.RunKind == GameRunKind.Expedition,
                    "Live ordinary expedition starts exactly one eligible session.");
                run.BeginRun();
                Check(ReferenceEquals(session, run.Session), "Repeated Begin cannot replace an existing backpack.");
                Check(!GameplayPreferences.SetRunKind(GameRunKind.Classic, false) && player.RunKind == GameRunKind.Expedition,
                    "Selection cannot change underneath a captured active Player.");
                RunJournal journal = RunJournal.Attach(player); journal.enabled = false;
                journal.BeginRun();
                Check(!journal.Started && !journal.EndRun("fixture"), "Expedition attempts do not enter the classic journal.");
                Check(!run.TryExtract() && run.EndRun(ExpeditionOutcome.Extracted) == null && !run.IsFinished,
                    "Public extraction cannot bypass a real rest.");
                Check(!run.TryCollect(ExpeditionFindKind.None), "Actual runtime rejects unknown cargo.");
                ExpeditionPickup fullBagFind = ValidatePickupClaims(player, run, naturalPowers, roots);
                Check(!run.TryCollect(ExpeditionFindKind.FireflyJar) && run.Session.Count == 3,
                    "Full runtime backpack does not consume or auto-replace the fourth find.");
                Check(!run.ProtectSlot(2) && !run.DiscardSlot(1), "Inventory edits require the explicit paused expedition panel.");
                ui.OpenBag();
                Check(ui.IsBagVisible && Time.timeScale == 0f && !run.CanCollect, "Real bag opening pauses collection and physics.");
                ClickSlot(ui, 2, "Защитить");
                ClickSlot(ui, 1, "Выбросить");
                Check(run.Session.Slots[0] == ExpeditionFindKind.StarShard && run.Session.Slots[1] == ExpeditionFindKind.None,
                    "Actual bag protect and discard buttons alter only the intended runtime slots.");
                Call(run, "OnApplicationPause", true);
                ExpeditionProgress.Reload();
                Check(!ExpeditionProgress.RecoverInterruptedRun() && !run.IsFinished && run.Session.Count == 2 &&
                    ExpeditionProgress.TotalCount == 0 && PendingKind(0) == ExpeditionFindKind.StarShard &&
                    PendingKind(1) == ExpeditionFindKind.None,
                    "Actual mobile pause checkpoints latest protected/discarded positions without settling or banking the live bag.");
                Call(run, "OnApplicationPause", false);
                ui.Close();
                Check(!ExpeditionUI.IsOpen && ExpeditionUI.ConsumesMenuInput && Time.timeScale == 1f,
                    "Closing bag restores time but consumes this close frame.");
                ResetClosedFrame();
                // Advance only this item's retry clock, not game simulation.
                // The assertion proves the same unclaimed object survives a
                // full bag and can be accepted after an explicit UI discard.
                Set(fullBagFind, "retryAfter", 0f);
                int beforeRetry = ExpeditionPickup.ActiveCount;
                Vector2 retryPosition = fullBagFind.transform.position;
                ExpeditionPickup.CollectNear(retryPosition, Vector2.one * .05f, 20f);
                Check(run.Session.Slots[1] == ExpeditionFindKind.CloudWool && run.Session.Count == 3 &&
                    !fullBagFind.gameObject.activeSelf && ExpeditionPickup.ActiveCount == beforeRetry - 1,
                    "The SAME full-bag world item is claimed only after a real bag discard frees its slot.");
                ExpeditionPickup.CollectNear(retryPosition, Vector2.one * .05f, 20f);
                Check(run.Session.Count == 3 && ExpeditionPickup.ActiveCount == beforeRetry - 1,
                    "Repeated collision with the accepted object cannot credit it again.");
                Collider2D support = SafePlatform(roots);
                Set(player, "reachedHeight", ExpeditionRun.RestInterval - 1);
                run.RecordLanding(support); SetAuto(player, "LastHazardAuditFrame", Time.frameCount);
                run.AfterHazardAudit();
                Check(!run.IsResting, "Below-threshold landings cannot offer a rest.");
                Set(player, "reachedHeight", ExpeditionRun.RestInterval + 50); Set(player, "score", ExpeditionRun.RestInterval + 100);
                run.RecordLanding(support);
                SetAuto(player, "LastHazardAuditFrame", Time.frameCount - 1); run.AfterHazardAudit();
                Check(!run.IsResting && Time.timeScale == 1f, "Yesterday's hazard audit cannot authorize this frame's pause.");
                SetAuto(player, "LastHazardAuditFrame", Time.frameCount + 1); run.AfterHazardAudit();
                Check(!run.IsResting, "A future audit value also cannot authorize a rest.");
                SetAuto(player, "LastHazardAuditFrame", Time.frameCount); Set(player, "isDead", true); run.AfterHazardAudit();
                Check(!run.IsResting, "Death wins even after a completed audit.");
                Set(player, "isDead", false); run.AfterHazardAudit();
                Check(run.IsResting && ui.IsRestVisible && Time.timeScale == 0f && !run.CanCollect,
                    "Only the current audited live landing opens a real rest panel.");
                Check(run.NeedsPerkChoice && !run.ContinueFromRest(), "Continue cannot skip the current rest talent choice.");
                Check(run.TryChoosePerk(run.PerkChoices[0]) && !run.NeedsPerkChoice,
                    "One offered talent releases the rest continuation without banking cargo.");
                Click(ui, "Продолжить подъём");
                Check(!run.IsResting && !ExpeditionUI.IsOpen && run.NextRestHeight == ExpeditionRun.RestInterval * 2 && Time.timeScale == 1f,
                    "Actual Continue button resumes and advances the next checkpoint once.");
                ResetClosedFrame();
                Set(player, "reachedHeight", run.NextRestHeight + 30);
                run.RecordLanding(support); SetAuto(player, "LastHazardAuditFrame", Time.frameCount); run.AfterHazardAudit();
                Check(run.IsResting && run.TryExtract() && run.IsFinished && run.RewardsSaved &&
                    run.Result.Outcome == ExpeditionOutcome.Extracted && run.Result.KeptCount == 3 &&
                    ExpeditionProgress.TotalCount == 3 && ui.IsResultVisible && Time.timeScale == 0f,
                    "Real safe extraction records exactly three cargo items and opens its result.");
                ExpeditionResult settled = run.Result;
                Check(!run.TryExtract() && ReferenceEquals(run.EndRun(ExpeditionOutcome.Abandoned), settled) &&
                    ExpeditionProgress.TotalCount == 3 && !run.CanCollect && PendingId() == null,
                    "End, quit or restart callbacks cannot duplicate cargo and settled checkpoint is cleared atomically.");
                Hide(ui); ResetClosedFrame();
                Time.timeScale = 1f;
                ExpeditionPickup afterFinish = SpawnFind(ExpeditionFindKind.FireflyJar, new Vector2(8008f, 8000f), roots);
                int finishedActive = ExpeditionPickup.ActiveCount;
                ExpeditionPickup.CollectNear(afterFinish.transform.position, Vector2.one * .05f, 20f);
                Check(afterFinish.gameObject.activeSelf && ExpeditionPickup.ActiveCount == finishedActive && run.Session.Count == 3,
                    "Finished attempts cannot collect an overlapping world item, even with time resumed and no modal open.");
                Object.DestroyImmediate(afterFinish.gameObject);

                Player doomed = NewPlayer(roots, GameRunKind.Expedition);
                ExpeditionRun loss = ExpeditionRun.Attach(doomed); loss.enabled = false; SetAuto(doomed, "Expedition", loss);
                Time.timeScale = 1f; loss.BeginRun();
                Check(loss.TryCollect(ExpeditionFindKind.FireflyJar) && loss.TryCollect(ExpeditionFindKind.CloudWool),
                    "Defeat fixture collects protected and unprotected cargo.");
                Call(doomed, "Die", "Проверка экспедиции");
                Check(doomed.IsDead && loss.IsFinished && loss.Result.Outcome == ExpeditionOutcome.Lost &&
                    loss.Result.KeptCount == 1 && loss.Result.LostCount == 1 && loss.RewardsSaved &&
                    ExpeditionProgress.TotalCount == 4 && Time.timeScale == 0f,
                    "The actual Player death path freezes, settles protected cargo and shows the expedition result.");
                Hide(ui); ResetClosedFrame();

                Player abandoned = NewPlayer(roots, GameRunKind.Expedition);
                ExpeditionRun quit = ExpeditionRun.Attach(abandoned); quit.enabled = false; SetAuto(abandoned, "Expedition", quit);
                Time.timeScale = 1f; quit.BeginRun(); quit.TryCollect(ExpeditionFindKind.CloudWool); quit.TryCollect(ExpeditionFindKind.StarShard);
                Check(quit.EndRun(ExpeditionOutcome.Abandoned).KeptCount == 1 && ExpeditionProgress.TotalCount == 5,
                    "Return/restart/quit style abandonment keeps only protected cargo.");
                Object.DestroyImmediate(abandoned.gameObject);
                Check(ExpeditionProgress.TotalCount == 5, "Destruction after abandonment cannot credit a second transaction.");

                Player test = NewPlayer(roots, GameRunKind.Expedition);
                ExpeditionRun invalid = ExpeditionRun.Attach(test); invalid.enabled = false; SetAuto(test, "Expedition", invalid);
                Time.timeScale = 1f; invalid.BeginRun(); invalid.TryCollect(ExpeditionFindKind.StarShard);
                AssertImmediateAdminInvalidation(test, invalid);
                ExpeditionResult invalidResult = invalid.EndRun(ExpeditionOutcome.Abandoned);
                Check(!invalidResult.Eligible && !invalid.RewardsSaved && ExpeditionProgress.TotalCount == 5,
                    "An attempt marked as admin/test cannot bank even its protected cargo.");
                Set(test, "testAttempt", false);
                Check(ReferenceEquals(invalid.EndRun(ExpeditionOutcome.Extracted), invalidResult) && !invalidResult.Eligible &&
                    ExpeditionProgress.TotalCount == 5, "Turning testing off cannot legitimise the same settled attempt.");
                ValidateInterruptedRuntime(roots);
                Check(PlayerPrefs.GetString(normalPrimary) == originalNormal && PlayerPrefs.GetString(normalBackup) == originalRecovery &&
                    PlayerPrefs.HasKey(normalPrimary) == originalNormalExists && PlayerPrefs.HasKey(normalBackup) == originalRecoveryExists,
                    "Runtime extraction, death, abandonment and test attempts preserve both ordinary save copies.");
            }
        }
        finally
        {
            Hide(ui);
            foreach (GameObject root in roots)
                if (root != null)
                {
                    // A failing assertion can leave a fixture unsettled after
                    // the cargo snapshot was restored. Destruction must not
                    // bank that fixture into the outer validation profile.
                    root.GetComponent<ExpeditionRun>()?.Session?.Invalidate();
                    Object.DestroyImmediate(root);
                }
            typeof(ExpeditionRun).GetField("<Active>k__BackingField", Static).SetValue(null, originalActive);
            Call(ui, "Bind", originalBoundRun);
            RunFlow.RunActive = originalActiveFlag; RunFlow.IsTransitioning = originalTransition;
            foreach (IntPreference pref in preferences) pref.Restore();
            home.ShowHome();
            typeof(ExpeditionUI).GetField("closedFrame", Static).SetValue(null, originalCloseFrame);
            Time.timeScale = originalTime;
            EventSystem.current?.SetSelectedGameObject(originalFocus);
            PlayerPrefs.Save();
        }
        foreach (IntPreference pref in preferences) Check(pref.Unchanged, "Restored record/preference key: " + pref.Key);
        Check(ExpeditionPickup.ActiveCount == originalPickupCount, "All fixture world items are removed without touching natural pickup registrations.");
        Debug.Log("EXPEDITION_RUNTIME_PASS checks=" + checks +
            " recordKeys=49 capturedVariant=true noDraftRewards=true safeCurrentAudit=true bag=true extract=true death=true abandon=true" +
            " actualPickups=true sweptClaim=true wrappedClaim=true reentrantClaim=true fullBagRetry=true magnetIgnored=true stickyTest=true ordinarySaveUnchanged=true" +
            " pauseCheckpoint=true liveReloadSafe=true interruptedRecoveryOnce=true");
        return checks;
    }

    private static ExpeditionPickup ValidatePickupClaims(Player player, ExpeditionRun run, RunPowerups naturalPowers,
        List<GameObject> roots)
    {
        Require(naturalPowers != null, "Natural scene must provide the ordinary magnet owner.");
        Vector2 center = new Vector2(8000f, 8000f), half = Vector2.one * .05f;
        const float width = 20f;
        int baseline = ExpeditionPickup.ActiveCount;
        ExpeditionPickup outside = SpawnFind(ExpeditionFindKind.StarShard, center + Vector2.right, roots);
        float originalMagnet = (float)Get(naturalPowers, "magnetTime");
        try
        {
            Set(naturalPowers, "magnetTime", 30f);
            Vector3 originalPosition = outside.transform.position;
            Call(outside, "Update");
            ExpeditionPickup.CollectNear(center, half, width);
            Check(naturalPowers.MagnetTimeRemaining > 0f && outside.gameObject.activeSelf && run.Session.Count == 0 &&
                outside.transform.position == originalPosition && ExpeditionPickup.ActiveCount == baseline + 1,
                "An active real magnet neither attracts nor remotely claims expedition cargo.");
        }
        finally { Set(naturalPowers, "magnetTime", originalMagnet); }
        Object.DestroyImmediate(outside.gameObject);
        Check(ExpeditionPickup.ActiveCount == baseline, "Unclaimed fixture cleanup unregisters the world item immediately.");

        ExpeditionPickup first = SpawnFind(ExpeditionFindKind.FireflyJar, center, roots);
        Time.timeScale = 0f;
        ExpeditionPickup.CollectNear(center, half, width);
        Check(first.gameObject.activeSelf && run.Session.Count == 0 && ExpeditionPickup.ActiveCount == baseline + 1,
            "Paused overlap does not consume a world item.");
        Time.timeScale = 1f; Set(player, "isDead", true);
        ExpeditionPickup.CollectNear(center, half, width);
        Check(first.gameObject.activeSelf && run.Session.Count == 0, "A dead Player cannot claim overlapping cargo.");
        Set(player, "isDead", false); RunFlow.RunActive = false;
        ExpeditionPickup.CollectNear(center, half, width);
        Check(first.gameObject.activeSelf && run.Session.Count == 0, "Home/inactive attempts cannot claim cargo.");
        RunFlow.RunActive = true; RunFlow.IsTransitioning = true;
        ExpeditionPickup.CollectNear(center, half, width);
        Check(first.gameObject.activeSelf && run.Session.Count == 0, "Scene transitions cannot claim cargo.");
        RunFlow.IsTransitioning = false;
        ExpeditionPickup.CollectAlong(center - Vector2.right * 2f, center + Vector2.right * 2f,
            new Vector2(float.NaN, .05f), width);
        ExpeditionPickup.CollectNear(center, half, 0f);
        Check(first.gameObject.activeSelf && run.Session.Count == 0, "Malformed body or screen geometry cannot claim a find.");
        ExpeditionPickup.CollectAlong(center - Vector2.right * 2f, center + Vector2.right * 2f, half, width);
        Check(!first.gameObject.activeSelf && run.Session.Count == 1 && run.Session.Slots[0] == ExpeditionFindKind.FireflyJar &&
            ExpeditionPickup.ActiveCount == baseline, "A fast pass-through claims cargo even though both endpoints lie outside its radius.");
        ExpeditionPickup.CollectAlong(center - Vector2.right * 2f, center + Vector2.right * 2f, half, width);
        Check(run.Session.Count == 1 && ExpeditionPickup.ActiveCount == baseline, "Repeated sweep cannot claim a consumed item twice.");

        ExpeditionPickup wrapped = SpawnFind(ExpeditionFindKind.CloudWool, new Vector2(7990.1f, 8000f), roots);
        Vector2 wrapFrom = new Vector2(8009.7f, 8000f), wrapTo = new Vector2(7990.4f, 8000f);
        ExpeditionPickup.CollectAlong(wrapFrom, wrapTo, half, width);
        Check(!wrapped.gameObject.activeSelf && run.Session.Count == 2 && run.Session.Slots[1] == ExpeditionFindKind.CloudWool &&
            ExpeditionPickup.ActiveCount == baseline, "Wrapped-edge sweep claims the physically crossed copy once.");
        ExpeditionPickup.CollectAlong(wrapFrom, wrapTo, half, width);
        Check(run.Session.Count == 2, "Repeating the edge crossing does not duplicate cargo.");

        Vector2 lastCenter = center + Vector2.right * 4f;
        ExpeditionPickup last = SpawnFind(ExpeditionFindKind.StarShard, lastCenter, roots);
        int callbacks = 0;
        Action reentrant = () =>
        {
            callbacks++;
            ExpeditionPickup.CollectAlong(lastCenter - Vector2.right * 2f, lastCenter + Vector2.right * 2f, half, width);
        };
        run.Changed += reentrant;
        try { ExpeditionPickup.CollectAlong(lastCenter - Vector2.right * 2f, lastCenter + Vector2.right * 2f, half, width); }
        finally { run.Changed -= reentrant; }
        Check(callbacks == 1 && !last.gameObject.activeSelf && run.Session.Count == 3 &&
            run.Session.Slots[2] == ExpeditionFindKind.StarShard && ExpeditionPickup.ActiveCount == baseline,
            "Synchronous Changed callback re-entry cannot double-claim the same active world object.");

        ExpeditionPickup blocked = SpawnFind(ExpeditionFindKind.CloudWool, center + Vector2.right * 6f, roots);
        ExpeditionPickup.CollectNear(blocked.transform.position, half, width);
        Check(blocked.gameObject.activeSelf && run.Session.Count == 3 && ExpeditionPickup.ActiveCount == baseline + 1 &&
            !(bool)Get(blocked, "collected"), "Full backpack leaves its world item alive, unclaimed and registered.");
        return blocked;
    }

    private static ExpeditionPickup SpawnFind(ExpeditionFindKind kind, Vector2 position, List<GameObject> roots)
    {
        ExpeditionPickup pickup = ExpeditionPickup.Spawn(kind, position, Camera.main);
        Require(pickup != null, "Production cargo factory must create its mesh/material object.");
        pickup.gameObject.hideFlags = HideFlags.DontSave;
        roots.Add(pickup.gameObject);
        return pickup;
    }

    private static void AssertImmediateAdminInvalidation(Player player, ExpeditionRun run)
    {
        // The outer normal profile is already disposable. Preserve its entire
        // cache/events rather than nesting or touching ordinary save keys.
        string[] fields = { "data", "revision", "readOnlyFutureSave", "adminSandbox", "Changed" };
        var values = new object[fields.Length];
        for (int i = 0; i < fields.Length; i++) values[i] = typeof(PlayerProgress).GetField(fields[i], Static).GetValue(null);
        try
        {
            typeof(PlayerProgress).GetField("Changed", Static).SetValue(null, null);
            PlayerProgress.BeginAdminSandbox();
            player.RefreshAdminState();
            Check(player.IsTestAttempt && !run.Session.IsEligible && PendingId() == null,
                "Actual RefreshAdminState immediately invalidates cargo and revokes its durable checkpoint, without another Update frame.");
        }
        finally
        {
            for (int i = 0; i < fields.Length; i++) typeof(PlayerProgress).GetField(fields[i], Static).SetValue(null, values[i]);
        }
    }

    private static void ValidateInterruptedRuntime(List<GameObject> roots)
    {
        Player interrupted = NewPlayer(roots, GameRunKind.Expedition);
        ExpeditionRun run = ExpeditionRun.Attach(interrupted); run.enabled = false; SetAuto(interrupted, "Expedition", run);
        Time.timeScale = 1f; run.BeginRun();
        Check(run.TryCollect(ExpeditionFindKind.FireflyJar) && run.TryCollect(ExpeditionFindKind.CloudWool) &&
            PendingId() == run.Session.RunId, "Runtime pickup hooks retain an independent live cargo checkpoint.");
        Set(interrupted, "reachedHeight", 1750); Set(interrupted, "score", 1900);
        Call(run, "OnApplicationFocus", false);
        Call(run, "OnApplicationPause", true);
        int originalCount = ExpeditionProgress.TotalCount, originalLost = ExpeditionProgress.LostRuns;
        ExpeditionProgress.Reload();
        Check(!ExpeditionProgress.RecoverInterruptedRun() && !run.IsFinished && run.Session.Count == 2 &&
            ExpeditionProgress.TotalCount == originalCount, "Focus loss, suspension and a live Reload do not masquerade as abandonment.");
        ProgressField("liveRunId").SetValue(null, null); // Simulated fresh process in disposable TEST storage only.
        ExpeditionProgress.Reload();
        Check(ExpeditionProgress.RecoverInterruptedRun() && ExpeditionProgress.TotalCount == originalCount + 1 &&
            ExpeditionProgress.LostRuns == originalLost + 1 && ExpeditionProgress.LastResult.Outcome == ExpeditionOutcome.Abandoned &&
            ExpeditionProgress.LastResult.Kept[0] == ExpeditionFindKind.FireflyJar && ExpeditionProgress.LastResult.LostCount == 1 &&
            ExpeditionProgress.LastResult.Height == 1750 && PendingId() == null,
            "Simulated mobile OS kill recovers exactly the protected slot, cached metrics and one abandonment count.");
        ExpeditionProgress.Reload();
        Check(!ExpeditionProgress.RecoverInterruptedRun() && ExpeditionProgress.TotalCount == originalCount + 1,
            "Second startup or Reload cannot replay the recovered checkpoint.");
        Object.DestroyImmediate(interrupted.gameObject);
        Check(ExpeditionProgress.TotalCount == originalCount + 1,
            "A late destruction/final receipt after recovery cannot credit the same run again.");
    }

    private static object PendingData()
    {
        object data = ProgressField("data").GetValue(null);
        return data?.GetType().GetField("pendingRun").GetValue(data);
    }
    private static string PendingId()
    {
        object pending = PendingData();
        return pending == null ? null : (string)pending.GetType().GetField("runId").GetValue(pending);
    }
    private static ExpeditionFindKind PendingKind(int index)
    {
        object pending = PendingData();
        return pending == null ? ExpeditionFindKind.None : (ExpeditionFindKind)((int[])pending.GetType().GetField("slots").GetValue(pending))[index];
    }

    private static void ValidateRecordsAndPreferenceRules()
    {
        RunFlow.RunActive = false; RunFlow.IsTransitioning = false;
        PlayerPrefs.DeleteKey(GameplayPreferences.RunKindKey);
        Check(GameplayPreferences.RunKind == GameRunKind.Classic, "Missing run kind defaults to classic.");
        foreach (int corrupt in new[] { -1, 2, 99, int.MaxValue })
        {
            PlayerPrefs.SetInt(GameplayPreferences.RunKindKey, corrupt);
            Check(GameplayPreferences.RunKind == GameRunKind.Classic &&
                !GameplayPreferences.SetRunKind((GameRunKind)corrupt, false) &&
                PlayerPrefs.GetInt(GameplayPreferences.RunKindKey) == corrupt, "Corrupt run kind cannot create a third variant.");
        }
        Check(GameplayPreferences.SetRunKind(GameRunKind.Expedition, false), "Expedition can be selected before a run.");
        RunFlow.IsTransitioning = true;
        Check(!GameplayPreferences.SetRunKind(GameRunKind.Classic, false), "Selection is blocked during scene transitions.");
        RunFlow.IsTransitioning = false;
        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in RunRecords.AllKeys) Check(all.Add(key), "Classic key unique: " + key);
        foreach (string key in RunRecords.AllExpeditionKeys) Check(all.Add(key), "Expedition key disjoint: " + key);
        Check(RunRecords.AllKeys.Count == 25 && RunRecords.AllExpeditionKeys.Count == 24 && all.Count == 49,
            "Existing 25 classic keys stay intact and 24 expedition keys are appended separately.");
        for (int portrait = 0; portrait < 2; portrait++)
        for (int inertia = 0; inertia < 2; inertia++)
        for (int difficulty = 0; difficulty < 3; difficulty++)
        {
            string classic = RunRecords.ScoreKey(portrait != 0, inertia != 0, (GameDifficulty)difficulty);
            string height = RunRecords.HeightKey(portrait != 0, inertia != 0, (GameDifficulty)difficulty);
            Check(classic == RunRecords.ScoreKey(portrait != 0, inertia != 0, (GameDifficulty)difficulty, GameRunKind.Classic) &&
                height == RunRecords.HeightKey(portrait != 0, inertia != 0, (GameDifficulty)difficulty, GameRunKind.Classic),
                "Legacy three-argument records always mean classic.");
            Check(RunRecords.ScoreKey(portrait != 0, inertia != 0, (GameDifficulty)difficulty, GameRunKind.Expedition) ==
                "DoodlikExpeditionRecord_" + classic, "Variant prefix keeps all mode/difficulty combinations distinct.");
        }
        int index = 0;
        foreach (string key in RunRecords.AllKeys) PlayerPrefs.SetInt(key, 200 + index++);
        index = 0;
        foreach (string key in RunRecords.AllExpeditionKeys) PlayerPrefs.SetInt(key, 800 + index++);
        var classicSnapshot = new List<IntPreference>();
        foreach (string key in RunRecords.AllKeys) classicSnapshot.Add(new IntPreference(key));
        RunRecords.ResetCurrent();
        Check(!PlayerPrefs.HasKey(RunRecords.CurrentScoreKey) && !PlayerPrefs.HasKey(RunRecords.CurrentHeightKey),
            "Reset current deletes only the selected expedition pair.");
        foreach (IntPreference pref in classicSnapshot) Check(pref.Unchanged, "Expedition reset preserves classic key: " + pref.Key);
        string heightKey = RunRecords.HeightKey(false, false, GameDifficulty.Hard, GameRunKind.Expedition);
        PlayerPrefs.SetInt(heightKey, 6500);
        Check(RunRecords.BestHeight == 6500, "Astronaut height lookup sees true expedition height without multiplying score.");
        RunRecords.ResetAll();
        foreach (string key in all) Check(!PlayerPrefs.HasKey(key), "Reset all includes both variants: " + key);
        Check(ExpeditionProgress.TotalCount == 0, "Record reset does not change cargo inventory.");
    }

    private static Player NewPlayer(List<GameObject> roots, GameRunKind kind)
    {
        GameObject root = new GameObject("Expedition runtime fixture " + kind); root.hideFlags = HideFlags.DontSave;
        roots.Add(root); root.transform.position = new Vector3(8000f, 8000f);
        Rigidbody2D body = root.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
        root.AddComponent<BoxCollider2D>().size = new Vector2(.4f, .1f);
        root.AddComponent<SpriteRenderer>();
        Player player = root.AddComponent<Player>(); player.enabled = false;
        Set(player, "rb", body); SetAuto(player, "RunKind", kind);
        Set(player, "currentHighScoreKey", RunRecords.ScoreKey(false, false, GameDifficulty.Normal, kind));
        Set(player, "currentBestHeightKey", RunRecords.HeightKey(false, false, GameDifficulty.Normal, kind));
        return player;
    }
    private static Collider2D SafePlatform(List<GameObject> roots)
    {
        GameObject root = new GameObject("Expedition runtime safe support"); root.hideFlags = HideFlags.DontSave;
        roots.Add(root); root.tag = "Platform"; root.transform.position = new Vector3(8000f, 7999f);
        BoxCollider2D support = root.AddComponent<BoxCollider2D>(); support.size = new Vector2(2f, .2f);
        Check(ExpeditionRun.SafeRestSurface(support), "Plain active platform is safe for rest.");
        support.isTrigger = true; Check(!ExpeditionRun.SafeRestSurface(support), "Trigger is not a landing surface."); support.isTrigger = false;
        support.enabled = false; Check(!ExpeditionRun.SafeRestSurface(support), "Disabled surface is not safe."); support.enabled = true;
        root.tag = "Untagged"; Check(!ExpeditionRun.SafeRestSurface(support), "Wrong-tag collider cannot open a rest."); root.tag = "Platform";
        return support;
    }
    private static void Click(ExpeditionUI ui, string name)
    {
        foreach (UnityEngine.UI.Button button in ui.GetComponentsInChildren<UnityEngine.UI.Button>())
            if (button.gameObject.name == name) { Check(button.interactable, "Actual panel button is enabled: " + name); button.onClick.Invoke(); return; }
        throw new InvalidOperationException("Missing fixture panel button: " + name);
    }
    private static void ClickSlot(ExpeditionUI ui, int slot, string name)
    {
        foreach (Transform row in ui.GetComponentsInChildren<Transform>())
            if (row.gameObject.name == "Bag slot " + slot)
                foreach (UnityEngine.UI.Button button in row.GetComponentsInChildren<UnityEngine.UI.Button>())
                    if (button.gameObject.name == name)
                    {
                        Check(button.interactable, "Actual bag slot action is enabled: " + slot + "/" + name);
                        button.onClick.Invoke();
                        return;
                    }
        throw new InvalidOperationException("Missing fixture bag action: " + slot + "/" + name);
    }
    private static void Hide(ExpeditionUI ui) { if (ui != null && ExpeditionUI.IsOpen) Call(ui, "Hide", false); }
    private static void ResetClosedFrame() => typeof(ExpeditionUI).GetField("closedFrame", Static).SetValue(null, -1);
    private static FieldInfo ProgressField(string name) => typeof(ExpeditionProgress).GetField(name, Static);
    private static object Get(object owner, string name) => owner.GetType().GetField(name, Instance).GetValue(owner);
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Instance).SetValue(owner, value);
    private static void SetAuto(object owner, string name, object value) => Set(owner, "<" + name + ">k__BackingField", value);
    private static void Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Instance).Invoke(owner, args);
    private static void Check(bool condition, string message) { checks++; Require(condition, message); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Expedition runtime: " + message); }
}
#endif
