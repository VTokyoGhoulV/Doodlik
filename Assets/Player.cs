using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class Player : MonoBehaviour
{
    public const string HighScoreKey = "DoodlikHighScore";

    public static readonly string[] ModeHighScoreKeys =
    {
        "DoodlikHighScore_Landscape_Precise",
        "DoodlikHighScore_Landscape_Inertia",
        "DoodlikHighScore_Portrait_Precise",
        "DoodlikHighScore_Portrait_Inertia"
    };

    // Keep the old public entry point for menus/tools, with difficulty isolation.
    public static string CurrentHighScoreKey => RunRecords.CurrentScoreKey;

    [Header("Movement")]
    public float moveSpeed = 7f;
    public float jumpForce = 10f;
    [Range(1f, 1.2f)]
    [Tooltip("Множитель обычного прыжка; кустовый прыжок не изменяет")]
    public float normalJumpMultiplier = 1.1f;
    public float NormalJumpForce => jumpForce * Mathf.Max(0.1f, normalJumpMultiplier) * AdminRuntime.JumpMultiplier;

    [Header("Movement Feel")]
    [Tooltip("Плавный разгон и воздушная инерция. Отключено для эксклюзивной сборки.")]
    public bool smoothHorizontalMovement = false;
    [Tooltip("Использовать плавное движение на Android и iOS независимо от настройки ПК.")]
    public bool smoothMovementOnMobile = true;
    public float acceleration = 26f;
    public float turnAcceleration = 38f;
    public float airDeceleration = 8f;
    [Range(0.5f, 1.5f)] public float risingControl = 0.85f;
    [Range(0.5f, 1.5f)] public float fallingControl = 1.18f;

    [Header("Jump Feel")]
    public float risingGravityScale = 2f;
    public float apexGravityScale = 1.45f;
    public float fallingGravityScale = 3.15f;
    public float apexVelocityThreshold = 1.4f;
    public float maximumFallSpeed = 18f;
    [Range(0f, 1f)] public float movingPlatformMomentum = 0.7f;
    public float maximumInheritedSpeed = 2.5f;

    [Header("Squash And Stretch")]
    public float visualResponse = 12f;
    public float landingSquashDuration = 0.11f;
    public Vector2 landingSquash = new Vector2(1.16f, 0.82f);
    public Vector2 risingStretch = new Vector2(0.92f, 1.1f);
    public Vector2 fallingStretch = new Vector2(0.95f, 1.06f);

    [Tooltip("Сила усиленного прыжка после касания куста")]
    public float bushJumpForce = 17f;

    [Header("Controls")]
    [Tooltip("Доля ширины экрана, используемая как нейтральная зона в центре телефона")]
    [Range(0f, 0.4f)] public float mobileDeadZone = 0.08f;

    [Header("Score")]
    public TMP_Text scoreText;

    [Tooltip("Количество очков за одну единицу высоты")]
    public float pointsPerUnit = 10f;

    [Header("Game Over")]
    public GameObject gameOverPanel;
    public float deathOffset = 1f;

    private Rigidbody2D rb;
    private Collider2D playerCollider;
    private Camera mainCamera;
    private SpriteRenderer spriteRenderer;
    private Transform visualTransform;
    private float horizontalInput;
    private float squashTimer;

    private float startHeight;
    private float maximumHeight;
    private int score;
    private int reachedHeight;
    private int airCoinsThisAttempt;
    private int highScore;
    private int highScoreAtRunStart;
    private string currentHighScoreKey;
    private int bestHeight;
    private int bestHeightAtRunStart;
    private string currentBestHeightKey;
    private GameDifficulty runDifficulty = GameDifficulty.Normal;
    private string runModeDisplayName;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
    private bool testAttempt;
    private bool scoreKeyExistedAtRunStart;
    private bool recordWrittenThisAttempt;
    private bool heightKeyExistedAtRunStart;
    private bool heightRecordWrittenThisAttempt;
#endif

    private bool isDead;
    public bool IsDead => isDead;
    public Vector2 HazardBodyHalfSize => hazardBodyHalfSize;
    public Vector2 HazardBodyCenter => spriteRenderer != null
        ? (Vector2)spriteRenderer.bounds.center : (Vector2)transform.position;
    public int LastHazardAuditFrame { get; private set; } = -1;
    public int CurrentScore => score;
    public RunJournal Journal { get; private set; }
    public ExpeditionRun Expedition { get; private set; }
    public GameRunKind RunKind { get; private set; }
    public int CurrentHeight => reachedHeight;
    public int BestScore => highScore;
    public int BestHeight => bestHeight;
    public GameDifficulty RunDifficulty => runDifficulty;
    public bool IsTestAttempt
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
            return testAttempt;
#else
            return false;
#endif
        }
    }
    private static bool AdminCheatEnabled
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
            return AdminTools.IsAvailable && PlayerProgress.AdminSandboxActive && AdminTools.CheatMode;
#else
            return false;
#endif
        }
    }
    private static bool AdminTestingActive
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
            return PlayerProgress.AdminSandboxActive || AdminCheatEnabled;
#else
            return false;
#endif
        }
    }
    private Vector2 previousHazardPosition;
    private Vector2 previousFeetPosition;
    private Vector2 hazardBodyHalfSize;
    private RunPowerups powers;
    public RunChallenges Challenges { get; private set; }

    private void Start()
    {
        PortraitModelScaler.Attach(gameObject);
        rb = GetComponent<Rigidbody2D>();
        playerCollider = GetComponent<Collider2D>();
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        mainCamera = Camera.main;
        SetupVisual();
        powers = gameObject.AddComponent<RunPowerups>();
        previousHazardPosition = spriteRenderer.bounds.center;
        previousFeetPosition = FeetPosition();
        // The landing collider only covers the paws. Hazards use a forgiving
        // body-sized hit area, independent of squash/stretch animation.
        hazardBodyHalfSize = (Vector2)spriteRenderer.bounds.extents * 0.65f;

        isDead = false;
        Time.timeScale = RunFlow.IsPlaying ? 1f : 0f;

        startHeight = transform.position.y;
        WorldZones.ConfigureRun(startHeight, pointsPerUnit);
        maximumHeight = startHeight;
        score = 0;
        reachedHeight = 0;
        airCoinsThisAttempt = 0;
        runDifficulty = GameplayPreferences.Difficulty;
        RunKind = GameplayPreferences.RunKind;
        runModeDisplayName = (RunKind == GameRunKind.Expedition ? "Экспедиция · " : "") +
            GameplayPreferences.CurrentModeDisplayName;
        currentHighScoreKey = CurrentHighScoreKey;
        currentBestHeightKey = RunRecords.CurrentHeightKey;
        // Opening a debug sandbox must not migrate or write the real records.
        if (!AdminTestingActive && RunKind == GameRunKind.Classic)
            RunRecords.MigrateLegacy(GameplayPreferences.IsPortrait, GameplayPreferences.UseInertia);
        highScore = Mathf.Max(0, PlayerPrefs.GetInt(currentHighScoreKey, 0));
        highScoreAtRunStart = highScore;
        bestHeight = Mathf.Max(0, PlayerPrefs.GetInt(currentBestHeightKey, 0));
        bestHeightAtRunStart = bestHeight;
        // Achievements added after a player's earlier records should still be
        // granted on the next launch, irrespective of the selected mode.
        if (!AdminTestingActive)
        {
            PlayerProgress.AwardHeightUnlocks(RunRecords.BestHeight, true);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
        scoreKeyExistedAtRunStart = PlayerPrefs.HasKey(currentHighScoreKey);
        recordWrittenThisAttempt = false;
        heightKeyExistedAtRunStart = PlayerPrefs.HasKey(currentBestHeightKey);
        heightRecordWrittenThisAttempt = false;
        testAttempt = false;
#endif
        RefreshAdminState();

        Challenges = RunChallenges.Attach(this);
        Journal = RunJournal.Attach(this);
        if (RunKind == GameRunKind.Expedition) Expedition = ExpeditionRun.Attach(this);
        ApplyInterfaceFont();

        if (scoreText != null)
        {
            scoreText.color = new Color(0.27f, 0.12f, 0.035f, 1f);
            scoreText.fontStyle = FontStyles.Bold;
            scoreText.fontSize = 30f;
            scoreText.textWrappingMode = TextWrappingModes.NoWrap;
            scoreText.overflowMode = TextOverflowModes.Overflow;
            scoreText.rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                720f
            );
            scoreText.rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                105f
            );
        }

        UpdateScoreText();
        powers.CreateHud(scoreText);
        HeightProgressUI.Create(this, scoreText);
        ChallengeHud.Attach(this, scoreText);

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
            AddKeyboardHintToRestartButton();
        }
    }

    private void Update()
    {
        if (!RunFlow.IsPlaying || GameMenuUI.BlocksGameplayInput) return;
        if (isDead)
        {
            if (Keyboard.current != null &&
                Keyboard.current.rKey.wasPressedThisFrame)
            {
                RestartGame();
            }

            return;
        }

        horizontalInput = ReadHorizontalInput();
        UpdateFacing();
        UpdateVisualFeel();
        ScreenWrap();
        UpdateHeightScore();
    }

    private void FixedUpdate()
    {
        if (isDead || !RunFlow.IsPlaying || GameMenuUI.BlocksGameplayInput)
        {
            return;
        }

        ScreenWrap();
        ApplyHorizontalMovement();
        if (powers != null && powers.ApplyRocketMotion(Time.fixedDeltaTime)) return;
        ApplyGravityCurve();
    }

    private void LateUpdate()
    {
        if (scoreText != null)
        {
            scoreText.enabled = RunFlow.RunActive;
            scoreText.color = new Color32(94, 49, 24, 255);
        }
        Vector2 current = spriteRenderer.bounds.center;
        if (!isDead && RunFlow.IsPlaying && Time.timeScale > 0f)
        {
            float width = mainCamera.orthographicSize * mainCamera.aspect * 2f;
            ResolveTravel(current, width);
            if (!isDead) TryLandOnProtectedThorns(width);
            LastHazardAuditFrame = Time.frameCount;
            ChargingBumblebee.RecordPlayerAudit(this);
        }
        previousHazardPosition = spriteRenderer.bounds.center;
        previousFeetPosition = FeetPosition();
        // Resolve pickups before a fall on a long frame: a crossed shield is not lost.
        if (!isDead && RunFlow.IsPlaying && Time.timeScale > 0f) CheckDeath();
        if (!isDead && RunFlow.IsPlaying && Time.timeScale > 0f) Expedition?.AfterHazardAudit();
    }

    private bool ResolveTravel(Vector2 current, float width, bool sweepMotion = true)
    {
        RunPickup.CollectAlong(previousHazardPosition, current, hazardBodyHalfSize, width, powers, sweepMotion);
        ExpeditionPickup.CollectAlong(previousHazardPosition, current, hazardBodyHalfSize, width, sweepMotion);
        bool enemyBounce = ExpeditionEnemy.ResolveTravel(Expedition, previousHazardPosition, current, hazardBodyHalfSize, width, sweepMotion);
        if (enemyBounce)
            current = spriteRenderer.bounds.center;
        if (isDead) return enemyBounce;
        DoodlikHazard hit = DoodlikHazard.FindHit(previousHazardPosition, current,
            hazardBodyHalfSize, width, out ChargingBumblebee beeContact, sweepMotion);
        // Contact invalidates a dodge even when a carried shield or exit grace
        // absorbs it. Only a genuinely avoided bee attack earns that event.
        beeContact?.RecordPlayerContact();
        if (hit != null && !AdminCheatEnabled && !powers.TryProtect(false, rb, mainCamera, playerCollider))
        {
            GameAudio.Play(GameAudio.Cue.Hurt);
            Die(hit.Type == DoodlikHazard.Kind.FlyingSeed ? "Ой! Колючее семечко!" :
                hit.Type == DoodlikHazard.Kind.Bumblebee ? "Ой! Сердитый шмель!" : "Ой! Колючки!");
        }
        return enemyBounce;
    }

    private bool ResolveTravelBeforeBounce(bool normalLanding = false)
    {
        // A long rendered frame can contain both a descent and a rebound.
        // Preserve that turning point instead of replacing the excursion with
        // a straight line between the two above-platform render positions.
        float width = mainCamera.orthographicSize * mainCamera.aspect * 2f;
        // Physics may already have reduced velocity to zero at a platform
        // contact. That is a descending pickup, not a jump-apex pickup.
        Challenges?.BeginContactCollection(normalLanding || rb.linearVelocity.y < -0.1f);
        bool enemyBounce = false;
        try { enemyBounce = ResolveTravel(spriteRenderer.bounds.center, width, false); }
        finally { Challenges?.EndContactCollection(); }
        previousHazardPosition = spriteRenderer.bounds.center;
        previousFeetPosition = FeetPosition();
        // A sweep can find a beetle before this already-reported platform
        // contact. Its rebound replaces that stale collision completely.
        return !isDead && !enemyBounce;
    }

    private Vector2 FeetPosition() => new Vector2(playerCollider.bounds.center.x,
        playerCollider.bounds.min.y);

    private void TryLandOnProtectedThorns(float screenWidth)
    {
        if (rb.linearVelocity.y > 0f || (!AdminCheatEnabled && !powers.HasShield && !powers.IsProtected)) return;
        Vector2 feet = FeetPosition();
        if (!DoodlikHazard.FindProtectedLanding(previousFeetPosition, feet,
            playerCollider.bounds.extents.x, screenWidth, out Vector2 contact)) return;
        if (!AdminCheatEnabled && !powers.TryProtect(false, rb, mainCamera, playerCollider)) return;

        // Resolve at the swept contact, including a crossing on a long frame.
        // Never leave the cat embedded in the platform or bounce on a body hit.
        Vector2 position = rb.position;
        position.x += TrapGeometry.WrappedDelta(feet.x, contact.x, screenWidth);
        position.y += contact.y - feet.y + 0.01f;
        float left = mainCamera.transform.position.x - screenWidth * 0.5f;
        position.x = left + Mathf.Repeat(position.x - left, screenWidth);
        rb.position = position;
        transform.position = new Vector3(position.x, position.y, transform.position.z);
        Physics2D.SyncTransforms();
        squashTimer = landingSquashDuration;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, NormalJumpForce);
        GameAudio.Play(GameAudio.Cue.Jump);
    }

    private void ApplyInterfaceFont()
    {
        TMP_FontAsset interfaceFont = Resources.Load<TMP_FontAsset>(
            "Fonts & Materials/LiberationSans SDF - Fallback"
        );
        if (interfaceFont == null)
        {
            return;
        }

        if (scoreText != null)
        {
            scoreText.font = interfaceFont;
        }

        if (gameOverPanel != null)
        {
            foreach (TMP_Text text in
                gameOverPanel.GetComponentsInChildren<TMP_Text>(true))
            {
                text.font = interfaceFont;
            }
        }
    }

    private void ApplyHorizontalMovement()
    {
        float movementScale = GameplayPreferences.HorizontalMovementScale * AdminRuntime.MovementMultiplier;
        float targetSpeed = horizontalInput * moveSpeed * movementScale;

        if (!ShouldUseSmoothHorizontalMovement())
        {
            rb.linearVelocity = new Vector2(targetSpeed, rb.linearVelocity.y);
            return;
        }

        bool isTurning =
            Mathf.Abs(horizontalInput) > 0.01f &&
            Mathf.Abs(rb.linearVelocity.x) > 0.1f &&
            Mathf.Sign(targetSpeed) != Mathf.Sign(rb.linearVelocity.x);
        float control = rb.linearVelocity.y < 0f ? fallingControl : risingControl;
        float changeRate;

        if (Mathf.Abs(horizontalInput) < 0.01f)
        {
            changeRate = airDeceleration * movementScale;
        }
        else
        {
            changeRate = isTurning ? turnAcceleration : acceleration;
            changeRate *= control * movementScale;
        }

        rb.linearVelocity = new Vector2(
            Mathf.MoveTowards(
                rb.linearVelocity.x,
                targetSpeed,
                changeRate * Time.fixedDeltaTime
            ),
            rb.linearVelocity.y
        );
    }

    private bool ShouldUseSmoothHorizontalMovement()
    {
        return GameplayPreferences.UseInertia;
    }

    private void ApplyGravityCurve()
    {
        float verticalSpeed = rb.linearVelocity.y;

        if (Mathf.Abs(verticalSpeed) <= apexVelocityThreshold)
        {
            rb.gravityScale = apexGravityScale * AdminRuntime.GravityMultiplier;
        }
        else if (verticalSpeed > 0f)
        {
            rb.gravityScale = risingGravityScale * AdminRuntime.GravityMultiplier;
        }
        else
        {
            rb.gravityScale = fallingGravityScale * AdminRuntime.GravityMultiplier;
        }

        if (verticalSpeed < -maximumFallSpeed)
        {
            rb.linearVelocity = new Vector2(
                rb.linearVelocity.x,
                -maximumFallSpeed
            );
        }
    }

    private void UpdateFacing()
    {
        if (horizontalInput < -0.01f)
        {
            spriteRenderer.flipX = false;
        }
        else if (horizontalInput > 0.01f)
        {
            spriteRenderer.flipX = true;
        }
    }

    private void SetupVisual()
    {
        SpriteRenderer sourceRenderer = GetComponent<SpriteRenderer>();
        GameObject visual = new GameObject("PlayerVisual");
        visual.transform.SetParent(transform, false);

        SpriteRenderer visualRenderer = visual.AddComponent<SpriteRenderer>();
        visualRenderer.sprite = sourceRenderer.sprite;
        visualRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
        visualRenderer.color = sourceRenderer.color;
        visualRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        // Платформы используют order 1, поэтому котик всегда рисуется поверх них.
        visualRenderer.sortingOrder = Mathf.Max(sourceRenderer.sortingOrder, 2);
        visualRenderer.flipX = sourceRenderer.flipX;
        visualRenderer.flipY = sourceRenderer.flipY;

        sourceRenderer.enabled = false;
        spriteRenderer = visualRenderer;
        visualTransform = visual.transform;
        CatAppearance.Attach(visualTransform, spriteRenderer);
    }

    private void UpdateVisualFeel()
    {
        if (visualTransform == null)
        {
            return;
        }

        Vector2 targetScale = Vector2.one;
        if (squashTimer > 0f)
        {
            squashTimer -= Time.deltaTime;
            targetScale = landingSquash;
        }
        else if (rb.linearVelocity.y > apexVelocityThreshold)
        {
            targetScale = risingStretch;
        }
        else if (rb.linearVelocity.y < -apexVelocityThreshold)
        {
            targetScale = fallingStretch;
        }

        visualTransform.localScale = Vector3.Lerp(
            visualTransform.localScale,
            new Vector3(targetScale.x, targetScale.y, 1f),
            1f - Mathf.Exp(-visualResponse * Time.deltaTime)
        );
    }

    private float ReadHorizontalInput()
    {
        float horizontal = 0f;

        if (Keyboard.current != null)
        {
            bool moveLeft = Keyboard.current.leftArrowKey.isPressed ||
                            Keyboard.current.aKey.isPressed;
            bool moveRight = Keyboard.current.rightArrowKey.isPressed ||
                             Keyboard.current.dKey.isPressed;
            horizontal = (moveRight ? 1f : 0f) - (moveLeft ? 1f : 0f);
        }

        if (Touchscreen.current != null)
        {
            float leftLimit = Screen.width * (0.5f - mobileDeadZone * 0.5f);
            float rightLimit = Screen.width * (0.5f + mobileDeadZone * 0.5f);
            bool touchLeft = false;
            bool touchRight = false;

            foreach (var touch in Touchscreen.current.touches)
            {
                if (!touch.press.isPressed || (EventSystem.current != null &&
                    EventSystem.current.IsPointerOverGameObject(touch.touchId.ReadValue())))
                {
                    continue;
                }

                float touchX = touch.position.ReadValue().x;
                touchLeft |= touchX < leftLimit;
                touchRight |= touchX > rightLimit;
            }

            if (touchLeft || touchRight)
            {
                horizontal = (touchRight ? 1f : 0f) - (touchLeft ? 1f : 0f);
            }
        }

        return horizontal;
    }

    private void UpdateHeightScore()
    {
        RefreshAdminState();
        // Запоминаем только наибольшую достигнутую высоту
        maximumHeight = Mathf.Max(
            maximumHeight,
            transform.position.y
        );

        int newHeight = WorldZones.ScoreForHeight(maximumHeight, startHeight, pointsPerUnit);
        // World progression, biome pressure and cosmetic rewards use ascent only.
        // Multipliers never move the space boundary or modify coin rewards.
        WorldZones.SetHeightProgress(newHeight);

        if (newHeight == reachedHeight)
        {
            return;
        }

        reachedHeight = newHeight;
        score = DifficultyRules.ScoreForHeight(reachedHeight, runDifficulty);
        Challenges?.RecordHeight(reachedHeight);
        PlayerProgress.AwardHeightUnlocks(reachedHeight, !IsTestAttempt);

        if (!IsTestAttempt && reachedHeight > bestHeight)
        {
            bestHeight = reachedHeight;
            if (!string.IsNullOrEmpty(currentBestHeightKey))
                PlayerPrefs.SetInt(currentBestHeightKey, bestHeight);
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
            heightRecordWrittenThisAttempt = true;
#endif
        }

        if (!IsTestAttempt && score > highScore)
        {
            highScore = score;
            PlayerPrefs.SetInt(currentHighScoreKey, highScore);
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
            recordWrittenThisAttempt = true;
#endif
        }

        UpdateScoreText();
    }

    public void RegisterAirCoinCollected(int amount = 1)
    {
        if (isDead || !RunFlow.IsPlaying || IsTestAttempt || PlayerProgress.AdminSandboxActive || amount <= 0 || amount > 100)
            return;
        int before = airCoinsThisAttempt;
        airCoinsThisAttempt = (int)System.Math.Min(int.MaxValue, (long)airCoinsThisAttempt + amount);
        for (int i = 0; i < amount; i++)
            Challenges?.RecordCoin(powers != null && powers.MagnetTimeRemaining > 0f);
        if (before < 10 && airCoinsThisAttempt >= 10)
            PlayerProgress.AwardAirCoinAchievement(true);
    }

    private void UpdateScoreText()
    {
        if (scoreText != null)
        {
            scoreText.text =
                $"Высота: {reachedHeight}\n" +
                $"<size=80%>Счёт: {score}  {DifficultyRules.MultiplierLabel(runDifficulty)} · {DifficultyRules.Name(runDifficulty)}</size>\n" +
                $"<size=65%>{runModeDisplayName ?? GameplayPreferences.CurrentModeDisplayName}</size>";
        }
    }

    // Called before and after admin toggles. The latch belongs to this Player
    // instance (one attempt), not the menu: disabling cheats cannot legitimise
    // points already gained in a sandbox. A fresh scene starts a fresh latch.
    public void RefreshAdminState()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
        if (testAttempt || isDead || !RunFlow.RunActive || !AdminTestingActive ||
            string.IsNullOrEmpty(currentHighScoreKey)) return;
        testAttempt = true;
        Challenges?.Invalidate();
        Expedition?.Invalidate();
        // Normal scoring writes incrementally. Undo this attempt's earlier
        // writes as well, even if opening a menu already flushed PlayerPrefs.
        bool restoreRecords = recordWrittenThisAttempt || heightRecordWrittenThisAttempt;
        if (recordWrittenThisAttempt)
        {
            if (scoreKeyExistedAtRunStart) PlayerPrefs.SetInt(currentHighScoreKey, highScoreAtRunStart);
            else PlayerPrefs.DeleteKey(currentHighScoreKey);
            recordWrittenThisAttempt = false;
        }
        if (heightRecordWrittenThisAttempt && !string.IsNullOrEmpty(currentBestHeightKey))
        {
            if (heightKeyExistedAtRunStart) PlayerPrefs.SetInt(currentBestHeightKey, bestHeightAtRunStart);
            else PlayerPrefs.DeleteKey(currentBestHeightKey);
            heightRecordWrittenThisAttempt = false;
        }
        if (restoreRecords) PlayerPrefs.Save();
        highScore = highScoreAtRunStart;
        bestHeight = bestHeightAtRunStart;
        UpdateScoreText();
#endif
    }

    private void ScreenWrap()
    {
        float cameraHeight = mainCamera.orthographicSize;
        float cameraWidth = cameraHeight * mainCamera.aspect;

        float leftBorder =
            mainCamera.transform.position.x - cameraWidth;

        float rightBorder =
            mainCamera.transform.position.x + cameraWidth;

        if (rb.position.x < leftBorder || rb.position.x > rightBorder)
        {
            // Preserve overshoot so wrapping does not steal horizontal distance at low FPS.
            Vector2 position = rb.position;
            float previousX = position.x;
            position.x = leftBorder + Mathf.Repeat(position.x - leftBorder, rightBorder - leftBorder);
            rb.position = position;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            Challenges?.RecordWrap(previousX - position.x);
        }
    }

    private void CheckDeath()
    {
        float bottomBorder =
            mainCamera.transform.position.y -
            mainCamera.orthographicSize;

        if (transform.position.y < bottomBorder - deathOffset)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
            if (AdminCheatEnabled)
            {
                RefreshAdminState();
                RecoverFromAdminFall(bottomBorder);
                return;
            }
#endif
            if (powers.TryProtect(true, rb, mainCamera, playerCollider))
            {
                previousHazardPosition = spriteRenderer.bounds.center;
                previousFeetPosition = FeetPosition();
                return;
            }
            GameAudio.Play(GameAudio.Cue.Fall);
            Die();
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
    private void RecoverFromAdminFall(float bottomBorder)
    {
        float halfWidth = mainCamera.orthographicSize * mainCamera.aspect;
        float edgeMargin = playerCollider.bounds.extents.x + 0.08f;
        float left = mainCamera.transform.position.x - halfWidth + edgeMargin;
        float right = mainCamera.transform.position.x + halfWidth - edgeMargin;
        float highestSafeTop = mainCamera.transform.position.y + mainCamera.orthographicSize - 1f;
        Collider2D rescue = null;
        foreach (Collider2D platform in FindObjectsByType<Collider2D>())
        {
            if (!platform.enabled || platform.isTrigger || !platform.CompareTag("Platform") ||
                platform.GetComponentInChildren<DoodlikHazard>() != null ||
                platform.GetComponent<CrumblingPlatform>() != null) continue;
            Bounds bounds = platform.bounds;
            if (bounds.max.y < bottomBorder + 0.35f || bounds.max.y > highestSafeTop ||
                bounds.size.x < edgeMargin * 2f || bounds.center.x < left || bounds.center.x > right) continue;
            if (rescue == null || bounds.max.y < rescue.bounds.max.y) rescue = platform;
        }

        float feetOffset = rb.position.y - playerCollider.bounds.min.y;
        Vector2 position = rescue != null
            ? new Vector2(rescue.bounds.center.x, rescue.bounds.max.y + feetOffset + 0.03f)
            : new Vector2(Mathf.Clamp(rb.position.x, left, right), bottomBorder + 0.75f + feetOffset);
        rb.position = position;
        transform.position = new Vector3(position.x, position.y, transform.position.z);
        rb.linearVelocity = new Vector2(0f, NormalJumpForce);
        Physics2D.SyncTransforms();
        previousHazardPosition = spriteRenderer.bounds.center;
        previousFeetPosition = FeetPosition();
        squashTimer = landingSquashDuration;
        GameAudio.Play(GameAudio.Cue.Jump);
    }
#endif

    private void Die(string message = "Ой! Котик упал")
    {
        if (AdminCheatEnabled) return;
        RefreshAdminState();
        if (isDead)
        {
            return;
        }

        isDead = true;
        Journal?.EndRun(message);
        Challenges?.EndRun();
        rb.linearVelocity = Vector2.zero;
        PlayerPrefs.Save();
        if (!IsTestAttempt && score > highScoreAtRunStart) GameAudio.Play(GameAudio.Cue.Record);

        if (gameOverPanel != null)
        {
            UpdateGameOverStats();
            Transform titleTransform = gameOverPanel.transform.Find("Game Over Card/GameOverText");
            if (titleTransform != null && titleTransform.TryGetComponent(out TMP_Text title))
                title.text = message;
            gameOverPanel.SetActive(true);
        }

        Time.timeScale = 0f;
        Expedition?.EndRun(ExpeditionOutcome.Lost, true);
    }

    private void UpdateGameOverStats()
    {
        foreach (Image image in gameOverPanel.GetComponentsInChildren<Image>(true))
            if (image.name == "Sad Cat Image")
                image.color = CatAppearance.GetCatTint(PlayerProgress.EquippedId(PlayerProgress.Category.Cat));
        Transform statsTransform = gameOverPanel.transform.Find(
            "Game Over Card/Game Over Stats"
        );

        if (statsTransform == null)
        {
            Transform parent = gameOverPanel.transform.Find("Game Over Card");
            if (parent == null)
            {
                parent = gameOverPanel.transform;
            }

            GameObject statsObject = new GameObject(
                "Game Over Stats",
                typeof(RectTransform)
            );
            statsObject.transform.SetParent(parent, false);
            statsTransform = statsObject.transform;

            RectTransform rect = statsObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -150f);
            rect.sizeDelta = new Vector2(470f, 70f);

            TMP_Text createdText = statsObject.AddComponent<TextMeshProUGUI>();
            createdText.alignment = TextAlignmentOptions.Center;
            createdText.fontSize = 27f;
            createdText.fontStyle = FontStyles.Bold;
            createdText.color = new Color(0.36f, 0.17f, 0.06f, 1f);
        }

        TMP_Text statsText = statsTransform.GetComponent<TMP_Text>();
        if (statsText != null)
        {
            RectTransform statsRect = statsText.rectTransform;
            statsRect.anchoredPosition = new Vector2(0f, -144f);
            statsRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                520f
            );
            statsRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                84f
            );
            statsText.fontSize = 23f;
            statsText.textWrappingMode = TextWrappingModes.NoWrap;
            statsText.overflowMode = TextOverflowModes.Overflow;
            statsText.text = $"Высота: {reachedHeight}    Лучшая: {bestHeight}\n" +
                $"Счёт: {score}    Рекорд: {highScore}\n" +
                $"<size=80%>{DifficultyRules.Name(runDifficulty)} · {DifficultyRules.MultiplierLabel(runDifficulty)}</size>";
        }

        Transform celebration = gameOverPanel.transform.Find(
            "Game Over Card/New Record Celebration"
        );
        if (celebration != null)
        {
            RectTransform celebrationRect = (RectTransform)celebration;
            celebrationRect.anchoredPosition = new Vector2(0f, -207f);
            celebrationRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 36f);
            if (celebration.TryGetComponent(out TMP_Text celebrationText)) celebrationText.fontSize = 26f;
            celebration.gameObject.SetActive(!IsTestAttempt && score > highScoreAtRunStart);
        }
        Button restart = gameOverPanel.GetComponentInChildren<Button>(true);
        if (restart != null)
        {
            RectTransform buttonRect = (RectTransform)restart.transform;
            buttonRect.anchoredPosition = new Vector2(0f, -274f);
            buttonRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 74f);
        }
    }

    public void RestartGame()
    {
        GameMenuUI.RestartCurrentSceneSmoothly();
    }

    private void AddKeyboardHintToRestartButton()
    {
        Button restartButton = gameOverPanel.GetComponentInChildren<Button>(true);
        if (restartButton == null)
        {
            return;
        }

        TMP_Text restartLabel =
            restartButton.GetComponentInChildren<TMP_Text>(true);
        if (restartLabel != null && !restartLabel.text.Contains("(R)"))
        {
            restartLabel.text += " (R)";
        }
    }

    private void OnApplicationPause(bool isPaused)
    {
        if (isPaused)
        {
            PlayerPrefs.Save();
        }
    }

    private void OnApplicationQuit()
    {
        Expedition?.EndRun(ExpeditionOutcome.Abandoned);
        Journal?.EndRun("Выход");
        PlayerPrefs.Save();
    }

    public void BounceFromBush()
    {
        if (isDead || !RunFlow.IsPlaying || Time.timeScale <= 0f ||
            powers != null && powers.IsRocketActive || !ResolveTravelBeforeBounce())
        {
            return;
        }

        rb.linearVelocity = new Vector2(
            rb.linearVelocity.x,
            Mathf.Max(rb.linearVelocity.y, bushJumpForce * AdminRuntime.BushMultiplier)
        );
        Challenges?.RecordBush();
        GameAudio.Play(GameAudio.Cue.Bush);
    }

    public bool AdminTestingInvulnerable => AdminCheatEnabled;

    internal void DefeatByExpeditionEnemy()
    {
        if (Expedition == null || RunKind != GameRunKind.Expedition || isDead) return;
        Die("Ой! Грозовой жук!");
    }

    public void BounceFromExpeditionEnemy(Vector2 contact)
    {
        if (isDead || Expedition == null || !Expedition.CanFight || rb == null || playerCollider == null ||
            !float.IsFinite(contact.x) || !float.IsFinite(contact.y)) return;
        float width = mainCamera.orthographicSize * mainCamera.aspect * 2f;
        float x = rb.position.x + TrapGeometry.WrappedDelta(rb.position.x, contact.x, width);
        float feetOffset = rb.position.y - playerCollider.bounds.min.y;
        rb.position = new Vector2(x, contact.y + feetOffset + .025f * GameplayPreferences.ModelScale);
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, NormalJumpForce);
        squashTimer = landingSquashDuration;
        Physics2D.SyncTransforms();
        Expedition.CancelPendingLandingAfterEnemyBounce();
        GameAudio.Play(GameAudio.Cue.Jump);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
    internal bool JumpFromAdmin()
    {
        if (!PlayerProgress.AdminSandboxActive || isDead || !RunFlow.IsPlaying || rb == null ||
            powers != null && powers.IsRocketActive) return false;
        RefreshAdminState();
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, NormalJumpForce);
        return true;
    }
#endif

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isDead || !RunFlow.IsPlaying)
        {
            return;
        }

        if (collision.gameObject.CompareTag("Platform") &&
            rb.linearVelocity.y <= 0)
        {
            if (!ResolveTravelBeforeBounce(true)) return;
            CorrectLandingPenetration(collision.collider);
            if (powers.IsRocketActive && !powers.TryCompleteRocketLanding(collision.collider)) return;
            Challenges?.RecordLanding(collision.collider);

            float inheritedVelocity = 0f;
            MovingPlatform movingPlatform =
                collision.gameObject.GetComponent<MovingPlatform>();
            if (movingPlatform != null)
            {
                float movementScale =
                    GameplayPreferences.HorizontalMovementScale;
                inheritedVelocity = Mathf.Clamp(
                    movingPlatform.CurrentVelocity.x *
                    movingPlatformMomentum * movementScale,
                    -maximumInheritedSpeed * movementScale,
                    maximumInheritedSpeed * movementScale
                );
            }

            squashTimer = landingSquashDuration;
            rb.linearVelocity = new Vector2(
                rb.linearVelocity.x + inheritedVelocity,
                NormalJumpForce
            );
            collision.gameObject.GetComponent<CrumblingPlatform>()?.OnLanded();
            GameAudio.Play(GameAudio.Cue.Jump);
            Expedition?.RecordLanding(collision.collider);
        }
    }

    private void CorrectLandingPenetration(Collider2D platformCollider)
    {
        if (playerCollider == null || platformCollider == null)
        {
            return;
        }

        float penetration =
            platformCollider.bounds.max.y - playerCollider.bounds.min.y;

        // Исправляем только небольшое вертикальное проникновение при посадке.
        // Большое значение означает, что столкновение произошло не сверху.
        if (penetration > 0f && penetration < 0.35f)
        {
            rb.position += Vector2.up * (penetration + 0.01f);
            Physics2D.SyncTransforms();
        }
    }
}
