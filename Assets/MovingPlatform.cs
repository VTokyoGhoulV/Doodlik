using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class MovingPlatform : MonoBehaviour
{
    public Vector2 CurrentVelocity { get; private set; }

    private Rigidbody2D rb;
    private float leftX;
    private float rightX;
    private float speed;
    private float phase;
    private float fixedY;
    private float motionTime;

    public void Configure(
        float worldMinX,
        float worldMaxX,
        float movementRange,
        float movementSpeed,
        float startPhase
    )
    {
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        leftX = Mathf.Max(worldMinX, transform.position.x - movementRange);
        rightX = Mathf.Min(worldMaxX, transform.position.x + movementRange);
        speed = Mathf.Max(0.1f, movementSpeed);
        // Start at the authored/spawned position, not at a random point along
        // the sine wave. The latter caused a first-frame teleport and impulse.
        float startT = Mathf.InverseLerp(leftX, rightX, transform.position.x);
        phase = Mathf.Asin(Mathf.Clamp(startT * 2f - 1f, -1f, 1f));
        if (Mathf.Sin(startPhase) < 0f)
            phase = Mathf.PI - phase;
        motionTime = 0f;
        fixedY = transform.position.y;
        CurrentVelocity = Vector2.zero;
    }

    public void RemapHorizontalBounds(
        float oldWorldMinX,
        float oldWorldMaxX,
        float newWorldMinX,
        float newWorldMaxX
    )
    {
        float oldWidth = Mathf.Max(0.01f, oldWorldMaxX - oldWorldMinX);
        float newWidth = Mathf.Max(0.01f, newWorldMaxX - newWorldMinX);
        float positionT = Mathf.InverseLerp(
            oldWorldMinX,
            oldWorldMaxX,
            transform.position.x
        );
        float remappedX = Mathf.Lerp(newWorldMinX, newWorldMaxX, positionT);
        float rangeScale = newWidth / oldWidth;
        float halfRange = (rightX - leftX) * 0.5f * rangeScale;

        leftX = Mathf.Max(newWorldMinX, remappedX - halfRange);
        rightX = Mathf.Min(newWorldMaxX, remappedX + halfRange);

        Vector2 remappedPosition = new Vector2(remappedX, fixedY);
        transform.position = remappedPosition;
        if (rb != null)
        {
            rb.position = remappedPosition;
        }
        float positionPhase = Mathf.Asin(Mathf.Clamp(
            Mathf.InverseLerp(leftX, rightX, remappedX) * 2f - 1f, -1f, 1f));
        phase = Mathf.Cos(motionTime + phase) < 0f ? Mathf.PI - positionPhase : positionPhase;
        motionTime = 0f;
        CurrentVelocity = Vector2.zero;
    }

    private void FixedUpdate()
    {
        if (rb == null || !RunFlow.IsPlaying)
        {
            CurrentVelocity = Vector2.zero;
            return;
        }

        // An integrated clock lets slow-time powerups change speed smoothly;
        // multiplying Time.time would instead jump to a different phase.
        motionTime += Time.fixedDeltaTime * speed * RunPowerups.PlatformSpeedMultiplier;
        float t = (Mathf.Sin(motionTime + phase) + 1f) * 0.5f;
        Vector2 targetPosition = new Vector2(
            Mathf.Lerp(leftX, rightX, t),
            fixedY
        );
        CurrentVelocity = (targetPosition - rb.position) / Time.fixedDeltaTime;
        rb.MovePosition(targetPosition);
    }
}
