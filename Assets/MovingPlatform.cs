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
        phase = startPhase;
        fixedY = transform.position.y;
    }

    private void FixedUpdate()
    {
        if (rb == null)
        {
            return;
        }

        float t = (Mathf.Sin(Time.time * speed + phase) + 1f) * 0.5f;
        Vector2 targetPosition = new Vector2(
            Mathf.Lerp(leftX, rightX, t),
            fixedY
        );
        CurrentVelocity = (targetPosition - rb.position) / Time.fixedDeltaTime;
        rb.MovePosition(targetPosition);
    }
}
