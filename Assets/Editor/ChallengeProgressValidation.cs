#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Deterministic domain/save tests. Only disposable PlayerProgress keys are
// written; no real wallet, records, preferences or Unity assets are replaced.
public static class ChallengeProgressValidation
{
    private static int checks;

    [Serializable] private sealed class Envelope
    {
        public int version;
        public long revision;
        public string payload, checksum;
    }

    [Serializable] private sealed class Fixture
    {
        public int coins;
        public List<string> owned = new List<string>();
        public string cat = "cat_ginger", clothing = "clothing_original", accessory = "accessory_none", trail = "trail_none";
        public List<string> accessories = new List<string>();
        public List<string> redeemedCodes = new List<string>();
        public string selectedChallenge;
        public List<string> completedChallenges;
        public int centeredLandings, fallingRescues, preciseChallengeHeight, inertiaChallengeHeight;
        public int hardCleanChallengeHeight, hardChallengeHeight;
        public int challengeCoinsCollected, challengeBushesUsed, challengeMovingLandings, challengeNarrowLandings;
    }

    [Serializable] private sealed class LegacyFixture
    {
        public int coins;
        public List<string> owned = new List<string>();
        public string cat = "cat_ginger", clothing = "clothing_original", accessory = "accessory_none", trail = "trail_none";
        public List<string> accessories = new List<string>();
        public List<string> redeemedCodes = new List<string>();
    }

    [Serializable] private sealed class V5Fixture
    {
        public int coins;
        public List<string> owned = new List<string>();
        public string cat = "cat_ginger", clothing = "clothing_original", accessory = "accessory_none", trail = "trail_none";
        public List<string> accessories = new List<string>();
        public List<string> redeemedCodes = new List<string>();
        public string selectedChallenge;
        public List<string> completedChallenges = new List<string>();
        public int centeredLandings, fallingRescues, preciseChallengeHeight, inertiaChallengeHeight;
        public int hardCleanChallengeHeight, hardChallengeHeight;
    }

    public static void Run() => Validate();

    public static void RunAll()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-"))
            throw new InvalidOperationException("Challenge core validation requires an isolated batch-mode QA project.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikChallengeCoreSandbox";
        EditorUtility.audioMasterMute = true;
        AudioListener.volume = 0f;
        AudioListener.pause = true;
        Validate();
        CosmeticsProgressValidation.Validate();
        CosmeticMigrationValidation.Validate();
        Debug.Log("CHALLENGE_CORE_AND_MIGRATION_PASS: fifty-five challenge rules, wallet/reward atomicity, v1-v7 migration and recovery.");
    }

    public static int Validate()
    {
        checks = 0;
        bool active = RunFlow.RunActive, transitioning = RunFlow.IsTransitioning;
        RunFlow.RunActive = RunFlow.IsTransitioning = false;
        try
        {
            using (PlayerProgress.UseIsolatedStorageForValidation(out string primary, out string backup))
            {
                Catalog();
                SelectedTasks(primary, backup);
                Restrictions(primary, backup);
                BackgroundAchievements(primary, backup);
                ExtendedRunTasks(primary, backup);
                ExtendedCareerAchievements(primary, backup);
                Eligibility(primary, backup);
                MigrationRecoveryAndBounds(primary, backup);
                V5MigrationAndTaskMilestone(primary, backup);
                checks += AdventureChallengeProgressValidation.ValidateInScope(primary, backup);
            }
        }
        finally { RunFlow.RunActive = active; RunFlow.IsTransitioning = transitioning; }
        Debug.Log($"CHALLENGE_PROGRESS_PASS checks={checks} definitions={ChallengeCatalog.All.Count} isolatedStorage=True schema=9");
        return checks;
    }

    private static void Catalog()
    {
        HashSet<string> ids = new HashSet<string>();
        int run = 0, career = 0;
        foreach (ChallengeDefinition definition in ChallengeCatalog.All)
        {
            Check(!string.IsNullOrEmpty(definition.Id) && ids.Add(definition.Id), "Challenge IDs are unique.");
            Check(definition.Target > 0 && !string.IsNullOrEmpty(definition.Name) &&
                !string.IsNullOrEmpty(definition.Description), "Every task has a real goal and readable description.");
            Check(ReferenceEquals(ChallengeCatalog.Find(definition.Id), definition), "Catalog lookup returns its definition.");
            if (definition.IsAchievement)
            {
                career++;
                if (definition.RewardItemId.Length > 0)
                {
                    Check(definition.RewardCoins == 0, "Exclusive cosmetic reward is not replaced by coins.");
                    bool found = false;
                    foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
                        if (item.Id == definition.RewardItemId) { found = true; Check(item.Price < 0, "Reward cannot be bought."); }
                    Check(found && !string.IsNullOrEmpty(PlayerProgress.UnlockDescription(definition.RewardItemId)),
                        "Career reward is present and explains its unlock.");
                }
                else Check(definition.RewardCoins > 0, "Additional career milestone grants a visible coin reward.");
            }
            else { run++; Check(definition.RewardCoins > 0 && definition.RewardItemId.Length == 0, "Run task grants coins once."); }
        }
        Check(run == 42 && career == 13 && ids.Count == 55, "The full release has forty-two run tasks and thirteen career achievements.");
        Check(ChallengeCatalog.Find(null) == null && ChallengeCatalog.Find("") == null &&
            ChallengeCatalog.Find("unknown") == null, "Unknown tasks are not fabricated.");
        Check(PlayerProgress.SelectChallenge("") && PlayerProgress.SelectedChallengeId == "", "No challenge is a valid selection.");
        Check(!PlayerProgress.SelectChallenge("unknown") && !PlayerProgress.SelectChallenge("mastery_center50"),
            "Unknown and automatic achievements cannot be selected as run tasks.");
    }

    private static void SelectedTasks(string primary, string backup)
    {
        Reset(primary, backup);
        Check(PlayerProgress.SelectChallenge("run_coin10"), "Run task can be selected at home.");
        ChallengeSession run = new ChallengeSession(PlayerProgress.SelectedChallengeId, false, GameDifficulty.Easy);
        ChallengeRunStats snapshot = run.Stats;
        for (int i = 0; i < 9; i++) run.RecordCoin(false);
        Check(run.SelectedProgress == 9 && PlayerProgress.Coins == 0 && !PlayerProgress.IsChallengeCompleted("run_coin10"),
            "Below-goal progress never pays.");
        run.RecordCoin(false);
        Check(PlayerProgress.IsChallengeCompleted("run_coin10") && PlayerProgress.Coins == 30 && run.SelectedProgress == 10,
            "Goal grants the mark and exact reward atomically.");
        Check(PlayerProgress.SelectedChallengeId == "" && run.SelectedId == "run_coin10",
            "Completed selection clears for the next attempt while this session retains its finished card.");
        Check(PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup), "Both recovery copies include the reward transaction.");
        for (int i = 0; i < 40; i++) run.RecordCoin(false);
        Check(PlayerProgress.Coins == 30 && !PlayerProgress.IsChallengeCompleted("run_coin25"),
            "Extra events neither pay twice nor complete an unselected task.");
        Check(snapshot.Coins == 0 && snapshot.Height == 0, "Stats are immutable snapshots, not shared mutable state.");
        PlayerProgress.Reload();
        Check(PlayerProgress.IsChallengeCompleted("run_coin10") && PlayerProgress.Coins == 30 &&
            !PlayerProgress.SelectChallenge("run_coin10"), "Completion survives reload and cannot be farmed again.");

        Reset(primary, backup);
        PlayerProgress.SelectChallenge("run_coin10");
        run = new ChallengeSession("run_coin10", false, GameDifficulty.Normal);
        for (int i = 0; i < 6; i++) run.RecordCoin(false);
        run = new ChallengeSession("run_coin10", false, GameDifficulty.Normal);
        for (int i = 0; i < 4; i++) run.RecordCoin(false);
        Check(!PlayerProgress.IsChallengeCompleted("run_coin10") && run.SelectedProgress == 4 &&
            PlayerProgress.ChallengeProgressValue("run_coin10") == 0, "Run-task counters restart rather than accumulating over attempts.");

        string[] counterTasks = { "run_moving5", "run_narrow8", "run_center10", "run_wide5" };
        foreach (string id in counterTasks)
        {
            Reset(primary, backup);
            PlayerProgress.SelectChallenge(id);
            run = new ChallengeSession(id, true, GameDifficulty.Hard);
            ChallengeDefinition definition = ChallengeCatalog.Find(id);
            for (int i = 0; i < definition.Target; i++)
                run.RecordLanding(id == "run_center10", id == "run_moving5", id == "run_narrow8", id == "run_wide5");
            Check(PlayerProgress.IsChallengeCompleted(id) && PlayerProgress.Coins == definition.RewardCoins,
                "Real landing-event category awards the selected goal: " + id);
        }
    }

    private static void Restrictions(string primary, string backup)
    {
        Reset(primary, backup);
        PlayerProgress.SelectChallenge("run_coin_nomagnet15");
        ChallengeSession run = new ChallengeSession("run_coin_nomagnet15", false, GameDifficulty.Normal);
        for (int i = 0; i < 14; i++) run.RecordCoin(false);
        run.RecordPowerup(true);
        for (int i = 0; i < 30; i++) run.RecordCoin(false);
        Check(run.SelectedFailed && !PlayerProgress.IsChallengeCompleted(run.SelectedId) && PlayerProgress.Coins == 0,
            "Collecting a magnet before the goal fails the whole prefix even after it expires.");

        Reset(primary, backup);
        PlayerProgress.SelectChallenge("run_coin_nomagnet15");
        run = new ChallengeSession("run_coin_nomagnet15", true, GameDifficulty.Normal);
        run.RecordCoin(true);
        Check(run.SelectedFailed, "A magnet-attracted coin defensively latches magnet use too.");
        Reset(primary, backup);
        PlayerProgress.SelectChallenge("run_coin_nomagnet15");
        run = new ChallengeSession("run_coin_nomagnet15", false, GameDifficulty.Normal);
        for (int i = 0; i < 15; i++) run.RecordCoin(false);
        run.RecordPowerup(true);
        Check(PlayerProgress.IsChallengeCompleted(run.SelectedId) && !run.SelectedFailed && PlayerProgress.Coins == 45,
            "Later magnet use never revokes an already-earned prefix reward.");

        Reset(primary, backup);
        PlayerProgress.SelectChallenge("run_height500_nopowers");
        run = new ChallengeSession("run_height500_nopowers", false, GameDifficulty.Normal);
        run.RecordPowerup(false);
        run.SetHeight(600);
        Check(run.SelectedFailed && !PlayerProgress.IsChallengeCompleted(run.SelectedId), "Any power pickup blocks the no-powers goal.");
        Reset(primary, backup);
        PlayerProgress.SelectChallenge("run_height500_noshield");
        run = new ChallengeSession("run_height500_noshield", false, GameDifficulty.Normal);
        run.RecordPowerup(false); // Merely carrying a shield is permitted here.
        run.SetHeight(499);
        Check(!PlayerProgress.IsChallengeCompleted(run.SelectedId), "Height threshold is exact.");
        run.SetHeight(500);
        Check(PlayerProgress.IsChallengeCompleted(run.SelectedId) && PlayerProgress.Coins == 40, "Unused shield does not fail a no-shield-use task.");
        Reset(primary, backup);
        PlayerProgress.SelectChallenge("run_height500_noshield");
        run = new ChallengeSession("run_height500_noshield", false, GameDifficulty.Normal);
        run.RecordShieldUse(false);
        run.SetHeight(1000);
        Check(run.SelectedFailed && PlayerProgress.Coins == 0, "An actual shield block fails no-shield use.");
    }

    private static void BackgroundAchievements(string primary, string backup)
    {
        Reset(primary, backup);
        PlayerProgress.SelectChallenge("");
        ChallengeSession run = new ChallengeSession("", false, GameDifficulty.Normal);
        for (int i = 0; i < 25; i++) run.RecordLanding(true, false, false, false);
        PlayerProgress.Reload();
        Check(PlayerProgress.ChallengeProgressValue("mastery_center50") == 25, "Career counters survive reload without an active run task.");
        run = new ChallengeSession("", true, GameDifficulty.Easy);
        for (int i = 0; i < 25; i++) run.RecordLanding(true, false, false, false);
        Check(PlayerProgress.Owns("accessory_target") && PlayerProgress.IsChallengeCompleted("mastery_center50"),
            "Fifty career center landings grant the target accessory.");
        for (int i = 0; i < 3; i++) run.RecordShieldUse(false);
        Check(PlayerProgress.ChallengeProgressValue("mastery_rescue3") == 0, "A hazard block is not a falling rescue.");
        for (int i = 0; i < 3; i++) run.RecordShieldUse(true);
        Check(PlayerProgress.Owns("trail_ninelives") && PlayerProgress.IsChallengeCompleted("mastery_rescue3"),
            "Three actual falling rescues grant the paw trail.");
        Check(!PlayerProgress.TryPurchase("accessory_target") && !PlayerProgress.TryPurchase("trail_ninelives"),
            "Exclusive rewards stay unbuyable after being earned too.");

        Reset(primary, backup);
        PlayerProgress.SelectChallenge("");
        run = new ChallengeSession("", false, GameDifficulty.Normal);
        run.SetHeight(WorldZones.GoalScore);
        Check(!PlayerProgress.Owns("trail_meteor") && !PlayerProgress.Owns("accessory_patch"), "Normal mode cannot grant Hard-only achievements.");
        run = new ChallengeSession("", true, GameDifficulty.Hard);
        run.RecordShieldUse(false);
        run.SetHeight(1000);
        Check(!PlayerProgress.Owns("accessory_patch") && PlayerProgress.Owns("clothing_explorer"),
            "Shield use blocks clean Hard ascent, while both real control heights grant explorer clothing.");
        run = new ChallengeSession("", true, GameDifficulty.Hard);
        run.SetHeight(999);
        Check(!PlayerProgress.Owns("accessory_patch"), "Hard clean goal does not round up.");
        run.SetHeight(1000);
        Check(PlayerProgress.Owns("accessory_patch"), "Hard clean height grants the patch.");
        run.SetHeight(WorldZones.GoalScore);
        Check(PlayerProgress.Owns("trail_meteor") && PlayerProgress.IsChallengeCompleted("mastery_hard_space"),
            "Raw goal height in Hard grants meteor trail.");
        PlayerProgress.Reload();
        Check(PlayerProgress.Owns("trail_meteor") && PlayerProgress.Owns("clothing_explorer"), "Exclusive rewards persist.");
    }

    private static void ExtendedRunTasks(string primary, string backup)
    {
        ExpectRunTask(primary, backup, "run_coin50", s => Repeat(49, () => s.RecordCoin(false)), s => s.RecordCoin(false));
        ExpectRunTask(primary, backup, "run_coin_fall_save", s => s.RecordCoin(false, true, false, false),
            s => s.RecordLanding(new ChallengeLandingEvent()));
        ExpectRunTask(primary, backup, "run_coin_apex3", s => Repeat(2, () => s.RecordCoin(false, false, true, false)),
            s => s.RecordCoin(false, false, true, false));
        ExpectRunTask(primary, backup, "run_magnet2", s => { s.RecordPowerup(RunPowerups.Kind.Magnet); s.RecordCoin(true); }, s => s.RecordCoin(true));
        ExpectRunTask(primary, backup, "run_space_coins3", s => Repeat(2, () => s.RecordCoin(false, false, false, true)),
            s => s.RecordCoin(false, false, false, true));
        ExpectRunTask(primary, backup, "run_edge5", s => Repeat(4, () => s.RecordLanding(new ChallengeLandingEvent { Edge = true })),
            s => s.RecordLanding(new ChallengeLandingEvent { Edge = true }));
        ExpectRunTask(primary, backup, "run_center_streak5", s => Repeat(4, () => s.RecordLanding(new ChallengeLandingEvent { Centered = true })),
            s => s.RecordLanding(new ChallengeLandingEvent { Centered = true }));
        ExpectRunTask(primary, backup, "run_moving10", s => Repeat(9, () => s.RecordLanding(new ChallengeLandingEvent { Moving = true })),
            s => s.RecordLanding(new ChallengeLandingEvent { Moving = true }));
        ExpectRunTask(primary, backup, "run_narrow12", s => Repeat(11, () => s.RecordLanding(new ChallengeLandingEvent { Narrow = true })),
            s => s.RecordLanding(new ChallengeLandingEvent { Narrow = true }));
        ExpectRunTask(primary, backup, "run_wide10", s => Repeat(9, () => s.RecordLanding(new ChallengeLandingEvent { Wide = true })),
            s => s.RecordLanding(new ChallengeLandingEvent { Wide = true }));
        ExpectRunTask(primary, backup, "run_turn4", s =>
        {
            s.RecordLanding(new ChallengeLandingEvent { Direction = 1 });
            s.RecordLanding(new ChallengeLandingEvent { Direction = -1 });
            s.RecordLanding(new ChallengeLandingEvent { Direction = 1 });
            s.RecordLanding(new ChallengeLandingEvent { Direction = -1 });
        }, s => s.RecordLanding(new ChallengeLandingEvent { Direction = 1 }));
        ExpectRunTask(primary, backup, "run_wrap5", s => Repeat(4, s.RecordWrap), s => s.RecordWrap());
        ExpectRunTask(primary, backup, "run_bush3", s => Repeat(2, s.RecordBush), s => s.RecordBush());
        ExpectRunTask(primary, backup, "run_bush_chain2", s => s.RecordBush(), s => s.RecordBush());
        ExpectRunTask(primary, backup, "run_bush_skip2", s => { s.RecordBush(); s.RecordLanding(new ChallengeLandingEvent { BushSkippedRows = 1 }); },
            s => { s.RecordBush(); s.RecordLanding(new ChallengeLandingEvent { BushSkippedRows = 2 }); });
        ExpectRunTask(primary, backup, "run_all_powers", s => { s.RecordPowerup(RunPowerups.Kind.Shield); s.RecordPowerup(RunPowerups.Kind.Magnet); },
            s => s.RecordPowerup(RunPowerups.Kind.Slow));
        ExpectRunTask(primary, backup, "run_shield_recovery100", s => { s.SetHeight(300); s.RecordShieldUse(false); s.SetHeight(399); },
            s => s.SetHeight(400));
        ExpectRunTask(primary, backup, "run_slow_moving1", s => s.RecordLanding(new ChallengeLandingEvent { Moving = true }),
            s => s.RecordLanding(new ChallengeLandingEvent { Moving = true, SlowActive = true }));
        ExpectRunTask(primary, backup, "run_clean1000", s => s.SetHeight(999), s => s.SetHeight(1000));
        ExpectRunTask(primary, backup, "run_no_powers1000", s => s.SetHeight(999), s => s.SetHeight(1000));

        Reset(primary, backup);
        ChallengeSession run = new ChallengeSession("run_coin_fall_save", false, GameDifficulty.Normal);
        run.RecordCoin(false, true, false, false); run.RecordOrdinaryLanding(true);
        run.RecordLanding(new ChallengeLandingEvent());
        Check(run.GetProgress("run_coin_fall_save") == 0, "A lower landing cannot turn a falling coin into a new-higher landing reward.");
        run.RecordCoin(false, true, false, false); run.RecordShieldUse(true); run.RecordLanding(new ChallengeLandingEvent());
        Check(run.GetProgress("run_coin_fall_save") == 0, "Shield rescue is not an independent falling-coin save.");
        run.RecordCoin(false, true, false, false); run.RecordLanding(new ChallengeLandingEvent());
        run.RecordLanding(new ChallengeLandingEvent());
        Check(run.Stats.FallingCoinSaves == 1, "A pending falling coin is consumed by exactly one safe landing.");

        Reset(primary, backup);
        run = new ChallengeSession("run_magnet2", false, GameDifficulty.Normal);
        run.RecordPowerup(RunPowerups.Kind.Magnet); run.RecordCoin(true);
        run.RecordPowerup(RunPowerups.Kind.Magnet); run.RecordCoin(true);
        Check(!PlayerProgress.IsChallengeCompleted("run_magnet2") && run.SelectedProgress == 1,
            "Coins from different magnet activations cannot be added together.");
        run.RecordCoin(true);
        Check(PlayerProgress.IsChallengeCompleted("run_magnet2"), "Two coins in the same activation qualify.");

        Reset(primary, backup);
        run = new ChallengeSession("run_center_streak5", false, GameDifficulty.Normal);
        Repeat(4, () => run.RecordLanding(new ChallengeLandingEvent { Centered = true }));
        run.RecordOrdinaryLanding(true);
        run.RecordLanding(new ChallengeLandingEvent { Centered = true });
        Check(!PlayerProgress.IsChallengeCompleted(run.SelectedId) && run.SelectedProgress == 1,
            "An uncounted/lower support breaks a four-center streak.");
        run.RecordLanding(new ChallengeLandingEvent { Centered = false });
        Check(run.SelectedProgress == 0, "A non-centered higher landing also breaks the streak.");
        Repeat(5, () => run.RecordLanding(new ChallengeLandingEvent { Centered = true }));
        Check(PlayerProgress.IsChallengeCompleted(run.SelectedId), "A fresh five-center streak completes normally.");

        Reset(primary, backup);
        run = new ChallengeSession("run_bush_chain2", false, GameDifficulty.Normal);
        run.RecordBush(); run.RecordOrdinaryLanding(); run.RecordBush();
        Check(!PlayerProgress.IsChallengeCompleted(run.SelectedId) && run.Stats.BushChain == 1,
            "An ordinary support between bushes breaks the chain.");
        run.RecordShieldUse(false);
        Check(run.Stats.BushChain == 0 && run.Stats.CenterStreak == 0, "A shield rescue breaks chains and center streaks.");
        run.RecordBush(); run.RecordBush();
        Check(PlayerProgress.IsChallengeCompleted(run.SelectedId), "Two actual consecutive bushes complete after an earlier failed chain.");

        Reset(primary, backup);
        run = new ChallengeSession("run_bush_skip2", false, GameDifficulty.Normal);
        run.RecordLanding(new ChallengeLandingEvent { BushSkippedRows = 100 });
        Check(run.Stats.BushSkippedRows == 0, "A skipped-row claim without a bush launch is ignored.");
        Repeat(2, () => { run.RecordBush(); run.RecordLanding(new ChallengeLandingEvent { BushSkippedRows = 1 }); });
        Check(!PlayerProgress.IsChallengeCompleted(run.SelectedId) && run.Stats.BushSkippedRows == 1,
            "One skipped row on two different flights is not a two-row flight.");

        Reset(primary, backup);
        run = new ChallengeSession("run_all_powers", false, GameDifficulty.Normal);
        Repeat(8, () => run.RecordPowerup(RunPowerups.Kind.Shield));
        Check(run.Stats.PowerupKinds == 1 && !PlayerProgress.IsChallengeCompleted(run.SelectedId), "Repeated shields are one distinct kind.");
        run.RecordPowerup((RunPowerups.Kind)99);
        Check(run.Stats.PowerupKinds == 1 && run.Stats.Powerups == 8, "Invalid power kinds cannot become progress.");

        Reset(primary, backup);
        run = new ChallengeSession("run_space_coins3", false, GameDifficulty.Normal);
        run.SetHeight(WorldZones.GoalScore);
        Repeat(3, () => run.RecordCoin(false, false, false, false));
        Check(run.Stats.SpaceCoins == 0, "Previously visiting space does not turn lower-world coins into space coins.");

        Reset(primary, backup);
        run = new ChallengeSession("run_shield_recovery100", false, GameDifficulty.Normal);
        run.SetHeight(300); run.RecordShieldUse(false); run.SetHeight(390); run.RecordShieldUse(false); run.SetHeight(480);
        Check(run.SelectedProgress == 90 && !PlayerProgress.IsChallengeCompleted(run.SelectedId),
            "Two recoveries of ninety height cannot be combined into one hundred after a shield.");
        run.SetHeight(490);
        Check(PlayerProgress.IsChallengeCompleted(run.SelectedId), "A full hundred after the latest shield qualifies.");
    }

    private static void ExtendedCareerAchievements(string primary, string backup)
    {
        Reset(primary, backup);
        ChallengeSession run = new ChallengeSession("", false, GameDifficulty.Normal);
        Repeat(249, () => run.RecordCoin(false));
        PlayerProgress.AddCoins(1000);
        Check(PlayerProgress.ChallengeProgressValue("mastery_coins250") == 249 && !PlayerProgress.IsChallengeCompleted("mastery_coins250"),
            "Wallet adjustments and rewards are not actual collected coins.");
        run.RecordCoin(false);
        Check(PlayerProgress.Coins == 1150 && PlayerProgress.IsChallengeCompleted("mastery_coins250"), "250 actual coins grant the exact career reward.");
        Repeat(100, () => run.RecordCoin(false));
        Check(PlayerProgress.Coins == 1150 && PlayerProgress.ChallengeProgressValue("mastery_coins250") == 250,
            "Completed coin career is bounded and pays once.");
        PlayerProgress.Reload();
        Check(PlayerProgress.IsChallengeCompleted("mastery_coins250") && PlayerProgress.Coins == 1150, "Coin career reward survives reload.");

        Reset(primary, backup);
        run = new ChallengeSession("", true, GameDifficulty.Normal);
        Repeat(25, run.RecordBush);
        Check(PlayerProgress.IsChallengeCompleted("mastery_bush25") && PlayerProgress.Coins == 120, "Bush career counts actual boosts.");
        Repeat(50, () => run.RecordLanding(new ChallengeLandingEvent { Moving = true }));
        Check(PlayerProgress.IsChallengeCompleted("mastery_moving50") && PlayerProgress.Coins == 270, "Moving career grants 150 coins once.");
        Repeat(75, () => run.RecordLanding(new ChallengeLandingEvent { Narrow = true }));
        Check(PlayerProgress.IsChallengeCompleted("mastery_narrow75") && PlayerProgress.Coins == 450, "Narrow career grants 180 coins once.");
        Check(PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup), "Career reward and completion share both durable copies.");
    }

    private static void ExpectRunTask(string primary, string backup, string id, Action<ChallengeSession> before, Action<ChallengeSession> finish)
    {
        Reset(primary, backup);
        Check(PlayerProgress.SelectChallenge(id), "New run task is selectable: " + id);
        ChallengeSession run = new ChallengeSession(id, false, GameDifficulty.Normal);
        before(run);
        Check(!PlayerProgress.IsChallengeCompleted(id), "New task does not complete below its real condition: " + id);
        finish(run);
        Check(PlayerProgress.IsChallengeCompleted(id) && PlayerProgress.Coins == ChallengeCatalog.Find(id).RewardCoins,
            "New task completes with its exact one-time reward: " + id);
        int paid = PlayerProgress.Coins;
        finish(run);
        Check(PlayerProgress.Coins == paid, "Repeated final events cannot pay again: " + id);
    }

    private static void Repeat(int count, Action action)
    {
        for (int i = 0; i < count; i++) action();
    }

    private static void Eligibility(string primary, string backup)
    {
        Reset(primary, backup);
        PlayerProgress.SelectChallenge("run_coin10");
        ChallengeSession run = new ChallengeSession("run_coin10", false, GameDifficulty.Normal);
        run.Invalidate();
        for (int i = 0; i < 60; i++) { run.RecordCoin(false); run.RecordLanding(true, true, true, true); }
        run.SetHeight(WorldZones.GoalScore);
        run.RecordBush(); run.RecordWrap(); run.RecordPowerup(RunPowerups.Kind.Magnet);
        Check(!run.IsEligible && run.Stats.Coins == 0 && PlayerProgress.Coins == 0 &&
            run.Stats.Bushes == 0 && run.Stats.Wraps == 0 && run.Stats.PowerupKinds == 0 &&
            PlayerProgress.ChallengeProgressValue("mastery_center50") == 0 &&
            PlayerProgress.ChallengeProgressValue("mastery_coins250") == 0,
            "Invalid test attempts mutate neither original nor extended stats/career progress.");
        run = new ChallengeSession("run_coin10", false, GameDifficulty.Normal);
        PlayerProgress.BeginAdminSandbox();
        string durable = PlayerPrefs.GetString(primary);
        run.RecordCoin(false);
        Check(!run.IsEligible && !PlayerProgress.SelectChallenge("run_center10"), "An admin event permanently invalidates the session.");
        PlayerProgress.EndAdminSandbox();
        for (int i = 0; i < 20; i++) run.RecordCoin(false);
        Check(PlayerPrefs.GetString(primary) == durable && PlayerProgress.Coins == 0 && !run.IsEligible,
            "Leaving admin does not re-enable the invalidated attempt.");
        RunFlow.RunActive = true;
        Check(!PlayerProgress.SelectChallenge("run_center10"), "Cannot swap challenge during a run.");
        RunFlow.RunActive = false; RunFlow.IsTransitioning = true;
        Check(!PlayerProgress.SelectChallenge("run_center10"), "Cannot swap challenge during a transition.");
        RunFlow.IsTransitioning = false;
    }

    private static void MigrationRecoveryAndBounds(string primary, string backup)
    {
        Reset(primary, backup);
        LegacyFixture oldFixture = new LegacyFixture { coins = 143, cat = "cat_rose", owned = new List<string> { "cat_rose", "future_cosmetic" },
            redeemedCodes = new List<string> { "keep-code" } };
        string old = EnvelopeFor(4, oldFixture);
        Check(!old.Contains("centeredLandings") && !old.Contains("selectedChallenge"), "v4 fixture genuinely lacks all new fields.");
        PlayerPrefs.SetString(primary, old); PlayerPrefs.SetString(backup, old);
        PlayerProgress.Reload();
        Check(PlayerProgress.Coins == 143 && PlayerProgress.EquippedId(PlayerProgress.Category.Cat) == "cat_rose",
            "v4 migration preserves wallet and selected skin.");
        Envelope migrated = JsonUtility.FromJson<Envelope>(PlayerPrefs.GetString(primary));
        Check(migrated.version == 9 && migrated.payload.Contains("future_cosmetic") && migrated.payload.Contains("keep-code") &&
            PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup), "Current migration preserves unknown IDs/codes in both copies.");
        Check(PlayerProgress.ChallengeProgressValue("mastery_center50") == 0 && !PlayerProgress.Owns("trail_meteor"),
            "Old play history is not fabricated into new achievement progress.");
        PlayerProgress.SelectChallenge("run_coin10");
        ChallengeSession run = new ChallengeSession("run_coin10", false, GameDifficulty.Normal);
        for (int i = 0; i < 10; i++) run.RecordCoin(false);
        PlayerPrefs.SetString(primary, "broken");
        PlayerProgress.Reload();
        Check(PlayerProgress.Coins == 173 && PlayerProgress.IsChallengeCompleted("run_coin10"),
            "Recovery keeps the completion and its reward together.");
        run = new ChallengeSession("run_coin10", false, GameDifficulty.Normal);
        for (int i = 0; i < 10; i++) run.RecordCoin(false);
        Check(PlayerProgress.Coins == 173, "Recovered task cannot pay a second time.");

        Fixture fixture = new Fixture { coins = 9999990, selectedChallenge = "run_coin10", centeredLandings = -20, fallingRescues = 999,
            preciseChallengeHeight = -30, inertiaChallengeHeight = int.MaxValue, hardChallengeHeight = int.MaxValue,
            challengeCoinsCollected = int.MaxValue, challengeBushesUsed = -1,
            challengeMovingLandings = int.MaxValue, challengeNarrowLandings = -20,
            completedChallenges = new List<string> { "future_challenge", "future_challenge", "", null } };
        string bounded = EnvelopeFor(6, fixture);
        PlayerPrefs.SetString(primary, bounded); PlayerPrefs.SetString(backup, bounded);
        PlayerProgress.Reload();
        Check(PlayerProgress.ChallengeProgressValue("mastery_center50") == 0 &&
            PlayerProgress.ChallengeProgressValue("mastery_rescue3") == 3 &&
            PlayerProgress.ChallengeProgressValue("mastery_controls1000") == 1 &&
            PlayerProgress.ChallengeProgressValue("mastery_hard_space") == WorldZones.GoalScore,
            "Malformed negative/oversized career fields are bounded.");
        Check(PlayerProgress.ChallengeProgressValue("mastery_coins250") == 250 &&
            PlayerProgress.ChallengeProgressValue("mastery_bush25") == 0 &&
            PlayerProgress.ChallengeProgressValue("mastery_moving50") == 50 &&
            PlayerProgress.ChallengeProgressValue("mastery_narrow75") == 0,
            "New v6 event counters are bounded without integer wraparound.");
        run = new ChallengeSession("run_coin10", false, GameDifficulty.Normal);
        for (int i = 0; i < 10; i++) run.RecordCoin(false);
        Check(PlayerProgress.Coins == 9999999, "Coin reward clamps instead of overflowing the wallet.");

        string future = EnvelopeFor(10, new Fixture { coins = 999 });
        PlayerPrefs.SetString(primary, future); PlayerPrefs.SetString(backup, future);
        PlayerProgress.Reload();
        Check(!PlayerProgress.SelectChallenge("run_coin10"), "Future saves are read-only for challenge selection.");
        run = new ChallengeSession("run_coin10", false, GameDifficulty.Hard);
        for (int i = 0; i < 60; i++) { run.RecordCoin(false); run.RecordLanding(true, true, true, true); }
        Repeat(30, run.RecordBush); Repeat(6, run.RecordWrap);
        run.RecordPowerup(RunPowerups.Kind.Magnet); run.RecordCoin(true, true, true, true);
        run.SetHeight(WorldZones.GoalScore);
        PlayerProgress.Flush();
        Check(PlayerPrefs.GetString(primary) == future && PlayerPrefs.GetString(backup) == future,
            "No challenge event overwrites a future-version save or its backup.");
    }

    private static void V5MigrationAndTaskMilestone(string primary, string backup)
    {
        string[] originalRunTasks = { "run_coin10", "run_coin25", "run_coin_nomagnet15", "run_height500_nopowers",
            "run_height500_noshield", "run_height1000", "run_moving5", "run_narrow8", "run_center10", "run_wide5" };
        Reset(primary, backup);
        V5Fixture old = new V5Fixture { coins = 100, cat = "cat_rose", owned = new List<string> { "cat_rose", "future_cosmetic" },
            selectedChallenge = "run_wide5", centeredLandings = 17, fallingRescues = 1,
            preciseChallengeHeight = 800, inertiaChallengeHeight = 500, hardCleanChallengeHeight = 400, hardChallengeHeight = 500 };
        for (int i = 0; i < 9; i++) old.completedChallenges.Add(originalRunTasks[i]);
        old.completedChallenges.Add("future_task_keep");
        string oldJson = EnvelopeFor(5, old);
        Check(!oldJson.Contains("challengeCoinsCollected") && !oldJson.Contains("challengeBushesUsed"),
            "v5 fixture genuinely lacks all four v6 event counters.");
        PlayerPrefs.SetString(primary, oldJson); PlayerPrefs.SetString(backup, oldJson);
        PlayerProgress.Reload();
        Check(PlayerProgress.Coins == 100 && PlayerProgress.EquippedId(PlayerProgress.Category.Cat) == "cat_rose" &&
            PlayerProgress.SelectedChallengeId == "run_wide5", "v5 migration preserves wallet, skin and unfinished selection.");
        for (int i = 0; i < 9; i++) Check(PlayerProgress.IsChallengeCompleted(originalRunTasks[i]), "v5 original completion preserved: " + originalRunTasks[i]);
        Check(PlayerProgress.ChallengeProgressValue("mastery_center50") == 17 && PlayerProgress.ChallengeProgressValue("mastery_rescue3") == 1 &&
            PlayerProgress.ChallengeProgressValue("mastery_tasks10") == 9, "v5 career progress is preserved; unknown completion is not a tenth real task.");
        Check(PlayerProgress.ChallengeProgressValue("mastery_coins250") == 0 && PlayerProgress.ChallengeProgressValue("mastery_bush25") == 0 &&
            PlayerProgress.ChallengeProgressValue("mastery_moving50") == 0 && PlayerProgress.ChallengeProgressValue("mastery_narrow75") == 0,
            "v6 never fabricates historical event totals that v5 did not store.");
        Envelope migrated = JsonUtility.FromJson<Envelope>(PlayerPrefs.GetString(primary));
        Check(migrated.version == 9 && migrated.revision == 13 && migrated.payload.Contains("future_task_keep") && migrated.payload.Contains("future_cosmetic") &&
            PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup), "v5 migration writes both current copies once, retaining unknown IDs.");
        ChallengeSession run = new ChallengeSession("run_wide5", false, GameDifficulty.Normal);
        Repeat(5, () => run.RecordLanding(new ChallengeLandingEvent { Wide = true }));
        Check(PlayerProgress.IsChallengeCompleted("run_wide5") && PlayerProgress.IsChallengeCompleted("mastery_tasks10") &&
            PlayerProgress.Coins == 360, "Tenth task and its 200-coin milestone are awarded together with the task's 60 coins.");
        Check(PlayerProgress.SelectedChallengeId == "" && PlayerPrefs.GetString(primary) == PlayerPrefs.GetString(backup),
            "The tenth-task transaction clears selection and refreshes both recovery copies.");
        PlayerPrefs.SetString(primary, "broken"); PlayerProgress.Reload();
        run = new ChallengeSession("run_wide5", false, GameDifficulty.Normal);
        Repeat(5, () => run.RecordLanding(new ChallengeLandingEvent { Wide = true }));
        Check(PlayerProgress.Coins == 360 && PlayerProgress.IsChallengeCompleted("mastery_tasks10"),
            "Recovery and replay cannot repeat either the task reward or its career milestone.");

        Reset(primary, backup);
        old = new V5Fixture { coins = 70, completedChallenges = new List<string>(originalRunTasks) };
        oldJson = EnvelopeFor(5, old);
        PlayerPrefs.SetString(primary, oldJson); PlayerPrefs.SetString(backup, oldJson);
        PlayerProgress.Reload();
        Check(PlayerProgress.Coins == 270 && PlayerProgress.IsChallengeCompleted("mastery_tasks10"),
            "A v5 player with all ten original task proofs receives the new milestone during migration.");
        string once = PlayerPrefs.GetString(primary);
        PlayerProgress.Reload();
        Check(PlayerProgress.Coins == 270 && PlayerPrefs.GetString(primary) == once,
            "The migration milestone is one-time and does not rewrite on later loads.");
    }

    private static void Reset(string primary, string backup)
    {
        PlayerProgress.EndAdminSandbox();
        PlayerPrefs.DeleteKey(primary); PlayerPrefs.DeleteKey(backup);
        PlayerProgress.Reload();
    }

    private static string EnvelopeFor(int version, object fixture)
    {
        string payload = JsonUtility.ToJson(fixture);
        const long revision = 12;
        uint hash = 2166136261;
        unchecked
        {
            for (int i = 0; i < payload.Length; i++) hash = (hash ^ payload[i]) * 16777619;
            hash = (hash ^ (uint)revision) * 16777619;
            hash = (hash ^ (uint)(revision >> 32)) * 16777619;
        }
        return JsonUtility.ToJson(new Envelope { version = version, revision = revision, payload = payload, checksum = hash.ToString("X8") });
    }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("Challenge progress: " + message);
    }
}
#endif
