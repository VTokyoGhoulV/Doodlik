using System.Collections.Generic;
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
    [Range(0f, 0.35f)]
    [Tooltip("Дополнительный простор между рядами; ограничивается реальной высотой прыжка")]
    public float extraVerticalSpacing = 0.15f;
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

    [Header("Traps / Ловушки")]
    public TrapSettings traps = new TrapSettings();

    private float highestPlatformY = 0f;
    private float lastPlatformX = 0f;
    private int spawnedPlatformCount;
    private Camera mainCamera;
    private Player playerMovement;
    private float currentMinX;
    private float currentMaxX;
    private float lastCameraAspect;
    private float lastPlatformMovementRange;
    private bool lastPlatformWasNarrow;
    private int lastChallengeTier;
    private int lastWideJumpTier;
    private int lastPrecisionJumpTier;
    private int lastMainThornPatchTier;
    private int thornPatchScoreBand = -1;
    private int thornPatchesInBand;
    private int lastApproachDirection;
    private int sweepDirection;
    private bool deferredBush;
    private struct RouteNode
    {
        public float x, y, movementRange;
        public RouteNode(float x, float y, float movementRange)
        { this.x = x; this.y = y; this.movementRange = movementRange; }
    }
    private readonly List<RouteNode> currentTier = new List<RouteNode>(1);
    private HazardSpawner hazardSpawner;
    private PickupSpawnSchedule pickupSchedule;
    private AirCoinSpawner airCoins;
    private ExpeditionFindSpawner expeditionFinds;
    public ExpeditionFindSpawner ExpeditionFinds => expeditionFinds;
    private ExpeditionEnemySpawner expeditionEnemies;
    public ExpeditionEnemySpawner ExpeditionEnemies => expeditionEnemies;
    private DifficultyGenerationProfile balance = new DifficultyGenerationProfile(GameDifficulty.Normal);
    public GameDifficulty RunDifficulty => balance.Difficulty;
    // The opening route is certified for this viewport. A phone can finish
    // rotating its surface after Start has already generated that route.
    public float PreparedAspect { get; private set; }
    private float preparedOrthographicSize;
    public bool DraftMatchesCurrentSurface => PreparedAspect > 0f &&
        mainCamera != null && Mathf.Abs(mainCamera.aspect - PreparedAspect) <= 0.001f &&
        Mathf.Abs(mainCamera.orthographicSize - preparedOrthographicSize) <= 0.001f &&
        RunDifficulty == GameplayPreferences.Difficulty;

    private const float PlatformEdgePadding = 0.15f;
    private const float AspectChangeThreshold = 0.01f;
    private const int ThornPatchStartScore = 500;
    private const int ThornPatchScoreBandSize = 200;
    private const int MaxThornPatchesPerBand = 11;
#if UNITY_EDITOR
    // Fixed only by editor validation; normal runs keep independent pickup clocks.
    public static uint? ValidationPickupSeed;
#endif

    void Start()
    {
        balance = new DifficultyGenerationProfile(GameplayPreferences.Difficulty);
        mainCamera = Camera.main;
        playerMovement = FindFirstObjectByType<Player>();
        if (mainCamera == null || playerMovement == null || platformPrefab == null)
        {
            Debug.LogError("Spawner needs a main camera, player and platform prefab.", this);
            enabled = false;
            return;
        }
        WorldZones.ConfigureRun(playerMovement.transform.position.y, playerMovement.pointsPerUnit);
        UpdateHorizontalBounds(false);

        if (traps.enabled && playerMovement != null && platformPrefab != null)
            hazardSpawner = new HazardSpawner(mainCamera, playerMovement, platformPrefab, traps, RunDifficulty);

        SpawnStartPlatforms();
        preparedOrthographicSize = mainCamera.orthographicSize;
        PreparedAspect = mainCamera.aspect;
    }

    void Update()
    {
        if (!RunFlow.IsPlaying || mainCamera == null)
            return;

        if (Mathf.Abs(mainCamera.aspect - lastCameraAspect) >
            AspectChangeThreshold)
        {
            UpdateHorizontalBounds(true);
        }

        // Hazard planning does not advance while paused. Generating route rows
        // here would consume their candidate indices without placing traps.
        if (Time.timeScale <= 0f)
            return;

        float cameraTop = mainCamera.transform.position.y + mainCamera.orthographicSize;

        float lookAhead = hazardSpawner != null ? hazardSpawner.LookAhead : 5f;
        while (highestPlatformY < cameraTop + lookAhead)
        {
            SpawnPlatform();
        }
        airCoins?.Advance(highestPlatformY, hazardSpawner != null);
        expeditionFinds?.Advance(highestPlatformY, hazardSpawner != null,
            hazardSpawner == null || !hazardSpawner.HasPendingPlanning);
        expeditionEnemies?.Advance(highestPlatformY, hazardSpawner != null,
            hazardSpawner == null || !hazardSpawner.HasPendingPlanning);
    }

    private void SpawnStartPlatforms()
    {
        uint pickupSeed = unchecked((uint)System.Environment.TickCount ^ (uint)GetHashCode());
#if UNITY_EDITOR
        if (ValidationPickupSeed.HasValue) pickupSeed = ValidationPickupSeed.Value;
#endif
        pickupSchedule = new PickupSpawnSchedule(pickupSeed);
        airCoins = new AirCoinSpawner(playerMovement, mainCamera, pickupSchedule, movingPlatformRange.y);
        expeditionFinds = GameplayPreferences.RunKind == GameRunKind.Expedition
            ? new ExpeditionFindSpawner(playerMovement, mainCamera, movingPlatformRange.y, pickupSeed)
            : null;
        expeditionEnemies = GameplayPreferences.RunKind == GameRunKind.Expedition
            ? new ExpeditionEnemySpawner(playerMovement, mainCamera, movingPlatformRange.y, pickupSeed)
            : null;
        highestPlatformY = -2f;
        lastPlatformX = 0f;
        spawnedPlatformCount = 0;
        lastPlatformMovementRange = 0f;
        lastPlatformWasNarrow = false;
        lastChallengeTier = 0;
        lastWideJumpTier = 0;
        lastPrecisionJumpTier = 0;
        lastMainThornPatchTier = 0;
        thornPatchScoreBand = -1;
        thornPatchesInBand = 0;
        lastApproachDirection = 0;
        sweepDirection = Random.value < 0.5f ? -1 : 1;
        deferredBush = false;
        currentTier.Clear();
        currentTier.Add(new RouteNode(0f, -2f, 0f));
        SpawnPlatformAt(
            new Vector2(lastPlatformX, highestPlatformY),
            false,
            false,
            0f,
            0f
        );

        for (int i = 0; i < startPlatformCount; i++)
        {
            SpawnPlatform();
        }
    }

    private void SpawnPlatform()
    {
        WorldZones.Zone zone = WorldZones.AtHeight(highestPlatformY);
        float heightProgress = Mathf.Clamp01(
            Mathf.Max(0f, highestPlatformY) / Mathf.Max(1f, difficultyHeight)
        );
        float currentMaximumVerticalGap = Mathf.Lerp(
            startingMaximumVerticalGap,
            maximumVerticalGap,
            heightProgress
        ) * balance.VerticalGap;
        float jumpApex = JumpReachability.Simulate(playerMovement, 0f,
            GameplayPreferences.UseInertia, GameplayPreferences.HorizontalMovementScale).Apex;
        // Inspector tuning must never produce a vertically impossible jump.
        float safeMinimumGap = Mathf.Min(minimumVerticalGap + extraVerticalSpacing,
            Mathf.Max(0.2f, jumpApex - 0.3f)) * balance.VerticalGap;
        if (lastPlatformMovementRange > 0f)
        {
            // A moving platform can launch the cat with velocity opposite to
            // the intended direction. Its full patrol sometimes consumes all
            // horizontal reach at the usual minimum height. Reduce this one
            // gap instead of inserting a cramped rescue platform afterward.
            float requiredReach = lastPlatformMovementRange +
                0.18f * GameplayPreferences.ModelScale;
            if (CalculateSafeHorizontalReach(safeMinimumGap) < requiredReach)
            {
                float low = Mathf.Min(safeMinimumGap,
                    0.75f * GameplayPreferences.ModelScale);
                float high = safeMinimumGap;
                for (int i = 0; i < 12; i++)
                {
                    float middle = (low + high) * 0.5f;
                    if (CalculateSafeHorizontalReach(middle) >= requiredReach)
                        low = middle;
                    else high = middle;
                }
                safeMinimumGap = low;
            }
        }
        // A stronger jump should create lateral options, not simply stretch
        // every ordinary row to the new apex and rebuild a vertical ladder.
        float ordinaryGapLimit = Mathf.Min(jumpApex - 0.3f, jumpApex * 0.79f);
        currentMaximumVerticalGap = Mathf.Clamp(currentMaximumVerticalGap,
            safeMinimumGap, Mathf.Max(safeMinimumGap, ordinaryGapLimit));
#if UNITY_ANDROID || UNITY_IOS
        // Touch input and uneven phone frame pacing need a small safety margin.
        // Ordinary gaps stay unchanged; only the most extreme gaps are reduced.
        currentMaximumVerticalGap = Mathf.Lerp(
            safeMinimumGap,
            currentMaximumVerticalGap,
            0.9f
        );
#endif
        // После платформы, которая сама требует точного приземления, не
        // создаём сразу ещё и вертикально предельный прыжок.
        if (lastPlatformMovementRange > 0f || lastPlatformWasNarrow)
        {
            currentMaximumVerticalGap = Mathf.Lerp(
                safeMinimumGap,
                currentMaximumVerticalGap,
                0.84f
            );
        }
        // Ordinary jumps stay varied; precision jumps periodically make the
        // one required route demanding without spawning a second staircase.
        float verticalRoll = Mathf.Pow(Random.value, 0.7f);
        float randomY = Mathf.Lerp(
            safeMinimumGap,
            currentMaximumVerticalGap,
            verticalRoll
        );
        float verticalDifficulty = Mathf.InverseLerp(
            safeMinimumGap,
            currentMaximumVerticalGap,
            randomY
        );
        bool bonusLanding = pickupSchedule != null &&
            pickupSchedule.BonusScheduledFor(spawnedPlatformCount + 1);
        if (bonusLanding)
        {
            // Rare airborne rewards need an actual steerable detour. Leave
            // lateral and vertical slack on the scheduled route; the reward
            // itself is only moved off the platform after a safe witness.
            randomY = Mathf.Lerp(safeMinimumGap, currentMaximumVerticalGap,
                0.28f);
            verticalDifficulty = Mathf.InverseLerp(safeMinimumGap,
                currentMaximumVerticalGap, randomY);
        }
        bool canMakePrecisionJump = spawnedPlatformCount >= 4 &&
            lastPlatformMovementRange <= 0f &&
            !lastPlatformWasNarrow && !bonusLanding;
        bool portraitRoute = GameplayPreferences.IsPortrait;
        int tiersSincePrecision = spawnedPlatformCount - lastPrecisionJumpTier;
        bool precisionJump = canMakePrecisionJump &&
            (tiersSincePrecision >= balance.PrecisionMaximumInterval(portraitRoute) ||
             tiersSincePrecision >= balance.PrecisionMinimumInterval(portraitRoute) &&
             Random.value < balance.PrecisionChance(portraitRoute));
        if (precisionJump)
        {
            // A high landing shortens steering time, while the target is
            // placed close to the measured horizontal limit below. On a
            // portrait screen distance alone cannot be demanding: its width
            // caps the route. Both controls therefore get a high landing,
            // with more turning time reserved for inertia.
            if (portraitRoute)
            {
                float ceiling = GameplayPreferences.UseInertia
                    ? Mathf.Min(jumpApex - 0.32f, jumpApex * 0.87f)
                    : Mathf.Min(jumpApex - 0.23f, jumpApex * 0.91f);
                ceiling = Mathf.Max(safeMinimumGap, ceiling * balance.VerticalGap);
                randomY = Random.Range(Mathf.Max(safeMinimumGap, ceiling - 0.18f),
                    ceiling);
            }
            else
                randomY = Mathf.Lerp(safeMinimumGap, currentMaximumVerticalGap,
                    Random.Range(0.7f, 0.84f));
            verticalDifficulty = Mathf.InverseLerp(safeMinimumGap,
                currentMaximumVerticalGap, randomY);
        }
        float ordinaryGapBeforeWideJump = randomY;
        int tiersSinceWideJump = spawnedPlatformCount - lastWideJumpTier;
        bool wideJump =
            !portraitRoute &&
            !precisionJump &&
            spawnedPlatformCount >= 4 &&
            lastPlatformMovementRange <= 0f &&
            !lastPlatformWasNarrow &&
            !bonusLanding &&
            lastApproachDirection != 0 &&
            tiersSinceWideJump >= balance.WideMinimumInterval &&
            (tiersSinceWideJump >= balance.WideMaximumInterval || Random.value < balance.WideChance);
        if (wideJump)
        {
            // Trade a little height for a much longer sideways flight. The
            // next platform is still above the cat, never a drop-through.
            randomY = Mathf.Min(safeMinimumGap - 0.16f,
                Random.Range(0.85f, 1.2f));
            randomY = Mathf.Max(0.85f, randomY);
            verticalDifficulty = 0f;
        }
        bool currentGapIsHard = verticalDifficulty >= 0.72f;

        bool canUseChallengePlatform =
            !precisionJump &&
            !wideJump &&
            !currentGapIsHard &&
            !bonusLanding &&
            spawnedPlatformCount > 3 &&
            lastPlatformMovementRange <= 0f &&
            !lastPlatformWasNarrow;
        float movingChance = Mathf.Lerp(
            movingPlatformChance.x,
            movingPlatformChance.y,
            heightProgress
        );
        movingChance *= WorldZones.IsCloudZone(zone) ? 1.3f
            : zone == WorldZones.Zone.Meadow ? 1.2f : 1f;
        movingChance *= balance.MovingChance;
        // Early meadows should already ask for steering. A short dry spell
        // guarantees a skill landing without putting challenges back-to-back.
        bool challengeDue = spawnedPlatformCount - lastChallengeTier >= balance.ChallengeInterval;
        bool makeMoving =
            canUseChallengePlatform &&
            (Random.value < movingChance ||
             challengeDue && verticalDifficulty >= 0.6f);

        float narrowChance = Mathf.Lerp(
            narrowPlatformChance.x,
            narrowPlatformChance.y,
            heightProgress
        ) * balance.NarrowChance;
        bool makeNarrow =
            canUseChallengePlatform &&
            !makeMoving &&
            verticalDifficulty < 0.6f &&
            (Random.value < narrowChance * 1.5f || challengeDue);

        float movementSpeed = Mathf.Lerp(
            movingPlatformSpeed.x,
            movingPlatformSpeed.y,
            heightProgress
        ) * balance.MovingSpeed;
        float selectedMovementRange = makeMoving
            ? Random.Range(movingPlatformRange.x, movingPlatformRange.y) *
                GameplayPreferences.ModelScale * (WorldZones.IsCloudZone(zone) ? 0.85f : 1f)
            : 0f;

        // A moving source and destination can separate during the jump. If
        // their combined patrol already consumes the available horizontal
        // travel, lower this row before selecting its x coordinate.
        float movementUncertainty = lastPlatformMovementRange + selectedMovementRange;
        float minimumReach = movementUncertainty + 0.12f * GameplayPreferences.ModelScale;
        if (CalculateSafeHorizontalReach(randomY) < minimumReach)
        {
            float low = safeMinimumGap;
            float high = randomY;
            for (int i = 0; i < 10; i++)
            {
                float middle = (low + high) * 0.5f;
                if (CalculateSafeHorizontalReach(middle) >= minimumReach) low = middle;
                else high = middle;
            }
            randomY = low;
            verticalDifficulty = Mathf.InverseLerp(safeMinimumGap,
                currentMaximumVerticalGap, randomY);
            currentGapIsHard = verticalDifficulty >= 0.72f;
        }

        float allowedHorizontalStep = Mathf.Lerp(
            startingHorizontalReach,
            maximumHorizontalReach,
            heightProgress
        );

        // Чем выше прыжок, тем меньше времени остаётся на движение в сторону.
        if (!wideJump && !precisionJump)
            allowedHorizontalStep *= Mathf.Lerp(1f, 0.72f, verticalDifficulty);

        // Ordinary and precision jumps remain reachable even after a turn.
        // Only the scheduled wide jump may use incoming sideways momentum.
        float physicalReach = CalculateSafeHorizontalReach(randomY);
        allowedHorizontalStep = wideJump ? CalculatePreparedHorizontalReach(randomY) :
            precisionJump ? physicalReach :
            Mathf.Min(allowedHorizontalStep, physicalReach);

        // Движущаяся исходная или целевая платформа может уйти от своей
        // точки генерации до момента приземления. Оставляем под это запас.
        float movingPlatformUncertainty = movementUncertainty;
        // Account for the entire patrol of both platforms. A minimum distance
        // floor here used to silently override the actual reachability limit.
        allowedHorizontalStep = Mathf.Max(
            0f,
            allowedHorizontalStep - movingPlatformUncertainty
        );
        if (bonusLanding)
            allowedHorizontalStep *= 0.5f;

        // На узком портретном экране половина окружности экрана является
        // максимально возможной дистанцией с учётом перехода через край.
        float screenHalfWidth = mainCamera.orthographicSize * mainCamera.aspect;
        allowedHorizontalStep = Mathf.Min(
            allowedHorizontalStep,
            screenHalfWidth * 0.92f
        );
        if (precisionJump || wideJump)
            allowedHorizontalStep = Mathf.Min(allowedHorizontalStep,
                MaximumAvailableWrappedDistance());

        float minimumHorizontalDistance = allowedHorizontalStep *
            (precisionJump ? 0.87f : currentGapIsHard ? 0.54f : routeDistanceFraction);
        // A normal jump should demand at least a small steering decision.
        // Otherwise the cat can land on the next platform without touching
        // the controls, particularly in inertia mode after a tall jump.
        float stationaryLandingWidth = StationaryLandingWidth();
        if (allowedHorizontalStep >= stationaryLandingWidth)
            minimumHorizontalDistance = Mathf.Max(minimumHorizontalDistance,
                stationaryLandingWidth);
        if (wideJump)
            minimumHorizontalDistance = Mathf.Min(allowedHorizontalStep,
                Mathf.Max(allowedHorizontalStep * 0.74f,
                    stationaryLandingWidth + 0.45f * GameplayPreferences.ModelScale));
        UpdateSweepDirection();
        bool preferEdgeTransition =
            (wideJump && Mathf.Abs(lastPlatformX) > screenHalfWidth * 0.55f) ||
            (spawnedPlatformCount >= 5 &&
             spawnedPlatformCount % 5 == 0 &&
             verticalDifficulty < 0.58f &&
             lastPlatformMovementRange <= 0f &&
             !makeMoving);
        float randomX = ChooseReachableX(
            minimumHorizontalDistance,
            allowedHorizontalStep,
            preferEdgeTransition,
            precisionJump ? 0.97f : wideJump ? 0.88f : currentGapIsHard
                ? 0.68f
                : (GameplayPreferences.UseInertia ? 0.9f : 0.94f)
        );
        if (precisionJump && lastApproachDirection != 0 &&
            TryChoosePrecisionReversal(minimumHorizontalDistance,
                allowedHorizontalStep, out float reversalX))
            randomX = reversalX;
        if (wideJump)
        {
            // The cat keeps its horizontal velocity when it bounces. A long
            // leap is fair only when the previous landing is approached in
            // the same direction; do not spend the prepared reach on a turn.
            if (TryChooseDirectionalLanding(minimumHorizontalDistance,
                allowedHorizontalStep, lastApproachDirection, out float longX))
                randomX = longX;
            else
            {
                // Narrow bounds can leave no landing in that direction.
                // Restore the ordinary height too: keeping the deliberately
                // low wide-jump gap would create a free step here.
                wideJump = false;
                randomY = ordinaryGapBeforeWideJump;
                verticalDifficulty = Mathf.InverseLerp(safeMinimumGap,
                    currentMaximumVerticalGap, randomY);
                currentGapIsHard = verticalDifficulty >= 0.72f;
                allowedHorizontalStep = Mathf.Lerp(startingHorizontalReach,
                    maximumHorizontalReach, heightProgress) *
                    Mathf.Lerp(1f, 0.72f, verticalDifficulty);
                allowedHorizontalStep = Mathf.Min(allowedHorizontalStep,
                    CalculateSafeHorizontalReach(randomY));
                allowedHorizontalStep = Mathf.Min(allowedHorizontalStep,
                    screenHalfWidth * 0.92f);
                minimumHorizontalDistance = allowedHorizontalStep *
                    (currentGapIsHard ? 0.54f : routeDistanceFraction);
                if (allowedHorizontalStep >= stationaryLandingWidth)
                    minimumHorizontalDistance = Mathf.Max(
                        minimumHorizontalDistance, stationaryLandingWidth);
                randomX = ChooseReachableX(minimumHorizontalDistance,
                    allowedHorizontalStep, false, 0.9f);
            }
        }
        bool precisionSucceeded = precisionJump &&
            GetWrappedHorizontalDistance(lastPlatformX, randomX) >=
                allowedHorizontalStep * 0.82f &&
            GetWrappedHorizontalDistance(lastPlatformX, randomX) >=
                stationaryLandingWidth * 1.05f;
        Vector2 routeStart = new Vector2(lastPlatformX, highestPlatformY);
        float approachTravel = TrapGeometry.WrappedDelta(lastPlatformX, randomX,
            mainCamera.orthographicSize * mainCamera.aspect * 2f);
        if (Mathf.Abs(approachTravel) > 0.05f * GameplayPreferences.ModelScale)
            lastApproachDirection = approachTravel > 0f ? 1 : -1;
        highestPlatformY += randomY;
        lastPlatformX = randomX;
        spawnedPlatformCount++;
        if (makeMoving || makeNarrow)
            lastChallengeTier = spawnedPlatformCount;
        if (wideJump)
            lastWideJumpTier = spawnedPlatformCount;
        if (precisionSucceeded)
            lastPrecisionJumpTier = spawnedPlatformCount;

        int currentScore = WorldZones.ScoreAtHeight(highestPlatformY);
        int currentPatchBand = currentScore / ThornPatchScoreBandSize;
        if (currentPatchBand != thornPatchScoreBand)
        {
            thornPatchScoreBand = currentPatchBand;
            thornPatchesInBand = 0;
        }
        float thornPatchChance = GameplayPreferences.IsPortrait && GameplayPreferences.UseInertia
            ? Mathf.Lerp(0.68f, 0.75f,
                Mathf.Clamp01((currentScore - 1000f) /
                    Mathf.Max(1f, WorldZones.GoalScore - 1000f)))
            : GameplayPreferences.IsPortrait ? 0.12f
            : !GameplayPreferences.UseInertia
                ? Mathf.Lerp(0.12f, 0.22f,
                    Mathf.Clamp01((currentScore - 1000f) /
                        Mathf.Max(1f, WorldZones.GoalScore - 1000f)))
            : GameplayPreferences.IsDesktopBuild
                ? 0.08f : 0.12f;
        thornPatchChance *= balance.ThornPatchChance;
        bool mainThornPatch = hazardSpawner != null &&
            currentScore >= ThornPatchStartScore &&
            !wideJump &&
            !precisionJump &&
            !makeMoving && !makeNarrow && !bonusLanding &&
            lastPlatformMovementRange <= 0f && !lastPlatformWasNarrow &&
            !(WorldZones.IsCloudZone(zone) && spawnedPlatformCount % 4 == 0) &&
            (Random.value < thornPatchChance ||
             spawnedPlatformCount - lastMainThornPatchTier >=
                 balance.ThornPatchInterval(GameplayPreferences.IsPortrait)) &&
            thornPatchesInBand < MaxThornPatchesPerBand;

        GameObject routePlatform = SpawnPlatformAt(
            new Vector2(randomX, highestPlatformY),
            makeMoving,
            makeNarrow,
            movementSpeed,
            selectedMovementRange,
            true,
            !mainThornPatch && !precisionJump
        );
        if (mainThornPatch && AddThornPatch(routePlatform, routeStart))
        {
            lastMainThornPatchTier = spawnedPlatformCount;
            thornPatchesInBand++;
        }
        if (WorldZones.IsCloudZone(zone) && !wideJump && !precisionJump &&
            !makeMoving && !makeNarrow &&
            !currentGapIsHard && spawnedPlatformCount % 4 == 0)
            routePlatform.AddComponent<CrumblingPlatform>();

        currentTier.Clear();
        currentTier.Add(new RouteNode(randomX, highestPlatformY,
            selectedMovementRange));
        hazardSpawner?.AddRoute(routeStart, new Vector2(randomX, highestPlatformY),
            lastPlatformMovementRange, selectedMovementRange,
            // On static precision jumps, verify actual trajectories instead
            // of reserving the entire screen. Scheduled wide jumps in inertia
            // mode require prepared momentum in the travel direction; prove
            // an escape from all three positions with that incoming velocity.
            allowFlyingDetour: (!wideJump && !precisionJump ||
                    GameplayPreferences.UseInertia) &&
                !lastPlatformWasNarrow && !makeNarrow,
            preparedDirection: wideJump && GameplayPreferences.UseInertia
                ? lastApproachDirection : 0);
        // New route hazards are placed/planned before offering an aerial bonus.
        // The bonus then reserves its certified detour against later hazards.
        RunPickup bonus = RunPickup.SpawnBonusForPlatform(routePlatform,
            spawnedPlatformCount, pickupSchedule);
        airCoins?.TryPlaceScheduledBonus(bonus, spawnedPlatformCount);
        lastPlatformMovementRange = selectedMovementRange;
        lastPlatformWasNarrow = makeNarrow;
    }

    private float ChooseReachableX(
        float minimumDistance,
        float maximumDistance,
        bool preferEdgeTransition,
        float targetPreference
    )
    {
        const int candidateAttempts = 24;
        maximumDistance = Mathf.Max(0f, maximumDistance);
        minimumDistance = Mathf.Clamp(minimumDistance, 0f, maximumDistance);
        float targetDistance = Mathf.Lerp(
            minimumDistance,
            maximumDistance,
            targetPreference
        );
        float bestCandidate = lastPlatformX;
        float bestDifference = float.MaxValue;
        bool foundReachableCandidate = false;

        for (int i = 0; i < candidateAttempts; i++)
        {
            float candidate = preferEdgeTransition
                ? GetOppositeEdgeCandidate()
                : Random.Range(currentMinX, currentMaxX);
            float wrappedDistance = GetWrappedHorizontalDistance(
                lastPlatformX,
                candidate
            );

            bool isReachable =
                wrappedDistance >= minimumDistance &&
                wrappedDistance <= maximumDistance;
            float difference = Mathf.Abs(wrappedDistance - targetDistance) +
                SweepPenalty(candidate, preferEdgeTransition, maximumDistance);

            if (isReachable &&
                (!foundReachableCandidate || difference < bestDifference))
            {
                foundReachableCandidate = true;
                bestDifference = difference;
                bestCandidate = candidate;
            }
        }

        // Difficulty preference is soft; reachability is a hard requirement.
        // Random rejection can miss the small valid interval near a screen
        // edge. Try exact offsets in either direction, then scan the width;
        // never prefer one screen side just because its sample came first.
        if (foundReachableCandidate)
            return bestCandidate;
        float screenWidth = mainCamera.orthographicSize * mainCamera.aspect * 2f;
        float firstDirection = sweepDirection;
        for (int step = 0; step <= 8; step++)
        {
            float offset = Mathf.Lerp(targetDistance, minimumDistance, step / 8f);
            for (int directionIndex = 0; directionIndex < 2; directionIndex++)
            {
                float direction = directionIndex == 0 ? firstDirection : -firstDirection;
                float raw = lastPlatformX + direction * offset;
                for (int wrap = -1; wrap <= 1; wrap++)
                {
                    float candidate = raw + wrap * screenWidth;
                    if (candidate < currentMinX || candidate > currentMaxX)
                        continue;
                    float distance = GetWrappedHorizontalDistance(lastPlatformX,
                        candidate);
                    if (distance >= minimumDistance - 0.001f &&
                        distance <= maximumDistance + 0.001f)
                        return candidate;
                }
            }
        }
        int firstSample = Random.Range(0, 97);
        for (int i = 0; i <= 96; i++)
        {
            int sample = (firstSample + i) % 97;
            float candidate = Mathf.Lerp(currentMinX, currentMaxX, sample / 96f);
            float distance = GetWrappedHorizontalDistance(lastPlatformX, candidate);
            if (distance < minimumDistance || distance > maximumDistance)
                continue;
            float difference = Mathf.Abs(distance - targetDistance) +
                SweepPenalty(candidate, preferEdgeTransition, maximumDistance);
            if (difference >= bestDifference) continue;
            bestDifference = difference;
            bestCandidate = candidate;
            foundReachableCandidate = true;
        }
        if (foundReachableCandidate)
            return bestCandidate;

        // A designer can configure a reach so short that even the sampled
        // grid misses it. In that case keep the guaranteed physical route.
        return Mathf.Clamp(lastPlatformX, currentMinX, currentMaxX);
    }

    private void UpdateSweepDirection()
    {
        // Keep a direction across several landings instead of choosing each
        // row independently. Turn back near an edge; an occasional wrapped
        // transition remains available through preferEdgeTransition.
        float left = mainCamera.transform.position.x - currentMinX;
        float right = currentMaxX - mainCamera.transform.position.x;
        float turnAt = Mathf.Min(left, right) * 0.78f;
        if (lastPlatformX - mainCamera.transform.position.x > turnAt)
            sweepDirection = -1;
        else if (lastPlatformX - mainCamera.transform.position.x < -turnAt)
            sweepDirection = 1;
    }

    private float SweepPenalty(float candidate, bool preferEdgeTransition,
        float maximumDistance)
    {
        float direct = candidate - lastPlatformX;
        float width = mainCamera.orthographicSize * mainCamera.aspect * 2f;
        bool wrapped = Mathf.Abs(direct) > width * 0.5f;
        float travel = preferEdgeTransition
            ? TrapGeometry.WrappedDelta(lastPlatformX, candidate, width)
            : direct;
        float penalty = travel * sweepDirection < 0f
            ? Mathf.Min(maximumDistance, 3f * GameplayPreferences.ModelScale) * 0.65f
            : 0f;
        if (wrapped && !preferEdgeTransition)
            penalty += maximumDistance * 0.45f;
        return penalty;
    }

    private float StationaryLandingWidth()
    {
        BoxCollider2D platform = platformPrefab != null
            ? platformPrefab.GetComponent<BoxCollider2D>() : null;
        BoxCollider2D cat = playerMovement != null
            ? playerMovement.GetComponent<BoxCollider2D>() : null;
        if (platform == null || cat == null) return 0f;
        float scale = GameplayPreferences.ModelScale;
        float platformHalf = platform.size.x *
            Mathf.Abs(platform.transform.localScale.x) * scale * 0.5f * balance.PlatformWidth;
        // In Play Mode the player has already been resized by its scaler;
        // edit-mode generation validation invokes the spawner before Start.
        float catScale = cat.GetComponent<PortraitModelScaler>() == null
            ? scale : 1f;
        float catHalf = cat.size.x *
            Mathf.Abs(cat.transform.localScale.x) * catScale * 0.5f;
        return platformHalf + catHalf + 0.12f * scale;
    }

    private float CalculateSafeHorizontalReach(float verticalGap)
        => CalculateSafeHorizontalReach(verticalGap, lastPlatformMovementRange);

    private float CalculateSafeHorizontalReach(float verticalGap, float sourceMovementRange)
    {
        if (playerMovement == null)
        {
            playerMovement = FindFirstObjectByType<Player>();
        }

        if (playerMovement == null)
        {
            return startingHorizontalReach * 0.75f;
        }

        float movementScale = GameplayPreferences.HorizontalMovementScale;
        float inheritedOppositeSpeed = sourceMovementRange > 0f
            ? playerMovement.maximumInheritedSpeed * movementScale : 0f;
        JumpReachability.Result prediction = JumpReachability.Simulate(playerMovement,
            verticalGap, GameplayPreferences.UseInertia, movementScale, inheritedOppositeSpeed);

        // Точный режим допускает более близкие к пределу прыжки. Инерционный
        // получает больший запас на реакцию, разворот и небольшие ошибки.
        float safetyFactor = GameplayPreferences.UseInertia ? 0.9f : 0.92f;
        return prediction.CanLand ? prediction.HorizontalDistance * safetyFactor * balance.ReachMargin : 0f;
    }

    private float CalculatePreparedHorizontalReach(float verticalGap)
    {
        if (playerMovement == null)
            playerMovement = FindAnyObjectByType<Player>();
        if (playerMovement == null)
            return 0f;
        JumpReachability.Result prediction = JumpReachability.SimulatePrepared(
            playerMovement, verticalGap, GameplayPreferences.UseInertia,
            GameplayPreferences.HorizontalMovementScale);
        // More room for landing errors than the worst-case ordinary route.
        return prediction.CanLand ? prediction.HorizontalDistance * 0.87f * balance.ReachMargin : 0f;
    }

    private float GetOppositeEdgeCandidate()
    {
        float edgeBand = (currentMaxX - currentMinX) * 0.2f;

        if (lastPlatformX > 0.5f)
        {
            return Random.Range(currentMinX, currentMinX + edgeBand);
        }

        if (lastPlatformX < -0.5f)
        {
            return Random.Range(currentMaxX - edgeBand, currentMaxX);
        }

        return Random.value < 0.5f
            ? Random.Range(currentMinX, currentMinX + edgeBand)
            : Random.Range(currentMaxX - edgeBand, currentMaxX);
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

    private float MaximumAvailableWrappedDistance()
    {
        // On a narrow portrait screen a route point near the edge cannot
        // always reach the nominal half-screen distance: the platform bounds
        // shrink the available x interval. Do not demand an impossible target.
        float maximum = 0f;
        for (int i = 0; i <= 48; i++)
            maximum = Mathf.Max(maximum, GetWrappedHorizontalDistance(lastPlatformX,
                Mathf.Lerp(currentMinX, currentMaxX, i / 48f)));
        return maximum;
    }

    private bool TryChoosePrecisionReversal(float minimumDistance,
        float maximumDistance, out float x)
    {
        x = lastPlatformX;
        int wantedDirection = -lastApproachDirection;
        float width = mainCamera.orthographicSize * mainCamera.aspect * 2f;
        float bestDistance = float.NegativeInfinity;
        for (int i = 0; i <= 96; i++)
        {
            float candidate = Mathf.Lerp(currentMinX, currentMaxX, i / 96f);
            float travel = TrapGeometry.WrappedDelta(lastPlatformX, candidate, width);
            float distance = Mathf.Abs(travel);
            if (travel * wantedDirection <= 0f ||
                distance < minimumDistance || distance > maximumDistance ||
                distance <= bestDistance)
                continue;
            bestDistance = distance;
            x = candidate;
        }
        return bestDistance > float.NegativeInfinity;
    }

    private bool TryChooseDirectionalLanding(float minimumDistance,
        float maximumDistance, int wantedDirection, out float x)
    {
        x = lastPlatformX;
        float width = mainCamera.orthographicSize * mainCamera.aspect * 2f;
        float preferred = Mathf.Lerp(minimumDistance, maximumDistance,
            Random.Range(0.48f, 0.85f));
        float bestDifference = float.PositiveInfinity;
        for (int i = 0; i <= 192; i++)
        {
            float candidate = Mathf.Lerp(currentMinX, currentMaxX, i / 192f);
            float travel = TrapGeometry.WrappedDelta(lastPlatformX, candidate, width);
            float distance = Mathf.Abs(travel);
            if (travel * wantedDirection <= 0f || distance < minimumDistance ||
                distance > maximumDistance)
                continue;
            float difference = Mathf.Abs(distance - preferred);
            if (difference >= bestDifference) continue;
            bestDifference = difference;
            x = candidate;
        }
        return bestDifference < float.PositiveInfinity;
    }

    private GameObject SpawnPlatformAt(
        Vector2 position,
        bool makeMoving,
        bool makeNarrow,
        float movementSpeed,
        float movementRange,
        bool registerForAirCoinRoute = true,
        bool allowBush = true
    )
    {
        GameObject platform = Instantiate(platformPrefab, position, Quaternion.identity);

        if (balance.PlatformWidth != 1f)
        {
            Vector3 widthScale = platform.transform.localScale;
            widthScale.x *= balance.PlatformWidth;
            platform.transform.localScale = widthScale;
        }

        if (makeNarrow)
        {
            Vector3 scale = platform.transform.localScale;
            scale.x *= narrowPlatformScale;
            platform.transform.localScale = scale;
        }

        PortraitModelScaler.Attach(platform);

        if (makeMoving)
        {
            MovingPlatform movingPlatform = platform.AddComponent<MovingPlatform>();
            movingPlatform.Configure(
                currentMinX,
                currentMaxX,
                movementRange,
                movementSpeed * Random.Range(0.9f, 1.1f),
                Random.value * Mathf.PI * 2f
            );
        }

        if (allowBush && bushPrefab != null)
        {
            if (GameplayPreferences.UseInertia &&
                (GameplayPreferences.IsPortrait || GameplayPreferences.IsDesktopBuild) &&
                hazardSpawner != null)
            {
                // Move a pending bush to a later safe platform if this part of
                // an inertia route needs room for hazards. Keep the original
                // roll rate; do not reduce the hazard spawn chance.
                if (!deferredBush && Random.value < bushSpawnChance)
                    deferredBush = true;
                if (deferredBush &&
                    !hazardSpawner.NeedsOpenHazardSpace(position.y) &&
                    SpawnBushOn(platform, movementRange))
                {
                    deferredBush = false;
                }
            }
            else if (Random.value < bushSpawnChance)
            {
                SpawnBushOn(platform, movementRange);
            }
        }
        // The aerial-bonus planner follows the certified backbone. Additional
        // field platforms are still found as collision surfaces, but must not
        // create fake same-height or unreachable bonus routes.
        if (registerForAirCoinRoute)
        {
            airCoins?.RegisterPlatform(platform, movementRange);
            expeditionFinds?.RegisterPlatform(platform, movementRange);
            expeditionEnemies?.RegisterPlatform(platform, movementRange);
        }
        return platform;
    }

    private bool AddThornPatch(GameObject platform, Vector2 source)
    {
        BoxCollider2D surface = platform.GetComponent<BoxCollider2D>();
        BoxCollider2D feet = playerMovement != null
            ? playerMovement.GetComponent<BoxCollider2D>() : null;
        SpriteRenderer cat = playerMovement != null
            ? playerMovement.GetComponent<SpriteRenderer>() : null;
        if (surface == null || feet == null || cat == null || cat.sprite == null)
            return false;
        float scale = GameplayPreferences.ModelScale;
        float width = mainCamera.orthographicSize * mainCamera.aspect * 2f;
        float travel = TrapGeometry.WrappedDelta(source.x, platform.transform.position.x, width);
        if (Mathf.Abs(travel) < 0.01f)
            return false;
        float modelScale = playerMovement.GetComponent<PortraitModelScaler>() == null ? scale : 1f;
        float catWorldScale = Mathf.Abs(playerMovement.transform.lossyScale.x) * modelScale;
        float feetHalf = feet.size.x * catWorldScale * 0.5f;
        float bodyHalf = cat.sprite.bounds.extents.x * catWorldScale * 0.65f;
        // This runs immediately after Instantiate and difficulty/portrait
        // scaling, before Box2D synchronizes transforms. Its cached bounds can
        // still describe the wider prefab, making a narrow landing look safe.
        // Platforms are axis-aligned; preserve the collider's local offset
        // below, but derive its current width directly from live geometry.
        float platformHalf = surface.size.x * Mathf.Abs(surface.transform.lossyScale.x) * 0.5f;
        float landingHalf = platformHalf - feetHalf - 0.12f * scale;
        // Reserve the full drawn branch, not just its narrow damage box. The
        // mesh reaches about 0.54 local units either side, then inherits the
        // platform transform (including narrow and portrait variants).
        float drawnHalf = 0.54f * 1.7f * Mathf.Abs(platform.transform.lossyScale.x);
        float maxOffset = platformHalf - drawnHalf - 0.045f * scale;
        if (landingHalf <= 0f || maxOffset <= 0.3f * scale)
            return false;

        // Work in the direction of travel, including jumps across the screen seam.
        // On a maximum-range rightward jump only the left side of the target is
        // reachable; a thorn patch there must be rejected, even if the centre
        // of the platform looks clear in the scene view.
        float reach = CalculateSafeHorizontalReach(platform.transform.position.y - source.y, 0f);
        float target = Mathf.Abs(travel);
        float landingMin = Mathf.Max(0f, target - landingHalf);
        float landingMax = Mathf.Min(reach, target + landingHalf);
        const float minimumLandingWidth = 0.16f;
        if (landingMax - landingMin < minimumLandingWidth * scale)
            return false;

        float side = Mathf.Sign(travel);
        float dangerClearance = (0.19f + 0.12f) * scale + bodyHalf;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float fraction = attempt < 5 ? Random.Range(-1f, 1f) :
                (attempt == 5 ? 0f : attempt == 6 ? -0.9f : 0.9f);
            float offset = fraction * maxOffset;
            float dangerX = target + offset * side;
            if (!LeavesLandingLane(landingMin, landingMax, dangerX,
                dangerClearance, minimumLandingWidth * scale))
                continue;

            Vector3 localPosition = new Vector3(
                surface.offset.x + offset / Mathf.Abs(platform.transform.lossyScale.x),
                surface.offset.y + surface.size.y * 0.5f + 0.72f, -0.05f);
            Vector2 patchCenter = platform.transform.TransformPoint(localPosition);
            Rect visiblePatch = DoodlikHazard.PatchVisibleBounds(
                patchCenter, platform.transform.lossyScale);
            if (hazardSpawner != null && hazardSpawner.OverlapsBushLaunch(visiblePatch))
                continue;
            GameObject patch = new GameObject("Visible thorn patch");
            patch.transform.SetParent(platform.transform, false);
            patch.transform.localPosition = localPosition;
            DoodlikHazard danger = patch.AddComponent<DoodlikHazard>();
            danger.InitializePatch(mainCamera, new Vector2(0.19f, 0.18f) * scale,
                new Vector3(1.7f, 2f, 1f));
            danger.SetCertifiedRoute(new[] { source, (Vector2)platform.transform.position });
            hazardSpawner?.RegisterThornPatch(danger);
            return true;
        }
        return false;
    }

    private static bool LeavesLandingLane(float landingMin, float landingMax,
        float dangerX, float clearance, float requiredWidth)
    {
        // Coordinates are mirrored into the direction of travel. A free area
        // *behind* the thorns is not sufficient: the cat would have to cross
        // the patch before it could land there. Keep a near-side landing lane.
        return Mathf.Min(landingMax, dangerX - clearance) - landingMin >= requiredWidth;
    }

    private void UpdateHorizontalBounds(bool remapExistingPlatforms)
    {
        float previousMinX = currentMinX;
        float previousMaxX = currentMaxX;
        float halfPlatformWidth = 0.75f;
        SpriteRenderer prefabRenderer =
            platformPrefab != null
                ? platformPrefab.GetComponent<SpriteRenderer>()
                : null;
        if (prefabRenderer != null)
        {
            halfPlatformWidth = prefabRenderer.bounds.extents.x * GameplayPreferences.ModelScale * balance.PlatformWidth;
        }

        float screenHalfWidth = mainCamera.orthographicSize * mainCamera.aspect;
        float usableHalfWidth = Mathf.Max(
            0.6f,
            screenHalfWidth - halfPlatformWidth - PlatformEdgePadding
        );
        currentMinX = mainCamera.transform.position.x - usableHalfWidth;
        currentMaxX = mainCamera.transform.position.x + usableHalfWidth;
        lastCameraAspect = mainCamera.aspect;

        if (!remapExistingPlatforms ||
            previousMaxX - previousMinX <= 0.01f)
        {
            return;
        }

        // MovingPlatform remaps its patrol by the screen-width ratio below.
        // Keep the generator's uncertainty for the latest route in the same
        // units, or a wider window can make its next landing unreachable.
        lastPlatformMovementRange *=
            (currentMaxX - currentMinX) / (previousMaxX - previousMinX);

        foreach (GameObject platform in
                 GameObject.FindGameObjectsWithTag("Platform"))
        {
            MovingPlatform movingPlatform =
                platform.GetComponent<MovingPlatform>();
            if (movingPlatform != null)
            {
                movingPlatform.RemapHorizontalBounds(
                    previousMinX,
                    previousMaxX,
                    currentMinX,
                    currentMaxX
                );
                continue;
            }

            float positionT = (platform.transform.position.x - previousMinX) /
                (previousMaxX - previousMinX);
            Vector3 position = platform.transform.position;
            position.x = Mathf.Lerp(currentMinX, currentMaxX, positionT);
            platform.transform.position = position;
        }

        GameObject player = GameObject.Find("Player");
        if (player != null)
        {
            float playerT = Mathf.InverseLerp(
                previousMinX,
                previousMaxX,
                player.transform.position.x
            );
            Vector3 playerPosition = player.transform.position;
            playerPosition.x = Mathf.Lerp(currentMinX, currentMaxX, playerT);
            player.transform.position = playerPosition;
        }

        lastPlatformX = Mathf.Lerp(
            currentMinX,
            currentMaxX,
            Mathf.InverseLerp(previousMinX, previousMaxX, lastPlatformX)
        );
        float oldWidth = previousMaxX - previousMinX;
        float newWidth = currentMaxX - currentMinX;
        for (int i = 0; i < currentTier.Count; i++)
        {
            RouteNode node = currentTier[i];
            node.x = currentMinX +
                (node.x - previousMinX) * newWidth / oldWidth;
            node.movementRange *= newWidth / oldWidth;
            currentTier[i] = node;
        }
        hazardSpawner?.InvalidateLayout(highestPlatformY,
            previousMinX, previousMaxX, currentMinX, currentMaxX);
        airCoins?.InvalidateLayout();
        expeditionFinds?.InvalidateLayout();
        expeditionEnemies?.InvalidateLayout();
    }

    private bool SpawnBushOn(GameObject platform, float movementRange)
    {
        SpriteRenderer platformRenderer = platform.GetComponent<SpriteRenderer>();
        if (platformRenderer == null)
        {
            return false;
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

        if (hazardSpawner == null) return true;
        // Reserve each part of the boosted ascent using the actual trigger and
        // cat hitbox. A second bush gets its own reservation, so chained boosts
        // cannot inherit an unprotected launch from the first one.
        BoxCollider2D trigger = bush.GetComponent<BoxCollider2D>();
        Rect[] launchAreas = BuildBushLaunchAreas(trigger, movementRange);
        if (hazardSpawner.TryReserveBushLaunch(launchAreas)) return true;
#if UNITY_EDITOR
        if (!Application.isPlaying) DestroyImmediate(bush);
        else
#endif
            Destroy(bush);
        return false;
    }

    private Rect[] BuildBushLaunchAreas(BoxCollider2D trigger, float movementRange)
    {
        BoxCollider2D feet = playerMovement.GetComponent<BoxCollider2D>();
        SpriteRenderer cat = playerMovement.GetComponent<SpriteRenderer>();
        if (trigger == null || feet == null || cat == null || cat.sprite == null)
            return new Rect[0];

        float modelScale = GameplayPreferences.ModelScale;
        float pendingScale = playerMovement.GetComponent<PortraitModelScaler>() == null
            ? modelScale : 1f;
        float scaleX = Mathf.Abs(playerMovement.transform.lossyScale.x) * pendingScale;
        float scaleY = Mathf.Abs(playerMovement.transform.lossyScale.y) * pendingScale;
        Vector2 bodyHalf = new Vector2(cat.sprite.bounds.extents.x * scaleX,
            cat.sprite.bounds.extents.y * scaleY) * 0.65f;
        float feetHalfX = feet.size.x * scaleX * 0.5f;
        float feetHalfY = feet.size.y * scaleY * 0.5f;
        float bodyOffsetX = (cat.sprite.bounds.center.x - feet.offset.x) * scaleX;
        float bodyOffsetY = (cat.sprite.bounds.center.y - feet.offset.y) * scaleY;
        Bounds triggerBounds = trigger.bounds;
        float padding = 0.12f * modelScale;
        float startHalfX = triggerBounds.extents.x + feetHalfX +
            Mathf.Abs(bodyOffsetX) + bodyHalf.x + padding + Mathf.Abs(movementRange);
        float startBottom = triggerBounds.min.y - feetHalfY +
            bodyOffsetY - bodyHalf.y - padding;
        float startTop = triggerBounds.max.y + feetHalfY +
            bodyOffsetY + bodyHalf.y + padding;

        float launchSpeed = Mathf.Max(playerMovement.bushJumpForce,
            playerMovement.NormalJumpForce);
        float gravity = Mathf.Max(0.1f, Mathf.Abs(Physics2D.gravity.y));
        float risingGravity = gravity * Mathf.Max(0.1f, playerMovement.risingGravityScale);
        float apexGravity = gravity * Mathf.Max(0.1f, playerMovement.apexGravityScale);
        float switchSpeed = Mathf.Clamp(playerMovement.apexVelocityThreshold,
            0f, launchSpeed);
        float switchTime = (launchSpeed - switchSpeed) / risingGravity;
        float switchRise = (launchSpeed * launchSpeed - switchSpeed * switchSpeed) /
            (2f * risingGravity);
        float apexTime = switchTime + switchSpeed / apexGravity;

        // Full steering is protected for a reaction window. Above that, the
        // reservation still covers the no-input drift of the inertia mode up
        // to the apex. Deliberately steering into a distant trap remains risky.
        const float reactionTime = 0.45f;
        float speed = playerMovement.moveSpeed *
            GameplayPreferences.HorizontalMovementScale +
            (movementRange > 0f ? playerMovement.maximumInheritedSpeed : 0f);
        float deceleration = Mathf.Max(0.1f, playerMovement.airDeceleration *
            GameplayPreferences.HorizontalMovementScale);
        const int slices = 5;
        Rect[] areas = new Rect[slices];
        for (int i = 0; i < slices; i++)
        {
            float fromTime = apexTime * i / slices;
            float toTime = apexTime * (i + 1) / slices;
            float fromRise = BushRiseAt(fromTime, launchSpeed, risingGravity,
                apexGravity, switchTime, switchRise, switchSpeed);
            float toRise = BushRiseAt(toTime, launchSpeed, risingGravity,
                apexGravity, switchTime, switchRise, switchSpeed);
            float horizontalTravel = speed * Mathf.Min(toTime, reactionTime);
            if (GameplayPreferences.UseInertia)
            {
                float coastTime = Mathf.Min(toTime, speed / deceleration);
                float coastTravel = speed * coastTime -
                    0.5f * deceleration * coastTime * coastTime;
                horizontalTravel = Mathf.Max(horizontalTravel, coastTravel);
            }
            float halfWidth = startHalfX + horizontalTravel;
            float bottom = startBottom + fromRise;
            areas[i] = new Rect(triggerBounds.center.x - halfWidth, bottom,
                halfWidth * 2f, startTop + toRise - bottom);
        }
        return areas;
    }

    private static float BushRiseAt(float time, float launchSpeed,
        float risingGravity, float apexGravity, float switchTime,
        float switchRise, float switchSpeed)
    {
        if (time <= switchTime)
            return launchSpeed * time - 0.5f * risingGravity * time * time;
        float slowTime = time - switchTime;
        return switchRise + switchSpeed * slowTime -
            0.5f * apexGravity * slowTime * slowTime;
    }
}
