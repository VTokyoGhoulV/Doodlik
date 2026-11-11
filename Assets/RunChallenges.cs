using System.Collections.Generic;
using UnityEngine;

// The scene bridge owns one plain challenge session for this Player attempt.
// No jump, input, platform or hazard parameters are changed by challenges.
[DisallowMultipleComponent]
public sealed class RunChallenges : MonoBehaviour
{
    public static RunChallenges Active { get; private set; }
    public ChallengeSession Session { get; private set; }
    public ChallengeDefinition SelectedDefinition => ChallengeCatalog.Find(
        Session != null ? Session.SelectedId : PlayerProgress.SelectedChallengeId);
    public bool SelectedCompleted => Session != null && SelectedDefinition != null &&
        PlayerProgress.IsChallengeCompleted(SelectedDefinition.Id);
    public bool IsEligible => Session != null && Session.IsEligible;
    public IReadOnlyList<string> CompletedThisRun => completed;
    public string ProgressText
    {
        get
        {
            ChallengeDefinition definition = SelectedDefinition;
            string id = definition != null ? definition.Id : string.Empty;
            int progress = Session != null ? Session.SelectedProgress : 0;
            int status = definition == null ? 0 : Session == null ? 1 : SelectedCompleted ? 2 :
                !Session.IsEligible ? 3 : Session.SelectedFailed ? 4 : 5;
            if (id == lastProgressId && progress == lastProgress && status == lastStatus) return cachedProgress;
            lastProgressId = id; lastProgress = progress; lastStatus = status;
            cachedProgress = status == 0 ? "Испытание не выбрано" : status == 1 ? "Выбрано: " + definition.Name :
                status == 2 ? "Выполнено: " + definition.Name : status == 3 ? "Тестовая попытка — без наград" :
                status == 4 ? "Условие нарушено — попробуй в новой попытке" :
                definition.Name + " · " + Mathf.Min(progress, definition.Target) + "/" + definition.Target;
            return cachedProgress;
        }
    }

    private Player owner;
    private Collider2D feet;
    private Rigidbody2D body;
    private RunPowerups powers;
    private Camera sceneCamera;
    private float normalPlatformWidth;
    private string lastProgressId, cachedProgress;
    private int lastProgress = -1, lastStatus = -1;
    private bool ended, subscribed, hasLaunch;
    private float highestLandingY = float.NegativeInfinity, launchX, launchY, flightWrapOffset;
    private bool ordinaryLandingPending, ordinaryBreaksStreak, bushFlight, flightOriginValid;
    private bool collectingAtContact, collectingAtDescendingContact;
    private bool assistedFlight;
    private float ordinaryLandingStep, lastBushStep = float.NegativeInfinity, bushLaunchY;
    private readonly List<float> bushSupportRows = new List<float>(32);
    private readonly List<string> completed = new List<string>(6);
    private readonly HashSet<string> completedBeforeRun = new HashSet<string>();
    private readonly HashSet<Collider2D> countedSurfaces = new HashSet<Collider2D>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => Active = null;

    public static RunChallenges Attach(Player player)
    {
        RunChallenges bridge = player.GetComponent<RunChallenges>();
        if (bridge == null) bridge = player.gameObject.AddComponent<RunChallenges>();
        bridge.owner = player;
        bridge.feet = player.GetComponent<Collider2D>();
        bridge.body = player.GetComponent<Rigidbody2D>();
        bridge.powers = player.GetComponent<RunPowerups>();
        bridge.sceneCamera = Camera.main;
        Spawner spawner = FindFirstObjectByType<Spawner>();
        BoxCollider2D prefab = spawner != null && spawner.platformPrefab != null
            ? spawner.platformPrefab.GetComponent<BoxCollider2D>() : null;
        if (prefab != null)
            bridge.normalPlatformWidth = prefab.size.x * Mathf.Abs(prefab.transform.lossyScale.x) *
                GameplayPreferences.ModelScale * new DifficultyGenerationProfile(player.RunDifficulty).PlatformWidth;
        bridge.Subscribe();
        Active = bridge;
        return bridge;
    }

    private void OnEnable() { if (owner != null) Subscribe(); }
    private void Subscribe()
    {
        if (subscribed) return;
        PlayerProgress.Changed += CaptureCompleted;
        subscribed = true;
    }
    private void OnDisable()
    {
        if (subscribed) PlayerProgress.Changed -= CaptureCompleted;
        subscribed = false;
    }
    private void OnDestroy() { if (Active == this) Active = null; }

    private void Update()
    {
        if (owner == null || ended) return;
        if (Session != null && (owner.IsTestAttempt || PlayerProgress.AdminSandboxActive)) Session.Invalidate();
        if (RunFlow.IsPlaying && Time.timeScale > 0f && !owner.IsDead) EnsureSession();
    }

    private void LateUpdate() => FlushOrdinaryLanding();

    private bool EnsureSession()
    {
        if (owner == null || owner.IsDead || ended || !RunFlow.IsPlaying || Time.timeScale <= 0f) return false;
        if (Session == null)
        {
            foreach (ChallengeDefinition definition in ChallengeCatalog.All)
                if (PlayerProgress.IsChallengeCompleted(definition.Id)) completedBeforeRun.Add(definition.Id);
            Session = new ChallengeSession(PlayerProgress.SelectedChallengeId,
                GameplayPreferences.UseInertia, owner.RunDifficulty);
        }
        if (owner.IsTestAttempt || PlayerProgress.AdminSandboxActive) Session.Invalidate();
        return Session.IsEligible;
    }

    private void CaptureCompleted()
    {
        if (Session == null || !Session.IsEligible || ended || owner == null ||
            owner.IsTestAttempt || PlayerProgress.AdminSandboxActive) return;
        foreach (ChallengeDefinition definition in ChallengeCatalog.All)
            if (!completedBeforeRun.Contains(definition.Id) && PlayerProgress.IsChallengeCompleted(definition.Id) &&
                !completed.Contains(definition.Id)) completed.Add(definition.Id);
    }

    public void RecordHeight(int height)
    {
        if (!EnsureSession()) return;
        FlushPreviousPhysicsStep();
        Session.SetHeight(height);
    }
    public void RecordCoin(bool magnet)
    {
        if (!EnsureSession()) return;
        FlushPreviousPhysicsStep();
        float velocity = body != null ? body.linearVelocity.y : 0f;
        bool nearApex = hasLaunch && flightOriginValid && !collectingAtContact &&
            Mathf.Abs(velocity) <= Mathf.Max(0.1f, owner.apexVelocityThreshold);
        float physicalHeight = feet != null ? feet.bounds.center.y : owner.transform.position.y;
        Session.RecordCoin(magnet, !assistedFlight && (collectingAtDescendingContact || velocity < -0.1f),
            !assistedFlight && nearApex,
            WorldZones.AtHeight(physicalHeight) == WorldZones.Zone.Moon);
    }
    public void BeginContactCollection(bool descending)
    {
        collectingAtContact = true;
        collectingAtDescendingContact = descending;
    }
    public void EndContactCollection() { collectingAtContact = collectingAtDescendingContact = false; }
    public void RecordPowerup(bool magnet)
    {
        if (!EnsureSession()) return;
        FlushPreviousPhysicsStep();
        Session.RecordPowerup(magnet);
    }
    public void RecordPowerup(RunPowerups.Kind kind)
    {
        if (!EnsureSession()) return;
        FlushPreviousPhysicsStep();
        Session.RecordPowerup(kind);
    }
    public void RecordGoldenFish()
    {
        if (!EnsureSession()) return;
        // A pickup can be resolved in Player.LateUpdate after this physics
        // step's bounce. Close that old landing first, so our own LateUpdate
        // cannot erase the new excursion's pending safe-return proof.
        // During contact collection, however, the bush trigger has not yet
        // identified this bounce: retain its established callback-order defer.
        if (collectingAtContact) FlushPreviousPhysicsStep();
        else FlushOrdinaryLanding();
        // A golden fish is one physical pickup, even though its wallet value
        // is five coins. Do not derive this event from weighted coin callbacks.
        Session.RecordGoldenFish(powers != null && powers.MagnetTimeRemaining > 0f,
            assistedFlight || powers != null && powers.IsRocketActive);
    }
    public void RecordRocketLaunch()
    {
        if (!EnsureSession()) return;
        FlushPreviousPhysicsStep();
        Session.RecordRocketLaunch();
    }
    public void RecordRocketEnd(bool safeLanding)
    {
        if (!EnsureSession()) return;
        FlushPreviousPhysicsStep();
        Session.RecordRocketEnd(safeLanding);
    }
    public void RecordBeeDodge()
    {
        if (!EnsureSession()) return;
        FlushPreviousPhysicsStep();
        Session.RecordBeeDodge();
    }
    public void RecordAssistedFlight()
    {
        if (!EnsureSession()) return;
        FlushOrdinaryLanding();
        assistedFlight = true;
        flightOriginValid = false;
        flightWrapOffset = 0f;
        bushFlight = false;
        bushSupportRows.Clear();
        Session.RecordAssistedTravel();
    }
    public void RecordShieldUse(bool fallingRescue)
    {
        if (!EnsureSession()) return;
        FlushOrdinaryLanding();
        bushFlight = false;
        flightOriginValid = false;
        flightWrapOffset = 0f;
        bushSupportRows.Clear();
        Session.RecordShieldUse(fallingRescue);
    }
    public void RecordWrap(float travelCorrection = 0f)
    {
        if (!EnsureSession()) return;
        FlushPreviousPhysicsStep();
        if (!float.IsNaN(travelCorrection) && !float.IsInfinity(travelCorrection))
            flightWrapOffset += travelCorrection;
        if (!assistedFlight || powers != null && powers.IsRocketFlying) Session.RecordWrap();
        if (powers != null && powers.IsRocketFlying) Session.RecordRocketWrap();
    }

    public void RecordBush()
    {
        if (!EnsureSession() || feet == null) return;
        FlushPreviousPhysicsStep();
        // Bush triggers and support collisions can arrive in either order in
        // one physics step. A bush contact must not break a two-bush chain.
        lastBushStep = Time.fixedTime;
        ordinaryLandingPending = false;
        ordinaryBreaksStreak = false;
        Session.RecordBush();
        bushFlight = hasLaunch = true;
        flightOriginValid = true;
        bushLaunchY = launchY = feet.bounds.min.y;
        launchX = feet.bounds.center.x;
        flightWrapOffset = 0f;
        highestLandingY = Mathf.Max(highestLandingY, bushLaunchY);
        bushSupportRows.Clear();
        // Snapshot at launch: the camera may remove skipped supports before
        // landing. This event-only scan never changes route generation or RNG.
        float rowTolerance = 0.12f * GameplayPreferences.ModelScale;
        foreach (Collider2D support in FindObjectsByType<Collider2D>())
        {
            if (!support.enabled || support.isTrigger || !support.CompareTag("Platform")) continue;
            float top = support.bounds.max.y;
            if (top <= bushLaunchY + rowTolerance) continue;
            bool duplicate = false;
            for (int i = 0; i < bushSupportRows.Count; i++)
                if (Mathf.Abs(bushSupportRows[i] - top) <= rowTolerance) { duplicate = true; break; }
            if (!duplicate && bushSupportRows.Count < 256) bushSupportRows.Add(top);
        }
    }
    public void Invalidate() { Session?.Invalidate(); }
    public void EndRun() { ended = true; }

    public void RecordLanding(Collider2D surface)
    {
        if (!EnsureSession() || surface == null || feet == null || sceneCamera == null) return;
        Bounds bounds = surface.bounds;
        float scale = GameplayPreferences.ModelScale;
        if (Mathf.Abs(feet.bounds.min.y - bounds.max.y) > 0.25f * scale) return;
        FlushPreviousPhysicsStep();
        if (assistedFlight)
        {
            highestLandingY = Mathf.Max(highestLandingY, bounds.max.y);
            countedSurfaces.Add(surface);
            hasLaunch = flightOriginValid = true;
            launchX = feet.bounds.center.x; launchY = bounds.max.y; flightWrapOffset = 0f;
            assistedFlight = false;
            QueueOrdinaryLanding(true);
            return;
        }
        // Staying on the opening pad, or bouncing repeatedly on an old pad,
        // must not farm a mastery or a selected landing task.
        if (!hasLaunch)
        {
            hasLaunch = true;
            highestLandingY = bounds.max.y;
            launchX = feet.bounds.center.x;
            launchY = bounds.max.y;
            flightWrapOffset = 0f;
            flightOriginValid = true;
            countedSurfaces.Add(surface);
            QueueOrdinaryLanding();
            return;
        }
        float width = sceneCamera.orthographicSize * sceneCamera.aspect * 2f;
        bool newHigher = bounds.max.y > highestLandingY + 0.04f * scale &&
            countedSurfaces.Count < 4096 && countedSurfaces.Add(surface);
        if (newHigher)
        {
            float offset = Mathf.Abs(TrapGeometry.WrappedDelta(bounds.center.x, feet.bounds.center.x, width));
            bool centered = offset <= bounds.size.x / 6f;
            bool moving = surface.GetComponent<MovingPlatform>() != null;
            bool narrow = normalPlatformWidth > 0f && bounds.size.x < normalPlatformWidth * 0.82f;
            float threshold = LongJumpThreshold(owner, bounds.max.y - launchY, width,
                GameplayPreferences.UseInertia, GameplayPreferences.HorizontalMovementScale);
            float displacement = feet.bounds.center.x - launchX + flightWrapOffset;
            bool wide = flightOriginValid && Mathf.Abs(displacement) >= threshold;
            bool edge = offset >= bounds.size.x * 0.30f && offset <= bounds.extents.x;
            int skippedRows = 0;
            if (bushFlight)
                for (int i = 0; i < bushSupportRows.Count; i++)
                    if (bushSupportRows[i] < bounds.max.y - 0.12f * scale) skippedRows++;
            Session.RecordLanding(new ChallengeLandingEvent
            {
                Centered = centered, Moving = moving, Narrow = narrow, Wide = wide, Edge = edge,
                SlowActive = powers != null && powers.SlowTimeRemaining > 0f,
                Direction = flightOriginValid && Mathf.Abs(displacement) > 0.15f * scale ? (displacement > 0f ? 1 : -1) : 0,
                BushSkippedRows = skippedRows
            });
            highestLandingY = bounds.max.y;
        }
        launchX = feet.bounds.center.x;
        launchY = bounds.max.y;
        flightWrapOffset = 0f;
        flightOriginValid = true;
        QueueOrdinaryLanding(!newHigher);
    }

    private void QueueOrdinaryLanding(bool breakCenterStreak = false)
    {
        if (Mathf.Approximately(lastBushStep, Time.fixedTime)) return;
        ordinaryLandingPending = true;
        ordinaryBreaksStreak |= breakCenterStreak;
        ordinaryLandingStep = Time.fixedTime;
    }

    private void FlushPreviousPhysicsStep()
    {
        if (ordinaryLandingPending && !Mathf.Approximately(ordinaryLandingStep, Time.fixedTime))
            FlushOrdinaryLanding();
    }

    private void FlushOrdinaryLanding()
    {
        if (!ordinaryLandingPending) return;
        ordinaryLandingPending = false;
        bushFlight = false;
        bushSupportRows.Clear();
        if (Session != null && Session.IsEligible) Session.RecordOrdinaryLanding(ordinaryBreaksStreak);
        ordinaryBreaksStreak = false;
    }

    public static float LongJumpThreshold(Player player, float gap, float screenWidth, bool inertia, float movementScale)
    {
        JumpReachability.Result flight = JumpReachability.SimulatePrepared(player, gap, inertia, movementScale);
        if (!flight.CanLand) flight = JumpReachability.SimulatePrepared(player, 0f, inertia, movementScale);
        // Very wide desktop windows must not require a jump beyond the actual
        // controller's range. This adjusts only a task metric, never the route.
        return Mathf.Max(0.01f, Mathf.Min(screenWidth * 0.30f, flight.HorizontalDistance * 0.75f));
    }
}
