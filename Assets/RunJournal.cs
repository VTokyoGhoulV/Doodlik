using System;
using UnityEngine;

// One natural Player attempt. This observes the already-certified challenge
// events; it never changes movement, spawning, rewards or the run's lifetime.
[DisallowMultipleComponent]
public sealed class RunJournal : MonoBehaviour
{
    public bool Started { get; private set; }
    public bool Finalized { get; private set; }
    public double ActiveSeconds { get; private set; }
    public string RunId { get; private set; }

    private Player owner;
    private RunChallenges challenges;
    private ChallengeSession session;
    private ChallengeRunStats latestStats;
    private bool portrait, inertia, ineligible, paused, focused, skipTimingFrame;
    private GameDifficulty difficulty;
    private int latestHeight, latestScore;

    public static RunJournal Attach(Player player)
    {
        RunJournal journal = player.GetComponent<RunJournal>();
        if (journal == null) journal = player.gameObject.AddComponent<RunJournal>();
        journal.owner = player;
        journal.challenges = player.Challenges;
        journal.focused = Application.isFocused;
        return journal;
    }

    public void BeginRun()
    {
        // Expedition receipts and their stash are independent from the classic
        // lifetime journal. Their height still opens the normal space rewards.
        if (owner != null && owner.RunKind == GameRunKind.Expedition) return;
        if (Started || Finalized || owner == null || !RunFlow.IsPlaying || Time.timeScale <= 0f) return;
        Started = true;
        RunId = Guid.NewGuid().ToString("N");
        portrait = GameplayPreferences.IsPortrait;
        inertia = GameplayPreferences.UseInertia;
        difficulty = owner.RunDifficulty;
        skipTimingFrame = true;
        Capture();
    }

    private void Update()
    {
        if (owner != null && owner.RunKind == GameRunKind.Expedition) return;
        if (Finalized) return;
        if (!Started && RunFlow.IsPlaying && Time.timeScale > 0f && !GameMenuUI.BlocksGameplayInput)
            BeginRun();
        if (!Started) return;
        Capture();
        if (owner != null && owner.IsDead) { EndRun("Поражение"); return; }
        if (!RunFlow.RunActive && !RunFlow.IsTransitioning) { EndRun("В меню"); return; }
        if (!ineligible && focused && !paused && RunFlow.IsPlaying &&
            Time.timeScale > 0f && !GameMenuUI.BlocksGameplayInput)
        {
            // A resumed frame can carry delta from the suspended interval.
            // The next complete gameplay frame is the first timed frame.
            if (skipTimingFrame) { skipTimingFrame = false; return; }
            float elapsed = Time.unscaledDeltaTime;
            if (!float.IsNaN(elapsed) && !float.IsInfinity(elapsed) && elapsed > 0f)
                ActiveSeconds += elapsed;
        }
    }

    private void Capture()
    {
        if (owner != null)
        {
            latestHeight = Mathf.Max(latestHeight, owner.CurrentHeight);
            latestScore = Mathf.Max(latestScore, owner.CurrentScore);
            ineligible |= owner.IsTestAttempt;
        }
        if (challenges != null && challenges.Session != null) session = challenges.Session;
        if (session != null)
        {
            latestStats = session.Stats;
            ineligible |= !session.IsEligible;
        }
        ineligible |= PlayerProgress.AdminSandboxActive;
    }

    public bool EndRun(string reason)
    {
        if (Finalized) return false;
        if (!Started) BeginRun();
        if (!Started) return false; // Home, loading and unopened drafts aren't runs.
        Capture();
        Finalized = true;
        if (ineligible) return false;
        return PlayerProgress.RecordRun(new RunStatisticsEntry(RunId, portrait, inertia, difficulty,
            latestHeight, latestScore, latestStats.Coins, latestStats.Bushes,
            latestStats.MovingLandings, latestStats.NarrowLandings, latestStats.WideJumps,
            ActiveSeconds, reason));
    }

    private void OnApplicationFocus(bool hasFocus) { focused = hasFocus; skipTimingFrame = true; }
    private void OnApplicationPause(bool isPaused) { paused = isPaused; skipTimingFrame = true; }
    private void OnApplicationQuit() => EndRun("Выход");
    private void OnDestroy()
    {
        // Cache and the plain session survive Unity's component destruction
        // order, even if the Player / bridge is already a fake-null object.
        if (Started && !Finalized) EndRun("Выход из забега");
    }
}
