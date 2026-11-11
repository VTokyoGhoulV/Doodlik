using System;

public readonly struct ChallengeRunStats
{
    public readonly int Height, Coins, CoinsWithoutMagnet, CenterLandings, MovingLandings,
        NarrowLandings, WideJumps, Powerups, ShieldUses, FallingRescues;
    public readonly int FallingCoinSaves, ApexCoins, BestMagnetCoins, SpaceCoins, EdgeLandings,
        CenterStreak, BestCenterStreak, Turns, Wraps, Bushes, BushChain, BestBushChain,
        BushSkippedRows, PowerupKinds, BestShieldRecovery, SlowMovingLandings;
    public readonly int RocketLaunches, RocketLandings, RocketWrappedLandings, BestPostRocketClimb,
        GoldenFish, GoldenFishWithoutMagnet, GoldenFishWithMagnet, GoldenFishSafeLandings, BeeDodges;
    public readonly bool TookPowerup, UsedMagnet, UsedShield, Inertia;
    public readonly GameDifficulty Difficulty;

    internal ChallengeRunStats(int height, int coins, int unassistedCoins, int centered, int moving,
        int narrow, int wide, int powerups, int shields, int rescues, bool usedMagnet,
        bool inertia, GameDifficulty difficulty, int fallingCoinSaves, int apexCoins,
        int bestMagnetCoins, int spaceCoins, int edges, int centerStreak, int bestCenterStreak,
        int turns, int wraps, int bushes, int bushChain, int bestBushChain, int skippedRows,
        int powerupKinds, int bestShieldRecovery, int slowMovingLandings,
        int rocketLaunches = 0, int rocketLandings = 0, int rocketWrappedLandings = 0, int bestPostRocketClimb = 0,
        int goldenFish = 0, int goldenManual = 0, int goldenMagnet = 0, int goldenSafeLandings = 0, int beeDodges = 0)
    {
        Height = height; Coins = coins; CoinsWithoutMagnet = unassistedCoins;
        CenterLandings = centered; MovingLandings = moving; NarrowLandings = narrow; WideJumps = wide;
        Powerups = powerups; ShieldUses = shields; FallingRescues = rescues;
        TookPowerup = powerups > 0; UsedMagnet = usedMagnet; UsedShield = shields > 0;
        Inertia = inertia; Difficulty = difficulty;
        FallingCoinSaves = fallingCoinSaves; ApexCoins = apexCoins; BestMagnetCoins = bestMagnetCoins;
        SpaceCoins = spaceCoins; EdgeLandings = edges; CenterStreak = centerStreak; BestCenterStreak = bestCenterStreak;
        Turns = turns; Wraps = wraps; Bushes = bushes; BushChain = bushChain; BestBushChain = bestBushChain;
        BushSkippedRows = skippedRows; PowerupKinds = powerupKinds; BestShieldRecovery = bestShieldRecovery;
        SlowMovingLandings = slowMovingLandings;
        RocketLaunches = rocketLaunches; RocketLandings = rocketLandings; RocketWrappedLandings = rocketWrappedLandings;
        BestPostRocketClimb = bestPostRocketClimb; GoldenFish = goldenFish; GoldenFishWithoutMagnet = goldenManual;
        GoldenFishWithMagnet = goldenMagnet; GoldenFishSafeLandings = goldenSafeLandings; BeeDodges = beeDodges;
    }
}

// Values are certified by the physics bridge for a new higher safe platform.
public struct ChallengeLandingEvent
{
    public bool Centered, Moving, Narrow, Wide, Edge, SlowActive;
    public int Direction, BushSkippedRows;
}

// Deterministic rules only. The Player's integration layer owns this object and
// supplies actual, de-duplicated physics/pickup events for one natural attempt.
public sealed class ChallengeSession
{
    public string SelectedId { get; }
    public bool IsEligible { get; private set; } = true;
    public event Action Changed;
    private readonly bool inertia;
    private readonly GameDifficulty difficulty;
    private int height, coins, unassistedCoins, centered, moving, narrow, wide, powerups, shields, rescues;
    private bool usedMagnet;
    private int fallingCoinSaves, apexCoins, magnetCoins, bestMagnetCoins, spaceCoins, edges;
    private int centerStreak, bestCenterStreak, turns, previousDirection, wraps, bushes, bushChain, bestBushChain;
    private int skippedRows, powerupMask, shieldAtHeight = -1, bestShieldRecovery, slowMovingLandings;
    private bool pendingFallingCoin, bushFlight, magnetArmed;
    private int rocketLaunches, rocketLandings, rocketWrappedLandings, bestPostRocketClimb, postRocketAtHeight = -1;
    private int goldenFish, goldenManual, goldenMagnet, goldenSafeLandings, beeDodges;
    private bool rocketActive, rocketWrapped, pendingGoldenFish;

    public ChallengeSession(string selectedId, bool inertia, GameDifficulty difficulty)
    {
        ChallengeDefinition definition = ChallengeCatalog.Find(selectedId);
        SelectedId = definition != null && !definition.IsAchievement ? definition.Id : string.Empty;
        this.inertia = inertia;
        this.difficulty = DifficultyRules.Sanitize(difficulty);
        if (PlayerProgress.AdminSandboxActive) IsEligible = false;
    }

    public ChallengeRunStats Stats => new ChallengeRunStats(height, coins, unassistedCoins,
        centered, moving, narrow, wide, powerups, shields, rescues, usedMagnet, inertia, difficulty,
        fallingCoinSaves, apexCoins, bestMagnetCoins, spaceCoins, edges, centerStreak, bestCenterStreak,
        turns, wraps, bushes, bushChain, bestBushChain, skippedRows, CountKinds(), bestShieldRecovery, slowMovingLandings,
        rocketLaunches, rocketLandings, rocketWrappedLandings, bestPostRocketClimb, goldenFish, goldenManual,
        goldenMagnet, goldenSafeLandings, beeDodges);
    public int SelectedProgress => GetProgress(SelectedId);
    public bool SelectedFailed => SelectedId.Length > 0 && !PlayerProgress.IsChallengeCompleted(SelectedId) &&
        (!IsEligible || Failed(SelectedId));

    public int GetProgress(string id)
    {
        ChallengeDefinition definition = ChallengeCatalog.Find(id);
        if (definition == null) return 0;
        if (PlayerProgress.IsChallengeCompleted(id)) return definition.Target;
        if (definition.IsAchievement) return PlayerProgress.ChallengeProgressValue(id);
        if (id != SelectedId) return PlayerProgress.IsChallengeCompleted(id) ? definition.Target : 0;
        return Math.Min(definition.Target, ProgressFor(id));
    }

    public void SetHeight(int value)
    {
        if (!Accept() || value <= height) return;
        height = Math.Max(0, value);
        if (shieldAtHeight >= 0) bestShieldRecovery = Math.Max(bestShieldRecovery, height - shieldAtHeight);
        if (postRocketAtHeight >= 0 && !rocketActive)
            bestPostRocketClimb = Math.Max(bestPostRocketClimb, height - postRocketAtHeight);
        Evaluate(false, false);
    }

    public void RecordCoin(bool magnet) => RecordCoin(magnet, false, false, false);

    public void RecordCoin(bool magnet, bool falling, bool nearApex, bool inSpace)
    {
        if (!Accept()) return;
        coins = Increment(coins);
        if (magnet) usedMagnet = true;
        else unassistedCoins = Increment(unassistedCoins);
        pendingFallingCoin |= falling;
        if (nearApex) apexCoins = Increment(apexCoins);
        if (inSpace) spaceCoins = Increment(spaceCoins);
        if (magnet && magnetArmed)
        {
            magnetCoins = Increment(magnetCoins);
            bestMagnetCoins = Math.Max(bestMagnetCoins, magnetCoins);
        }
        Evaluate(false, false, coinEvent: true);
    }

    public void RecordPowerup(bool isMagnet = false)
        => RecordPowerupCore(isMagnet, isMagnet ? 1 << (int)RunPowerups.Kind.Magnet : 0);

    public void RecordPowerup(RunPowerups.Kind kind)
    {
        if (kind != RunPowerups.Kind.Shield && kind != RunPowerups.Kind.Magnet &&
            kind != RunPowerups.Kind.Slow && kind != RunPowerups.Kind.Rocket) return;
        RecordPowerupCore(kind == RunPowerups.Kind.Magnet, kind == RunPowerups.Kind.Rocket ? 0 : 1 << (int)kind);
    }

    private void RecordPowerupCore(bool isMagnet, int kindBit)
    {
        if (!Accept()) return;
        powerups = Increment(powerups);
        usedMagnet |= isMagnet;
        powerupMask |= kindBit;
        if (isMagnet) { magnetCoins = 0; magnetArmed = true; }
        Evaluate(false, false);
    }

    public void RecordShieldUse(bool fallingRescue)
    {
        if (!Accept()) return;
        shields = Increment(shields);
        if (fallingRescue) rescues = Increment(rescues);
        pendingFallingCoin = false;
        pendingGoldenFish = false;
        postRocketAtHeight = -1;
        bushFlight = false;
        bushChain = centerStreak = previousDirection = 0;
        shieldAtHeight = height;
        Evaluate(false, fallingRescue);
    }

    public void RecordLanding(bool centered, bool moving, bool narrow, bool wide)
    {
        RecordLanding(new ChallengeLandingEvent { Centered = centered, Moving = moving, Narrow = narrow, Wide = wide });
        RecordOrdinaryLanding();
    }

    public void RecordLanding(ChallengeLandingEvent landing)
    {
        if (!Accept()) return;
        if (landing.Centered) centered = Increment(centered);
        if (landing.Moving) moving = Increment(moving);
        if (landing.Narrow) narrow = Increment(narrow);
        if (landing.Wide) wide = Increment(wide);
        if (landing.Edge) edges = Increment(edges);
        centerStreak = landing.Centered ? Increment(centerStreak) : 0;
        bestCenterStreak = Math.Max(bestCenterStreak, centerStreak);
        int direction = Math.Sign(landing.Direction);
        if (direction != 0)
        {
            if (previousDirection != 0 && direction != previousDirection) turns = Increment(turns);
            previousDirection = direction;
        }
        if (landing.Moving && landing.SlowActive) slowMovingLandings = Increment(slowMovingLandings);
        if (pendingFallingCoin) fallingCoinSaves = Increment(fallingCoinSaves);
        if (pendingGoldenFish) goldenSafeLandings = Increment(goldenSafeLandings);
        pendingFallingCoin = false;
        pendingGoldenFish = false;
        if (bushFlight) skippedRows = Math.Max(skippedRows, Math.Max(0, landing.BushSkippedRows));
        bushFlight = false;
        Evaluate(landing.Centered, false, movingEvent: landing.Moving, narrowEvent: landing.Narrow);
    }

    public void RecordBush()
    {
        if (!Accept()) return;
        bushes = Increment(bushes);
        bushChain = Increment(bushChain);
        bestBushChain = Math.Max(bestBushChain, bushChain);
        bushFlight = true;
        Evaluate(false, false, bushEvent: true);
    }

    public void RecordWrap()
    {
        if (!Accept()) return;
        wraps = Increment(wraps);
        Evaluate(false, false);
    }

    public void RecordOrdinaryLanding(bool breakCenterStreak = false)
    {
        if (!Accept()) return;
        bool changed = bushChain != 0 || pendingFallingCoin || pendingGoldenFish || bushFlight || breakCenterStreak && centerStreak != 0;
        bushChain = 0;
        pendingFallingCoin = false;
        pendingGoldenFish = false;
        bushFlight = false;
        if (breakCenterStreak) centerStreak = 0;
        if (changed) Changed?.Invoke();
    }

    public void RecordAssistedTravel()
    {
        if (!Accept()) return;
        pendingFallingCoin = bushFlight = false;
        pendingGoldenFish = false;
        postRocketAtHeight = -1;
        bushChain = centerStreak = previousDirection = 0;
        Changed?.Invoke();
    }

    public void RecordRocketLaunch()
    {
        if (!Accept() || rocketActive) return;
        rocketActive = true;
        rocketWrapped = false;
        pendingGoldenFish = false;
        postRocketAtHeight = -1;
        rocketLaunches = Increment(rocketLaunches);
        Evaluate(false, false);
    }

    // The bridge calls this only for an actual seam crossing during powered,
    // player-controlled flight, never during the automatic landing phase.
    public void RecordRocketWrap()
    {
        if (!Accept() || !rocketActive) return;
        rocketWrapped = true;
    }

    public void RecordRocketEnd(bool safeLanding)
    {
        if (!Accept() || !rocketActive) return;
        rocketActive = false;
        bool wrapped = rocketWrapped;
        rocketWrapped = false;
        if (!safeLanding) { postRocketAtHeight = -1; Changed?.Invoke(); return; }
        rocketLandings = Increment(rocketLandings);
        if (wrapped) rocketWrappedLandings = Increment(rocketWrappedLandings);
        // Height is the current maximum supplied by the physical run bridge;
        // automatic flight height must not become post-flight climbing credit.
        postRocketAtHeight = height;
        Evaluate(false, false, rocketLandingEvent: true);
    }

    // Identity event, not currency: the ordinary coin callback separately
    // awards / records the fish's five nominal coin units exactly once.
    public void RecordGoldenFish(bool magnet, bool assisted)
    {
        if (!Accept()) return;
        goldenFish = Increment(goldenFish);
        if (magnet) goldenMagnet = Increment(goldenMagnet);
        else goldenManual = Increment(goldenManual);
        pendingGoldenFish = !assisted;
        Evaluate(false, false, goldenFishEvent: true);
    }

    public void RecordBeeDodge()
    {
        if (!Accept()) return;
        beeDodges = Increment(beeDodges);
        Evaluate(false, false, beeDodgeEvent: true);
    }

    public void Invalidate()
    {
        if (!IsEligible) return;
        IsEligible = false;
        pendingFallingCoin = bushFlight = magnetArmed = false;
        rocketActive = rocketWrapped = pendingGoldenFish = false;
        postRocketAtHeight = -1;
        Changed?.Invoke();
    }

    private bool Accept()
    {
        if (PlayerProgress.AdminSandboxActive) Invalidate();
        return IsEligible;
    }

    private void Evaluate(bool centeredEvent, bool rescueEvent, bool coinEvent = false,
        bool bushEvent = false, bool movingEvent = false, bool narrowEvent = false,
        bool rocketLandingEvent = false, bool goldenFishEvent = false, bool beeDodgeEvent = false)
    {
        PlayerProgress.ApplyChallengeProgress(Stats, SelectedId, centeredEvent, rescueEvent,
            !Failed(SelectedId), ProgressFor(SelectedId), coinEvent, bushEvent, movingEvent, narrowEvent,
            rocketLandingEvent, goldenFishEvent, beeDodgeEvent);
        Changed?.Invoke();
    }

    private bool Failed(string id) => id == "run_coin_nomagnet15" && usedMagnet ||
        (id == "run_height500_nopowers" || id == "run_no_powers1000") && powerups > 0 ||
        (id == "run_height500_noshield" || id == "run_clean1000") && shields > 0;

    private int ProgressFor(string id)
    {
        switch (id)
        {
            case "run_coin10": case "run_coin25": case "run_coin50": return coins;
            case "run_coin_nomagnet15": return unassistedCoins;
            case "run_height500_nopowers": case "run_height500_noshield": case "run_height1000":
            case "run_clean1000": case "run_no_powers1000": return height;
            case "run_moving5": case "run_moving10": return moving;
            case "run_narrow8": case "run_narrow12": return narrow;
            case "run_center10": return centered;
            case "run_wide5": case "run_wide10": return wide;
            case "run_coin_fall_save": return fallingCoinSaves;
            case "run_coin_apex3": return apexCoins;
            case "run_magnet2": return bestMagnetCoins;
            case "run_space_coins3": return spaceCoins;
            case "run_edge5": return edges;
            case "run_center_streak5": return centerStreak;
            case "run_turn4": return turns;
            case "run_wrap5": return wraps;
            case "run_bush3": return bushes;
            case "run_bush_chain2": return bushChain;
            case "run_bush_skip2": return skippedRows;
            case "run_all_powers": return CountKinds();
            case "run_shield_recovery100": return bestShieldRecovery;
            case "run_slow_moving1": return slowMovingLandings;
            case "run_rocket_land1": case "run_rocket_land2": return rocketLandings;
            case "run_rocket_wrap1": return rocketWrappedLandings;
            case "run_rocket_climb100": return bestPostRocketClimb;
            case "run_gold1": case "run_gold3": return goldenFish;
            case "run_gold_manual1": return goldenManual;
            case "run_gold_magnet1": return goldenMagnet;
            case "run_gold_safe1": return goldenSafeLandings;
            case "run_bee_dodge1": case "run_bee_dodge3": return beeDodges;
            case "run_sky_combo3": return (rocketLandings > 0 ? 1 : 0) + (goldenFish > 0 ? 1 : 0) + (beeDodges > 0 ? 1 : 0);
            default: return 0;
        }
    }

    private static int Increment(int value) => value < int.MaxValue ? value + 1 : value;
    private int CountKinds() => ((powerupMask & 1) != 0 ? 1 : 0) + ((powerupMask & 2) != 0 ? 1 : 0) +
        ((powerupMask & 4) != 0 ? 1 : 0);
}
