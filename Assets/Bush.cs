using UnityEngine;

public class Bush : MonoBehaviour
{
    [Header("Bees")]
    public GameObject scaredBeePrefab;
    [Min(1)] public int minBeeCount = 4;
    [Min(1)] public int maxBeeCount = 7;
    public float minBeeSpeed = 3.5f;
    public float maxBeeSpeed = 6.5f;
    public float beeLifetime = 2.5f;

    private bool wasTriggered;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (wasTriggered)
        {
            return;
        }

        Player player = other.GetComponent<Player>();
        if (player == null)
        {
            return;
        }

        wasTriggered = true;
        ReleaseBees();
        player.BounceFromBush();
        Destroy(gameObject);
    }

    private void ReleaseBees()
    {
        if (scaredBeePrefab == null)
        {
            return;
        }

        int beeCount = Random.Range(minBeeCount, maxBeeCount + 1);
        for (int i = 0; i < beeCount; i++)
        {
            GameObject bee = Instantiate(scaredBeePrefab, transform.position, Quaternion.identity);
            Vector2 direction = Random.insideUnitCircle.normalized;
            direction.y = Mathf.Abs(direction.y) + 0.25f;
            direction.Normalize();

            FlyingBee flyingBee = bee.GetComponent<FlyingBee>();
            if (flyingBee != null)
            {
                flyingBee.Launch(direction * Random.Range(minBeeSpeed, maxBeeSpeed), beeLifetime);
            }
        }
    }
}
