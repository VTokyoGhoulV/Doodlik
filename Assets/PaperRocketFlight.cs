using UnityEngine;

// Temporary movement modifier. Required platform reach never depends on it.
// Horizontal input is unchanged during the flight; the short protected exit
// aligns with a real support instead of dropping the cat into a random trap.
[DisallowMultipleComponent]
public sealed class PaperRocketFlight : MonoBehaviour
{
    public const float FlightDuration = 2.2f;
    public const float MaximumLandingTime = 2.8f;
    private enum Phase { Idle, Flying, Landing }
    private Phase phase;
    private Player player;
    private RunPowerups powers;
    private Rigidbody2D body;
    private Collider2D feet, landing;
    private Camera camera;
    private float elapsed, landingElapsed, searchIn;
    private float landingX, landingOffset;
    private readonly float[] candidateXs = new float[5];
    public bool IsActive => phase != Phase.Idle;
    public bool IsFlying => phase == Phase.Flying;
    public float TimeRemaining => IsFlying ? Mathf.Max(0f, FlightDuration - elapsed) : 0f;
    public int EmergencyExits { get; private set; }

    public static PaperRocketFlight Attach(RunPowerups owner)
    {
        PaperRocketFlight flight = owner.GetComponent<PaperRocketFlight>();
        if (flight == null) flight = owner.gameObject.AddComponent<PaperRocketFlight>();
        flight.powers = owner;
        flight.player = owner.GetComponent<Player>();
        flight.body = owner.GetComponent<Rigidbody2D>();
        flight.feet = owner.GetComponent<Collider2D>();
        flight.camera = Camera.main;
        return flight;
    }

    public bool TryLaunch()
    {
        if (IsActive || player == null || player.IsDead || body == null || feet == null || camera == null ||
            !RunFlow.IsPlaying || Time.timeScale <= 0f) return false;
        // Even malformed/empty test worlds cannot enable an immortal flight.
        if (!FindSupport(true, out landingX)) return false;
        elapsed = landingElapsed = searchIn = 0f;
        phase = Phase.Flying;
        body.gravityScale = 0f;
        body.linearVelocity = new Vector2(body.linearVelocity.x, CruiseSpeed);
        player.Challenges?.RecordAssistedFlight();
        player.Challenges?.RecordRocketLaunch();
        return true;
    }

    private float CruiseSpeed => Mathf.Clamp(player.NormalJumpForce * 0.55f, 4.5f, 7.5f);

    // Called from Player.FixedUpdate, after its usual horizontal controller.
    public bool Step(float deltaTime)
    {
        if (!IsActive) return false;
        if (player == null || player.IsDead || !RunFlow.RunActive) { Cancel(); return false; }
        if (!RunFlow.IsPlaying || Time.timeScale <= 0f || deltaTime <= 0f) return true;
        body.gravityScale = 0f;
        if (phase == Phase.Flying)
        {
            elapsed += deltaTime;
            body.linearVelocity = new Vector2(body.linearVelocity.x, CruiseSpeed);
            if (elapsed < FlightDuration) return true;
            phase = Phase.Landing;
            PickLanding();
            landingElapsed = 0f;
            searchIn = 0.15f;
        }

        landingElapsed += deltaTime;
        searchIn -= deltaTime;
        if (!Usable(landing) || landing.bounds.max.y > feet.bounds.min.y + 0.2f * GameplayPreferences.ModelScale)
        {
            if (searchIn <= 0f) { PickLanding(); searchIn = 0.15f; }
        }
        if (landingElapsed >= MaximumLandingTime)
        {
            EmergencyExits++;
            Finish(true);
            return true;
        }
        if (!Usable(landing))
        {
            body.linearVelocity = Vector2.zero;
            return true;
        }
        Bounds bounds = landing.bounds;
        float scale = GameplayPreferences.ModelScale;
        float margin = feet.bounds.extents.x + 0.05f * scale;
        if (bounds.size.x <= margin * 2f) { landing = null; return true; }
        landingX = Mathf.Clamp(bounds.center.x + landingOffset, bounds.min.x + margin, bounds.max.x - margin);
        float width = camera.orthographicSize * camera.aspect * 2f;
        float dx = TrapGeometry.WrappedDelta(feet.bounds.center.x, landingX, width);
        float gap = feet.bounds.min.y - bounds.max.y;
        float speed = player.moveSpeed * GameplayPreferences.HorizontalMovementScale * 1.35f;
        float vx = Mathf.Clamp(dx * 9f, -speed, speed);
        float centeredMargin = Mathf.Max(0.025f * scale, bounds.extents.x - margin);
        // Wait just above the support until horizontal alignment. This is
        // velocity-based travel, not a teleport through the screen or a trap.
        float vy = Mathf.Abs(dx) > centeredMargin ? (gap > 1.2f * scale ? -0.6f : 0f) :
            -Mathf.Clamp(gap * 6f, 1.5f, 7f);
        body.linearVelocity = new Vector2(vx, vy);
        return true;
    }

    public bool TryLand(Collider2D surface)
    {
        if (phase != Phase.Landing || !Usable(surface) ||
            !ClearAt(feet.bounds.center.x, surface.bounds.max.y, FindObjectsByType<DoodlikHazard>())) return false;
        if (Mathf.Abs(feet.bounds.min.y - surface.bounds.max.y) > 0.25f * GameplayPreferences.ModelScale) return false;
        Finish(false);
        return true;
    }

    private bool Usable(Collider2D support) => support != null && support.enabled && !support.isTrigger &&
        support.gameObject.activeInHierarchy && support.CompareTag("Platform");

    private void PickLanding()
    {
        landing = FindSupport(false, out landingX);
        landingOffset = landing != null ? landingX - landing.bounds.center.x : 0f;
    }

    private Collider2D FindSupport(bool launchCheck, out float selectedX)
    {
        selectedX = 0f;
        if (feet == null || camera == null) return null;
        float scale = GameplayPreferences.ModelScale;
        float bottom = camera.transform.position.y - camera.orthographicSize + 0.45f * scale;
        float topLimit = feet.bounds.min.y + (launchCheck ? 3f * scale : -0.02f * scale);
        float halfWidth = camera.orthographicSize * camera.aspect;
        float width = halfWidth * 2f;
        float margin = feet.bounds.extents.x + 0.08f * scale;
        DoodlikHazard[] hazards = FindObjectsByType<DoodlikHazard>();
        Collider2D best = null;
        float bestCost = float.PositiveInfinity;
        foreach (Collider2D support in FindObjectsByType<Collider2D>())
        {
            if (!Usable(support)) continue;
            Bounds bounds = support.bounds;
            if (bounds.max.y < bottom || bounds.max.y > topLimit || bounds.size.x < margin * 2f) continue;
            float left = Mathf.Max(bounds.min.x + margin, camera.transform.position.x - halfWidth + margin);
            float right = Mathf.Min(bounds.max.x - margin, camera.transform.position.x + halfWidth - margin);
            if (left > right) continue;
            float nearest = Mathf.Clamp(feet.bounds.center.x, left, right);
            candidateXs[0] = nearest; candidateXs[1] = (left + right) * 0.5f;
            candidateXs[2] = left; candidateXs[3] = right;
            candidateXs[4] = Mathf.Lerp(left, right, 0.25f);
            for (int candidate = 0; candidate < candidateXs.Length; candidate++)
            {
                float x = candidateXs[candidate];
                if (!ClearAt(x, bounds.max.y, hazards)) continue;
                float dx = Mathf.Abs(TrapGeometry.WrappedDelta(feet.bounds.center.x, x, width));
                float cost = dx + Mathf.Abs(feet.bounds.min.y - bounds.max.y) * 0.22f;
                if (cost >= bestCost) continue;
                bestCost = cost; best = support; selectedX = x;
            }
        }
        return best;
    }

    private bool ClearAt(float x, float top, DoodlikHazard[] hazards)
    {
        float scale = GameplayPreferences.ModelScale;
        float width = camera.orthographicSize * camera.aspect * 2f;
        float margin = feet.bounds.extents.x + 0.08f * scale;
        SpriteRenderer art = player.GetComponent<SpriteRenderer>();
        float bodyHeight = art != null ? art.bounds.size.y : feet.bounds.size.y * 3f;
        Rect clearance = new Rect(x - margin, top + 0.02f * scale, margin * 2f, bodyHeight + 0.2f * scale);
        foreach (DoodlikHazard hazard in hazards)
        {
            if (hazard == null) continue;
            Rect patrol = hazard.PatrolBounds.width > 0f ? hazard.PatrolBounds : hazard.WarningBounds;
            for (int copy = -1; copy <= 1; copy++)
            {
                Rect shifted = patrol; shifted.x += copy * width;
                if (clearance.Overlaps(shifted)) return false;
            }
        }
        return true;
    }

    private void Finish(bool emergency)
    {
        player.Challenges?.RecordRocketEnd(!emergency);
        phase = Phase.Idle;
        landing = null;
        body.gravityScale = player.risingGravityScale;
        powers.GrantRocketExitProtection(1.25f);
        // A real contact gets its ordinary bounce from Player. Only a broken
        // world/removed target needs a finite emergency normal-jump handoff.
        if (emergency) body.linearVelocity = new Vector2(0f, player.NormalJumpForce);
    }

    public void Cancel()
    {
        if (IsActive) player?.Challenges?.RecordRocketEnd(false);
        phase = Phase.Idle;
        landing = null;
        if (body != null && player != null) body.gravityScale = player.risingGravityScale;
    }
    private void OnDisable() => Cancel();
}
