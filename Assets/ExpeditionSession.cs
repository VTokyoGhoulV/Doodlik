using System;
using System.Collections.Generic;

public enum ExpeditionOutcome { Extracted = 0, Lost = 1, Abandoned = 2 }

// One immutable end-of-run receipt. Its lists never alias the live backpack.
public sealed class ExpeditionResult
{
    public string RunId { get; }
    public ExpeditionOutcome Outcome { get; }
    public bool Eligible { get; }
    public int Height { get; }
    public int Score { get; }
    public IReadOnlyList<ExpeditionFindKind> Slots { get; }
    public IReadOnlyList<ExpeditionFindKind> Kept { get; }
    public IReadOnlyList<ExpeditionFindKind> Lost { get; }
    public int KeptCount => Kept.Count;
    public int LostCount => Lost.Count;
    public int Value => ExpeditionCatalog.TotalValue(Kept);

    internal ExpeditionResult(string runId, ExpeditionOutcome outcome, bool eligible,
        int height, int score, IReadOnlyList<ExpeditionFindKind> slots)
    {
        RunId = runId; Outcome = outcome; Eligible = eligible;
        Height = Math.Max(0, height); Score = Math.Max(0, score);
        var snapshot = new ExpeditionFindKind[ExpeditionSession.Capacity];
        var kept = new List<ExpeditionFindKind>(ExpeditionSession.Capacity);
        var lost = new List<ExpeditionFindKind>(ExpeditionSession.Capacity);
        for (int i = 0; i < snapshot.Length; i++)
        {
            ExpeditionFindKind kind = slots != null && i < slots.Count ? slots[i] : ExpeditionFindKind.None;
            snapshot[i] = ExpeditionCatalog.IsValid(kind) ? kind : ExpeditionFindKind.None;
            if (!ExpeditionCatalog.IsValid(kind)) continue;
            if (outcome == ExpeditionOutcome.Extracted || i == ExpeditionSession.ProtectedSlot) kept.Add(kind);
            else lost.Add(kind);
        }
        Slots = Array.AsReadOnly(snapshot);
        Kept = kept.AsReadOnly(); Lost = lost.AsReadOnly();
    }
}

// Pure attempt state: no movement, time, PlayerPrefs or global RNG side effects.
public sealed class ExpeditionSession
{
    public const int Capacity = 3;
    public const int ProtectedSlot = 0;
    private readonly ExpeditionFindKind[] slots = new ExpeditionFindKind[Capacity];
    private readonly IReadOnlyList<ExpeditionFindKind> view;
    public string RunId { get; }
    public bool IsEligible { get; private set; }
    public bool IsSettled => Result != null;
    public ExpeditionResult Result { get; private set; }
    public IReadOnlyList<ExpeditionFindKind> Slots => view;
    public int Count
    {
        get { int count = 0; for (int i = 0; i < slots.Length; i++) if (slots[i] != ExpeditionFindKind.None) count++; return count; }
    }
    public int Value => ExpeditionCatalog.TotalValue(view);

    public ExpeditionSession(string runId, bool eligible = true)
    {
        if (!ValidRunId(runId)) throw new ArgumentException("An expedition needs a bounded, nonempty run ID.", nameof(runId));
        RunId = runId; IsEligible = eligible; view = Array.AsReadOnly(slots);
    }

    internal static bool ValidRunId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return false;
        for (int i = 0; i < id.Length; i++) if (char.IsControl(id[i])) return false;
        return true;
    }

    public bool TryCollect(ExpeditionFindKind kind)
    {
        if (IsSettled || !ExpeditionCatalog.IsValid(kind)) return false;
        // Ineligible sessions still support the visual admin sandbox.
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] == ExpeditionFindKind.None) { slots[i] = kind; return true; }
        return false;
    }

    public bool DiscardSlot(int index)
    {
        if (IsSettled || index < 0 || index >= Capacity || slots[index] == ExpeditionFindKind.None) return false;
        slots[index] = ExpeditionFindKind.None;
        return true; // Deliberately no packing: the protected position is explicit.
    }

    public bool ProtectSlot(int index)
    {
        if (IsSettled || index < 0 || index >= Capacity || slots[index] == ExpeditionFindKind.None) return false;
        ExpeditionFindKind previous = slots[ProtectedSlot];
        slots[ProtectedSlot] = slots[index]; slots[index] = previous;
        return true;
    }

    public void Invalidate() { if (!IsSettled) IsEligible = false; }

    public ExpeditionResult Settle(ExpeditionOutcome outcome, int height = 0, int score = 0)
    {
        if (Result != null) return Result;
        if ((int)outcome < (int)ExpeditionOutcome.Extracted || (int)outcome > (int)ExpeditionOutcome.Abandoned) return null;
        Result = new ExpeditionResult(RunId, outcome, IsEligible, height, score, slots);
        return Result;
    }
}
