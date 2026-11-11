using System.Collections.Generic;
using UnityEngine;

public sealed class DoodlikHazard : MonoBehaviour
{
    public enum Kind { Thorns, FlyingSeed, ThornPatch, VerticalThorns, Bumblebee }
    public enum FlightPattern { Horizontal, Vertical, RisingDiagonal, FallingDiagonal }
    public const float ThornBranchScale = 1.5f;
    // The vertical vine is the ThornBranch sprite rotated 90 degrees. Its
    // visible alpha is narrower and slightly right of the sprite pivot.
    public const float VerticalThornHalfWidth = 0.115f;
    public const float VerticalThornCenterOffsetX = 0.018f;
    private static readonly List<DoodlikHazard> Active = new List<DoodlikHazard>();

    public Kind Type { get; private set; }
    public bool IsAirborne => Type == Kind.FlyingSeed || Type == Kind.Bumblebee;
    public bool DamageEnabled { get; private set; } = true;
    public Rect PatrolBounds { get; private set; }
    public Rect DamageBounds => new Rect((Vector2)transform.position + damageOffset - damageHalfSize,
        damageHalfSize * 2f);
    // The thorn-patch mesh is intentionally wider than its forgiving damage
    // box. Use its whole silhouette when keeping bush launches readable.
    public Rect WarningBounds => Type == Kind.ThornPatch
        ? PatchVisibleBounds(transform.position, transform.lossyScale) : DamageBounds;
    public static Rect PatchVisibleBounds(Vector2 position, Vector3 parentScale)
    {
        float halfWidth = 1.00f * Mathf.Abs(parentScale.x);
        float below = 1.08f * Mathf.Abs(parentScale.y);
        float above = 0.90f * Mathf.Abs(parentScale.y);
        return new Rect(position.x - halfWidth, position.y - below,
            halfWidth * 2f, below + above);
    }
    public Vector2[] CertifiedRoute { get; private set; }
#if UNITY_EDITOR
    public Rect[] CertifiedExistingPatrols { get; private set; }
    public FlyingHazardRoutes.Paths CertifiedInitialPaths { get; private set; }
    public void SetCertifiedProofInputs(Rect[] existingPatrols,
        FlyingHazardRoutes.Paths initialPaths)
    {
        CertifiedExistingPatrols = existingPatrols;
        CertifiedInitialPaths = initialPaths;
    }
#endif
    public FlightPattern MotionPattern { get; private set; }
    public Vector2 FlightAmplitude => new Vector2(patrol, verticalPatrol);
    public float FlightSpeed => speed;
    public void SetCertifiedRoute(Vector2[] route) => CertifiedRoute = route;

    private Camera sceneCamera;
    private Vector2 origin;
    private Vector2 previousPosition;
    private Vector2 damageHalfSize;
    private Vector2 damageOffset;
    private Transform drawing;
    private float patrol;
    private float verticalPatrol;
    private float speed;
    private float phase;
    private float age;
    private float size;
    private BoxCollider2D thornPlatform;
    private ChargingBumblebee bumblebee;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => Active.Clear();

    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);

    public void Initialize(Kind type, Camera camera, float modelScale,
        float patrolRange, float patrolSpeed, float startPhase, Rect reservedBounds,
        FlightPattern flightPattern = FlightPattern.Horizontal,
        float verticalPatrolRange = -1f)
    {
        Type = type;
        DamageEnabled = true;
        sceneCamera = camera;
        origin = transform.position;
        previousPosition = origin;
        size = modelScale;
        patrol = patrolRange;
        verticalPatrol = verticalPatrolRange < 0f ? 0.1f * modelScale : verticalPatrolRange;
        speed = patrolSpeed;
        phase = startPhase;
        MotionPattern = flightPattern;
        PatrolBounds = reservedBounds;
        thornPlatform = type == Kind.Thorns && transform.parent != null
            ? transform.parent.GetComponent<BoxCollider2D>() : null;
        damageHalfSize = type == Kind.Thorns
            ? new Vector2(0.49f, 0.22f) * size * ThornBranchScale
            : type == Kind.VerticalThorns
                ? new Vector2(VerticalThornHalfWidth, 0.49f) * size * ThornBranchScale
                : new Vector2(0.24f, 0.24f) * size;
        damageOffset = type == Kind.VerticalThorns
            ? Vector2.right * (VerticalThornCenterOffsetX * size * ThornBranchScale)
            : Vector2.zero;
        drawing = HazardDrawing.Attach(transform, type);
        drawing.localScale = Vector3.one * size *
            (type == Kind.Thorns || type == Kind.VerticalThorns ? ThornBranchScale : 1f);
        if (type == Kind.VerticalThorns)
            drawing.localRotation = Quaternion.Euler(0f, 0f, 90f);
    }

    public void InitializePatch(Camera camera, Vector2 halfSize, Vector3 artScale)
    {
        Type = Kind.ThornPatch;
        DamageEnabled = true;
        sceneCamera = camera;
        origin = transform.position;
        previousPosition = origin;
        size = 1f;
        damageHalfSize = halfSize;
        damageOffset = Vector2.zero;
        thornPlatform = null;
        drawing = HazardDrawing.Attach(transform, Type);
        drawing.localScale = artScale;
        drawing.localPosition = new Vector3(0f, -0.64f, 0f);
    }

    public void InitializeBumblebee(Camera camera, Player target, float modelScale,
        Rect reservedBounds, GameDifficulty difficulty)
    {
        Type = Kind.Bumblebee;
        DamageEnabled = false;
        sceneCamera = camera;
        origin = previousPosition = transform.position;
        size = Mathf.Max(0.01f, modelScale);
        PatrolBounds = reservedBounds;
        // A forgiving core, entirely inside AdventureArt's dense body. Wings,
        // antennae and motion ink cannot kill the cat. Art faces horizontally.
        damageHalfSize = new Vector2(0.16f, 0.065f) * size;
        damageOffset = Vector2.zero;
        GameObject art = new GameObject("Angry paper bumblebee", typeof(MeshFilter), typeof(MeshRenderer));
        art.transform.SetParent(transform, false);
        art.GetComponent<MeshFilter>().sharedMesh = AdventureArt.BeeBody;
        MeshRenderer renderer = art.GetComponent<MeshRenderer>();
        renderer.sortingOrder = 3;
        drawing = art.transform;
        bumblebee = gameObject.AddComponent<ChargingBumblebee>();
        bumblebee.Initialize(this, target, camera, size, difficulty, drawing, renderer);
    }

    internal void SetExternalPosition(Vector2 position) =>
        transform.position = new Vector3(position.x, position.y, transform.position.z);
    internal void SetDamageEnabled(bool enabled) => DamageEnabled = enabled;

    private void Update()
    {
        previousPosition = transform.position;
        if (Time.timeScale <= 0f || sceneCamera == null)
            return;

        age += Time.deltaTime * RunPowerups.PlatformSpeedMultiplier;
        if (Type == Kind.FlyingSeed)
        {
            // No pursuit, no screen-edge teleport: the complete patrol was
            // reserved by the generator before this seed became visible.
            transform.position = FlightPosition(origin, age, FlightAmplitude,
                speed, phase, MotionPattern);
            drawing.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Sin(age * 2f + phase) * 12f);
        }
        else if (Type == Kind.Bumblebee && bumblebee != null && bumblebee.enabled)
        {
            // Capture previousPosition above BEFORE the external controller
            // moves. Player.LateUpdate then sweeps this exact frame once.
            bumblebee.Step(Time.deltaTime);
        }

        if (transform.position.y < sceneCamera.transform.position.y -
            sceneCamera.orthographicSize - 3f)
            Destroy(Type == Kind.Thorns && transform.parent != null
                ? transform.parent.gameObject : gameObject);
    }

    // Shared by the visible hazard and the route prover: placement is only
    // accepted when a real jump can meet this exact moving shape.
    public static Vector2 FlightPosition(Vector2 origin, float elapsed,
        Vector2 amplitude, float speed, float phase, FlightPattern pattern)
    {
        float angle = elapsed * speed + phase;
        switch (pattern)
        {
            case FlightPattern.Vertical:
                return origin + new Vector2(
                    Mathf.Sin(angle * 1.3f) * amplitude.x * 0.2f,
                    Mathf.Sin(angle) * amplitude.y);
            case FlightPattern.RisingDiagonal:
                return origin + new Vector2(Mathf.Sin(angle) * amplitude.x,
                    Mathf.Sin(angle) * amplitude.y);
            case FlightPattern.FallingDiagonal:
                return origin + new Vector2(Mathf.Sin(angle) * amplitude.x,
                    -Mathf.Sin(angle) * amplitude.y);
            default:
                return origin + new Vector2(Mathf.Sin(angle) * amplitude.x,
                    Mathf.Sin(angle * 1.4f) * amplitude.y * 0.25f);
        }
    }

    // Called from Player.LateUpdate after all hazard movement. Sweeping instead
    // of relying on trigger callbacks also catches crossings at low frame rates.
    public static DoodlikHazard FindHit(Vector2 from, Vector2 to,
        Vector2 playerHalfSize, float screenWidth, bool sweepMotion = true)
        => FindHit(from, to, playerHalfSize, screenWidth, out _, sweepMotion);

    // Preserve the first damaging hit for the existing death/shield rules,
    // but also report a simultaneous bee contact for honest dodge accounting.
    // A different trap absorbed by exit grace must not hide that contact.
    public static DoodlikHazard FindHit(Vector2 from, Vector2 to,
        Vector2 playerHalfSize, float screenWidth, out ChargingBumblebee beeContact, bool sweepMotion = true)
    {
        beeContact = null;
        DoodlikHazard firstHit = null;
        to.x = from.x + TrapGeometry.WrappedDelta(from.x, to.x, screenWidth);
        for (int i = 0; i < Active.Count; i++)
        {
            DoodlikHazard hazard = Active[i];
            if (hazard == null || hazard.drawing == null || !hazard.DamageEnabled)
                continue;

            Vector2 current = hazard.transform.position;
            // Physics bounce callbacks happen before the next hazard Update.
            // Do not replay the hazard's already-checked previous render move.
            Vector2 relativeFrom = from - ((sweepMotion ? hazard.previousPosition : current) + hazard.damageOffset);
            Vector2 relativeTo = to - (current + hazard.damageOffset);
            for (int copy = -1; copy <= 1; copy++)
            {
                Vector2 center = Vector2.right * (copy * screenWidth);
                Rect box = new Rect(center - hazard.damageHalfSize, hazard.damageHalfSize * 2f);
                if (TrapGeometry.SweptBox(relativeFrom, relativeTo, playerHalfSize, box))
                {
                    if (firstHit == null) firstHit = hazard;
                    if (hazard.Type == Kind.Bumblebee) beeContact = hazard.bumblebee;
                    break;
                }
            }
        }
        return firstHit;
    }

    // Thorn platforms keep their collider disabled so an unprotected cat cannot
    // bounce before the damage check. A shield supplies one-way landing support
    // separately, at the same surface as the platform's ordinary collider.
    public static bool FindProtectedLanding(Vector2 previousFeet, Vector2 currentFeet,
        float feetHalfWidth, float screenWidth, out Vector2 contact)
    {
        contact = default;
        if (currentFeet.y >= previousFeet.y || screenWidth <= 0f) return false;
        currentFeet.x = previousFeet.x +
            TrapGeometry.WrappedDelta(previousFeet.x, currentFeet.x, screenWidth);
        float earliest = float.PositiveInfinity;
        for (int i = 0; i < Active.Count; i++)
        {
            DoodlikHazard hazard = Active[i];
            if (hazard == null || hazard.Type != Kind.Thorns || hazard.thornPlatform == null ||
                !hazard.thornPlatform.gameObject.activeInHierarchy) continue;
            BoxCollider2D platform = hazard.thornPlatform;
            Vector2 topLeft = platform.transform.TransformPoint(platform.offset +
                new Vector2(-platform.size.x * 0.5f, platform.size.y * 0.5f));
            Vector2 topRight = platform.transform.TransformPoint(platform.offset +
                platform.size * 0.5f);
            float top = Mathf.Max(topLeft.y, topRight.y);
            // Only a downward crossing from above is a landing. In particular,
            // an early body hit must not make the cat jump off empty air.
            if (previousFeet.y < top || currentFeet.y > top) continue;
            float progress = (previousFeet.y - top) / (previousFeet.y - currentFeet.y);
            if (progress >= earliest) continue;
            float x = Mathf.Lerp(previousFeet.x, currentFeet.x, progress);
            float middle = (topLeft.x + topRight.x) * 0.5f;
            float halfWidth = Mathf.Abs(topRight.x - topLeft.x) * 0.5f;
            float distance = Mathf.Abs(TrapGeometry.WrappedDelta(middle, x, screenWidth));
            if (distance >= halfWidth + feetHalfWidth) continue;
            earliest = progress;
            contact = new Vector2(x, top);
        }
        return !float.IsPositiveInfinity(earliest);
    }
}
