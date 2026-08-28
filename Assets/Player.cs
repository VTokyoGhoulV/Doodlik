using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class Player : MonoBehaviour
{
    public const string HighScoreKey = "DoodlikHighScore";

    [Header("Movement")]
    public float moveSpeed = 7f;
    public float jumpForce = 12f;

    [Header("Movement Feel")]
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
    private int highScore;
    private int highScoreAtRunStart;

    private bool isDead;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        playerCollider = GetComponent<Collider2D>();
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        mainCamera = Camera.main;
        SetupVisual();

        isDead = false;
        Time.timeScale = 1f;

        startHeight = transform.position.y;
        maximumHeight = startHeight;
        score = 0;
        highScore = PlayerPrefs.GetInt(HighScoreKey, 0);
        highScoreAtRunStart = highScore;

        if (scoreText != null)
        {
            scoreText.color = new Color(0.27f, 0.12f, 0.035f, 1f);
            scoreText.fontStyle = FontStyles.Bold;
            scoreText.rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                80f
            );
        }

        UpdateScoreText();

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
            AddKeyboardHintToRestartButton();
        }
    }

    private void Update()
    {
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
        CheckDeath();
    }

    private void FixedUpdate()
    {
        if (isDead)
        {
            return;
        }

        ApplyHorizontalMovement();
        ApplyGravityCurve();
    }

    private void ApplyHorizontalMovement()
    {
        float targetSpeed = horizontalInput * moveSpeed;
        bool isTurning =
            Mathf.Abs(horizontalInput) > 0.01f &&
            Mathf.Abs(rb.linearVelocity.x) > 0.1f &&
            Mathf.Sign(targetSpeed) != Mathf.Sign(rb.linearVelocity.x);
        float control = rb.linearVelocity.y < 0f ? fallingControl : risingControl;
        float changeRate;

        if (Mathf.Abs(horizontalInput) < 0.01f)
        {
            changeRate = airDeceleration;
        }
        else
        {
            changeRate = isTurning ? turnAcceleration : acceleration;
            changeRate *= control;
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

    private void ApplyGravityCurve()
    {
        float verticalSpeed = rb.linearVelocity.y;

        if (Mathf.Abs(verticalSpeed) <= apexVelocityThreshold)
        {
            rb.gravityScale = apexGravityScale;
        }
        else if (verticalSpeed > 0f)
        {
            rb.gravityScale = risingGravityScale;
        }
        else
        {
            rb.gravityScale = fallingGravityScale;
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
                if (!touch.press.isPressed)
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
        // Запоминаем только наибольшую достигнутую высоту
        maximumHeight = Mathf.Max(
            maximumHeight,
            transform.position.y
        );

        float gainedHeight = maximumHeight - startHeight;

        int newScore = Mathf.FloorToInt(
            gainedHeight * pointsPerUnit
        );

        if (newScore == score)
        {
            return;
        }

        score = newScore;

        if (score > highScore)
        {
            highScore = score;
            PlayerPrefs.SetInt(HighScoreKey, highScore);
        }

        UpdateScoreText();
    }

    private void UpdateScoreText()
    {
        if (scoreText != null)
        {
            scoreText.text = $"Счёт: {score}\nРекорд: {highScore}";
        }
    }

    private void ScreenWrap()
    {
        float cameraHeight = mainCamera.orthographicSize;
        float cameraWidth = cameraHeight * mainCamera.aspect;

        float leftBorder =
            mainCamera.transform.position.x - cameraWidth;

        float rightBorder =
            mainCamera.transform.position.x + cameraWidth;

        if (transform.position.x < leftBorder)
        {
            transform.position = new Vector3(
                rightBorder,
                transform.position.y,
                transform.position.z
            );
        }
        else if (transform.position.x > rightBorder)
        {
            transform.position = new Vector3(
                leftBorder,
                transform.position.y,
                transform.position.z
            );
        }
    }

    private void CheckDeath()
    {
        float bottomBorder =
            mainCamera.transform.position.y -
            mainCamera.orthographicSize;

        if (transform.position.y < bottomBorder - deathOffset)
        {
            Die();
        }
    }

    private void Die()
    {
        if (isDead)
        {
            return;
        }

        isDead = true;
        rb.linearVelocity = Vector2.zero;
        PlayerPrefs.Save();

        if (gameOverPanel != null)
        {
            UpdateGameOverStats();
            gameOverPanel.SetActive(true);
        }

        Time.timeScale = 0f;
    }

    private void UpdateGameOverStats()
    {
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
            statsText.text = $"Счёт: {score}     Рекорд: {highScore}";
        }

        Transform celebration = gameOverPanel.transform.Find(
            "Game Over Card/New Record Celebration"
        );
        if (celebration != null)
        {
            celebration.gameObject.SetActive(score > highScoreAtRunStart);
        }
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;

        SceneManager.LoadScene(
            SceneManager.GetActiveScene().buildIndex
        );
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
        PlayerPrefs.Save();
    }

    public void BounceFromBush()
    {
        if (isDead)
        {
            return;
        }

        rb.linearVelocity = new Vector2(
            rb.linearVelocity.x,
            Mathf.Max(rb.linearVelocity.y, bushJumpForce)
        );
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isDead)
        {
            return;
        }

        if (collision.gameObject.CompareTag("Platform") &&
            rb.linearVelocity.y <= 0)
        {
            CorrectLandingPenetration(collision.collider);

            float inheritedVelocity = 0f;
            MovingPlatform movingPlatform =
                collision.gameObject.GetComponent<MovingPlatform>();
            if (movingPlatform != null)
            {
                inheritedVelocity = Mathf.Clamp(
                    movingPlatform.CurrentVelocity.x * movingPlatformMomentum,
                    -maximumInheritedSpeed,
                    maximumInheritedSpeed
                );
            }

            squashTimer = landingSquashDuration;
            rb.linearVelocity = new Vector2(
                rb.linearVelocity.x + inheritedVelocity,
                jumpForce
            );
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
