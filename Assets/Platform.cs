using UnityEngine;

public class Platform : MonoBehaviour
{
    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;
    }

    void Update()
    {
        float destroyY = mainCamera.transform.position.y - mainCamera.orthographicSize - 2f;

        if (transform.position.y < destroyY)
        {
            Destroy(gameObject);
        }
    }
}