using System;
using System.Collections.Generic;

// Immutable public models. Save DTOs remain private to PlayerProgress so a UI
// consumer cannot modify the journal, totals or an already recorded attempt.
public sealed class RunStatisticsEntry
{
    public readonly string Id, EndReason;
    public readonly bool Portrait, Inertia;
    public readonly GameDifficulty Difficulty;
    public readonly int Height, Score, Coins, Bushes, MovingLandings, NarrowLandings, WideJumps;
    public readonly double ActiveSeconds;

    public RunStatisticsEntry(string id, bool portrait, bool inertia, GameDifficulty difficulty,
        int height, int score, int coins, int bushes, int movingLandings, int narrowLandings,
        int wideJumps, double activeSeconds, string endReason)
    {
        Id = id;
        Portrait = portrait;
        Inertia = inertia;
        Difficulty = difficulty;
        Height = height;
        Score = score;
        Coins = coins;
        Bushes = bushes;
        MovingLandings = movingLandings;
        NarrowLandings = narrowLandings;
        WideJumps = wideJumps;
        ActiveSeconds = activeSeconds;
        EndReason = endReason;
    }
}

public sealed class CareerStatisticsSnapshot
{
    public readonly int Runs, BestRunHeight, BestRunScore, BestRunCoins;
    public readonly long TotalCoins, TotalBushes, TotalMovingLandings, TotalNarrowLandings, TotalWideJumps;
    public readonly double ActiveSeconds;
    public readonly IReadOnlyList<RunStatisticsEntry> RecentRuns;

    internal CareerStatisticsSnapshot(int runs, long totalCoins, long totalBushes, long totalMovingLandings,
        long totalNarrowLandings, long totalWideJumps, double activeSeconds, int bestRunHeight,
        int bestRunScore, int bestRunCoins, RunStatisticsEntry[] recentRuns)
    {
        Runs = runs;
        TotalCoins = totalCoins;
        TotalBushes = totalBushes;
        TotalMovingLandings = totalMovingLandings;
        TotalNarrowLandings = totalNarrowLandings;
        TotalWideJumps = totalWideJumps;
        ActiveSeconds = activeSeconds;
        BestRunHeight = bestRunHeight;
        BestRunScore = bestRunScore;
        BestRunCoins = bestRunCoins;
        RecentRuns = Array.AsReadOnly((RunStatisticsEntry[])recentRuns.Clone());
    }
}
