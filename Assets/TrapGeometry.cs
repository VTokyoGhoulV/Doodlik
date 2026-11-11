using UnityEngine;

// Shared by generation and contact checks: all distances use the screen's wrap.
public static class TrapGeometry
{
    public static float WrappedDelta(float from, float to, float width)
    {
        return width > 0f ? Mathf.Repeat(to - from + width * 0.5f, width) - width * 0.5f : to - from;
    }

    public static bool OverlapsRoute(Rect hazard, Vector2 from, Vector2 to,
        float screenWidth, float horizontalMargin, float jumpHeight, float verticalMargin,
        float turnMargin = 0f)
    {
        if (hazard.yMax < from.y - verticalMargin ||
            hazard.yMin > from.y + jumpHeight + verticalMargin)
            return false;

        float deltaX = WrappedDelta(from.x, to.x, screenWidth);
        // A corridor spanning the whole ascent is intentionally conservative:
        // the real jump has different rising/apex/falling gravity, and inertia
        // makes a symmetric parabola an unsafe approximation. Protect both
        // takeoff/landing centres and the complete steering corridor at every
        // height the normal jump can reach, including screen-wrap copies.
        float landingX = from.x + deltaX;
        float corridorMin = Mathf.Min(from.x, landingX) - horizontalMargin - turnMargin;
        float corridorMax = Mathf.Max(from.x, landingX) + horizontalMargin + turnMargin;
        for (int copy = -1; copy <= 1; copy++)
        {
            float shift = copy * screenWidth;
            if (hazard.xMax + shift >= corridorMin && hazard.xMin + shift <= corridorMax)
                return true;
        }
        return false;
    }

    public static bool SweptBox(Vector2 from, Vector2 to, Vector2 halfSize, Rect target)
    {
        float enter = 0f;
        float exit = 1f;
        Vector2 delta = to - from;
        return Clip(from.x, delta.x, target.xMin - halfSize.x,
                   target.xMax + halfSize.x, ref enter, ref exit) &&
               Clip(from.y, delta.y, target.yMin - halfSize.y,
                   target.yMax + halfSize.y, ref enter, ref exit);
    }

    private static bool Clip(float origin, float delta, float min, float max,
        ref float enter, ref float exit)
    {
        if (Mathf.Abs(delta) < 0.00001f)
            return origin >= min && origin <= max;
        float a = (min - origin) / delta;
        float b = (max - origin) / delta;
        enter = Mathf.Max(enter, Mathf.Min(a, b));
        exit = Mathf.Min(exit, Mathf.Max(a, b));
        return enter <= exit;
    }
}
