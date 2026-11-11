using System.Collections.Generic;
using UnityEngine;

// Optional rewards only: the platform route is never changed to accommodate a
// pickup. A small deterministic steering witness must collect it and still land.
public static class AerialPickupPlacement
{
    public static bool TryDetour(Player player, Vector2 from, Vector2 to,
        float targetHalfWidth, float screenWidth, float bodyAboveFeet, Vector2 bodyHalfSize,
        int seed, out Vector2 position, List<Vector2> witness = null, int maximumAttempts = 18)
    {
        position = default;
        witness?.Clear();
        if (player == null || targetHalfWidth <= 0f || screenWidth <= 0f) return false;
        float scale = GameplayPreferences.HorizontalMovementScale;
        bool inertia = GameplayPreferences.UseInertia;
        float gap = to.y - from.y;
        float delta = TrapGeometry.WrappedDelta(from.x, to.x, screenWidth);
        JumpReachability.Result jump = JumpReachability.Simulate(player, gap, inertia, scale);
        if (!jump.CanLand || gap <= 0f ||
            // Height margin is physical jump time, not cosmetic cat scale.
            // Scaling it down in portrait admitted near-apex mandatory jumps.
            gap > jump.Apex - 0.35f ||
            Mathf.Abs(delta) > jump.HorizontalDistance * 0.88f) return false;
        // A shorter normal jump leaves less time for an optional excursion.
        // Keep the lure beyond the cat's straight-route body width even in
        // portrait; accepted rewards must require a deliberate side step.
        float offset = Mathf.Max(0.34f * scale,
            bodyHalfSize.x + 0.20f);
        float speed = player.moveSpeed * scale;
        // Both sides lie outside the simple takeoff-to-landing corridor. They
        // require a deliberate excursion and return, never just following arrows.
        for (int attempt = 0; attempt < Mathf.Clamp(maximumAttempts, 1, 18); attempt++)
        {
            int variant = (attempt + Mathf.Abs(seed % 18)) % 18;
            float x = (variant % 2 == 0 ? Mathf.Min(0f, delta) - offset : Mathf.Max(0f, delta) + offset);
            x += (variant % 2 == 0 ? -1f : 1f) * (variant / 6) * 0.18f * scale;
            if (Mathf.Abs(x) >= screenWidth * 0.48f) continue;
            float time = jump.LandingTime * (0.32f + 0.10f * (variant % 3));
            Vector2 candidate = new Vector2(x, HeightAt(player, time) + bodyAboveFeet);
            if (candidate.y < gap + 0.38f * GameplayPreferences.ModelScale ||
                candidate.y > jump.Apex + bodyAboveFeet - 0.10f) continue;
            bool valid = true;
            for (int start = 0; start <= (inertia ? 1 : 0); start++)
            {
                if (!CanCollectAndLand(player, gap, delta, targetHalfWidth, bodyAboveFeet,
                    candidate, start * Mathf.Sign(candidate.x) * speed, inertia, scale)) { valid = false; break; }
            }
            if (!valid) continue;
            if (witness != null)
                CanCollectAndLand(player, gap, delta, targetHalfWidth, bodyAboveFeet, candidate,
                    0f, inertia, scale, witness);
            position = from + candidate;
            return true;
        }
        return false;
    }

    public static bool TryPlace(Player player, Vector2 from, Vector2 to,
        float targetHalfWidth, float screenWidth, float sourcePatrol, float targetPatrol,
        float bodyAboveFeet, int platformIndex, out Vector2 position)
    {
        position = default;
        if (player == null || sourcePatrol > 0f || targetPatrol > 0f ||
            targetHalfWidth <= 0f || platformIndex % 3 == 0)
            return false;

        float scale = GameplayPreferences.HorizontalMovementScale;
        bool inertia = GameplayPreferences.UseInertia;
        float gap = to.y - from.y;
        float delta = TrapGeometry.WrappedDelta(from.x, to.x, screenWidth);
        JumpReachability.Result jump = JumpReachability.Simulate(player, gap, inertia, scale);
        if (!jump.CanLand || gap <= 0f || gap > jump.Apex - 0.65f ||
            Mathf.Abs(delta) < 1f * scale || Mathf.Abs(delta) > jump.HorizontalDistance * 0.74f)
            return false;

        float speed = player.moveSpeed * scale;
        // Airborne rewards stay in the existing protected route corridor. Early
        // lateral progress and braking are optional; the normal landing remains.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            float fraction = 0.38f + 0.08f * ((attempt + platformIndex) % 3);
            float atTime = jump.LandingTime * (attempt < 3 ? 0.44f : 0.54f);
            Vector2 candidate = new Vector2(delta * fraction,
                HeightAt(player, atTime) + bodyAboveFeet);
            bool valid = true;
            for (int start = inertia ? -1 : 0; start <= (inertia ? 1 : 0); start++)
            {
                if (!CanCollectAndLand(player, gap, delta, targetHalfWidth, bodyAboveFeet,
                    candidate, start * speed, inertia, scale))
                {
                    valid = false;
                    break;
                }
            }
            if (!valid) continue;
            position = new Vector2(from.x + candidate.x, from.y + candidate.y);
            return true;
        }
        return false;
    }

    public static bool CanCollectAndLand(Player player, float gap, float landingX,
        float targetHalfWidth, float bodyAboveFeet, Vector2 pickup, float initialVx,
        bool inertia, float movementScale, List<Vector2> witness = null)
    {
        witness?.Clear();
        float step = Mathf.Clamp(Time.fixedDeltaTime, 0.005f, 0.04f);
        float x = 0f, y = 0f, vx = initialVx, vy = player.NormalJumpForce;
        float speed = player.moveSpeed * movementScale;
        bool collected = false, roseAbove = false;
        witness?.Add(new Vector2(0f, bodyAboveFeet));
        float collectedAt = -1f;
        // Smaller than the actual base pickup radius, without counting the cat's
        // body, magnets or upgrades. No powerup is needed to reach this reward.
        const float pickupMargin = 0.14f;
        Rect pickupArea = new Rect(pickup - Vector2.one * pickupMargin, Vector2.one * (pickupMargin * 2f));
        for (float time = 0f; time < 6f; time += step)
        {
            float control = vy < 0f ? player.fallingControl : player.risingControl;
            // Keep a small reaction margin after collecting before correcting
            // for the mandatory landing; a frame-perfect reversal is not needed.
            float goal = collected && time - collectedAt >= 0.06f ? landingX : pickup.x;
            float input = SteeringInput(player, goal - x, vx, control, movementScale, inertia, step);
            if (!inertia) vx = input * speed;
            else
            {
                bool turning = input != 0f && Mathf.Abs(vx) > 0.1f && Mathf.Sign(input) != Mathf.Sign(vx);
                float rate = input == 0f ? player.airDeceleration * movementScale :
                    (turning ? player.turnAcceleration : player.acceleration) * control * movementScale;
                vx = Mathf.MoveTowards(vx, input * speed, rate * step);
            }
            float previousX = x, previousY = y;
            AdvanceVertical(player, ref y, ref vy, step);
            x += vx * step;
            witness?.Add(new Vector2(x, y + bodyAboveFeet));
            if (!collected && TrapGeometry.SweptBox(new Vector2(previousX, previousY + bodyAboveFeet),
                new Vector2(x, y + bodyAboveFeet), Vector2.zero, pickupArea))
            {
                collected = true;
                collectedAt = time;
            }
            roseAbove |= y >= gap;
            if (vy < 0f && y <= gap && roseAbove)
            {
                float fraction = Mathf.InverseLerp(previousY, y, gap);
                float landedX = Mathf.Lerp(previousX, x, fraction);
                // Aim well inside the platform, not merely for a pixel of feet
                // overlap with its edge. Real foot width adds further tolerance.
                return collected && Mathf.Abs(landedX - landingX) <= targetHalfWidth * 0.6f;
            }
            if (vy < 0f && y < 0f) return false;
        }
        return false;
    }

    public static float SteeringInput(Player player, float distance, float velocity,
        float control, float scale, bool inertia, float step)
    {
        float speed = player.moveSpeed * scale;
        if (!inertia)
            return Mathf.Abs(distance) <= speed * step * 0.6f ? 0f : Mathf.Sign(distance);
        if (Mathf.Abs(distance) < 0.055f * scale && Mathf.Abs(velocity) < speed * 0.1f)
            return 0f;
        float brake = Mathf.Max(0.01f, player.turnAcceleration * control * scale);
        float stoppingDistance = velocity * velocity / (2f * brake);
        if (Mathf.Sign(distance) == Mathf.Sign(velocity) &&
            Mathf.Abs(distance) < stoppingDistance + Mathf.Abs(velocity) * step)
            return -Mathf.Sign(velocity);
        return Mathf.Sign(distance);
    }

    private static float HeightAt(Player player, float until)
    {
        float y = 0f, vy = player.NormalJumpForce;
        float step = Mathf.Clamp(Time.fixedDeltaTime, 0.005f, 0.04f);
        for (float t = 0f; t < until; t += step)
            AdvanceVertical(player, ref y, ref vy, Mathf.Min(step, until - t));
        return y;
    }

    private static void AdvanceVertical(Player player, ref float y, ref float vy, float step)
    {
        // Same fixed-step order as Player.ApplyGravityCurve and Box2D, including
        // the apex band and the pre-gravity terminal-speed clamp.
        float gravityScale = Mathf.Abs(vy) <= player.apexVelocityThreshold ? player.apexGravityScale :
            vy > 0f ? player.risingGravityScale : player.fallingGravityScale;
        vy = Mathf.Max(vy, -player.maximumFallSpeed);
        vy -= Mathf.Abs(Physics2D.gravity.y) * gravityScale * step;
        y += vy * step;
    }
}
