using UnityEngine;

public class Spawner : MonoBehaviour
{
    public GameObject platformPrefab;

    [Header("Bushes")]
    public GameObject bushPrefab;
    [Range(0f, 1f)] public float bushSpawnChance = 0.3f;
    [Tooltip("Дополнительный отступ куста от края платформы")]
    public float bushHorizontalPadding = 0.05f;
    [Tooltip("Насколько глубоко куст утоплен в платформу")]
    public float bushSinkAmount = 0.08f;

    public int startPlatformCount = 10;
    public float minX = -3f;
    public float maxX = 3f;

    [Header("Dynamic Platform Generation")]
    [Tooltip("Минимальный вертикальный промежуток")]
    public float minimumVerticalGap = 1.35f;
    [Tooltip("Максимальный промежуток в начале забега")]
    public float startingMaximumVerticalGap = 2.4f;
    [Tooltip("Максимальный промежуток на высокой сложности")]
    public float maximumVerticalGap = 3.2f;
    [Tooltip("Допустимая дальность с учётом перехода через край в начале забега")]
    public float startingHorizontalReach = 5f;
    [Tooltip("Допустимая дальность с учётом перехода через край на высокой сложности")]
    public float maximumHorizontalReach = 7.2f;
    [Tooltip("Высота, на которой генератор достигает максимальной сложности")]
    public float difficultyHeight = 120f;
    [Tooltip("Насколько далеко от предыдущей стараемся ставить обычные платформы")]
    [Range(0f, 0.9f)] public float routeDistanceFraction = 0.62f;
    [Header("Platform Challenges")]
    [Tooltip("Вероятность движущейся платформы в начале и на высокой сложности")]
    public Vector2 movingPlatformChance = new Vector2(0.16f, 0.42f);
    [Tooltip("Минимальный и максимальный размах движения")]
    public Vector2 movingPlatformRange = new Vector2(0.6f, 1.2f);
    [Tooltip("Скорость движущихся платформ")]
    public Vector2 movingPlatformSpeed = new Vector2(0.75f, 1.35f);
    [Tooltip("Вероятность узкой платформы в начале и на высокой сложности")]
    public Vector2 narrowPlatformChance = new Vector2(0.14f, 0.34f);
    [Range(0.55f, 1f)] public float narrowPlatformScale = 0.68f;

    private float highestPlatformY = 0f;
    private float lastPlatformX = 0f;
    private int spawnedPlatformCount;
    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;

        SpawnStartPlatforms();
    }

    void Update()
    {
        float cameraTop = mainCamera.transform.position.y + mainCamera.orthographicSize;

        while (highestPlatformY < cameraTop + 5f)
        {
            SpawnPlatform();
        }
    }

    private void SpawnStartPlatforms()
    {
        highestPlatformY = -2f;
        lastPlatformX = 0f;
        spawnedPlatformCount = 0;
        SpawnPlatformAt(new Vector2(lastPlatformX, highestPlatformY), false, false, 0f);

        for (int i = 0; i < startPlatformCount; i++)
        {
            SpawnPlatform();
        }
    }

    private void SpawnPlatform()
    {
        float heightProgress = Mathf.Clamp01(
            Mathf.Max(0f, highestPlatformY) / Mathf.Max(1f, difficultyHeight)
        );
        float currentMaximumVerticalGap = Mathf.Lerp(
            startingMaximumVerticalGap,
            maximumVerticalGap,
            heightProgress
        );
        float randomY = Random.Range(
            minimumVerticalGap,
            currentMaximumVerticalGap
        );
        float verticalDifficulty = Mathf.InverseLerp(
            minimumVerticalGap,
            currentMaximumVerticalGap,
            randomY
        );
        bool currentGapIsHard = verticalDifficulty >= 0.78f;

        float allowedHorizontalStep = Mathf.Lerp(
            startingHorizontalReach,
            maximumHorizontalReach,
            heightProgress
        );

        // Чем выше прыжок, тем меньше времени остаётся на движение в сторону.
        allowedHorizontalStep *= Mathf.Lerp(1f, 0.72f, verticalDifficulty);

        float minimumHorizontalDistance =
            allowedHorizontalStep * routeDistanceFraction;
        bool preferEdgeTransition =
            spawnedPlatformCount >= 5 &&
            spawnedPlatformCount % 5 == 0;
        float randomX = ChooseReachableX(
            minimumHorizontalDistance,
            allowedHorizontalStep,
            preferEdgeTransition
        );

        highestPlatformY += randomY;
        lastPlatformX = randomX;
        spawnedPlatformCount++;

        bool canUseChallengePlatform =
            !currentGapIsHard &&
            spawnedPlatformCount > 3;
        float movingChance = Mathf.Lerp(
            movingPlatformChance.x,
            movingPlatformChance.y,
            heightProgress
        );
        bool makeMoving =
            canUseChallengePlatform &&
            Random.value < movingChance;

        float narrowChance = Mathf.Lerp(
            narrowPlatformChance.x,
            narrowPlatformChance.y,
            heightProgress
        );
        bool makeNarrow =
            canUseChallengePlatform &&
            !makeMoving &&
            verticalDifficulty < 0.65f &&
            Random.value < narrowChance;

        float movementSpeed = Mathf.Lerp(
            movingPlatformSpeed.x,
            movingPlatformSpeed.y,
            heightProgress
        );

        SpawnPlatformAt(
            new Vector2(randomX, highestPlatformY),
            makeMoving,
            makeNarrow,
            movementSpeed
        );
    }

    private float ChooseReachableX(
        float minimumDistance,
        float maximumDistance,
        bool preferEdgeTransition
    )
    {
        const int candidateAttempts = 24;
        float targetDistance = Mathf.Lerp(minimumDistance, maximumDistance, 0.88f);
        float bestCandidate = lastPlatformX;
        float bestDifference = float.MaxValue;
        bool foundReachableCandidate = false;

        for (int i = 0; i < candidateAttempts; i++)
        {
            float candidate = preferEdgeTransition
                ? GetOppositeEdgeCandidate()
                : Random.Range(minX, maxX);
            float wrappedDistance = GetWrappedHorizontalDistance(
                lastPlatformX,
                candidate
            );

            bool isReachable =
                wrappedDistance >= minimumDistance &&
                wrappedDistance <= maximumDistance;
            float difference = Mathf.Abs(wrappedDistance - targetDistance);

            if (isReachable &&
                (!foundReachableCandidate || difference < bestDifference))
            {
                foundReachableCandidate = true;
                bestDifference = difference;
                bestCandidate = candidate;
            }
            else if (!foundReachableCandidate && difference < bestDifference)
            {
                bestDifference = difference;
                bestCandidate = candidate;
            }
        }

        return bestCandidate;
    }

    private float GetOppositeEdgeCandidate()
    {
        float edgeBand = (maxX - minX) * 0.2f;

        if (lastPlatformX > 0.5f)
        {
            return Random.Range(minX, minX + edgeBand);
        }

        if (lastPlatformX < -0.5f)
        {
            return Random.Range(maxX - edgeBand, maxX);
        }

        return Random.value < 0.5f
            ? Random.Range(minX, minX + edgeBand)
            : Random.Range(maxX - edgeBand, maxX);
    }

    private float GetWrappedHorizontalDistance(float fromX, float toX)
    {
        float screenHalfWidth = mainCamera.orthographicSize * mainCamera.aspect;
        float screenWidth = screenHalfWidth * 2f;
        float directDistance = Mathf.Abs(toX - fromX);

        if (screenWidth <= 0f)
        {
            return directDistance;
        }

        directDistance = Mathf.Repeat(directDistance, screenWidth);
        return Mathf.Min(directDistance, screenWidth - directDistance);
    }

    private void SpawnPlatformAt(
        Vector2 position,
        bool makeMoving,
        bool makeNarrow,
        float movementSpeed
    )
    {
        GameObject platform = Instantiate(platformPrefab, position, Quaternion.identity);

        if (makeNarrow)
        {
            Vector3 scale = platform.transform.localScale;
            scale.x *= narrowPlatformScale;
            platform.transform.localScale = scale;
        }

        if (makeMoving)
        {
            MovingPlatform movingPlatform = platform.AddComponent<MovingPlatform>();
            movingPlatform.Configure(
                minX,
                maxX,
                Random.Range(movingPlatformRange.x, movingPlatformRange.y),
                movementSpeed * Random.Range(0.9f, 1.1f),
                Random.value * Mathf.PI * 2f
            );
        }

        if (bushPrefab != null && Random.value < bushSpawnChance)
        {
            SpawnBushOn(platform);
        }
    }

    private void SpawnBushOn(GameObject platform)
    {
        SpriteRenderer platformRenderer = platform.GetComponent<SpriteRenderer>();
        if (platformRenderer == null)
        {
            return;
        }

        Bounds bounds = platformRenderer.bounds;
        float halfBushWidth = 0.4f;
        SpriteRenderer bushRenderer = bushPrefab.GetComponent<SpriteRenderer>();
        if (bushRenderer != null)
        {
            halfBushWidth = bushRenderer.bounds.extents.x;
        }

        float safeHalfWidth = Mathf.Min(
            halfBushWidth,
            bounds.extents.x * 0.4f
        );
        float minBushX = bounds.min.x + safeHalfWidth + bushHorizontalPadding;
        float maxBushX = bounds.max.x - safeHalfWidth - bushHorizontalPadding;
        float bushX = minBushX < maxBushX
            ? Random.Range(minBushX, maxBushX)
            : bounds.center.x;

        GameObject bush = Instantiate(
            bushPrefab,
            new Vector3(bushX, bounds.max.y, platform.transform.position.z - 0.1f),
            Quaternion.identity
        );
        bush.transform.SetParent(platform.transform, true);

        SpriteRenderer spawnedRenderer = bush.GetComponent<SpriteRenderer>();
        if (spawnedRenderer != null)
        {
            spawnedRenderer.sortingOrder = platformRenderer.sortingOrder - 1;
            bush.transform.position += Vector3.up *
                (spawnedRenderer.bounds.extents.y - bushSinkAmount);
        }
    }
}
