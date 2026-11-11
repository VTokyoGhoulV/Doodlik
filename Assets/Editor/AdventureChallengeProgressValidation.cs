#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

// Certified new-content events only. This suite never moves the player,
// changes generation, launches Unity or writes real player-profile keys.
public static class AdventureChallengeProgressValidation
{
    private static int checks;
    [Serializable] private sealed class Envelope { public int version; public long revision; public string payload, checksum; }
    [Serializable] private sealed class BoundsFixture
    {
        public int coins = 10;
        public int challengeRocketLandings, challengeGoldenFish, challengeBeeDodges;
        public List<string> completedChallenges = new List<string>();
    }

    public static int Validate()
    {
        bool active = RunFlow.RunActive, transition = RunFlow.IsTransitioning;
        RunFlow.RunActive = RunFlow.IsTransitioning = false;
        try
        {
            using (PlayerProgress.UseIsolatedStorageForValidation(out string primary, out string backup))
                return ValidateInScope(primary, backup);
        }
        finally { RunFlow.RunActive = active; RunFlow.IsTransitioning = transition; }
    }

    internal static int ValidateInScope(string primary, string backup)
    {
        checks = 0;
        SelectedTrials(primary, backup);
        RocketProofs(primary, backup);
        GoldenProofs(primary, backup);
        CareerAndAtomicRecovery(primary, backup);
        V7MigrationAndBounds(primary, backup);
        EligibilityAndFuture(primary, backup);
        Debug.Log($"ADVENTURE_CHALLENGE_PROGRESS_PASS checks={checks} definitions=55 schema=9 isolatedStorage=True");
        return checks;
    }

    private static void SelectedTrials(string primary, string backup)
    {
        Expect(primary, backup, "run_rocket_land1", s => { s.RecordRocketLaunch(); s.RecordRocketEnd(false); s.RecordRocketLaunch(); }, s => s.RecordRocketEnd(true));
        Expect(primary, backup, "run_rocket_wrap1", s => { s.RecordRocketLaunch(); s.RecordRocketWrap(); }, s => s.RecordRocketEnd(true));
        Expect(primary, backup, "run_rocket_land2", SafeFlight, SafeFlight);
        Expect(primary, backup, "run_rocket_climb100", s => { s.RecordRocketLaunch(); s.SetHeight(500); s.RecordRocketEnd(true); s.SetHeight(599); }, s => s.SetHeight(600));
        Expect(primary, backup, "run_gold1", s => Repeat(5, () => s.RecordCoin(false)), s => s.RecordGoldenFish(false, false));
        Expect(primary, backup, "run_gold3", s => Repeat(2, () => s.RecordGoldenFish(false, false)), s => s.RecordGoldenFish(false, false));
        Expect(primary, backup, "run_gold_manual1", s => s.RecordGoldenFish(true, false), s => s.RecordGoldenFish(false, false));
        Expect(primary, backup, "run_gold_magnet1", s => s.RecordGoldenFish(false, false), s => s.RecordGoldenFish(true, false));
        Expect(primary, backup, "run_gold_safe1", s => s.RecordGoldenFish(false, false), s => s.RecordLanding(new ChallengeLandingEvent()));
        Expect(primary, backup, "run_bee_dodge1", s => s.RecordOrdinaryLanding(), s => s.RecordBeeDodge());
        Expect(primary, backup, "run_bee_dodge3", s => Repeat(2, s.RecordBeeDodge), s => s.RecordBeeDodge());
        Expect(primary, backup, "run_sky_combo3", s => { SafeFlight(s); s.RecordGoldenFish(false, false); }, s => s.RecordBeeDodge());
        Reset(primary, backup);
        ChallengeSession unselected = New();
        SafeFlight(unselected); unselected.RecordGoldenFish(false, false); unselected.RecordBeeDodge();
        Check(!PlayerProgress.IsChallengeCompleted("run_sky_combo3") && !PlayerProgress.IsChallengeCompleted("run_gold1") &&
            PlayerProgress.Coins == 0, "New run tasks pay only when selected, never retroactively from another task.");
        Check(PlayerProgress.ChallengeProgressValue("mastery_rocket10") == 1 && PlayerProgress.ChallengeProgressValue("mastery_gold15") == 1 &&
            PlayerProgress.ChallengeProgressValue("mastery_bee20") == 1, "All three new careers advance in the background without a selected trial.");
    }

    private static void RocketProofs(string primary, string backup)
    {
        Reset(primary, backup);
        ChallengeSession s = New();
        s.RecordRocketEnd(true); s.RecordRocketWrap();
        Check(s.Stats.RocketLandings == 0 && s.Stats.RocketWrappedLandings == 0, "A flight cannot finish or wrap before launch.");
        s.RecordRocketLaunch(); s.RecordRocketLaunch();
        s.RecordRocketWrap(); s.RecordRocketWrap(); s.RecordRocketEnd(false); s.RecordRocketEnd(true);
        Check(s.Stats.RocketLaunches == 1 && s.Stats.RocketLandings == 0 && s.Stats.RocketWrappedLandings == 0 &&
            PlayerProgress.ChallengeProgressValue("mastery_rocket10") == 0, "Duplicate launch and aborted wrapped flight cannot fabricate a safe landing.");
        s.RecordRocketLaunch(); s.RecordRocketEnd(true); s.RecordRocketEnd(true);
        Check(s.Stats.RocketLandings == 1 && s.Stats.RocketWrappedLandings == 0,
            "Safe end counts once and does not inherit the aborted flight's wrap flag.");
        s.RecordRocketLaunch(); s.RecordRocketWrap(); s.RecordRocketEnd(true); s.RecordRocketWrap();
        Check(s.Stats.RocketLaunches == 3 && s.Stats.RocketLandings == 2 && s.Stats.RocketWrappedLandings == 1,
            "One completed wrapped flight grants exactly one proof, separate from launch count.");
        Check(s.Stats.Coins == 0 && s.Stats.Powerups == 0 && s.Stats.Wraps == 0,
            "Rocket identity events never duplicate the ordinary power / wrap / currency callbacks.");

        Reset(primary, backup);
        s = New();
        s.SetHeight(200); s.RecordRocketLaunch(); s.SetHeight(500);
        Check(s.Stats.BestPostRocketClimb == 0, "Automatic powered ascent never counts as post-flight climbing.");
        s.RecordRocketEnd(true); s.SetHeight(599);
        Check(s.Stats.BestPostRocketClimb == 99, "Post-flight climb starts at the flight's current maximum height.");
        s.RecordRocketLaunch(); s.SetHeight(900); s.RecordRocketEnd(true);
        Check(s.Stats.BestPostRocketClimb == 99, "A new rocket cancels the previous anchor without losing its earned best.");
        s.SetHeight(901);
        Check(s.Stats.BestPostRocketClimb == 99, "Two smaller climbs cannot accumulate through another automatic flight.");
        s.SetHeight(999); s.RecordBush(); s.SetHeight(1000);
        Check(s.Stats.BestPostRocketClimb == 100, "A normal bush boost is allowed after handoff and height is unmultiplied.");
        foreach (Action<ChallengeSession> interruption in new Action<ChallengeSession>[]
            { run => run.RecordShieldUse(false), run => run.RecordAssistedTravel() })
        {
            Reset(primary, backup); s = New();
            s.RecordRocketLaunch(); s.SetHeight(500); s.RecordRocketEnd(true); s.SetHeight(550);
            interruption(s); s.SetHeight(700);
            Check(s.Stats.BestPostRocketClimb == 50, "Shield or assisted travel cancels post-flight climbing but preserves the previous best.");
        }
        Reset(primary, backup); s = New();
        s.RecordRocketLaunch(); s.SetHeight(500); s.RecordRocketEnd(false); s.SetHeight(700);
        Check(s.Stats.BestPostRocketClimb == 0, "Emergency / cancelled flight does not open a post-flight climbing anchor.");
    }

    private static void GoldenProofs(string primary, string backup)
    {
        Reset(primary, backup);
        ChallengeSession s = New();
        s.RecordGoldenFish(false, false);
        Check(s.Stats.GoldenFish == 1 && s.Stats.GoldenFishWithoutMagnet == 1 && s.Stats.GoldenFishWithMagnet == 0 &&
            s.Stats.Coins == 0 && PlayerProgress.ChallengeProgressValue("mastery_gold15") == 1 &&
            PlayerProgress.ChallengeProgressValue("mastery_coins250") == 0 && PlayerProgress.Coins == 0,
            "Physical golden identity counts one, never its five-unit currency value or a second wallet grant.");
        Repeat(5, () => s.RecordCoin(false));
        Check(s.Stats.Coins == 5 && s.Stats.GoldenFish == 1 && PlayerProgress.ChallengeProgressValue("mastery_coins250") == 5 &&
            PlayerProgress.ChallengeProgressValue("mastery_gold15") == 1, "Nominal coin callbacks cannot multiply the physical fish count.");
        s.RecordLanding(new ChallengeLandingEvent()); s.RecordLanding(new ChallengeLandingEvent());
        Check(s.Stats.GoldenFishSafeLandings == 1, "The pending golden pickup is consumed by one certified new-higher landing.");
        s.RecordGoldenFish(true, false); s.RecordGoldenFish(false, false); s.RecordLanding(new ChallengeLandingEvent());
        Check(s.Stats.GoldenFishSafeLandings == 2 && s.Stats.GoldenFish == 3 && s.Stats.GoldenFishWithMagnet == 1,
            "Several fish in one excursion form one safe-landing proof; each fish identity and magnet condition remain distinct.");
        s.RecordGoldenFish(false, true); s.RecordLanding(new ChallengeLandingEvent());
        Check(s.Stats.GoldenFishSafeLandings == 2 && s.Stats.GoldenFish == 4,
            "A rocket-assisted fish counts as a fish but cannot certify the independent safe-return task.");
        foreach (Action<ChallengeSession> interruption in new Action<ChallengeSession>[]
            { run => run.RecordOrdinaryLanding(true), run => run.RecordShieldUse(true),
                run => run.RecordAssistedTravel(), run => run.RecordRocketLaunch() })
        {
            Reset(primary, backup); s = New();
            s.RecordGoldenFish(false, false); interruption(s); s.RecordLanding(new ChallengeLandingEvent());
            Check(s.Stats.GoldenFishSafeLandings == 0, "Lower/repeat landing, shield, assisted travel or a new rocket invalidates the pending safe-return proof.");
        }
        Reset(primary, backup); s = New();
        s.RecordPowerup(RunPowerups.Kind.Magnet); s.RecordGoldenFish(false, false);
        Check(s.Stats.GoldenFishWithoutMagnet == 1, "Manual fish means no active magnet at collection, not a ban on earlier magnets.");
        s.RecordPowerup(RunPowerups.Kind.Rocket);
        Check(s.Stats.Powerups == 2 && s.Stats.PowerupKinds == 1,
            "Rocket is still a real power pickup but never a fourth requirement in the old three-kind task.");
    }

    private static void CareerAndAtomicRecovery(string primary, string backup)
    {
        string[] ids = { "mastery_rocket10", "mastery_gold15", "mastery_bee20" };
        int[] targets = { 10, 15, 20 }, rewards = { 150, 150, 200 };
        Action<ChallengeSession>[] events = { SafeFlight, s => s.RecordGoldenFish(false, false), s => s.RecordBeeDodge() };
        for (int kind = 0; kind < ids.Length; kind++)
        {
            Reset(primary, backup);
            for (int i = 0; i < targets[kind] - 1; i++) events[kind](New());
            Check(!PlayerProgress.IsChallengeCompleted(ids[kind]) && PlayerProgress.ChallengeProgressValue(ids[kind]) == targets[kind] - 1 &&
                PlayerProgress.Coins == 0, "Career stores certified identities across runs, without paying early: " + ids[kind]);
            PlayerProgress.Reload();
            events[kind](New());
            Check(PlayerProgress.IsChallengeCompleted(ids[kind]) && PlayerProgress.Coins == rewards[kind] &&
                PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup),
                "Career completion, saved counter and its coin reward share one durable transaction: " + ids[kind]);
            PlayerPrefs.SetString(primary, "{broken"); PlayerProgress.Reload();
            events[kind](New());
            Check(PlayerProgress.IsChallengeCompleted(ids[kind]) && PlayerProgress.Coins == rewards[kind] &&
                PlayerProgress.ChallengeProgressValue(ids[kind]) == targets[kind],
                "Backup recovery and later events cannot repeat a career payout: " + ids[kind]);
        }
    }

    private static void V7MigrationAndBounds(string primary, string backup)
    {
        Reset(primary, backup);
        PlayerProgress.AddCoins(1000);
        Check(PlayerProgress.TryPurchase("cat_rose") && PlayerProgress.Equip("cat_rose") &&
            PlayerProgress.TryPurchase("accessory_glasses") && PlayerProgress.Equip("accessory_glasses") &&
            PlayerProgress.TryUpgrade(PlayerProgress.Upgrade.Magnet, "portrait_precise"), "v7 fixture owns a real outfit and mode upgrade.");
        Check(PlayerProgress.SelectChallenge("run_center10"), "v7 fixture selects an original task.");
        ChallengeSession oldRun = new ChallengeSession("run_center10", false, GameDifficulty.Normal);
        Repeat(10, () => oldRun.RecordLanding(new ChallengeLandingEvent { Centered = true }));
        Check(PlayerProgress.SelectChallenge("run_wide10"), "v7 fixture keeps another old task selected.");
        Check(PlayerProgress.RecordRun(new RunStatisticsEntry("v7_history", true, true, GameDifficulty.Hard,
            321, 450, 9, 2, 3, 4, 5, 123.5, "Поражение")), "v7 fixture contains a finished journal entry.");
        int oldWallet = PlayerProgress.Coins;
        Envelope v7 = Read(primary);
        v7.payload = v7.payload.Replace(",\"challengeRocketLandings\":0", string.Empty)
            .Replace(",\"challengeGoldenFish\":0", string.Empty).Replace(",\"challengeBeeDodges\":0", string.Empty)
            .Replace("\"owned\":[", "\"owned\":[\"future_v7_cosmetic\",")
            .Replace("\"completedChallenges\":[", "\"completedChallenges\":[\"future_v7_task\",")
            .Replace("\"redeemedCodes\":[]", "\"redeemedCodes\":[\"v7-code-proof\"]");
        Check(!v7.payload.Contains("challengeRocketLandings") && !v7.payload.Contains("challengeGoldenFish") &&
            !v7.payload.Contains("challengeBeeDodges"), "The v7 fixture genuinely lacks all three new proof counters.");
        string legacy = Encode(7, v7.payload, v7.revision);
        Put(primary, backup, legacy, legacy);
        Envelope migrated = Read(primary);
        CareerStatisticsSnapshot stats = PlayerProgress.Statistics;
        Check(migrated.version == 9 && migrated.revision == v7.revision + 1 && PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup),
            "v7 migrates once into two matching current recovery copies.");
        Check(PlayerProgress.Coins == oldWallet && PlayerProgress.IsEquipped("cat_rose") && PlayerProgress.IsEquipped("accessory_glasses") &&
            PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Magnet, "portrait_precise") == 1 &&
            PlayerProgress.IsChallengeCompleted("run_center10") && PlayerProgress.SelectedChallengeId == "run_wide10" &&
            PlayerProgress.ChallengeProgressValue("mastery_center50") == 10,
            "v7 wallet, outfit, mode upgrade, old completion, selection and old career progress survive exactly.");
        Check(stats.Runs == 1 && stats.TotalCoins == 9 && stats.ActiveSeconds == 123.5 && stats.BestRunHeight == 321 &&
            stats.RecentRuns.Count == 1 && stats.RecentRuns[0].Id == "v7_history" && stats.RecentRuns[0].Portrait &&
            stats.RecentRuns[0].Inertia && stats.RecentRuns[0].Difficulty == GameDifficulty.Hard,
            "v7 finished-run totals and all recent journal mode fields are preserved, not reset.");
        Check(migrated.payload.Contains("future_v7_cosmetic") && migrated.payload.Contains("future_v7_task") &&
            migrated.payload.Contains("v7-code-proof"), "v7 migration retains unknown ownership/completions and redeemed proofs.");
        Check(PlayerProgress.ChallengeProgressValue("mastery_rocket10") == 0 && PlayerProgress.ChallengeProgressValue("mastery_gold15") == 0 &&
            PlayerProgress.ChallengeProgressValue("mastery_bee20") == 0, "Old journal and coin values never invent missing new-content identities.");
        string once = PlayerPrefs.GetString(primary);
        PlayerProgress.Reload();
        Check(PlayerPrefs.GetString(primary) == once && PlayerProgress.Coins == oldWallet &&
            !PlayerProgress.RecordRun(new RunStatisticsEntry("v7_history", false, false, GameDifficulty.Easy, 0, 0, 0, 0, 0, 0, 0, 0, "Выход")),
            "Current reload does not remigrate, repay or lose the journal's duplicate-ID guarantee.");
        PlayerPrefs.SetString(primary, "broken"); PlayerProgress.Reload();
        Check(PlayerProgress.Coins == oldWallet && PlayerProgress.Statistics.Runs == 1 && PlayerProgress.IsChallengeCompleted("run_center10"),
            "Recovery of migrated data retains the old wallet, challenge and journal together.");

        BoundsFixture bounds = new BoundsFixture { challengeRocketLandings = int.MaxValue, challengeGoldenFish = -20,
            challengeBeeDodges = int.MaxValue, completedChallenges = new List<string> { "future_keep", "future_keep" } };
        Put(primary, backup, Encode(8, JsonUtility.ToJson(bounds), 12), null);
        Check(PlayerProgress.ChallengeProgressValue("mastery_rocket10") == 10 && PlayerProgress.ChallengeProgressValue("mastery_gold15") == 0 &&
            PlayerProgress.ChallengeProgressValue("mastery_bee20") == 20, "New persisted counters clamp negative and oversized values without overflow.");
        Put(primary, backup, Encode(7, JsonUtility.ToJson(bounds), 12), null);
        Check(PlayerProgress.ChallengeProgressValue("mastery_rocket10") == 0 && PlayerProgress.ChallengeProgressValue("mastery_gold15") == 0 &&
            PlayerProgress.ChallengeProgressValue("mastery_bee20") == 0, "A pre-v8 schema cannot smuggle fabricated new-content counters through migration.");
    }

    private static void EligibilityAndFuture(string primary, string backup)
    {
        Reset(primary, backup);
        ChallengeSession s = New();
        s.RecordRocketLaunch(); s.RecordGoldenFish(false, false); s.SetHeight(400);
        PlayerProgress.BeginAdminSandbox();
        s.RecordRocketEnd(true); s.RecordBeeDodge();
        PlayerProgress.EndAdminSandbox();
        s.RecordRocketLaunch(); s.RecordRocketWrap(); s.RecordRocketEnd(true); s.RecordGoldenFish(false, false);
        s.RecordBeeDodge(); s.RecordLanding(new ChallengeLandingEvent()); s.SetHeight(1000);
        Check(!s.IsEligible && s.Stats.RocketLandings == 0 && s.Stats.GoldenFish == 1 && s.Stats.BeeDodges == 0 &&
            s.Stats.GoldenFishSafeLandings == 0 && s.Stats.BestPostRocketClimb == 0,
            "Admin invalidates all pending proof / climbing state permanently, even after the sandbox closes.");
        Check(PlayerProgress.ChallengeProgressValue("mastery_rocket10") == 0 && PlayerProgress.ChallengeProgressValue("mastery_gold15") == 1 &&
            PlayerProgress.ChallengeProgressValue("mastery_bee20") == 0, "Admin and invalid attempts cannot advance normal new-content career counters.");
        s = New(); s.RecordRocketLaunch(); s.RecordRocketWrap(); s.RecordGoldenFish(false, false); s.Invalidate();
        s.RecordRocketEnd(true); s.RecordBeeDodge(); s.RecordLanding(new ChallengeLandingEvent());
        Check(s.Stats.RocketLandings == 0 && s.Stats.BeeDodges == 0 && s.Stats.GoldenFishSafeLandings == 0,
            "Explicit test invalidation also clears pending rocket / fish state before callbacks can finish.");
        Reset(primary, backup); PlayerProgress.AddCoins(17);
        string supported = PlayerPrefs.GetString(primary);
        Envelope current = Read(primary);
        string future = Encode(10, current.payload, current.revision);
        foreach (bool primaryFuture in new[] { true, false })
        {
            Put(primary, backup, primaryFuture ? future : supported, primaryFuture ? supported : future);
            string first = PlayerPrefs.GetString(primary), second = PlayerPrefs.GetString(backup);
            s = New();
            Repeat(20, () => { SafeFlight(s); s.RecordGoldenFish(false, false); s.RecordBeeDodge(); });
            PlayerProgress.Flush();
            Check(PlayerPrefs.GetString(primary) == first && PlayerPrefs.GetString(backup) == second && PlayerProgress.Coins == 17,
                "All new events preserve a future v10 primary or recovery copy byte-for-byte.");
        }
    }

    private static void Expect(string primary, string backup, string id, Action<ChallengeSession> partial, Action<ChallengeSession> finish)
    {
        Reset(primary, backup);
        Check(PlayerProgress.SelectChallenge(id), "New trial can be selected: " + id);
        ChallengeSession session = new ChallengeSession(id, false, GameDifficulty.Normal);
        partial(session);
        Check(!PlayerProgress.IsChallengeCompleted(id), "Partial proof does not finish early: " + id);
        int before = PlayerProgress.Coins;
        finish(session);
        ChallengeDefinition definition = ChallengeCatalog.Find(id);
        Check(PlayerProgress.IsChallengeCompleted(id) && session.SelectedProgress == definition.Target &&
            PlayerProgress.Coins == before + definition.RewardCoins && PlayerProgress.SelectedChallengeId == string.Empty &&
            PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup), "Exact threshold atomically completes, pays and clears selection: " + id);
        int paid = PlayerProgress.Coins;
        finish(session);
        Check(PlayerProgress.Coins == paid, "Repeated proof never repeats a selected payout: " + id);
        PlayerProgress.Reload();
        Check(PlayerProgress.IsChallengeCompleted(id) && PlayerProgress.Coins == paid, "New trial completion persists: " + id);
    }
    private static ChallengeSession New() => new ChallengeSession(string.Empty, false, GameDifficulty.Normal);
    private static void SafeFlight(ChallengeSession s) { s.RecordRocketLaunch(); s.RecordRocketEnd(true); }
    private static void Repeat(int count, Action action) { for (int i = 0; i < count; i++) action(); }
    private static void Reset(string primary, string backup) => Put(primary, backup, null, null);
    private static void Put(string primary, string backup, string first, string second)
    {
        PlayerProgress.EndAdminSandbox();
        if (first == null) PlayerPrefs.DeleteKey(primary); else PlayerPrefs.SetString(primary, first);
        if (second == null) PlayerPrefs.DeleteKey(backup); else PlayerPrefs.SetString(backup, second);
        PlayerProgress.Reload();
    }
    private static Envelope Read(string key) => JsonUtility.FromJson<Envelope>(PlayerPrefs.GetString(key));
    private static string Encode(int version, string payload, long revision)
    {
        uint hash = 2166136261;
        unchecked
        {
            foreach (char c in payload) hash = (hash ^ c) * 16777619;
            hash = (hash ^ (uint)revision) * 16777619;
            hash = (hash ^ (uint)(revision >> 32)) * 16777619;
        }
        return JsonUtility.ToJson(new Envelope { version = version, revision = revision, payload = payload, checksum = hash.ToString("X8") });
    }
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("Adventure challenge progress: " + message);
    }
}
#endif
