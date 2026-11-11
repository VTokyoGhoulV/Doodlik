#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

// Synchronous Editor-only tests; run before entering Play Mode in a QA copy.
public static class AdminRuntimeValidation
{
    private static int checks;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static int Validate()
    {
        Require(Application.isBatchMode && !Application.isPlaying &&
            Application.dataPath.Replace('\\', '/').Contains("/.utmp/"), "Isolated batch Edit Mode only.");
        Require(Object.FindAnyObjectByType<Player>() == null, "No live player or scene is mutated.");
        checks = 0;
        float savedTime = Time.timeScale;
        bool savedActive = RunFlow.RunActive, savedTransition = RunFlow.IsTransitioning;
        GameObject root = null;
        try
        {
            using (PlayerProgress.UseIsolatedStorageForValidation(out string primary, out string backup))
            {
                PlayerProgress.AddCoins(123);
                PlayerProgress.Flush();
                string ordinary = PlayerPrefs.GetString(primary), ordinaryBackup = PlayerPrefs.GetString(backup);
                AdminRuntime.ResetTuning();
                RunFlow.RunActive = true;
                RunFlow.IsTransitioning = false;
                Time.timeScale = .6f;
                AdminRuntime.ApplyGameSpeed();
                Check(Time.timeScale == .6f, "Ordinary gameplay time is not overridden outside the sandbox.");
                Check(!AdminRuntime.SetMovementMultiplier(2f) && !AdminRuntime.SetJumpMultiplier(2f) &&
                    !AdminRuntime.SetGravityMultiplier(2f) && !AdminRuntime.SetBushMultiplier(2f) &&
                    !AdminRuntime.SetGameSpeed(2f) && !AdminRuntime.SetBuffDuration(30f), "No settings outside sandbox.");
                Check(!AdminRuntime.GrantPower(RunPowerups.Kind.Shield) && !AdminRuntime.JumpNow() &&
                    !AdminRuntime.ClearPowers(), "No commands outside sandbox.");
                PlayerProgress.BeginAdminSandbox();
                Bounds(AdminRuntime.SetMovementMultiplier, () => AdminRuntime.MovementMultiplier, .5f, 3f);
                Bounds(AdminRuntime.SetJumpMultiplier, () => AdminRuntime.JumpMultiplier, .5f, 2f);
                Bounds(AdminRuntime.SetGravityMultiplier, () => AdminRuntime.GravityMultiplier, .4f, 2f);
                Bounds(AdminRuntime.SetBushMultiplier, () => AdminRuntime.BushMultiplier, .5f, 2f);
                Bounds(AdminRuntime.SetGameSpeed, () => AdminRuntime.GameSpeed, .25f, 2f);
                Bounds(AdminRuntime.SetBuffDuration, () => AdminRuntime.BuffDuration, 1f, 120f);
                Time.timeScale = 0f;
                RunFlow.RunActive = true;
                RunFlow.IsTransitioning = false;
                AdminRuntime.SetGameSpeed(.5f);
                AdminRuntime.ApplyGameSpeed();
                Check(Time.timeScale == 0f, "Setting game speed cannot resume pause.");
                Time.timeScale = 1f;
                AdminRuntime.ApplyGameSpeed();
                Check(Time.timeScale == .5f, "Positive live game time applies selected speed.");
                RunFlow.IsTransitioning = true;
                Time.timeScale = 1f;
                AdminRuntime.ApplyGameSpeed();
                Check(Time.timeScale == 1f, "Transitions do not inherit admin time overrides.");
                RunFlow.IsTransitioning = false;
                root = new GameObject("Admin runtime isolated physics fixture");
                Rigidbody2D body = root.AddComponent<Rigidbody2D>();
                root.AddComponent<BoxCollider2D>();
                Player player = root.AddComponent<Player>();
                player.enabled = false;
                Set(player, "rb", body);
                RunPowerups powers = root.AddComponent<RunPowerups>();
                powers.enabled = false;
                Set(powers, "owner", player); // Edit Mode does not dispatch runtime Awake.
                Set(player, "powers", powers);
                AdminRuntime.SetMovementMultiplier(1.75f);
                AdminRuntime.SetJumpMultiplier(1.5f);
                AdminRuntime.SetGravityMultiplier(.5f);
                AdminRuntime.SetBushMultiplier(1.25f);
                AdminRuntime.SetBuffDuration(37f);
                Check(Mathf.Abs(player.NormalJumpForce - player.jumpForce * player.normalJumpMultiplier * 1.5f) < .001f,
                    "Actual normal jump uses selected multiplier without overwriting base configuration.");
                Set(player, "horizontalInput", 1f);
                Call(player, "ApplyHorizontalMovement");
                // Precise mode is selected only in this temporary profile by the test host.
                Check(body.linearVelocity.x > 0f, "Actual movement controller responds under tuning.");
                body.linearVelocity = new Vector2(2f, 4f);
                Call(player, "ApplyGravityCurve");
                Check(Mathf.Abs(body.gravityScale - player.risingGravityScale * .5f) < .001f,
                    "Actual rising gravity uses selected multiplier.");
                Time.timeScale = 0f;
                Check(AdminRuntime.GrantPower(RunPowerups.Kind.Shield) && powers.HasShield, "Paused admin grants shield.");
                Check(AdminRuntime.GrantPower(RunPowerups.Kind.Magnet) && powers.MagnetTimeRemaining == 37f,
                    "Paused admin grants selected magnet duration.");
                Check(AdminRuntime.GrantPower(RunPowerups.Kind.Slow) && powers.SlowTimeRemaining == 37f,
                    "Paused admin grants selected slow duration.");
                Check(Time.timeScale == 0f, "Power commands do not unpause gameplay.");
                Check(!AdminRuntime.GrantPower((RunPowerups.Kind)100), "Invalid powers rejected.");
                Check(AdminRuntime.JumpNow() && Mathf.Abs(body.linearVelocity.y - player.NormalJumpForce) < .001f,
                    "Manual jump commands actual Rigidbody while paused.");
                Set(player, "isDead", true);
                Check(!AdminRuntime.JumpNow() && !AdminRuntime.GrantPower(RunPowerups.Kind.Shield), "Dead player rejects jump and power commands.");
                Set(player, "isDead", false);
                Check(AdminRuntime.ClearPowers() && !powers.HasShield && powers.MagnetTimeRemaining == 0f &&
                    powers.SlowTimeRemaining == 0f && !powers.IsProtected && !powers.IsRocketActive,
                    "Clear removes every held power and active protection.");
                AdminRuntime.ResetTuning();
                Check(Time.timeScale == 0f, "Reset preserves a paused state.");
                Check(AdminRuntime.MovementMultiplier == 1f && AdminRuntime.JumpMultiplier == 1f &&
                    AdminRuntime.GravityMultiplier == 1f && AdminRuntime.BushMultiplier == 1f &&
                    AdminRuntime.GameSpeed == 1f && AdminRuntime.BuffDuration == 15f, "Reset restores every default.");
                Check(AdminRuntime.GrantPower(RunPowerups.Kind.Shield) && AdminRuntime.GrantPower(RunPowerups.Kind.Magnet),
                    "Prepare powers for a direct programmatic sandbox exit.");
                AdminRuntime.SetMovementMultiplier(2f);
                AdminRuntime.SetGameSpeed(.5f);
                PlayerProgress.EndAdminSandbox();
                Check(!powers.HasShield && powers.MagnetTimeRemaining == 0f && powers.SlowTimeRemaining == 0f &&
                    !powers.IsRocketActive && AdminRuntime.MovementMultiplier == 1f && AdminRuntime.GameSpeed == 1f &&
                    Time.timeScale == 0f, "Programmatic exit also clears powers and tuning without resuming pause.");
                Check(PlayerPrefs.GetString(primary) == ordinary && PlayerPrefs.GetString(backup) == ordinaryBackup &&
                    PlayerProgress.Coins == 123, "All tuning and commands preserve both ordinary save copies.");
                Check(!AdminRuntime.SetGameSpeed(2f) && AdminRuntime.GameSpeed == 1f, "Exited sandbox cannot retain effects.");
            }
            Debug.Log("ADMIN_RUNTIME_PASS checks=" + checks + " bounds/pause/physics/powers/save-isolation");
            return checks;
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            AdminRuntime.ResetTuning();
            RunFlow.RunActive = savedActive;
            RunFlow.IsTransitioning = savedTransition;
            Time.timeScale = savedTime;
        }
    }

    private static void Bounds(Func<float, bool> set, Func<float> get, float minimum, float maximum)
    {
        Check(set(-100f) && get() == minimum, "Finite negative clamps to lower bound.");
        Check(set(1000f) && get() == maximum, "Finite positive clamps to upper bound.");
        Check(!set(float.NaN) && !set(float.PositiveInfinity) && !set(float.NegativeInfinity) && get() == maximum,
            "Nonfinite values rejected without state mutation.");
    }
    private static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);
    private static void Call(object obj, string method) => obj.GetType().GetMethod(method, Private).Invoke(obj, null);
    private static void Check(bool value, string message) { checks++; Require(value, message); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("Admin runtime: " + message); }
}
#endif
