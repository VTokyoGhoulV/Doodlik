using UnityEngine;

public class ScrollingBackground : MonoBehaviour
{
    [SerializeField] private float upwardSpeed = 0.18f;
    [SerializeField] private Color tint = new Color(0.7f, 0.74f, 0.76f, 1f);
    [SerializeField] private float seamOverlap = 0.08f;

    private readonly SpriteRenderer[] tiles = new SpriteRenderer[3];
    private Camera mainCamera;
    private float tileHeight;
    private float tileSpacing;
    private float scrollOffset;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForScene()
    {
        if (FindAnyObjectByType<ScrollingBackground>() != null)
        {
            return;
        }

        GameObject background = new GameObject("Scrolling Background");
        DontDestroyOnLoad(background);
        background.AddComponent<ScrollingBackground>();
    }

    private void Start()
    {
        mainCamera = Camera.main;
        Sprite sprite = Resources.Load<Sprite>("DoodlikBackground");
        if (mainCamera == null || sprite == null)
        {
            enabled = false;
            return;
        }

        float viewHeight = mainCamera.orthographicSize * 2f;
        float viewWidth = viewHeight * mainCamera.aspect;
        float scale = Mathf.Max(
            viewWidth / sprite.bounds.size.x,
            viewHeight / sprite.bounds.size.y
        ) * 1.08f;
        tileHeight = sprite.bounds.size.y * scale;
        tileSpacing = tileHeight - seamOverlap;

        for (int i = 0; i < tiles.Length; i++)
        {
            GameObject tile = new GameObject($"Background Tile {i + 1}");
            tile.transform.SetParent(transform, false);
            tile.transform.localScale = Vector3.one * scale;

            SpriteRenderer renderer = tile.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = tint;
            renderer.sortingOrder = -100;
            tiles[i] = renderer;
        }

        UpdateTilePositions();
    }

    private void LateUpdate()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null || tileSpacing <= 0f)
        {
            return;
        }

        transform.position = new Vector3(
            mainCamera.transform.position.x,
            mainCamera.transform.position.y,
            5f
        );

        scrollOffset = Mathf.Repeat(
            scrollOffset + upwardSpeed * Time.deltaTime,
            tileSpacing
        );
        UpdateTilePositions();
    }

    private void UpdateTilePositions()
    {
        for (int i = 0; i < tiles.Length; i++)
        {
            if (tiles[i] != null)
            {
                tiles[i].transform.localPosition = new Vector3(
                    0f,
                    scrollOffset + (i - 1) * tileSpacing,
                    0f
                );
            }
        }
    }
}
