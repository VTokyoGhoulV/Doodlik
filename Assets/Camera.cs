using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform player;
    public float smoothSpeed = 5f;
    [Tooltip("Насколько ниже максимальной высоты котика остаётся центр камеры")]
    public float verticalFollowOffset = 1.4f;

    private float highestY;

    void Start()
    {
        highestY = transform.position.y;
    }

    void LateUpdate()
    {
        if (player == null || !RunFlow.IsPlaying) return;
        if (player.position.y > highestY)
        {
            highestY = player.position.y;

            float targetY = highestY - verticalFollowOffset;
            if (targetY <= transform.position.y)
            {
                return;
            }

            Vector3 targetPosition = new Vector3(
                transform.position.x,
                targetY,
                transform.position.z
            );

            transform.position = targetPosition;
        }
    }
}
