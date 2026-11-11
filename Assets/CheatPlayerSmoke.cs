#if DOODLIK_CHEATS_SMOKE
using System;
using System.Reflection;
using TMPro;
using UnityEngine;

// Included only in a separate disposable QA player, never in the handed-off EXE.
public sealed class CheatPlayerSmoke : MonoBehaviour
{
    private enum Phase { Home, StartRun, Bounce, Restart }
    private Phase phase;
    private double started, phaseStarted;
    private bool finished, sawFall;
    private int checks, baselineCoins;
    private string primary, backup;
    private Player player, previousPlayer;
    private Rigidbody2D body;
    private AdminTools tools;
    private GameMenuUI menu;
    private static bool IsSafePlayer => Application.isBatchMode &&
        Application.companyName == "CodexValidation" && Application.productName == "DoodlikCheatsSmoke";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Prepare()
    {
        if (!IsSafePlayer) return;
        GameplayPreferences.SetMode(false, false, false);
        GameplayPreferences.SetDifficulty(GameDifficulty.Normal);
        AudioListener.pause = true;
        AudioListener.volume = 0f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        if (!IsSafePlayer) return;
        GameObject root = new GameObject("Cheats Player Smoke");
        DontDestroyOnLoad(root);
        root.AddComponent<CheatPlayerSmoke>();
    }

    private void Awake()
    {
        started = phaseStarted = Time.realtimeSinceStartupAsDouble;
        Application.logMessageReceived += CaptureError;
        Debug.Log("CHEATS_PLAYER_SMOKE_BEGIN version=" + Application.version + " development=" + Debug.isDebugBuild);
    }

    private void Update()
    {
        AudioListener.pause = true;
        AudioListener.volume = 0f;
        if (finished) return;
        try
        {
            double now = Time.realtimeSinceStartupAsDouble;
            Check(now - started < 65, "Smoke must finish within 65 seconds.");
            if (Time.frameCount < 8 || now - phaseStarted < 0.25) return;
            switch (phase)
            {
                case Phase.Home: CheckHomeAndCommands(); break;
                case Phase.StartRun:
                    if (!RunFlow.IsPlaying)
                    {
                        menu.StartRunFromHome();
                        if (!RunFlow.IsPlaying) return;
                    }
                    CheckRunCommands();
                    break;
                case Phase.Bounce:
                    Check(player != null && !player.IsDead && RunFlow.IsPlaying, "Cheat attempt stays alive.");
                    if (body.linearVelocity.y < -0.3f) sawFall = true;
                    if (!sawFall || body.linearVelocity.y < player.NormalJumpForce * 0.5f) return;
                    Check(player.CurrentHeight > 0, "Actual physics advances height and bounces.");
                    Debug.Log("CHEATS_PLAYER_SMOKE_BOUNCE height=" + player.CurrentHeight);
                    tools.Open();
                    tools.EndSandbox();
                    Check(!AdminTools.IsOpen && !AdminTools.CheatMode && !PlayerProgress.AdminSandboxActive,
                        "Ending sandbox closes panel and clears invincibility.");
                    Check(AdminRuntime.MovementMultiplier == 1f && AdminRuntime.JumpMultiplier == 1f &&
                        AdminRuntime.GravityMultiplier == 1f && AdminRuntime.BushMultiplier == 1f &&
                        AdminRuntime.GameSpeed == 1f && AdminRuntime.BuffDuration == 15f,
                        "Ending sandbox restores neutral tuning.");
                    Check(player.IsTestAttempt, "Current attempt stays ineligible after cheats end.");
                    Check(PlayerProgress.Coins == baselineCoins, "Original wallet restored exactly.");
                    CheckSaves();
                    previousPlayer = player;
                    GameMenuUI.RestartCurrentSceneSmoothly();
                    phase = Phase.Restart; phaseStarted = now;
                    break;
                case Phase.Restart:
                    if (RunFlow.IsTransitioning) return;
                    player = FindAnyObjectByType<Player>();
                    Check(player != null && !ReferenceEquals(player, previousPlayer) && !player.IsDead,
                        "Restart creates a fresh live player.");
                    Check(!player.IsTestAttempt && !PlayerProgress.AdminSandboxActive && RunFlow.IsPlaying,
                        "New ordinary attempt no longer inherits sandbox eligibility.");
                    CheckSaves();
                    Finish(null);
                    break;
            }
        }
        catch (Exception exception) { Finish(exception.ToString()); }
    }

    private void CheckHomeAndCommands()
    {
        Check(!Debug.isDebugBuild && Application.version == "0.0.4", "Expected non-Development 0.0.4 player.");
        player = FindAnyObjectByType<Player>();
        menu = FindAnyObjectByType<GameMenuUI>();
        tools = FindAnyObjectByType<AdminTools>();
        Check(player != null && menu != null && tools != null && AdminTools.IsAvailable, "Scene naturally creates cheat panel.");
        Check(!RunFlow.RunActive && Time.timeScale == 0f, "Natural home screen remains paused.");
        Check(GameObject.FindGameObjectsWithTag("Platform").Length >= 5, "Natural generator creates platforms.");
        Check(!AdminRuntime.SetMovementMultiplier(2f) && !AdminRuntime.GrantPower(RunPowerups.Kind.Shield),
            "Commands reject use outside sandbox.");
        PlayerProgress.AddCoins(37);
        PlayerProgress.Flush();
        baselineCoins = PlayerProgress.Coins;
        primary = PlayerPrefs.GetString(PlayerProgress.SaveKey);
        backup = PlayerPrefs.GetString(PlayerProgress.BackupKey);
        tools.Open();
        Check(AdminTools.IsOpen && Time.timeScale == 0f && PlayerProgress.AdminSandboxActive, "Panel opens paused sandbox.");
        foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
            Check(PlayerProgress.Owns(item.Id), "Wardrobe unlock: " + item.Id);
        TMP_InputField amount = (TMP_InputField)typeof(AdminTools).GetField("amount", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tools);
        amount.text = "123";
        Check(tools.ChangeCoins(1) && PlayerProgress.Coins == baselineCoins + 123, "UI currency grant.");
        Check(tools.ChangeCoins(-1) && PlayerProgress.Coins == baselineCoins, "UI currency withdrawal.");
        amount.text = "0";
        Check(!tools.ChangeCoins(1), "Invalid currency amount rejected.");
        Check(!AdminRuntime.SetMovementMultiplier(float.NaN) && !AdminRuntime.SetGameSpeed(float.PositiveInfinity),
            "Non-finite tuning rejected.");
        Check(AdminRuntime.SetMovementMultiplier(99f) && AdminRuntime.MovementMultiplier == 3f, "Movement cap.");
        Check(AdminRuntime.SetMovementMultiplier(1.5f) && AdminRuntime.SetJumpMultiplier(1.2f) &&
            AdminRuntime.SetGravityMultiplier(0.8f) && AdminRuntime.SetBushMultiplier(1.3f) &&
            AdminRuntime.SetGameSpeed(0.5f) && AdminRuntime.SetBuffDuration(27f), "All six tuning controls apply.");
        Check(Time.timeScale == 0f, "Tuning never resumes a paused panel.");
        tools.Close();
        Check(!AdminTools.IsOpen && Time.timeScale == 0f, "Closing from home preserves home pause.");
        phase = Phase.StartRun; phaseStarted = Time.realtimeSinceStartupAsDouble;
    }

    private void CheckRunCommands()
    {
        Check(RunFlow.IsPlaying, "Real home action starts gameplay.");
        body = player.GetComponent<Rigidbody2D>();
        RunPowerups powers = player.GetComponent<RunPowerups>();
        Check(body != null && body.simulated && powers != null, "Real gameplay body and powers.");
        AdminRuntime.ApplyGameSpeed();
        Check(Mathf.Abs(Time.timeScale - 0.5f) < 0.001f, "Actual game speed applies.");
        AdminTools.SetCheatMode(true);
        Check(player.IsTestAttempt, "Cheat activation marks the actual attempt ineligible.");
        tools.Open();
        Vector2 position = body.position;
        Check(AdminTools.CheatMode && Time.timeScale == 0f && GameMenuUI.BlocksGameplayInput, "Invincibility and panel input block.");
        Check(AdminRuntime.GrantPower(RunPowerups.Kind.Shield) && powers.HasShield, "Shield command.");
        Check(AdminRuntime.GrantPower(RunPowerups.Kind.Magnet) && powers.MagnetTimeRemaining == 27f, "Magnet duration command.");
        Check(AdminRuntime.GrantPower(RunPowerups.Kind.Slow) && powers.SlowTimeRemaining == 27f, "Slow duration command.");
        Check(AdminRuntime.GrantPower(RunPowerups.Kind.Rocket) && powers.IsRocketActive, "Rocket starts with a real safe support.");
        Check(Time.timeScale == 0f && body.position == position, "Rocket launch while panel paused never advances physics.");
        Check(AdminRuntime.ClearPowers() && !powers.HasShield && !powers.IsRocketActive &&
            powers.MagnetTimeRemaining == 0f && powers.SlowTimeRemaining == 0f, "Clear removes powers including rocket.");
        Check(AdminRuntime.JumpNow() && body.linearVelocity.y > 0f, "Manual jump launches actual body.");
        CheckSaves();
        tools.Close();
        Check(!AdminTools.IsOpen && Mathf.Abs(Time.timeScale - 0.5f) < 0.001f, "Closing restores selected speed.");
        phase = Phase.Bounce; phaseStarted = Time.realtimeSinceStartupAsDouble;
    }

    private void CheckSaves()
    {
        Check(PlayerPrefs.GetString(PlayerProgress.SaveKey) == primary && PlayerPrefs.GetString(PlayerProgress.BackupKey) == backup,
            "Primary and backup progress remain byte-identical.");
    }

    private void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message + " Phase=" + phase);
        checks++;
    }

    private void CaptureError(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Finish(message + "\n" + trace);
    }

    private void Finish(string failure)
    {
        if (finished) return;
        finished = true;
        Application.logMessageReceived -= CaptureError;
        if (failure == null) Debug.Log("CHEATS_PLAYER_SMOKE_PASS checks=" + checks);
        else Debug.LogError("CHEATS_PLAYER_SMOKE_FAIL " + failure);
        Application.Quit(failure == null ? 0 : 1);
    }

    private void OnDestroy() { Application.logMessageReceived -= CaptureError; }
}
#endif
