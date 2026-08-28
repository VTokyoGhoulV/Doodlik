using UnityEngine;

public class FlyingBee : MonoBehaviour
{
    public float spinSpeed = 240f;

    private Vector2 velocity;

    public void Launch(Vector2 launchVelocity, float lifetime)
    {
        velocity = launchVelocity;

        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = velocity.x < 0f;
        }

        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        transform.position += (Vector3)(velocity * Time.deltaTime);
        transform.Rotate(0f, 0f, spinSpeed * Mathf.Sign(velocity.x) * Time.deltaTime);
        velocity += Vector2.down * (1.2f * Time.deltaTime);
    }
}
