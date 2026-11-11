using UnityEngine;

// One telegraphed straight attack inside an existing certified hazard box.
// DoodlikHazard drives Step after capturing its previous position, so moving
// damage uses the same relative sweep as the original flying seeds.
[DisallowMultipleComponent]
public sealed class ChargingBumblebee : MonoBehaviour
{
    public enum Phase { Waiting, Windup, Dash, Recovery, Retreat, Finished }
    public const int StartHeight = 220;
    private const float ArtScale = 0.85f;
    private static ChargingBumblebee active;
    public static bool HasActive => active != null && active.isActiveAndEnabled;
    public Phase CurrentPhase { get; private set; }
    public float WindupDuration { get; private set; }
    public float DashSpeed { get; private set; }
    public Vector2 AimPoint { get; private set; }
    public Rect CenterBounds { get; private set; }
    public bool IsDamaging => hazard != null && hazard.DamageEnabled;
    public bool DodgeThreatened => dodgeThreat;
    public bool DodgeAwarded => dodgeAwarded;

    private DoodlikHazard hazard;
    private Player target;
    private RunPowerups powers;
    private Camera sceneCamera;
    private Transform drawing;
    private MeshFilter bodyFilter;
    private MeshRenderer arrowRenderer;
    private Mesh calmMesh, arrowMesh;
    private Material material;
    private Vector2 origin;
    private float size, phaseTime, poseTime, facing = 1f;
    private bool rocketInterrupted, reachedDashEnd;
    private bool dodgeThreat, dodgeMovedAway, dodgeCancelled, dodgeEvaluated, dodgeAwarded, dodgeAudited;
    private Vector2 dodgePlayerCenter, dodgePlayerHalf, dodgeBeeFrom, dodgeBeeTo, dodgeBeeHalf;
    private float dodgeScreenWidth;
    private int dodgeShieldUses, dodgeCompletedFrame = -1, dodgeRecoveryFrame = -1;
    private readonly Vector3[] arrowVertices = new Vector3[30];
    private readonly Color32[] arrowColors = new Color32[30];
    private static readonly int[] ArrowTriangles =
    {
        0,1,2,0,2,3,4,5,6,7,8,9,7,9,10,11,12,13,
        14,15,16,14,16,17,18,19,20,18,20,21,
        22,23,24,22,24,25,26,27,28,26,28,29
    };

    public static bool ShouldReplace(int eligibleOrdinal, bool anotherAlive) =>
        !anotherAlive && eligibleOrdinal >= 0 && eligibleOrdinal % 3 == 0;
    public static float WarningFor(GameDifficulty difficulty) =>
        difficulty == GameDifficulty.Easy ? 1.4f : difficulty == GameDifficulty.Hard ? 1.05f : 1.2f;
    public static float SpeedFor(float modelScale, float screenWidth, GameDifficulty difficulty)
    {
        float scale = Mathf.Max(0.01f, modelScale);
        float baseSpeed = Mathf.Clamp(screenWidth * 0.55f, 4.8f * scale, 8.5f * scale);
        return baseSpeed * (difficulty == GameDifficulty.Easy ? 0.85f : difficulty == GameDifficulty.Hard ? 1.1f : 1f);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => active = null;

    private void OnEnable() => active = this;
    private void OnDisable()
    {
        if (active == this) active = null;
        dodgeCancelled = true;
        hazard?.SetDamageEnabled(false);
    }

    // Player calls this before shield/grace handling for EVERY real contact.
    // Survival through protection is not an avoided attack.
    public void RecordPlayerContact() => dodgeCancelled = true;

    // Player calls this after its real LateUpdate sweep. Latch the exact final
    // damage-frame proof now: a later pause/resume must not erase it, while a
    // paused/skipped sweep must never be replaced by an unrelated later audit.
    public static void RecordPlayerAudit(Player player)
    {
        ChargingBumblebee bee = active;
        if (bee != null && bee.target == player && bee.CertificationLive() &&
            bee.reachedDashEnd && bee.dodgeCompletedFrame == Time.frameCount && bee.IsDamaging)
            bee.dodgeAudited = true;
    }

    internal void Initialize(DoodlikHazard owner, Player player, Camera camera,
        float modelScale, GameDifficulty difficulty, Transform art, MeshRenderer renderer)
    {
        // The generator's EditMode replay does not run MonoBehaviour.OnEnable.
        // Explicit initialization owns the same idempotent live-slot contract.
        active = this;
        hazard = owner;
        target = player;
        powers = player != null ? player.GetComponent<RunPowerups>() : null;
        sceneCamera = camera;
        drawing = art;
        bodyFilter = art.GetComponent<MeshFilter>();
        size = modelScale;
        origin = transform.position;
        WindupDuration = WarningFor(DifficultyRules.Sanitize(difficulty));
        DashSpeed = SpeedFor(size, camera != null ? camera.orthographicSize * camera.aspect * 2f : 12f, difficulty);
        // The dense visible body (and its much smaller damage box) never leave
        // the old seed's reservation. Non-dangerous folded wings are decorative.
        Rect reserved = owner.PatrolBounds;
        Vector2 inset = new Vector2(0.27f, 0.17f) * size;
        float width = Mathf.Max(0f, reserved.width - inset.x * 2f);
        float height = Mathf.Max(0f, reserved.height - inset.y * 2f);
        CenterBounds = new Rect(reserved.center - new Vector2(width, height) * 0.5f, new Vector2(width, height));
        origin = Clamp(origin);
        hazard.SetExternalPosition(origin);
        material = new Material(Resources.Load<Shader>("HazardInk")) { name = "Bumblebee ink", hideFlags = HideFlags.DontSave };
        renderer.sharedMaterial = material;
        calmMesh = Instantiate(AdventureArt.BeeBody);
        calmMesh.name = "Bumblebee harmless pose";
        Color[] colors = calmMesh.colors;
        for (int i = 0; i < colors.Length; i++)
        {
            Color color = Color.Lerp(colors[i], new Color(1f, 0.91f, 0.65f, 1f), 0.24f);
            color.a *= 0.72f;
            colors[i] = color;
        }
        calmMesh.colors = colors;
        GameObject warning = new GameObject("Bumblebee warning arrow", typeof(MeshFilter), typeof(MeshRenderer));
        warning.transform.SetParent(transform, false);
        arrowMesh = new Mesh { name = "Bumblebee telegraph", hideFlags = HideFlags.DontSave };
        arrowMesh.MarkDynamic();
        warning.GetComponent<MeshFilter>().sharedMesh = arrowMesh;
        arrowRenderer = warning.GetComponent<MeshRenderer>();
        arrowRenderer.sharedMaterial = material;
        arrowRenderer.sortingOrder = 4;
        arrowRenderer.enabled = false;
        CurrentPhase = Phase.Waiting;
        hazard.SetDamageEnabled(false);
        SetPose();
    }

    public void Step(float deltaTime)
    {
        if (target == null || target.IsDead) dodgeCancelled = true;
        if (hazard == null || target == null || target.IsDead || !RunFlow.IsPlaying || GameMenuUI.BlocksGameplayInput)
        { hazard?.SetDamageEnabled(false); return; }
        if (!(deltaTime > 0f) || float.IsInfinity(deltaTime)) return;
        float elapsed = deltaTime * RunPowerups.PlatformSpeedMultiplier;
        poseTime += elapsed;
        if (powers == null) powers = target.GetComponent<RunPowerups>();
        if (powers != null && powers.IsRocketActive)
        {
            if (CurrentPhase == Phase.Windup || CurrentPhase == Phase.Dash || CurrentPhase == Phase.Recovery)
                dodgeCancelled = true;
            hazard.SetDamageEnabled(false);
            arrowRenderer.enabled = false;
            rocketInterrupted = true;
            // An attack already in flight is cancelled, not stored to ambush
            // the cat at rocket exit. A pending windup must restart in full.
            if (CurrentPhase == Phase.Dash) ChangePhase(Phase.Recovery);
            SetPose();
            return;
        }
        if (rocketInterrupted)
        {
            rocketInterrupted = false;
            if (CurrentPhase == Phase.Windup) ChangePhase(Phase.Waiting);
        }

        ObserveDodge();

        switch (CurrentPhase)
        {
            case Phase.Waiting:
                if (Visible()) BeginWindup();
                break;
            case Phase.Windup:
                phaseTime += elapsed;
                if (phaseTime >= WindupDuration)
                {
                    ChangePhase(Phase.Dash);
                    arrowRenderer.enabled = false;
                    hazard.SetDamageEnabled(true);
                }
                break;
            case Phase.Dash:
                if (reachedDashEnd)
                {
                    ChangePhase(Phase.Recovery);
                    dodgeRecoveryFrame = CertificationLive() ? Time.frameCount : -1;
                    hazard.SetDamageEnabled(false);
                    break;
                }
                hazard.SetDamageEnabled(true);
                Vector2 next = Vector2.MoveTowards(transform.position, AimPoint, DashSpeed * elapsed);
                hazard.SetExternalPosition(Clamp(next));
                // Keep damage enabled through this movement frame. Disabling
                // it immediately would drop the entire dash at low frame rates.
                reachedDashEnd = ((Vector2)transform.position - AimPoint).sqrMagnitude <= 0.000001f;
                if (reachedDashEnd) dodgeCompletedFrame = CertificationLive() ? Time.frameCount : -1;
                break;
            case Phase.Recovery:
                TryAwardDodge();
                phaseTime += elapsed;
                if (phaseTime >= 0.5f) ChangePhase(Phase.Retreat);
                break;
            case Phase.Retreat:
                phaseTime += elapsed;
                hazard.SetExternalPosition(Clamp(Vector2.MoveTowards(transform.position, origin, DashSpeed * 0.32f * elapsed)));
                if (phaseTime >= 0.65f)
                {
                    ChangePhase(Phase.Finished);
                    hazard.SetDamageEnabled(false);
                    Destroy(gameObject);
                }
                break;
        }
        SetPose();
    }

    private bool Visible()
    {
        if (sceneCamera == null) return false;
        // Activate when both visible and approaching the cat's jump height,
        // synchronizing the one-shot attack with the climb rather than camera
        // look-ahead. This does not alter the certified reservation or warning.
        float approach = Mathf.Min(sceneCamera.orthographicSize * 0.45f,
            Mathf.Max(1f, target.NormalJumpForce * WindupDuration * 0.16f));
        return target.transform.position.y >= hazard.PatrolBounds.yMin - approach &&
            hazard.PatrolBounds.yMin < sceneCamera.transform.position.y + sceneCamera.orthographicSize - 0.2f * size &&
            hazard.PatrolBounds.yMax > sceneCamera.transform.position.y - sceneCamera.orthographicSize;
    }

    private void BeginWindup()
    {
        // Snapshot once, before the complete warning interval. No homing during
        // windup or dash; moving after the arrow appears is a genuine dodge.
        AimPoint = Clamp(target.transform.position);
        if ((AimPoint - (Vector2)transform.position).sqrMagnitude < 0.01f * size * size)
        {
            float left = Mathf.Abs(CenterBounds.xMin - transform.position.x);
            float right = Mathf.Abs(CenterBounds.xMax - transform.position.x);
            AimPoint = new Vector2(left > right ? CenterBounds.xMin : CenterBounds.xMax, AimPoint.y);
        }
        facing = AimPoint.x < transform.position.x ? -1f : 1f;
        reachedDashEnd = false;
        CaptureDodgeThreat();
        ChangePhase(Phase.Windup);
        hazard.SetDamageEnabled(false);
        BuildArrow();
        arrowRenderer.enabled = true;
    }

    private bool CertificationLive() => isActiveAndEnabled && hazard != null && hazard.isActiveAndEnabled &&
        target != null && target.isActiveAndEnabled && !target.IsDead && RunFlow.IsPlaying &&
        Time.timeScale > 0f && !GameMenuUI.BlocksGameplayInput;

    private void CaptureDodgeThreat()
    {
        dodgeThreat = dodgeMovedAway = dodgeEvaluated = dodgeAudited = false;
        dodgeCompletedFrame = dodgeRecoveryFrame = -1;
        if (!CertificationLive() || sceneCamera == null) return;
        dodgePlayerCenter = target.HazardBodyCenter;
        // Exact cached Player collision recipe, not a fresh animated/tail bound.
        dodgePlayerHalf = target.HazardBodyHalfSize;
        if (!(dodgePlayerHalf.x > 0f) || !(dodgePlayerHalf.y > 0f)) return;
        Rect damage = hazard.DamageBounds;
        dodgeBeeFrom = damage.center;
        dodgeBeeTo = AimPoint + (damage.center - (Vector2)transform.position);
        dodgeBeeHalf = damage.size * 0.5f;
        dodgeScreenWidth = sceneCamera.orthographicSize * sceneCamera.aspect * 2f;
        Rect view = new Rect((Vector2)sceneCamera.transform.position -
            new Vector2(dodgeScreenWidth * 0.5f, sceneCamera.orthographicSize),
            new Vector2(dodgeScreenWidth, sceneCamera.orthographicSize * 2f));
        dodgeShieldUses = ShieldUses();
        // Merely being spawned or visible nearby is insufficient. This exact
        // straight shot must geometrically threaten the current visible cat.
        dodgeThreat = view.Contains(dodgePlayerCenter) && view.Contains(dodgeBeeFrom) &&
            Threatens(dodgePlayerCenter);
    }

    private bool Threatens(Vector2 bodyCenter)
    {
        for (int copy = -1; copy <= 1; copy++)
        {
            Rect body = new Rect(bodyCenter + Vector2.right * (copy * dodgeScreenWidth) - dodgePlayerHalf,
                dodgePlayerHalf * 2f);
            if (TrapGeometry.SweptBox(dodgeBeeFrom, dodgeBeeTo, dodgeBeeHalf, body)) return true;
        }
        return false;
    }

    private int ShieldUses()
    {
        RunChallenges run = target != null ? target.Challenges : null;
        return run != null && run.Session != null ? run.Session.Stats.ShieldUses : 0;
    }

    private void ObserveDodge()
    {
        if (!dodgeThreat || dodgeCancelled || !CertificationLive()) return;
        float width = sceneCamera.orthographicSize * sceneCamera.aspect * 2f;
        if (Mathf.Abs(width - dodgeScreenWidth) > 0.01f || ShieldUses() != dodgeShieldUses)
        { dodgeCancelled = true; return; }
        if (CurrentPhase != Phase.Windup && CurrentPhase != Phase.Dash) return;
        Vector2 center = target.HazardBodyCenter;
        Vector2 displacement = center - dodgePlayerCenter;
        displacement.x = TrapGeometry.WrappedDelta(dodgePlayerCenter.x, center.x, dodgeScreenWidth);
        float minimum = Mathf.Max(0.12f * GameplayPreferences.ModelScale,
            Mathf.Min(dodgePlayerHalf.x, dodgePlayerHalf.y) * 0.5f);
        if (displacement.sqrMagnitude >= minimum * minimum && !Threatens(center)) dodgeMovedAway = true;
    }

    private void TryAwardDodge()
    {
        if (dodgeEvaluated || dodgeAwarded || !CertificationLive() || dodgeCompletedFrame < 0 ||
            dodgeRecoveryFrame < 0 || Time.frameCount <= Mathf.Max(dodgeCompletedFrame, dodgeRecoveryFrame)) return;
        // Both transitions are fenced by actual render frames. Player.LateUpdate
        // (including shield-protected contacts) has audited the final damaging
        // movement before this later Recovery step can issue a certificate.
        dodgeEvaluated = true;
        if (!dodgeThreat || !dodgeMovedAway || !dodgeAudited || dodgeCancelled || ShieldUses() != dodgeShieldUses) return;
        RunChallenges run = target.Challenges;
        if (run == null || run.Session == null || !run.IsEligible) return;
        int before = run.Session.Stats.BeeDodges;
        run.RecordBeeDodge();
        dodgeAwarded = run.Session.Stats.BeeDodges > before;
    }

    private Vector2 Clamp(Vector2 point) => new Vector2(Mathf.Clamp(point.x, CenterBounds.xMin, CenterBounds.xMax),
        Mathf.Clamp(point.y, CenterBounds.yMin, CenterBounds.yMax));
    private void ChangePhase(Phase value) { CurrentPhase = value; phaseTime = 0f; }

    private void SetPose()
    {
        if (drawing == null || bodyFilter == null) return;
        bool attacking = CurrentPhase == Phase.Dash && IsDamaging;
        bodyFilter.sharedMesh = attacking ? AdventureArt.BeeBody : calmMesh;
        float tilt = attacking ? 0f : CurrentPhase == Phase.Windup ? Mathf.Sin(poseTime * 31f) * 3f : -6f;
        drawing.localRotation = Quaternion.Euler(0f, 0f, tilt * facing);
        drawing.localScale = new Vector3(facing, 1f, 1f) * (ArtScale * size);
        Vector2 center = AdventureArt.BeeDamageRect.center;
        drawing.localPosition = new Vector3(-center.x * ArtScale * size * facing, -center.y * ArtScale * size, 0f);
    }

    private void BuildArrow()
    {
        Vector2 end = AimPoint - (Vector2)transform.position;
        Vector2 direction = end.sqrMagnitude > 0.000001f ? end.normalized : Vector2.right;
        Vector2 side = new Vector2(-direction.y, direction.x);
        float arrowLength = Mathf.Min(end.magnitude * 0.38f, 0.17f * size);
        Vector2 neck = end - direction * arrowLength;
        Vector2 outer = side * (0.037f * size), inner = side * (0.018f * size);
        arrowVertices[0] = -outer; arrowVertices[1] = outer;
        arrowVertices[2] = neck + outer; arrowVertices[3] = neck - outer;
        arrowVertices[4] = neck + side * 0.115f * size;
        arrowVertices[5] = end; arrowVertices[6] = neck - side * 0.115f * size;
        arrowVertices[7] = -inner; arrowVertices[8] = inner;
        arrowVertices[9] = neck + inner; arrowVertices[10] = neck - inner;
        arrowVertices[11] = neck + side * 0.075f * size;
        arrowVertices[12] = end - direction * 0.035f * size;
        arrowVertices[13] = neck - side * 0.075f * size;
        // In a narrow certified box the arrow can be shorter than the bee.
        // A small ink/amber ! above the wings keeps the warning unmistakable
        // without making that decorative area dangerous or reserving more room.
        ArrowQuad(14, new Vector2(-0.031f, 0.35f), new Vector2(0.031f, 0.51f));
        ArrowQuad(18, new Vector2(-0.014f, 0.366f), new Vector2(0.014f, 0.494f));
        ArrowQuad(22, new Vector2(-0.031f, 0.278f), new Vector2(0.031f, 0.328f));
        ArrowQuad(26, new Vector2(-0.014f, 0.291f), new Vector2(0.014f, 0.315f));
        for (int i = 0; i < arrowColors.Length; i++)
            arrowColors[i] = i < 7 || i >= 14 && i < 18 || i >= 22 && i < 26
                ? new Color32(90, 40, 31, 230) : new Color32(244, 117, 76, 240);
        arrowMesh.vertices = arrowVertices; arrowMesh.colors32 = arrowColors;
        arrowMesh.triangles = ArrowTriangles; arrowMesh.RecalculateBounds();
    }

    private void ArrowQuad(int index, Vector2 minimum, Vector2 maximum)
    {
        arrowVertices[index] = minimum * size;
        arrowVertices[index + 1] = new Vector2(minimum.x, maximum.y) * size;
        arrowVertices[index + 2] = maximum * size;
        arrowVertices[index + 3] = new Vector2(maximum.x, minimum.y) * size;
    }

    private void OnDestroy()
    {
        if (active == this) active = null;
        Release(calmMesh); Release(arrowMesh); Release(material);
    }
    private static void Release(Object resource)
    {
        if (resource == null) return;
        if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
    }
}
