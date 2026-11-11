using System;
using System.Collections.Generic;
using UnityEngine;

// Owns one expedition attempt. The ordinary movement and generation rules stay
// authoritative; a rest is offered only after an audited real safe landing.
[DisallowMultipleComponent]
public sealed class ExpeditionRun : MonoBehaviour
{
    public const int RestInterval = 750;
    public static ExpeditionRun Active { get; private set; }
    public Player Owner { get; private set; }
    public ExpeditionSession Session { get; private set; }
    public ExpeditionResult Result => Session != null ? Session.Result : null;
    public bool HasStarted => Session != null;
    public bool IsFinished => Session != null && Session.IsSettled;
    public bool IsResting { get; private set; }
    public int NextRestHeight { get; private set; } = RestInterval;
    public bool RewardsSaved { get; private set; }
    public int HitPoints { get; private set; }
    public int MaximumHitPoints { get; private set; } = 2;
    public bool HasWard { get; private set; }
    public int StompDamage => Mathf.Min(2, 1 + trainingClaws + perkClaws);
    public int EnemyKills { get; private set; }
    public float EnemyDropChance => Mathf.Min(.55f, baseDropChance + perkFortune * .10f);
    public bool CanFight => CanCollect && (powers == null || !powers.IsRocketActive);
    public IReadOnlyList<ExpeditionPerkKind> PerkChoices => perkChoices;
    public bool NeedsPerkChoice => IsResting && !perkChosen && perkChoices.Count > 0;
    public string PerkSummary => "Таланты: " + perkCount + " · урон: " + StompDamage +
        " · редкая добыча: " + Mathf.RoundToInt(EnemyDropChance * 100f) + "%";
    public event Action Changed;
    public string StatusText { get; private set; } = "Три места для находок; первое защищено.";
    public bool CanCollect => Owner != null && !Owner.IsDead && HasStarted && !IsFinished &&
        !IsResting && RunFlow.IsPlaying && Time.timeScale > 0f && !GameMenuUI.BlocksGameplayInput;

    private Collider2D pendingLanding;
    private int landingFrame = -1, maximumHeight, maximumScore;
    private int trainingClaws, perkVitality, perkClaws, perkFortune, perkResolve, perkCount;
    private float baseDropChance = .25f, enemyGraceUntil;
    private bool perkChosen;
    private IReadOnlyList<ExpeditionPerkKind> perkChoices = Array.Empty<ExpeditionPerkKind>();
    private RunPowerups powers;
    private Rigidbody2D body;
    private Collider2D feet;
    private Camera sceneCamera;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => Active = null;

    public static ExpeditionRun Attach(Player player)
    {
        if (player == null || player.RunKind != GameRunKind.Expedition) return null;
        ExpeditionRun run = player.GetComponent<ExpeditionRun>();
        if (run == null) run = player.gameObject.AddComponent<ExpeditionRun>();
        run.Owner = player;
        Active = run;
        return run;
    }

    public void BeginRun()
    {
        if (HasStarted || Owner == null || Owner.IsDead || !RunFlow.IsPlaying || Time.timeScale <= 0f) return;
        Owner.RefreshAdminState();
        ExpeditionProgress.RecoverInterruptedRun();
        Session = new ExpeditionSession(Guid.NewGuid().ToString("N"),
            !Owner.IsTestAttempt && !PlayerProgress.AdminSandboxActive);
        MaximumHitPoints = 2 + PlayerProgress.GetExpeditionTrainingLevel(ExpeditionTrainingKind.Vitality);
        HitPoints = MaximumHitPoints;
        trainingClaws = PlayerProgress.GetExpeditionTrainingLevel(ExpeditionTrainingKind.Claws);
        baseDropChance = .25f + .05f * PlayerProgress.GetExpeditionTrainingLevel(ExpeditionTrainingKind.Fortune) +
            (ExpeditionProgress.HunterCharmUnlocked ? .10f : 0f);
        powers = Owner.GetComponent<RunPowerups>(); body = Owner.GetComponent<Rigidbody2D>();
        feet = Owner.GetComponent<Collider2D>(); sceneCamera = Camera.main;
        HasWard = Session.IsEligible && ExpeditionProgress.TryConsumePreparedWard();
        StatusText = "Жука бей сверху. Боковой удар ранит; колючки опасны.";
        Checkpoint();
        Notify();
    }

    private void Update()
    {
        if (Owner == null || IsFinished) return;
        if (!HasStarted && RunFlow.IsPlaying && Time.timeScale > 0f && !GameMenuUI.BlocksGameplayInput) BeginRun();
        if (!HasStarted) return;
        Capture();
        if (Owner.IsDead) { EndRun(ExpeditionOutcome.Lost, true); return; }
    }

    // Called by Player only after this frame's travel/hazard/fall checks. A
    // popup must not suppress the very sweep that certifies its safe landing.
    public void AfterHazardAudit()
    {
        if (!HasStarted || IsFinished || Owner == null || Owner.IsDead || pendingLanding == null ||
            Owner.LastHazardAuditFrame != Time.frameCount) return;
        if (!RunFlow.IsPlaying || Time.timeScale <= 0f || GameMenuUI.BlocksGameplayInput) return;
        Capture();
        Collider2D landing = pendingLanding;
        pendingLanding = null;
        if (Time.frameCount - landingFrame > 4 ||
            !SafeRestSurface(landing) || maximumHeight < NextRestHeight) return;
        RunPowerups powers = Owner.GetComponent<RunPowerups>();
        if (powers != null && powers.IsRocketActive) return;
        ExpeditionUI ui = FindAnyObjectByType<ExpeditionUI>();
        if (ui == null) return;
        IsResting = true;
        BuildPerkChoices();
        StatusText = "Привал: забрать добычу или продолжить подъём?";
        Notify();
        ui.ShowRest(this);
    }

    private void Capture()
    {
        bool wasEligible = Session != null && Session.IsEligible;
        if (Owner != null)
        {
            maximumHeight = Mathf.Max(maximumHeight, Owner.CurrentHeight);
            maximumScore = Mathf.Max(maximumScore, Owner.CurrentScore);
            if (Owner.IsTestAttempt) Session?.Invalidate();
        }
        if (PlayerProgress.AdminSandboxActive) Session?.Invalidate();
        if (wasEligible && !Session.IsEligible) ExpeditionProgress.InvalidatePending(Session.RunId);
    }

    private void Checkpoint()
    {
        if (!HasStarted || IsFinished) return;
        Capture();
        ExpeditionProgress.TryCheckpoint(Session, maximumHeight, maximumScore);
    }

    public void Invalidate()
    {
        if (IsFinished) return;
        Session?.Invalidate();
        if (Session != null) ExpeditionProgress.InvalidatePending(Session.RunId);
        StatusText = "Тестовая экспедиция — находки не попадут в кладовую.";
        Notify();
    }

    public bool TryCollect(ExpeditionFindKind kind)
    {
        if (!CanCollect || !ExpeditionCatalog.IsValid(kind)) return false;
        Capture();
        if (!Session.TryCollect(kind))
        {
            if (StatusText != "Рюкзак полон — освободи место или сохрани находки на привале.")
            {
                StatusText = "Рюкзак полон — освободи место или сохрани находки на привале.";
                Notify();
            }
            return false;
        }
        Checkpoint();
        StatusText = "Найдено: " + ExpeditionCatalog.Find(kind).Name;
        GameAudio.Play(GameAudio.Cue.Coin);
        Notify();
        return true;
    }

    public bool DiscardSlot(int index)
    {
        if (!CanEditBag() || !Session.DiscardSlot(index)) return false;
        Checkpoint();
        StatusText = "Место освобождено. Находка оставлена в этой экспедиции.";
        Notify(); return true;
    }

    public bool ProtectSlot(int index)
    {
        if (!CanEditBag() || !Session.ProtectSlot(index)) return false;
        Checkpoint();
        StatusText = "Выбранная находка теперь в защищённом месте.";
        Notify(); return true;
    }

    private bool CanEditBag() => HasStarted && !IsFinished && Owner != null && !Owner.IsDead &&
        RunFlow.IsPlaying && !RunFlow.IsTransitioning && ExpeditionUI.IsOpen && !AdminTools.IsOpen;

    public bool TryTakeEnemyHit()
    {
        if (!CanFight || Time.time < enemyGraceUntil || Owner.AdminTestingInvulnerable ||
            powers != null && (powers.IsProtected || powers.IsRocketActive)) return false;
        if (powers != null && body != null && feet != null && sceneCamera != null &&
            powers.TryProtect(false, body, sceneCamera, feet))
        {
            enemyGraceUntil = Time.time + .8f + perkResolve * .25f;
            return true;
        }
        if (HasWard) HasWard = false;
        else HitPoints = Mathf.Max(0, HitPoints - 1);
        enemyGraceUntil = Time.time + .8f + perkResolve * .25f;
        StatusText = HitPoints > 0 ? "Удар врага! Здоровье: " + HitPoints + "/" + MaximumHitPoints : "Грозовой жук оказался сильнее.";
        GameAudio.Play(GameAudio.Cue.Hurt);
        Notify();
        if (HitPoints == 0) Owner.DefeatByExpeditionEnemy();
        return true;
    }

    public void NotifyEnemyDefeated()
    {
        if (!CanFight) return;
        EnemyKills = EnemyKills == int.MaxValue ? EnemyKills : EnemyKills + 1;
        StatusText = "Грозовой жук побеждён! Редкую добычу нужно донести домой.";
        Notify();
    }

    private void BuildPerkChoices()
    {
        var available = new List<ExpeditionPerkKind>(6);
        if (perkVitality < 2) available.Add(ExpeditionPerkKind.Vitality);
        if (perkClaws < 1 && StompDamage < 2) available.Add(ExpeditionPerkKind.Claws);
        if (perkFortune < 2 && EnemyDropChance < .549f) available.Add(ExpeditionPerkKind.Fortune);
        if (!HasWard) available.Add(ExpeditionPerkKind.Carapace);
        if (HitPoints < MaximumHitPoints) available.Add(ExpeditionPerkKind.Recovery);
        if (perkResolve < 2) available.Add(ExpeditionPerkKind.Resolve);
        perkChoices = Array.AsReadOnly(ExpeditionPerks.Offer(available, ExpeditionPerks.Seed(Session.RunId, NextRestHeight)));
        perkChosen = false;
    }

    public bool TryChoosePerk(ExpeditionPerkKind kind)
    {
        if (!NeedsPerkChoice || Owner == null || Owner.IsDead || !RunFlow.IsPlaying ||
            RunFlow.IsTransitioning || AdminTools.IsOpen) return false;
        bool offered = false;
        foreach (ExpeditionPerkKind offer in perkChoices) if (offer == kind) { offered = true; break; }
        if (!offered) return false;
        switch (kind)
        {
            case ExpeditionPerkKind.Vitality: perkVitality++; MaximumHitPoints++; HitPoints++; break;
            case ExpeditionPerkKind.Claws: perkClaws++; break;
            case ExpeditionPerkKind.Fortune: perkFortune++; break;
            case ExpeditionPerkKind.Carapace: HasWard = true; break;
            case ExpeditionPerkKind.Recovery: HitPoints = Mathf.Min(MaximumHitPoints, HitPoints + 2); break;
            case ExpeditionPerkKind.Resolve: perkResolve++; break;
            default: return false;
        }
        perkChosen = true; perkCount++;
        StatusText = "Талант: " + ExpeditionPerks.Name(kind) + ". Действует только в этом забеге.";
        Notify(); return true;
    }

    public void RecordLanding(Collider2D surface)
    {
        if (!HasStarted || IsFinished || IsResting || Owner == null || Owner.IsDead ||
            !RunFlow.IsPlaying || Time.timeScale <= 0f) return;
        Capture();
        if (maximumHeight < NextRestHeight || !SafeRestSurface(surface)) return;
        pendingLanding = surface;
        landingFrame = Time.frameCount;
    }

    internal void CancelPendingLandingAfterEnemyBounce()
    {
        pendingLanding = null;
        landingFrame = -1;
    }

    public static bool SafeRestSurface(Collider2D surface) => surface != null && surface.enabled &&
        surface.gameObject.activeInHierarchy && !surface.isTrigger && surface.CompareTag("Platform") &&
        surface.GetComponent<MovingPlatform>() == null && surface.GetComponent<CrumblingPlatform>() == null &&
        surface.GetComponentInChildren<Bush>() == null && surface.GetComponentInChildren<DoodlikHazard>() == null;

    public bool ContinueFromRest()
    {
        if (!IsResting || IsFinished || NeedsPerkChoice || Owner == null || Owner.IsDead || !RunFlow.IsPlaying) return false;
        Capture();
        long next = ((long)maximumHeight / RestInterval + 1) * RestInterval;
        NextRestHeight = (int)Math.Min(int.MaxValue, next);
        IsResting = false;
        enemyGraceUntil = Time.time + .6f;
        pendingLanding = null;
        StatusText = "Продолжаем. Следующий привал — от высоты " + NextRestHeight + ".";
        Notify(); return true;
    }

    public bool TryExtract()
    {
        if (!IsResting || IsFinished || Owner == null || Owner.IsDead || !RunFlow.IsPlaying ||
            RunFlow.IsTransitioning || AdminTools.IsOpen) return false;
        return EndRun(ExpeditionOutcome.Extracted, true) != null;
    }

    public ExpeditionResult EndRun(ExpeditionOutcome outcome, bool showResult = false)
    {
        if (!HasStarted || IsFinished) return Result;
        if (outcome != ExpeditionOutcome.Extracted && outcome != ExpeditionOutcome.Lost && outcome != ExpeditionOutcome.Abandoned)
            return null;
        // External lifecycle callers can abandon, but cannot bypass a real rest.
        if (outcome == ExpeditionOutcome.Extracted && !IsResting) return null;
        Capture();
        ExpeditionResult result = Session.Settle(outcome, maximumHeight, maximumScore);
        if (result == null) return null;
        IsResting = false;
        pendingLanding = null;
        RewardsSaved = ExpeditionProgress.TryRecord(result);
        StatusText = !result.Eligible ? "Тестовая экспедиция — находки не записаны." :
            RewardsSaved ? "Находки отправлены в кладовую." : "Не удалось записать находки. Хранилище защищено.";
        Notify();
        if (showResult)
        {
            ExpeditionUI ui = FindAnyObjectByType<ExpeditionUI>();
            GameMenuUI menu = FindAnyObjectByType<GameMenuUI>();
            if (ui != null) ui.ShowResult(result, () => { if (menu != null) menu.ReturnToHome(); }, RewardsSaved);
        }
        return result;
    }

    private void Notify() => Changed?.Invoke();

    // Mobile platforms may kill a backgrounded process without OnApplicationQuit.
    // This is only a recovery receipt: pausing does not end or bank a live run.
    private void OnApplicationPause(bool paused) { if (paused) Checkpoint(); }
    private void OnApplicationFocus(bool focused) { if (!focused) Checkpoint(); }
    private void OnApplicationQuit() => EndRun(ExpeditionOutcome.Abandoned);
    private void OnDestroy()
    {
        EndRun(ExpeditionOutcome.Abandoned);
        if (Active == this) Active = null;
        Changed = null;
    }
}
