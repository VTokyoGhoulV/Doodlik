#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Domain and persistence checks use their own two storage namespaces. Ordinary
// currency, wardrobe, statistics and record keys are never replaced or migrated.
public static class ExpeditionProgressValidation
{
    private static int checks;
    public static void Run() => Validate();
    [Serializable] private sealed class Envelope
    {
        public int version;
        public long revision;
        public string payload, checksum;
    }
    [Serializable] private sealed class Stock { public string id; public int count; }
    [Serializable] private sealed class PendingFixture
    {
        public string runId;
        public int outcome, height, score;
        public int[] slots;
    }
    [Serializable] private sealed class Fixture
    {
        public List<Stock> stash = new List<Stock>();
        public List<string> settledRuns = new List<string>();
        public int extractedRuns, lostRuns, bestHeight;
        public PendingFixture pendingRun;
        public int pendingState;
    }

    public static int Validate()
    {
        Require(Application.isBatchMode && Application.dataPath.Replace('\\', '/').Contains("/.utmp/"),
            "Expedition progress validation needs a disposable batch Editor.");
        checks = 0;
        object priorData = Field("data").GetValue(null), priorChanged = Field("Changed").GetValue(null);
        ValidateCatalogAndSession();
        using (PlayerProgress.UseIsolatedStorageForValidation(out string normalPrimary, out string normalBackup))
        using (ExpeditionProgress.UseIsolatedStorageForValidation(out string primary, out string backup))
        {
            PlayerProgress.AddCoins(812);
            PlayerProgress.TryPurchase("cat_cocoa");
            PlayerProgress.Flush();
            string normal = PlayerPrefs.GetString(normalPrimary), recovery = PlayerPrefs.GetString(normalBackup);
            ValidateStorage(primary, backup);
            ValidatePendingRecovery(primary, backup);
            Check(PlayerPrefs.GetString(normalPrimary) == normal && PlayerPrefs.GetString(normalBackup) == recovery &&
                PlayerProgress.Coins == 692 && PlayerProgress.Owns("cat_cocoa"),
                "All cargo transactions preserve both ordinary profile copies, wallet and purchases.");
        }
        Check(ReferenceEquals(priorData, Field("data").GetValue(null)) &&
            ReferenceEquals(priorChanged, Field("Changed").GetValue(null)),
            "Isolated storage restores opaque outer cache and subscriptions.");
        Debug.Log("EXPEDITION_PROGRESS_PASS checks=" + checks +
            " capacity3=true protected0=true immutableReceipts=true durableIdempotence=true independentSave=true recovery=true futureReadOnly=true" +
            " pendingCheckpoint=true liveReloadSafe=true interruptedProtectedRecovery=true adminRevocation=true");
        return checks;
    }

    private static void ValidateCatalogAndSession()
    {
        Check(ExpeditionCatalog.All.Count == 4 && ExpeditionSession.Capacity == 3 && ExpeditionSession.ProtectedSlot == 0,
            "Three world finds plus one rare enemy drop use three explicit slots, first protected.");
        var ids = new HashSet<string>();
        int gate = -1, value = 0;
        foreach (ExpeditionCatalog.Definition definition in ExpeditionCatalog.All)
        {
            Check(ids.Add(definition.Id) && ExpeditionCatalog.Find(definition.Kind) == definition &&
                ExpeditionCatalog.Find(definition.Id) == definition, "Catalog IDs and kind mappings remain unique.");
            if (definition.EnemyOnly)
                Check(definition.Kind == ExpeditionFindKind.SparkDust && definition.Value > 0,
                    "Rare enemy material is explicitly excluded from ordinary height bands.");
            else
            {
                Check(definition.MinimumHeight >= gate && definition.Value > value, "Higher world finds have increasing gates and value.");
                gate = definition.MinimumHeight; value = definition.Value;
            }
        }
        Check(ExpeditionCatalog.Find(ExpeditionFindKind.None) == null &&
            ExpeditionCatalog.Find((ExpeditionFindKind)100) == null && ExpeditionCatalog.Find("missing") == null,
            "Invalid cargo cannot address an item.");
        bool rejected = false;
        try { new ExpeditionSession(""); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Empty run ID rejected.");
        rejected = false;
        try { new ExpeditionSession(new string('x', 65)); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Oversized run ID rejected.");

        ExpeditionSession session = Filled("domain-extraction");
        Check(session.Count == 3 && session.Value == 12 && !session.TryCollect(ExpeditionFindKind.FireflyJar),
            "Full backpack rejects fourth item without replacing anything.");
        Check(!session.TryCollect(ExpeditionFindKind.None) && !session.TryCollect((ExpeditionFindKind)(-1)) &&
            !session.DiscardSlot(-1) && !session.DiscardSlot(3) && !session.ProtectSlot(3), "Invalid indices and cargo are inert.");
        Check(session.ProtectSlot(2) && session.Slots[0] == ExpeditionFindKind.StarShard &&
            session.Slots[2] == ExpeditionFindKind.FireflyJar && session.Count == 3, "Protect swaps instead of duplicating or discarding cargo.");
        Check(session.DiscardSlot(1) && session.Slots[1] == ExpeditionFindKind.None &&
            session.Slots[2] == ExpeditionFindKind.FireflyJar && !session.ProtectSlot(1),
            "Discard leaves exact empty slot and cannot protect emptiness.");
        Check(session.TryCollect(ExpeditionFindKind.CloudWool) && session.Slots[1] == ExpeditionFindKind.CloudWool,
            "Next pickup fills the first exact empty slot.");
        Check(session.Settle((ExpeditionOutcome)99) == null && !session.IsSettled, "Invalid settlement leaves attempt open.");
        ExpeditionResult result = session.Settle(ExpeditionOutcome.Extracted, 1234, 1400);
        Check(result.Eligible && result.KeptCount == 3 && result.LostCount == 0 && result.Value == 12 &&
            result.Height == 1234 && result.Score == 1400 && result.Slots[0] == ExpeditionFindKind.StarShard,
            "Extraction receipt keeps complete cargo and captured metrics.");
        Check(ReferenceEquals(result, session.Settle(ExpeditionOutcome.Lost, 0, 0)) &&
            !session.TryCollect(ExpeditionFindKind.FireflyJar) && !session.DiscardSlot(0) && !session.ProtectSlot(1),
            "Settlement is immutable, idempotent and closes every inventory mutation.");
        var mutable = result.Slots as IList<ExpeditionFindKind>;
        rejected = false;
        try { mutable[0] = ExpeditionFindKind.None; } catch (NotSupportedException) { rejected = true; }
        Check(rejected && session.Slots[0] == ExpeditionFindKind.StarShard, "Published slots cannot mutate the session or receipt.");

        session = Filled("domain-loss"); session.ProtectSlot(2);
        result = session.Settle(ExpeditionOutcome.Lost, -10, -20);
        Check(result.KeptCount == 1 && result.Kept[0] == ExpeditionFindKind.StarShard && result.LostCount == 2 &&
            result.Height == 0 && result.Score == 0, "Defeat retains only explicitly protected slot and clamps negative metrics.");
        session = Filled("domain-empty-protection"); session.DiscardSlot(0);
        result = session.Settle(ExpeditionOutcome.Abandoned);
        Check(result.KeptCount == 0 && result.LostCount == 2, "Empty protected position does not auto-insure an unprotected item.");
        session = new ExpeditionSession("domain-admin"); session.Invalidate();
        Check(session.TryCollect(ExpeditionFindKind.StarShard) && !session.IsEligible &&
            !session.Settle(ExpeditionOutcome.Extracted).Eligible,
            "Admin preview can collect but invalidation remains sticky through settlement.");
    }

    private static void ValidateStorage(string primary, string backup)
    {
        Check(ExpeditionProgress.TotalCount == 0 && !ExpeditionProgress.IsReadOnly &&
            !PlayerPrefs.HasKey(primary) && !PlayerPrefs.HasKey(backup), "Fresh read creates no durable cargo or profile writes.");
        Check(!ExpeditionProgress.TryRecord(null) && ExpeditionProgress.Count(ExpeditionFindKind.None) == 0,
            "Null receipt and None cargo do not change stash.");
        ExpeditionResult extracted = Filled("extract-first").Settle(ExpeditionOutcome.Extracted, 1500, 1650);
        Check(ExpeditionProgress.TryRecord(extracted) && ExpeditionProgress.TotalCount == 3 &&
            ExpeditionProgress.ExtractedRuns == 1 && ExpeditionProgress.LostRuns == 0 && ExpeditionProgress.BestHeight == 1500,
            "One extraction atomically records cargo, run count and best height.");
        Check(PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup), "Both recovery copies describe the same transaction.");
        string once = PlayerPrefs.GetString(primary);
        Check(!ExpeditionProgress.TryRecord(extracted) && PlayerPrefs.GetString(primary) == once,
            "Repeated receipt cannot duplicate cargo, counts or revision.");
        ExpeditionProgress.Reload();
        Check(ExpeditionProgress.TotalCount == 3 && !ExpeditionProgress.TryRecord(extracted) &&
            ExpeditionProgress.LastResult.RunId == "extract-first" && ExpeditionProgress.LastResult.KeptCount == 3,
            "Cargo, immutable last receipt and duplicate guard survive reload.");
        ExpeditionSession loss = Filled("lost-second"); loss.ProtectSlot(2);
        Check(ExpeditionProgress.TryRecord(loss.Settle(ExpeditionOutcome.Lost, 1800, 1900)) &&
            ExpeditionProgress.TotalCount == 4 && ExpeditionProgress.Count(ExpeditionFindKind.StarShard) == 2 &&
            ExpeditionProgress.LostRuns == 1 && ExpeditionProgress.BestHeight == 1800, "Loss banks only protected cargo.");
        ExpeditionSession abandoned = Filled("abandoned-third");
        Check(ExpeditionProgress.TryRecord(abandoned.Settle(ExpeditionOutcome.Abandoned, 200, 200)) &&
            ExpeditionProgress.TotalCount == 5 && ExpeditionProgress.LostRuns == 2, "Abandon follows the same protected-slot rule.");

        ExpeditionSession test = Filled("test-ineligible"); test.Invalidate();
        string beforeTest = PlayerPrefs.GetString(primary), beforeBackup = PlayerPrefs.GetString(backup);
        Check(!ExpeditionProgress.TryRecord(test.Settle(ExpeditionOutcome.Extracted)), "Ineligible test cargo is never awarded.");
        PlayerProgress.BeginAdminSandbox();
        Check(!ExpeditionProgress.TryRecord(Filled("test-admin-global").Settle(ExpeditionOutcome.Extracted)),
            "Global admin state rejects even a receipt with a mistakenly eligible session.");
        PlayerProgress.EndAdminSandbox();
        Check(PlayerPrefs.GetString(primary) == beforeTest && PlayerPrefs.GetString(backup) == beforeBackup,
            "Rejected admin and test claims preserve both cargo save copies exactly.");

        for (int i = 0; i < 40; i++)
            Check(ExpeditionProgress.TryRecord(new ExpeditionSession("later-" + i).Settle(ExpeditionOutcome.Extracted)),
                "Distinct later empty run recorded once.");
        ExpeditionProgress.Reload();
        Check(!ExpeditionProgress.TryRecord(extracted) && ExpeditionProgress.TotalCount == 5,
            "An old receipt remains rejected beyond a small recent-run window.");
        PlayerPrefs.SetString(primary, "{broken"); ExpeditionProgress.Reload();
        Check(ExpeditionProgress.TotalCount == 5 && !ExpeditionProgress.TryRecord(extracted),
            "Corrupt primary recovers complete stash and deduplication ledger from backup.");
        Check(ExpeditionProgress.TryRecord(new ExpeditionSession("after-recovery").Settle(ExpeditionOutcome.Extracted)) &&
            PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup), "Next valid transaction repairs both copies coherently.");

        string current = PlayerPrefs.GetString(primary);
        string future = FutureVersion(current);
        Check(future != current, "Future schema fixture is distinct.");
        PlayerPrefs.SetString(primary, future); ExpeditionProgress.Reload();
        Check(ExpeditionProgress.IsReadOnly && !ExpeditionProgress.TryRecord(Filled("future-denied").Settle(ExpeditionOutcome.Extracted)),
            "A future primary makes the older client read-only.");
        ExpeditionProgress.Flush();
        Check(PlayerPrefs.GetString(primary) == future && PlayerPrefs.GetString(backup) == current,
            "Future primary and recovery bytes stay exact, including Flush.");
        PlayerPrefs.SetString(primary, current); PlayerPrefs.SetString(backup, future); ExpeditionProgress.Reload();
        Check(ExpeditionProgress.IsReadOnly && !ExpeditionProgress.TryRecord(Filled("future-backup-denied").Settle(ExpeditionOutcome.Extracted)) &&
            PlayerPrefs.GetString(backup) == future, "Future recovery copy is protected even with a readable current primary.");

        PlayerPrefs.SetString(primary, "bad"); PlayerPrefs.SetString(backup, "bad"); ExpeditionProgress.Reload();
        Check(!ExpeditionProgress.IsReadOnly && ExpeditionProgress.TotalCount == 0 && ExpeditionProgress.LastResult == null,
            "Both corrupt copies load empty safe data without minting cargo.");
        Check(ExpeditionProgress.TryRecord(Filled("repair-empty").Settle(ExpeditionOutcome.Extracted)),
            "A new legitimate result can repair empty corrupt storage.");
        ValidateSanitizingAndNewest(primary, backup);
    }

    private static void ValidateSanitizingAndNewest(string primary, string backup)
    {
        var fixture = new Fixture { extractedRuns = -1, lostRuns = int.MaxValue, bestHeight = -100 };
        fixture.stash.Add(new Stock { id = "firefly_jar", count = int.MaxValue });
        fixture.stash.Add(new Stock { id = "firefly_jar", count = 4 });
        fixture.stash.Add(new Stock { id = "cloud_wool", count = -1 });
        fixture.stash.Add(new Stock { id = "future_find_keep", count = 7 });
        fixture.settledRuns.Add("existing"); fixture.settledRuns.Add("existing"); fixture.settledRuns.Add("");
        string baseline = Encode(fixture, 100);
        PlayerPrefs.SetString(primary, baseline); PlayerPrefs.SetString(backup, baseline); ExpeditionProgress.Reload();
        Check(ExpeditionProgress.Count(ExpeditionFindKind.FireflyJar) == 9999999 &&
            ExpeditionProgress.Count(ExpeditionFindKind.CloudWool) == 0 && ExpeditionProgress.ExtractedRuns == 0 &&
            ExpeditionProgress.LostRuns == 9999999 && ExpeditionProgress.BestHeight == 0,
            "Malformed counts are clamped and duplicate stock entries are not summed.");
        Check(ExpeditionProgress.TryRecord(Filled("after-sanitize").Settle(ExpeditionOutcome.Extracted)) &&
            ExpeditionProgress.Count(ExpeditionFindKind.FireflyJar) == 9999999 &&
            PlayerPrefs.GetString(primary).Contains("future_find_keep"), "Stock cap cannot overflow and unknown find IDs survive the next write.");
        fixture.stash[0].count = 9;
        string newerBackup = Encode(fixture, 200);
        PlayerPrefs.SetString(backup, newerBackup); ExpeditionProgress.Reload();
        Check(ExpeditionProgress.Count(ExpeditionFindKind.FireflyJar) == 9,
            "Newest valid revision wins even when it is the recovery copy.");
        Envelope damaged = JsonUtility.FromJson<Envelope>(newerBackup); damaged.checksum = "DEADBEEF";
        PlayerPrefs.SetString(backup, JsonUtility.ToJson(damaged)); ExpeditionProgress.Reload();
        Check(ExpeditionProgress.Count(ExpeditionFindKind.FireflyJar) == 9999999,
            "Checksum mismatch cannot replace a complete valid transaction.");
    }

    private static void ValidatePendingRecovery(string primary, string backup)
    {
        PlayerPrefs.DeleteKey(primary); PlayerPrefs.DeleteKey(backup);
        Field("liveRunId").SetValue(null, null); ExpeditionProgress.Reload();
        Check(!ExpeditionProgress.TryCheckpoint(null) && !ExpeditionProgress.RecoverInterruptedRun() &&
            !PlayerPrefs.HasKey(primary), "Empty recovery and null checkpoints do not write or mint cargo.");
        ExpeditionSession interrupted = Filled("pending-first"); interrupted.ProtectSlot(2);
        Check(ExpeditionProgress.TryCheckpoint(interrupted, 1500, 1700) && !interrupted.IsSettled &&
            ExpeditionProgress.TotalCount == 0 && ExpeditionProgress.LostRuns == 0 && ExpeditionProgress.ExtractedRuns == 0 &&
            PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup),
            "Live checkpoint atomically saves both copies but never banks cargo or settles an attempt.");
        string checkpoint = PlayerPrefs.GetString(primary);
        Check(ExpeditionProgress.TryCheckpoint(interrupted, 1500, 1700) && PlayerPrefs.GetString(primary) == checkpoint,
            "An unchanged repeated checkpoint does not consume revisions or rewrite storage.");
        ExpeditionProgress.Reload();
        Check(!ExpeditionProgress.RecoverInterruptedRun() && ExpeditionProgress.TotalCount == 0 &&
            PlayerPrefs.GetString(primary) == checkpoint && interrupted.Count == 3 && !interrupted.IsSettled,
            "Reload and recovery in the same live process cannot prematurely abandon or bank its backpack.");
        Field("liveRunId").SetValue(null, null); // Simulate a new process, never a normal Reload.
        Check(ExpeditionProgress.RecoverInterruptedRun() && ExpeditionProgress.TotalCount == 1 &&
            ExpeditionProgress.Count(ExpeditionFindKind.StarShard) == 1 && ExpeditionProgress.LostRuns == 1 &&
            ExpeditionProgress.LastResult.Outcome == ExpeditionOutcome.Abandoned && ExpeditionProgress.LastResult.KeptCount == 1 &&
            ExpeditionProgress.LastResult.LostCount == 2 && ExpeditionProgress.LastResult.Height == 1500 &&
            ExpeditionProgress.LastResult.Score == 1700 && PendingIsCleared(primary),
            "Fresh startup recovers exactly the protected item with one honest abandonment receipt and clears pending atomically.");
        string recovered = PlayerPrefs.GetString(primary);
        ExpeditionProgress.Reload();
        Check(!ExpeditionProgress.RecoverInterruptedRun() && !ExpeditionProgress.TryRecord(interrupted.Settle(ExpeditionOutcome.Extracted)) &&
            ExpeditionProgress.TotalCount == 1 && PlayerPrefs.GetString(primary) == recovered,
            "Repeated startup, reload and a late full-extraction receipt cannot award an interrupted run twice.");

        ExpeditionSession empty = Filled("pending-empty-protected");
        Check(ExpeditionProgress.TryCheckpoint(empty) && empty.DiscardSlot(0) && ExpeditionProgress.TryCheckpoint(empty),
            "Discarding protected cargo replaces its durable checkpoint with the explicit empty slot.");
        Field("liveRunId").SetValue(null, null); ExpeditionProgress.Reload();
        Check(ExpeditionProgress.RecoverInterruptedRun() && ExpeditionProgress.TotalCount == 1 &&
            ExpeditionProgress.LastResult.KeptCount == 0 && ExpeditionProgress.LastResult.LostCount == 2,
            "Crash recovery never auto-insures an unprotected item when slot zero was discarded.");
        ExpeditionSession changed = Filled("pending-swapped");
        Check(ExpeditionProgress.TryCheckpoint(changed) && changed.ProtectSlot(1) && ExpeditionProgress.TryCheckpoint(changed),
            "Protect swap replaces the durable protected kind rather than adding a second insured item.");
        Field("liveRunId").SetValue(null, null); ExpeditionProgress.Reload();
        Check(ExpeditionProgress.RecoverInterruptedRun() && ExpeditionProgress.Count(ExpeditionFindKind.CloudWool) == 1 &&
            ExpeditionProgress.Count(ExpeditionFindKind.FireflyJar) == 0, "Recovery uses the latest explicit protected-slot choice.");

        ExpeditionSession complete = Filled("pending-complete");
        Check(ExpeditionProgress.TryCheckpoint(complete) && ExpeditionProgress.TryRecord(complete.Settle(ExpeditionOutcome.Extracted)) &&
            PendingIsCleared(primary) && PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup),
            "Normal settlement commits stash, deduplication and pending removal in the same complete envelope.");
        int count = ExpeditionProgress.TotalCount;
        Field("liveRunId").SetValue(null, null); ExpeditionProgress.Reload();
        Check(!ExpeditionProgress.RecoverInterruptedRun() && ExpeditionProgress.TotalCount == count,
            "Fresh startup after an ordinary settlement cannot recover a stale pending receipt.");

        ExpeditionSession test = Filled("pending-admin");
        Check(ExpeditionProgress.TryCheckpoint(test), "Eligible cargo exists durably before the admin toggle.");
        PlayerProgress.BeginAdminSandbox(); test.Invalidate();
        Check(ExpeditionProgress.InvalidatePending(test.RunId) && PendingIsCleared(primary) &&
            !ExpeditionProgress.TryRecord(test.Settle(ExpeditionOutcome.Extracted)),
            "Immediate checkpoint revocation works even when global admin is already active.");
        PlayerProgress.EndAdminSandbox(); Field("liveRunId").SetValue(null, null); ExpeditionProgress.Reload();
        Check(!ExpeditionProgress.RecoverInterruptedRun() && ExpeditionProgress.TotalCount == count,
            "A later crash or turning admin off cannot legitimise the revoked protected checkpoint.");

        ExpeditionSession direct = Filled("pending-direct-end");
        Check(ExpeditionProgress.TryRecord(direct.Settle(ExpeditionOutcome.Extracted)) && PendingIsCleared(primary),
            "A direct end receipt without a prior checkpoint persists an explicit cleared marker.");
        ExpeditionSession nextLive = Filled("pending-after-direct-end");
        Check(ExpeditionProgress.TryCheckpoint(nextLive) && !ExpeditionProgress.RecoverInterruptedRun() &&
            ExpeditionProgress.TryRecord(nextLive.Settle(ExpeditionOutcome.Lost)) && PendingIsCleared(primary),
            "A new live checkpoint succeeds immediately after direct settlement without Reload or a default-DTO blockage.");
        count = ExpeditionProgress.TotalCount;

        ExpeditionSession backupSession = Filled("pending-backup");
        Check(ExpeditionProgress.TryCheckpoint(backupSession), "Backup recovery fixture is durably checkpointed.");
        string stalePending = PlayerPrefs.GetString(primary);
        PlayerPrefs.SetString(primary, "broken"); Field("liveRunId").SetValue(null, null); ExpeditionProgress.Reload();
        Check(ExpeditionProgress.RecoverInterruptedRun() && ExpeditionProgress.TotalCount == count + 1 &&
            PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup),
            "Corrupt primary recovers one protected item from backup and repairs both coherent copies.");
        string final = PlayerPrefs.GetString(primary);
        PlayerPrefs.SetString(primary, stalePending); PlayerPrefs.SetString(backup, final); ExpeditionProgress.Reload();
        Check(!ExpeditionProgress.RecoverInterruptedRun() && ExpeditionProgress.TotalCount == count + 1,
            "A newer complete settled backup defeats an older primary pending receipt without duplication.");

        ExpeditionSession unavailable = Filled("pending-unavailable-final");
        Check(ExpeditionProgress.TryCheckpoint(unavailable), "Unavailable-final-commit fixture starts with a valid durable checkpoint.");
        Field("revision").SetValue(null, long.MaxValue);
        Check(!ExpeditionProgress.TryRecord(unavailable.Settle(ExpeditionOutcome.Extracted)) &&
            Field("liveRunId").GetValue(null) == null,
            "Even an unavailable final commit releases the ended live identity instead of blocking subsequent recovery.");
        ExpeditionProgress.Reload();
        Check(ExpeditionProgress.RecoverInterruptedRun() && ExpeditionProgress.LastResult.KeptCount == 1 &&
            ExpeditionProgress.LastResult.Outcome == ExpeditionOutcome.Abandoned,
            "An ended failed settlement may recover only its last protected checkpoint, never pretend the full extraction committed.");

        var fixture = new Fixture { pendingRun = new PendingFixture { runId = "invalid-pending", outcome = 0,
            height = 10, score = 10, slots = new[] { 3, 2, 1 } } };
        string invalid = Encode(fixture, 400);
        PlayerPrefs.SetString(primary, invalid); PlayerPrefs.SetString(backup, invalid); ExpeditionProgress.Reload();
        Check(!ExpeditionProgress.RecoverInterruptedRun() && ExpeditionProgress.TotalCount == 0,
            "A tampered pending extraction can never bank all cargo during crash recovery.");
        fixture.pendingRun.outcome = 2; fixture.pendingRun.slots[1] = 99;
        invalid = Encode(fixture, 401); PlayerPrefs.SetString(primary, invalid); PlayerPrefs.SetString(backup, invalid); ExpeditionProgress.Reload();
        Check(!ExpeditionProgress.RecoverInterruptedRun(), "An invalid enum in a pending snapshot cannot recover any cargo.");
        fixture.pendingRun.slots = new[] { 1, 2, 3 }; fixture.pendingState = 2;
        invalid = Encode(fixture, 402); PlayerPrefs.SetString(primary, invalid); PlayerPrefs.SetString(backup, invalid); ExpeditionProgress.Reload();
        Check(!ExpeditionProgress.RecoverInterruptedRun() && ExpeditionProgress.TotalCount == 0,
            "An explicit cleared marker wins over a stale inline DTO with otherwise valid run ID and cargo.");
        fixture.pendingState = 0;
        string olderPending = Encode(fixture, 403); PlayerPrefs.SetString(primary, olderPending); PlayerPrefs.SetString(backup, olderPending); ExpeditionProgress.Reload();
        Check(ExpeditionProgress.RecoverInterruptedRun() && ExpeditionProgress.TotalCount == 1 && PendingIsCleared(primary),
            "A valid older checkpoint without the additive marker still recovers once and is rewritten explicitly cleared.");

        PlayerPrefs.DeleteKey(primary); PlayerPrefs.DeleteKey(backup); ExpeditionProgress.Reload();
        ExpeditionSession futureSession = Filled("pending-future");
        Check(ExpeditionProgress.TryCheckpoint(futureSession), "Future-version fixture begins with a valid pending checkpoint.");
        string current = PlayerPrefs.GetString(primary), future = FutureVersion(current);
        PlayerPrefs.SetString(backup, future); Field("liveRunId").SetValue(null, null); ExpeditionProgress.Reload();
        Check(ExpeditionProgress.IsReadOnly && !ExpeditionProgress.RecoverInterruptedRun() &&
            !ExpeditionProgress.InvalidatePending(futureSession.RunId) && !ExpeditionProgress.TryCheckpoint(futureSession) &&
            PlayerPrefs.GetString(primary) == current && PlayerPrefs.GetString(backup) == future,
            "Future data in either copy blocks checkpoint, revocation and recovery without changing either byte stream.");
    }

    private static bool PendingIsCleared(string primary)
    {
        Envelope envelope = JsonUtility.FromJson<Envelope>(PlayerPrefs.GetString(primary));
        Fixture payload = JsonUtility.FromJson<Fixture>(envelope.payload);
        PendingFixture pending = payload.pendingRun;
        return payload.pendingState == 2 && (pending == null || string.IsNullOrEmpty(pending.runId) &&
            (pending.slots == null || pending.slots.Length == 0));
    }

    private static ExpeditionSession Filled(string id)
    {
        var session = new ExpeditionSession(id);
        Check(session.TryCollect(ExpeditionFindKind.FireflyJar) && session.TryCollect(ExpeditionFindKind.CloudWool) &&
            session.TryCollect(ExpeditionFindKind.StarShard), "Fixture collects all three known cargo kinds.");
        return session;
    }
    private static string Encode(Fixture fixture, long revision)
    {
        string payload = JsonUtility.ToJson(fixture);
        return JsonUtility.ToJson(new Envelope { version = 1, revision = revision, payload = payload, checksum = Checksum(payload, revision) });
    }
    private static string Checksum(string payload, long revision)
    {
        unchecked
        {
            uint hash = 2166136261;
            for (int i = 0; i < payload.Length; i++) hash = (hash ^ payload[i]) * 16777619;
            hash = (hash ^ (uint)revision) * 16777619;
            hash = (hash ^ (uint)(revision >> 32)) * 16777619;
            return hash.ToString("X8");
        }
    }
    private static FieldInfo Field(string name) => typeof(ExpeditionProgress).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
    private static string FutureVersion(string json)
    {
        Envelope envelope = JsonUtility.FromJson<Envelope>(json);
        envelope.version = (int)Field("CurrentVersion").GetRawConstantValue() + 1;
        return JsonUtility.ToJson(envelope);
    }
    private static void Check(bool condition, string message) { checks++; Require(condition, message); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Expedition progress: " + message); }
}
#endif
