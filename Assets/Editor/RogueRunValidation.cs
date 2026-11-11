#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

// Isolated EditMode contracts for combat health and run-only talents. Actual
// enemy sweeps and rest buttons have their own PlayMode/renderer coverage.
public static class RogueRunValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    public static int Validate()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/") || Application.isPlaying)
            throw new InvalidOperationException("Use an empty disposable EditMode QA scene.");
        checks = 0;
        bool active = RunFlow.RunActive, transition = RunFlow.IsTransitioning;
        float time = Time.timeScale;
        bool hadKind = PlayerPrefs.HasKey(GameplayPreferences.RunKindKey);
        int kind = PlayerPrefs.GetInt(GameplayPreferences.RunKindKey);
        Object previousRun = ExpeditionRun.Active;
        var roots = new List<GameObject>();
        UnityEngine.Random.State random = UnityEngine.Random.state;
        try
        {
            using (PlayerProgress.UseIsolatedStorageForValidation(out _, out _))
            using (ExpeditionProgress.UseIsolatedStorageForValidation(out _, out _))
            {
                RunFlow.RunActive = RunFlow.IsTransitioning = false;
                GameplayPreferences.SetRunKind(GameRunKind.Expedition, false);
                ValidateOffers();
                RunFlow.RunActive = true; Time.timeScale = 1f;
                ExpeditionRun run = NewRun(roots);
                float jump = run.Owner.NormalJumpForce;
                Check(run.HitPoints == 2 && run.MaximumHitPoints == 2 && run.StompDamage == 1 &&
                    Mathf.Abs(run.EnemyDropChance - .25f) < .001f && !run.HasWard, "Untrained combat baseline is bounded and explicit.");
                Check(run.TryTakeEnemyHit() && run.HitPoints == 1 && !run.Owner.IsDead, "One enemy hit wounds rather than kills.");
                Check(!run.TryTakeEnemyHit() && run.HitPoints == 1, "Immediate contact cannot spend health twice.");
                Time.timeScale = 0f;
                Check(!run.TryTakeEnemyHit() && run.HitPoints == 1, "Paused combat cannot spend health.");
                Time.timeScale = 1f;
                Set(run, "enemyGraceUntil", -1f); SetAuto(run, "HasWard", true);
                Check(run.TryTakeEnemyHit() && !run.HasWard && run.HitPoints == 1, "An expedition ward absorbs exactly one enemy hit.");
                Rest(run);
                Check(run.NeedsPerkChoice && run.PerkChoices.Count == 3 && !run.ContinueFromRest(), "Rest requires one of three distinct available talents.");
                Check(!run.TryChoosePerk((ExpeditionPerkKind)99), "Unknown talents cannot be chosen.");
                ExpeditionPerkKind selected = run.PerkChoices[0];
                Check(run.TryChoosePerk(selected) && !run.TryChoosePerk(selected) && !run.NeedsPerkChoice,
                    "One selection per rest; no duplicate talent farming.");
                Check(run.Owner.NormalJumpForce == jump && run.ContinueFromRest(), "Talents do not alter jump reach; continuation resumes.");
                Check(!run.TryChoosePerk(selected), "A closed rest cannot grant another talent.");
                run.EndRun(ExpeditionOutcome.Abandoned);

                foreach (ExpeditionPerkKind perk in Enum.GetValues(typeof(ExpeditionPerkKind)))
                {
                    run = NewRun(roots); SetAuto(run, "HitPoints", 1); Rest(run);
                    Set(run, "perkChoices", Array.AsReadOnly(new[] { perk }));
                    int damage = run.StompDamage, max = run.MaximumHitPoints; float chance = run.EnemyDropChance;
                    Check(run.TryChoosePerk(perk), "Every supported talent has an executable effect: " + perk);
                    Check(perk != ExpeditionPerkKind.Vitality || run.MaximumHitPoints == max + 1 && run.HitPoints == 2, "Vitality raises max and heals one.");
                    Check(perk != ExpeditionPerkKind.Claws || run.StompDamage == damage + 1, "Claws increase only attack damage.");
                    Check(perk != ExpeditionPerkKind.Fortune || Mathf.Abs(run.EnemyDropChance - chance - .1f) < .001f, "Fortune adds its stated bounded probability.");
                    Check(perk != ExpeditionPerkKind.Carapace || run.HasWard, "Carapace grants one ward.");
                    Check(perk != ExpeditionPerkKind.Recovery || run.HitPoints == max, "Recovery never overheals.");
                    Check(perk != ExpeditionPerkKind.Resolve || (int)Get(run, "perkResolve") == 1, "Resolve increases enemy hit grace only.");
                    Check(run.Owner.NormalJumpForce == jump && run.ContinueFromRest(), "Every talent preserves certified jumping.");
                    run.EndRun(ExpeditionOutcome.Abandoned);
                }
                run = NewRun(roots);
                Check(run.MaximumHitPoints == 2 && run.StompDamage == 1 && !run.HasWard, "New runs do not inherit run-only buffs.");
                Set(run, "perkVitality", 2); Set(run, "perkClaws", 1); Set(run, "perkFortune", 2); Set(run, "perkResolve", 2);
                SetAuto(run, "HasWard", true); SetAuto(run, "HitPoints", run.MaximumHitPoints);
                Rest(run);
                Check(run.PerkChoices.Count == 0 && !run.NeedsPerkChoice && run.ContinueFromRest(), "A fully capped healthy run is never locked at a rest.");
                Set(run, "enemyGraceUntil", -1f); SetAuto(run, "HasWard", false); SetAuto(run, "HitPoints", 1);
                Check(run.TryTakeEnemyHit() && run.Owner.IsDead && run.IsFinished && run.Result.Outcome == ExpeditionOutcome.Lost && Time.timeScale == 0f,
                    "Fatal enemy damage uses actual Player death and settles the expedition once.");
            }
            Debug.Log("ROGUE_RUN_PASS checks=" + checks + " perks=6 health=true wards=true death=true noJumpChange=true noBuffCarry=true");
            return checks;
        }
        finally
        {
            foreach (GameObject root in roots) if (root != null) { root.GetComponent<ExpeditionRun>()?.Session?.Invalidate(); Object.DestroyImmediate(root); }
            typeof(ExpeditionRun).GetField("<Active>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, previousRun);
            if (hadKind) PlayerPrefs.SetInt(GameplayPreferences.RunKindKey, kind); else PlayerPrefs.DeleteKey(GameplayPreferences.RunKindKey);
            RunFlow.RunActive = active; RunFlow.IsTransitioning = transition; Time.timeScale = time;
            UnityEngine.Random.state = random;
        }
    }
    private static void ValidateOffers()
    {
        var all = new List<ExpeditionPerkKind>();
        foreach (ExpeditionPerkKind perk in Enum.GetValues(typeof(ExpeditionPerkKind))) all.Add(perk);
        UnityEngine.Random.InitState(123); float expected = UnityEngine.Random.value; UnityEngine.Random.InitState(123);
        for (uint seed = 1; seed <= 64; seed++)
        {
            ExpeditionPerkKind[] offer = ExpeditionPerks.Offer(all, seed), repeated = ExpeditionPerks.Offer(all, seed);
            Check(offer.Length == 3 && new HashSet<ExpeditionPerkKind>(offer).Count == 3, "Offer has three different valid talents.");
            for (int i = 0; i < offer.Length; i++) Check(offer[i] == repeated[i], "An opened rest cannot reroll its choices.");
        }
        Check(ExpeditionPerks.Offer(null, 0).Length == 0 && UnityEngine.Random.value == expected, "Offer RNG is independent from world generation.");
    }
    private static ExpeditionRun NewRun(List<GameObject> roots)
    {
        RunFlow.RunActive = true; RunFlow.IsTransitioning = false; Time.timeScale = 1f;
        GameObject root = new GameObject("Isolated rogue combat fixture"); roots.Add(root);
        root.transform.position = new Vector3(8000f, 8000f);
        Rigidbody2D body = root.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
        BoxCollider2D feet = root.AddComponent<BoxCollider2D>(); feet.size = new Vector2(.4f, .1f);
        Player player = root.AddComponent<Player>(); player.enabled = false;
        Set(player, "rb", body); Set(player, "playerCollider", feet); SetAuto(player, "RunKind", GameRunKind.Expedition);
        ExpeditionRun run = ExpeditionRun.Attach(player); run.enabled = false; SetAuto(player, "Expedition", run); run.BeginRun();
        return run;
    }
    private static void Rest(ExpeditionRun run) { SetAuto(run, "IsResting", true); Call(run, "BuildPerkChoices"); }
    private static object Get(object owner, string field) => owner.GetType().GetField(field, Private).GetValue(owner);
    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
    private static void SetAuto(object owner, string name, object value) => Set(owner, "<" + name + ">k__BackingField", value);
    private static void Call(object owner, string name) => owner.GetType().GetMethod(name, Private).Invoke(owner, null);
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException("Rogue run: " + message); }
}
#endif
