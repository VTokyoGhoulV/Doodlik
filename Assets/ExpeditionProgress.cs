using System;
using System.Collections.Generic;
using UnityEngine;

// Independent cargo ledger. It never awards coins, changes wardrobe ownership
// or migrates the ordinary PlayerProgress profile. Both recovery copies contain
// the complete stash and settlement IDs from the same transaction.
public static class ExpeditionProgress
{
    public const string SaveKey = "DoodlikExpedition_v1";
    public const string BackupKey = "DoodlikExpedition_v1_Backup";
    private const int CurrentVersion = 2;
    private const int MaximumCount = 9999999;
    private static string primaryKey = SaveKey, backupKey = BackupKey;
    private static SaveData data;
    private static long revision;
    private static bool readOnly;
    private static HashSet<string> settledIds;
    private static ExpeditionResult lastResult;
    // Survives an ordinary Reload: re-reading the stash while paused must not
    // mistake this process's live attempt for an interrupted previous process.
    private static string liveRunId;
    public static event Action Changed;

    [Serializable] private sealed class Envelope
    {
        public int version;
        public long revision;
        public string payload, checksum;
    }
    [Serializable] private sealed class Stock
    {
        public string id;
        public int count;
    }
    [Serializable] private sealed class ResultData
    {
        public string runId;
        public int outcome, height, score;
        public int[] slots;
    }
    [Serializable] private sealed class SaveData
    {
        public List<Stock> stash = new List<Stock>();
        // Retain all settled IDs: retrying an older receipt after a newer one
        // must not award a second cargo transaction. IDs are added only on
        // settlement; live checkpoints update only on bag/lifecycle changes.
        public List<string> settledRuns = new List<string>();
        public int extractedRuns, lostRuns, bestHeight;
        public ResultData lastResult;
        public ResultData pendingRun;
        // JsonUtility serializes null inline classes as default objects, not
        // JSON null. This marker makes clearing unambiguous: 0 = older data,
        // 1 = active checkpoint, 2 = explicitly cleared checkpoint.
        public int pendingState;
        // v2 workshop stock/ownership and their material costs share one commit.
        public int preparedWards;
        public bool hunterCharmUnlocked, takeWardNextRun;
        public string lastWardRunId;
    }

    private sealed class Candidate
    {
        public SaveData Data;
        public long Revision;
        public int Version;
        public bool Future;
    }

    public static bool IsReadOnly { get { EnsureLoaded(); return readOnly; } }
    public static int ExtractedRuns { get { EnsureLoaded(); return data.extractedRuns; } }
    public static int LostRuns { get { EnsureLoaded(); return data.lostRuns; } }
    public static int BestHeight { get { EnsureLoaded(); return data.bestHeight; } }
    public static ExpeditionResult LastResult { get { EnsureLoaded(); return lastResult; } }
    public static int PreparedWards { get { EnsureLoaded(); return data.preparedWards; } }
    public static bool HunterCharmUnlocked { get { EnsureLoaded(); return data.hunterCharmUnlocked; } }
    public static bool TakeWardNextRun { get { EnsureLoaded(); return data.takeWardNextRun; } }
    public static bool WorkshopAvailable
    {
        get { EnsureLoaded(); return !readOnly && !PlayerProgress.AdminSandboxActive && !RunFlow.RunActive && !RunFlow.IsTransitioning; }
    }
    public static int TotalCount
    {
        get
        {
            int count = 0;
            foreach (ExpeditionCatalog.Definition find in ExpeditionCatalog.All) count += Count(find.Kind);
            return count;
        }
    }

    public static int Count(ExpeditionFindKind kind)
    {
        EnsureLoaded();
        ExpeditionCatalog.Definition definition = ExpeditionCatalog.Find(kind);
        if (definition == null) return 0;
        Stock stock = data.stash.Find(item => item.id == definition.Id);
        return stock != null ? stock.count : 0;
    }

    public static bool TryRecord(ExpeditionResult result)
    {
        EnsureLoaded();
        if (result == null || !ExpeditionSession.ValidRunId(result.RunId) ||
            (int)result.Outcome < 0 || (int)result.Outcome > 2) return false;
        // The session has ended even if its durable commit is unavailable.
        // A later fresh attempt may recover its last protected checkpoint.
        if (liveRunId == result.RunId) liveRunId = null;
        if (readOnly) return false;
        if (!result.Eligible || PlayerProgress.AdminSandboxActive)
        {
            InvalidatePending(result.RunId);
            return false;
        }
        if (settledIds.Contains(result.RunId)) return false;

        // Clone before committing, so a failed durable write cannot leave a
        // half-mutated authoritative in-memory stash or consume the receipt.
        SaveData next = Clone();
        foreach (ExpeditionFindKind kind in result.Kept)
        {
            ExpeditionCatalog.Definition definition = ExpeditionCatalog.Find(kind);
            if (definition == null) return false;
            Stock stock = next.stash.Find(item => item.id == definition.Id);
            if (stock == null) { stock = new Stock { id = definition.Id }; next.stash.Add(stock); }
            stock.count = Math.Min(MaximumCount, stock.count + 1);
        }
        if (result.Outcome == ExpeditionOutcome.Extracted)
            next.extractedRuns = Math.Min(MaximumCount, next.extractedRuns + 1);
        else next.lostRuns = Math.Min(MaximumCount, next.lostRuns + 1);
        next.bestHeight = Math.Max(next.bestHeight, result.Height);
        next.settledRuns.Add(result.RunId);
        next.lastResult = EncodeResult(result);
        if (next.pendingRun != null && next.pendingRun.runId == result.RunId) next.pendingRun = null;
        if (!Commit(next)) return false;
        if (liveRunId == result.RunId) liveRunId = null;
        return true;
    }

    public static bool SetTakeWardNextRun(bool take)
    {
        if (!WorkshopAvailable) return false;
        if (data.takeWardNextRun == take) return true;
        SaveData next = Clone(); next.takeWardNextRun = take;
        return Commit(next);
    }

    public static bool TryCraftWard()
    {
        if (!WorkshopAvailable || data.preparedWards >= ExpeditionWorkshop.MaxPreparedWards) return false;
        SaveData next = Clone();
        if (!Spend(next, ExpeditionFindKind.FireflyJar, ExpeditionWorkshop.WardJarCost) ||
            !Spend(next, ExpeditionFindKind.CloudWool, ExpeditionWorkshop.WardWoolCost)) return false;
        next.preparedWards++; next.takeWardNextRun = true;
        return Commit(next);
    }

    public static bool TryUnlockHunterCharm()
    {
        if (!WorkshopAvailable || data.hunterCharmUnlocked) return false;
        SaveData next = Clone();
        if (!Spend(next, ExpeditionFindKind.SparkDust, ExpeditionWorkshop.HunterSparkCost) ||
            !Spend(next, ExpeditionFindKind.StarShard, ExpeditionWorkshop.HunterStarCost)) return false;
        next.hunterCharmUnlocked = true;
        return Commit(next);
    }

    public static bool TryConsumePreparedWard()
    {
        EnsureLoaded();
        ExpeditionRun run = ExpeditionRun.Active;
        ExpeditionSession session = run != null ? run.Session : null;
        if (readOnly || PlayerProgress.AdminSandboxActive || !RunFlow.IsPlaying || Time.timeScale <= 0f ||
            GameplayPreferences.RunKind != GameRunKind.Expedition || run == null || run.Owner == null || run.Owner.IsDead ||
            run.Owner.IsTestAttempt || run.Owner.RunKind != GameRunKind.Expedition || session == null || !session.IsEligible ||
            session.IsSettled || settledIds.Contains(session.RunId) || data.lastWardRunId == session.RunId ||
            !data.takeWardNextRun || data.preparedWards <= 0) return false;
        SaveData next = Clone(); next.preparedWards--; next.lastWardRunId = session.RunId;
        return Commit(next);
    }

    private static bool Spend(SaveData value, ExpeditionFindKind kind, int amount)
    {
        ExpeditionCatalog.Definition definition = ExpeditionCatalog.Find(kind);
        Stock stock = definition != null ? value.stash.Find(item => item.id == definition.Id) : null;
        if (amount <= 0 || stock == null || stock.count < amount) return false;
        stock.count -= amount;
        return true;
    }

    // A checkpoint is not an award and does not settle the live session. Only
    // fresh-process recovery can turn its protected slot into an abandonment.
    public static bool TryCheckpoint(ExpeditionSession session, int height = 0, int score = 0)
    {
        EnsureLoaded();
        if (session == null || session.IsSettled || readOnly || !ExpeditionSession.ValidRunId(session.RunId)) return false;
        if (!session.IsEligible || PlayerProgress.AdminSandboxActive) return InvalidatePending(session.RunId);
        if (settledIds.Contains(session.RunId) || data.pendingRun != null && data.pendingRun.runId != session.RunId) return false;
        liveRunId = session.RunId;
        var snapshot = new ExpeditionResult(session.RunId, ExpeditionOutcome.Abandoned, true, height, score, session.Slots);
        ResultData pending = EncodeResult(snapshot);
        if (SameResult(data.pendingRun, pending)) return true;
        SaveData next = Clone(); next.pendingRun = pending; next.pendingState = 1;
        return Commit(next);
    }

    // Revocation is allowed while admin mode is already active: unlike a
    // reward, removing its old eligible checkpoint cannot mint test cargo.
    public static bool InvalidatePending(string runId)
    {
        EnsureLoaded();
        if (!ExpeditionSession.ValidRunId(runId) || readOnly) return false;
        if (data.pendingRun == null || data.pendingRun.runId != runId) return true;
        SaveData next = Clone(); next.pendingRun = null;
        return Commit(next);
    }

    public static bool RecoverInterruptedRun()
    {
        EnsureLoaded();
        ResultData pending = data.pendingRun;
        if (pending == null || pending.runId == liveRunId || readOnly || PlayerProgress.AdminSandboxActive) return false;
        if (settledIds.Contains(pending.runId))
        {
            SaveData next = Clone(); next.pendingRun = null;
            return Commit(next);
        }
        ExpeditionResult abandoned = DecodeResult(pending);
        return abandoned != null && TryRecord(abandoned);
    }

    private static SaveData Clone()
    {
        SaveData copy = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(data));
        Normalize(copy); // Canonicalize JsonUtility's materialized null DTOs.
        return copy;
    }

    private static bool SameResult(ResultData a, ResultData b)
    {
        if (a == null || b == null || a.runId != b.runId || a.outcome != b.outcome ||
            a.height != b.height || a.score != b.score || a.slots == null || b.slots == null ||
            a.slots.Length != b.slots.Length) return false;
        for (int i = 0; i < a.slots.Length; i++) if (a.slots[i] != b.slots[i]) return false;
        return true;
    }

    private static bool Commit(SaveData next)
    {
        if (readOnly) return false;
        if (revision == long.MaxValue) { readOnly = true; return false; }
        Normalize(next);
        long nextRevision = revision + 1;
        string payload = JsonUtility.ToJson(next);
        string encoded = JsonUtility.ToJson(new Envelope
        {
            version = CurrentVersion, revision = nextRevision,
            payload = payload, checksum = Checksum(payload, nextRevision)
        });
        try
        {
            PlayerPrefs.SetString(primaryKey, encoded);
            PlayerPrefs.SetString(backupKey, encoded);
            PlayerPrefs.Save();
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Expedition cargo was not committed: " + exception.GetType().Name);
            return false;
        }
        data = next; revision = nextRevision;
        settledIds = new HashSet<string>(data.settledRuns, StringComparer.Ordinal);
        lastResult = DecodeResult(data.lastResult);
        Changed?.Invoke();
        return true;
    }

    public static void Flush()
    {
        EnsureLoaded();
        if (!readOnly && !PlayerProgress.AdminSandboxActive) PlayerPrefs.Save();
    }

    public static void Reload()
    {
        data = null; settledIds = null; lastResult = null; revision = 0; readOnly = false;
        EnsureLoaded();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        data = null; settledIds = null; lastResult = null; revision = 0; readOnly = false;
        Changed = null;
        liveRunId = null;
    }

    private static void EnsureLoaded()
    {
        if (data != null) return;
        Candidate primary = Read(primaryKey), backup = Read(backupKey);
        readOnly = primary != null && primary.Future || backup != null && backup.Future;
        Candidate selected = primary;
        if (selected == null || backup != null && backup.Revision > selected.Revision) selected = backup;
        data = selected?.Data ?? new SaveData();
        revision = selected?.Revision ?? 0;
        if (selected != null && selected.Version < 2)
        {
            // Stage one never granted crafted equipment. Similar-looking
            // extra fields in an old payload cannot invent workshop rewards.
            data.preparedWards = 0; data.hunterCharmUnlocked = data.takeWardNextRun = false;
            data.lastWardRunId = string.Empty;
        }
        Normalize(data);
        settledIds = new HashSet<string>(data.settledRuns, StringComparer.Ordinal);
        lastResult = DecodeResult(data.lastResult);
    }

    private static Candidate Read(string key)
    {
        if (!PlayerPrefs.HasKey(key)) return null;
        try
        {
            Envelope envelope = JsonUtility.FromJson<Envelope>(PlayerPrefs.GetString(key));
            if (envelope == null || envelope.version < 1 || envelope.revision < 0) return null;
            bool future = envelope.version > CurrentVersion;
            // Future versions can change their checksum/layout. Preserve their
            // bytes even when only the envelope is understood by this client.
            if (!future && (string.IsNullOrEmpty(envelope.payload) ||
                envelope.checksum != Checksum(envelope.payload, envelope.revision))) return null;
            SaveData loaded = null;
            if (!string.IsNullOrEmpty(envelope.payload))
            {
                try { loaded = JsonUtility.FromJson<SaveData>(envelope.payload); }
                catch (Exception) { if (!future) return null; }
            }
            if (!future && loaded == null) return null;
            return new Candidate { Data = loaded ?? new SaveData(), Revision = envelope.revision, Version = envelope.version, Future = future };
        }
        catch (Exception) { return null; }
    }

    private static void Normalize(SaveData value)
    {
        if (value.stash == null) value.stash = new List<Stock>();
        var stock = new Dictionary<string, Stock>(StringComparer.Ordinal);
        foreach (Stock entry in value.stash)
        {
            if (entry == null || !ExpeditionSession.ValidRunId(entry.id)) continue;
            int count = Math.Max(0, Math.Min(MaximumCount, entry.count));
            if (stock.TryGetValue(entry.id, out Stock prior)) prior.count = Math.Max(prior.count, count);
            else stock.Add(entry.id, new Stock { id = entry.id, count = count });
        }
        value.stash = new List<Stock>(stock.Values); // Unknown find IDs remain intact.
        if (value.settledRuns == null) value.settledRuns = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        value.settledRuns.RemoveAll(id => !ExpeditionSession.ValidRunId(id) || !ids.Add(id));
        value.extractedRuns = Math.Max(0, Math.Min(MaximumCount, value.extractedRuns));
        value.lostRuns = Math.Max(0, Math.Min(MaximumCount, value.lostRuns));
        value.bestHeight = Math.Max(0, value.bestHeight);
        value.preparedWards = Math.Max(0, Math.Min(ExpeditionWorkshop.MaxPreparedWards, value.preparedWards));
        if (!ExpeditionSession.ValidRunId(value.lastWardRunId)) value.lastWardRunId = string.Empty;
        // Pending snapshots can only mean abandonment, never extraction. A
        // malformed checkpoint is discarded without altering the valid stash.
        if (value.pendingState == 2 || value.pendingState < 0 || value.pendingState > 2 ||
            value.pendingRun != null && (value.pendingRun.outcome != (int)ExpeditionOutcome.Abandoned ||
            DecodeResult(value.pendingRun) == null)) value.pendingRun = null;
        value.pendingState = value.pendingRun == null ? 2 : 1;
    }

    private static ResultData EncodeResult(ExpeditionResult result)
    {
        var slots = new int[ExpeditionSession.Capacity];
        for (int i = 0; i < slots.Length; i++) slots[i] = (int)result.Slots[i];
        return new ResultData { runId = result.RunId, outcome = (int)result.Outcome,
            height = result.Height, score = result.Score, slots = slots };
    }

    private static ExpeditionResult DecodeResult(ResultData result)
    {
        if (result == null || !ExpeditionSession.ValidRunId(result.runId) || result.outcome < 0 ||
            result.outcome > 2 || result.slots == null || result.slots.Length != ExpeditionSession.Capacity) return null;
        var slots = new ExpeditionFindKind[ExpeditionSession.Capacity];
        for (int i = 0; i < slots.Length; i++)
        {
            ExpeditionFindKind kind = (ExpeditionFindKind)result.slots[i];
            if (kind != ExpeditionFindKind.None && !ExpeditionCatalog.IsValid(kind)) return null;
            slots[i] = kind;
        }
        return new ExpeditionResult(result.runId, (ExpeditionOutcome)result.outcome, true, result.height, result.score, slots);
    }

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

#if UNITY_EDITOR
    public static IDisposable UseIsolatedStorageForValidation(out string primary, out string backup)
    {
        if (primaryKey != SaveKey || backupKey != BackupKey)
            throw new InvalidOperationException("An expedition validation storage scope is already active.");
        primary = "DoodlikExpeditionValidation_" + Guid.NewGuid().ToString("N");
        backup = primary + "_Backup";
        return new ValidationStorageScope(primary, backup);
    }

    private sealed class ValidationStorageScope : IDisposable
    {
        private readonly SaveData previousData = data;
        private readonly HashSet<string> previousIds = settledIds;
        private readonly ExpeditionResult previousResult = lastResult;
        private readonly long previousRevision = revision;
        private readonly bool previousReadOnly = readOnly;
        private readonly Action previousChanged = Changed;
        private readonly string previousLiveRunId = liveRunId;
        private readonly string primary, backup;
        private bool disposed;
        public ValidationStorageScope(string primary, string backup)
        {
            this.primary = primary; this.backup = backup;
            primaryKey = primary; backupKey = backup;
            data = null; settledIds = null; lastResult = null; revision = 0; readOnly = false; Changed = null;
            liveRunId = null;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { PlayerPrefs.DeleteKey(primary); PlayerPrefs.DeleteKey(backup); PlayerPrefs.Save(); }
            finally
            {
                primaryKey = SaveKey; backupKey = BackupKey;
                data = previousData; settledIds = previousIds; lastResult = previousResult;
                revision = previousRevision; readOnly = previousReadOnly; Changed = previousChanged;
                liveRunId = previousLiveRunId;
            }
        }
    }
#endif
}
