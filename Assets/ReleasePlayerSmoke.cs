#if UNITY_EDITOR || DOODLIK_RELEASE_SMOKE
using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Compiled only into the isolated, non-development release validation player.
// Every game component below still enters through its ordinary scene lifecycle.
public sealed class ReleasePlayerSmoke : MonoBehaviour
{
    private enum Phase { Home, FirstBounce, Paused, Resumed, Falling, Restarting, SecondBounce, ReturningHome, ThirdBounce }
    private const string SmokeCompany = "CodexValidation";
    private const string SmokeProduct = "DoodlikReleaseSmoke";
    private const double TimeoutSeconds = 75;
    private Phase phase;
    private Player player;
    private Rigidbody2D body;
    private GameMenuUI menu;
    private Button menuButton;
    private Player previousPlayer;
    private bool sawGravity, sawBounce, finished;
    private double startedAt, phaseStartedAt;
    private Vector2 pausePosition;
    private int checks;
    private static bool IsSmokePlayer => Application.isBatchMode &&
        Application.companyName == SmokeCompany && Application.productName == SmokeProduct;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void PrepareIsolatedPreferences()
    {
        if (!IsSmokePlayer) return;
        // This identity has its own PlayerPrefs branch; never reset real saves.
        GameplayPreferences.SetMode(false, false, false);
        GameplayPreferences.SetDifficulty(GameDifficulty.Normal);
        AudioListener.pause = true;
        AudioListener.volume = 0f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        if (!IsSmokePlayer) return;
        GameObject host = new GameObject("Release Player Smoke");
        DontDestroyOnLoad(host);
        host.AddComponent<ReleasePlayerSmoke>();
    }

    private void Awake()
    {
        startedAt = phaseStartedAt = Time.realtimeSinceStartupAsDouble;
        Application.logMessageReceived += OnLog;
        AudioListener.pause = true;
        AudioListener.volume = 0f;
        Debug.Log("RELEASE_PLAYER_SMOKE_BEGIN version=" + Application.version +
            " development=" + Debug.isDebugBuild + " scene=" + SceneManager.GetActiveScene().path);
    }

    private void Update()
    {
        AudioListener.volume = 0f;
        if (finished) return;
        try
        {
            if (Time.realtimeSinceStartupAsDouble - startedAt > TimeoutSeconds)
                throw new TimeoutException("Release player smoke timed out in " + phase);
            double elapsed = Time.realtimeSinceStartupAsDouble - phaseStartedAt;
            if (Time.frameCount < 5 || elapsed < 0.20) return;
            switch (phase)
            {
                case Phase.Home:
                    Check(!Debug.isDebugBuild, "Actual player is a non-development build.");
                    BindNaturalScene();
                    Check(!RunFlow.RunActive && menu.GetComponent<FrontEndUI>().IsVisible,
                        "Natural launch opens the home screen.");
                    Check(Time.timeScale == 0f, "Home pauses the prepared scene.");
                    menu.StartRunFromHome();
                    Check(RunFlow.IsPlaying && !GameMenuUI.BlocksGameplayInput && Time.timeScale > 0f,
                        "The real home action starts gameplay.");
                    BeginBounceObservation();
                    SetPhase(Phase.FirstBounce);
                    break;
                case Phase.FirstBounce:
                    if (!ObserveNaturalBounce(elapsed)) return;
                    menuButton = FindMenuButton();
                    menuButton.onClick.Invoke();
                    Check(Time.timeScale == 0f && GameMenuUI.BlocksGameplayInput,
                        "The menu button pauses gameplay.");
                    pausePosition = body.position;
                    SetPhase(Phase.Paused);
                    break;
                case Phase.Paused:
                    Check((body.position - pausePosition).sqrMagnitude < 0.000001f,
                        "Physics remains stopped while the menu is open.");
                    menuButton.onClick.Invoke();
                    Check(Time.timeScale > 0f && !GameMenuUI.BlocksGameplayInput,
                        "The same menu button resumes gameplay.");
                    SetPhase(Phase.Resumed);
                    break;
                case Phase.Resumed:
                    Check(!player.IsDead && RunFlow.IsPlaying, "The player survives pause and resume.");
                    int removed = 0;
                    foreach (GameObject platform in GameObject.FindGameObjectsWithTag("Platform"))
                    {
                        Collider2D support = platform.GetComponent<Collider2D>();
                        if (support != null && support.bounds.max.y < body.position.y + 0.5f)
                        {
                            Destroy(platform);
                            removed++;
                        }
                    }
                    Check(removed > 0, "The death scenario removes existing lower supports.");
                    body.linearVelocity = new Vector2(0f, -8f);
                    Debug.Log("RELEASE_PLAYER_SMOKE_FALL removedLowerPlatforms=" + removed);
                    SetPhase(Phase.Falling);
                    break;
                case Phase.Falling:
                    if (!player.IsDead)
                    {
                        Check(elapsed < 12, "An unsupported player must die below the camera.");
                        return;
                    }
                    Check(player.gameOverPanel != null && player.gameOverPanel.activeInHierarchy && Time.timeScale == 0f,
                        "An actual fall displays the results and stops the attempt.");
                    previousPlayer = player;
                    GameMenuUI.RestartCurrentSceneSmoothly();
                    SetPhase(Phase.Restarting);
                    break;
                case Phase.Restarting:
                    if (RunFlow.IsTransitioning) return;
                    BindNaturalScene();
                    Check(!ReferenceEquals(player, previousPlayer) && !player.IsDead,
                        "Restart creates a fresh, living scene player.");
                    Check(RunFlow.IsPlaying && Time.timeScale > 0f, "Restart resumes an active run.");
                    BeginBounceObservation();
                    SetPhase(Phase.SecondBounce);
                    break;
                case Phase.SecondBounce:
                    if (!ObserveNaturalBounce(elapsed)) return;
                    previousPlayer = player;
                    menu.ReturnToHome();
                    SetPhase(Phase.ReturningHome);
                    break;
                case Phase.ReturningHome:
                    if (RunFlow.IsTransitioning) return;
                    BindNaturalScene();
                    Check(!ReferenceEquals(player, previousPlayer) && !RunFlow.RunActive &&
                        menu.GetComponent<FrontEndUI>().IsVisible && Time.timeScale == 0f,
                        "Returning home rebuilds the scene and presents a paused home screen.");
                    menu.StartRunFromHome();
                    Check(RunFlow.IsPlaying && Time.timeScale > 0f, "A new attempt starts after returning home.");
                    BeginBounceObservation();
                    SetPhase(Phase.ThirdBounce);
                    break;
                case Phase.ThirdBounce:
                    if (ObserveNaturalBounce(elapsed)) Finish(null);
                    break;
            }
        }
        catch (Exception exception) { Finish(exception.ToString()); }
    }

    private void BindNaturalScene()
    {
        player = FindAnyObjectByType<Player>();
        Spawner spawner = FindAnyObjectByType<Spawner>();
        CameraFollow follow = FindAnyObjectByType<CameraFollow>();
        menu = FindAnyObjectByType<GameMenuUI>();
        Check(player != null && spawner != null && follow != null && menu != null,
            "The serialized scene retains player, generator, camera-follow and menu scripts.");
        Check(player.enabled && spawner.enabled && follow.enabled && follow.player == player.transform,
            "All natural gameplay components are enabled and connected.");
        body = player.GetComponent<Rigidbody2D>();
        Check(body != null && body.simulated && body.bodyType == RigidbodyType2D.Dynamic,
            "The real player has an enabled dynamic physics body.");
        int platforms = GameObject.FindGameObjectsWithTag("Platform").Length;
        Check(platforms >= 5, "Scene Start naturally generated at least five platforms.");
        JourneySettings journey = Resources.Load<JourneySettings>(JourneySettings.ResourcePath);
        Check(journey != null && journey.GoalScore == WorldZones.GoalHeight,
            "The serialized journey settings retain their runtime script.");
        foreach (GameObject platform in GameObject.FindGameObjectsWithTag("Platform"))
            Check(platform.GetComponent<Platform>() != null, "Generated supports retain their platform script.");
        Debug.Log("RELEASE_PLAYER_SMOKE_SCENE platforms=" + platforms + " player=" + player.name);
    }

    private void BeginBounceObservation() { sawGravity = sawBounce = false; }

    private bool ObserveNaturalBounce(double elapsed)
    {
        Check(player != null && !player.IsDead && RunFlow.IsPlaying && body.simulated,
            "The natural bounce scenario remains in a living active attempt.");
        if (body.linearVelocity.y < -0.2f) sawGravity = true;
        if (sawGravity && body.linearVelocity.y > player.NormalJumpForce * 0.5f) sawBounce = true;
        if (!sawBounce || player.CurrentHeight <= 0)
        {
            Check(elapsed < 12, "Real gravity, a real platform bounce and height gain must occur.");
            return false;
        }
        Debug.Log("RELEASE_PLAYER_SMOKE_BOUNCE phase=" + phase + " height=" + player.CurrentHeight +
            " score=" + player.CurrentScore + " velocity=" + body.linearVelocity);
        return true;
    }

    private Button FindMenuButton()
    {
        foreach (Button button in menu.GetComponentsInChildren<Button>(false))
        {
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (button.transform.parent == menu.transform && label != null && label.text == "Меню") return button;
        }
        throw new InvalidOperationException("The active in-game menu button is missing.");
    }

    private void SetPhase(Phase next)
    {
        phase = next;
        phaseStartedAt = Time.realtimeSinceStartupAsDouble;
    }

    private void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message + " Phase=" + phase);
        checks++;
    }

    private void OnLog(string condition, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            Finish(condition + "\n" + trace);
    }

    private void Finish(string failure)
    {
        if (finished) return;
        finished = true;
        Application.logMessageReceived -= OnLog;
        if (failure == null) Debug.Log("RELEASE_PLAYER_SMOKE_PASS checks=" + checks);
        else Debug.LogError("RELEASE_PLAYER_SMOKE_FAIL phase=" + phase + " " + failure);
        Application.Quit(failure == null ? 0 : 1);
    }

    private void OnDestroy() { Application.logMessageReceived -= OnLog; }
}
#endif
