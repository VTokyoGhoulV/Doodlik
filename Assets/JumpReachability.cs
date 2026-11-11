using UnityEngine;

// Generator-only prediction of the normal jump. Temporary boosts deliberately
// never enlarge the required route, so the route stays possible without them.
public static class JumpReachability
{
    public struct Result
    {
        public bool CanLand;
        public float Apex;
        public float HorizontalDistance;
        public float LandingTime;
    }

    public static Result Simulate(Player player, float targetHeight, bool inertia,
        float movementScale, float inheritedOppositeSpeed = 0f)
    {
        float targetSpeed = player != null ? player.moveSpeed * movementScale : 0f;
        float initialSpeed = inertia ? -targetSpeed - inheritedOppositeSpeed : targetSpeed;
        return SimulateFromVelocity(player, targetHeight, inertia, movementScale,
            initialSpeed);
    }

    // A deliberately demanding jump may follow the incoming travel direction.
    // Credit only 70% of that speed: a player still has to hold the direction,
    // but the generator must not assume a perfect full-speed launch.
    public static Result SimulatePrepared(Player player, float targetHeight,
        bool inertia, float movementScale)
    {
        float targetSpeed = player != null ? player.moveSpeed * movementScale : 0f;
        return SimulateFromVelocity(player, targetHeight, inertia, movementScale,
            inertia ? targetSpeed * 0.7f : targetSpeed);
    }

    private static Result SimulateFromVelocity(Player player, float targetHeight,
        bool inertia, float movementScale, float initialSpeed)
    {
        Result result = new Result();
        if (player == null || player.NormalJumpForce <= 0f)
            return result;

        float step = Mathf.Clamp(Time.fixedDeltaTime, 0.005f, 0.04f);
        float gravity = Mathf.Abs(Physics2D.gravity.y);
        if (gravity < 0.01f)
            return result;
        float targetSpeed = player.moveSpeed * movementScale;
        float vx = initialSpeed;
        float vy = player.NormalJumpForce;
        float y = 0f, x = 0f;
        bool roseAbove = targetHeight <= 0f;
        for (float elapsed = 0f; elapsed < 6f; elapsed += step)
        {
            if (inertia)
            {
                bool turning = vx < -0.1f;
                float control = vy < 0f ? player.fallingControl : player.risingControl;
                float rate = (turning ? player.turnAcceleration : player.acceleration) * control * movementScale;
                vx = Mathf.MoveTowards(vx, targetSpeed, rate * step);
            }

            float gravityScale = Mathf.Abs(vy) <= player.apexVelocityThreshold
                ? player.apexGravityScale
                : vy > 0f ? player.risingGravityScale : player.fallingGravityScale;
            vy = Mathf.Max(vy, -player.maximumFallSpeed);
            vy -= gravity * gravityScale * step;
            float previousY = y, previousX = x;
            x += vx * step;
            y += vy * step;
            result.Apex = Mathf.Max(result.Apex, y);
            roseAbove |= y >= targetHeight;

            if (vy < 0f && y <= targetHeight && roseAbove)
            {
                // Interpolate the final physics step: do not count travel after
                // the paws have already passed below the target platform.
                float fraction = Mathf.InverseLerp(previousY, y, targetHeight);
                result.CanLand = true;
                result.HorizontalDistance = Mathf.Max(0f, Mathf.Lerp(previousX, x, fraction));
                result.LandingTime = elapsed + step * fraction;
                return result;
            }
            if (vy < 0f && y < 0f)
                break;
        }
        return result;
    }
}
