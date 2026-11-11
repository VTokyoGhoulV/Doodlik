using UnityEngine;

// Session-only tuning. Ordinary releases compile neutral values, not commands.
// No serialized movement fields, saved preferences or normal progress are edited.
public static class AdminRuntime
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
    private static float movement = 1f, jump = 1f, gravity = 1f, bush = 1f;
    private static float gameSpeed = 1f, buffDuration = 15f;
    private static bool Active => PlayerProgress.AdminSandboxActive;
    public static float MovementMultiplier => Active ? movement : 1f;
    public static float JumpMultiplier => Active ? jump : 1f;
    public static float GravityMultiplier => Active ? gravity : 1f;
    public static float BushMultiplier => Active ? bush : 1f;
    public static float GameSpeed => Active ? gameSpeed : 1f;
    public static float BuffDuration => Active ? buffDuration : 15f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetTuning()
    {
        movement = jump = gravity = bush = gameSpeed = 1f;
        buffDuration = 15f;
        // Do not resume an admin/menu/death pause while removing tuning.
        if (Time.timeScale > 0f) Time.timeScale = 1f;
    }

    public static bool SetMovementMultiplier(float value) => Set(ref movement, value, 0.5f, 3f);
    public static bool SetJumpMultiplier(float value) => Set(ref jump, value, 0.5f, 2f);
    public static bool SetGravityMultiplier(float value) => Set(ref gravity, value, 0.4f, 2f);
    public static bool SetBushMultiplier(float value) => Set(ref bush, value, 0.5f, 2f);
    public static bool SetGameSpeed(float value) => Set(ref gameSpeed, value, 0.25f, 2f);
    public static bool SetBuffDuration(float value) => Set(ref buffDuration, value, 1f, 120f);

    private static bool Set(ref float target, float value, float minimum, float maximum)
    {
        if (!Active || float.IsNaN(value) || float.IsInfinity(value)) return false;
        InvalidateAttempt();
        target = Mathf.Clamp(value, minimum, maximum);
        return true;
    }

    // The UI owns pause transitions. Never turn a zero-time pause into gameplay.
    public static void ApplyGameSpeed()
    {
        if (Active && RunFlow.IsPlaying && Time.timeScale > 0f && !AdminTools.IsOpen)
            Time.timeScale = GameSpeed;
    }

    public static bool GrantPower(RunPowerups.Kind kind)
    {
        if (!Active || (int)kind < (int)RunPowerups.Kind.Shield || (int)kind > (int)RunPowerups.Kind.Rocket)
            return false;
        Player player = InvalidateAttempt();
        if (player == null || player.IsDead || !RunFlow.IsPlaying) return false;
        RunPowerups powers = player.GetComponent<RunPowerups>();
        return powers != null && powers.GrantAdminPower(kind, BuffDuration);
    }

    public static bool ClearPowers()
    {
        if (!Active) return false;
        Player player = InvalidateAttempt();
        RunPowerups powers = player != null ? player.GetComponent<RunPowerups>() : null;
        if (powers == null) return false;
        powers.ClearAdminPowers();
        return true;
    }

    public static bool JumpNow()
    {
        if (!Active) return false;
        Player player = InvalidateAttempt();
        return player != null && player.JumpFromAdmin();
    }

    private static Player InvalidateAttempt()
    {
        Player player = Object.FindAnyObjectByType<Player>();
        if (player != null) player.RefreshAdminState();
        return player;
    }
#else
    public static float MovementMultiplier => 1f;
    public static float JumpMultiplier => 1f;
    public static float GravityMultiplier => 1f;
    public static float BushMultiplier => 1f;
    public static float GameSpeed => 1f;
    public static float BuffDuration => 15f;
    public static void ResetTuning() { }
    public static void ApplyGameSpeed() { }
    public static bool SetMovementMultiplier(float value) => false;
    public static bool SetJumpMultiplier(float value) => false;
    public static bool SetGravityMultiplier(float value) => false;
    public static bool SetBushMultiplier(float value) => false;
    public static bool SetGameSpeed(float value) => false;
    public static bool SetBuffDuration(float value) => false;
    public static bool GrantPower(RunPowerups.Kind kind) => false;
    public static bool ClearPowers() => false;
    public static bool JumpNow() => false;
#endif
}
