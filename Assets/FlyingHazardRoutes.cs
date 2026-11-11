using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Small, cached witness set for ordinary jumps, not a runtime bot. A flying
// hazard is admitted only when an actual landing path stays outside its ENTIRE
// patrol, so auto-jumping never requires waiting for a particular flight phase.
public sealed class FlyingHazardRoutes
{
    internal sealed class Witness
    {
        internal Vector2[] Body;
        internal Vector2 LandingFeet;
        internal float LandingVelocity;
        internal Rect Bounds;
    }

    public sealed class Paths
    {
        internal readonly List<Witness>[] Starts = new List<Witness>[9];
        internal readonly List<Witness> Ordinary = new List<Witness>();
        internal int RequiredStartMask = 0x1ff;
        internal int OrdinaryStartIndex = 4;
        public bool Complete { get; internal set; }

        internal void RequirePreparedVelocity(int direction)
        {
            if (direction == 0) return;
            int velocityOffset = direction > 0 ? 2 : 0;
            RequiredStartMask = (1 << velocityOffset) |
                (1 << (3 + velocityOffset)) | (1 << (6 + velocityOffset));
            OrdinaryStartIndex = 3 + velocityOffset;
        }
    }

    private readonly Player player;
    private readonly bool inertia;
    private readonly float scale, width, step, halfPlatform, feetHalf, platformTop;
    private readonly Vector2 bodyOffset, bodyHalf;
    private readonly List<Vector2> scratchSamples = new List<Vector2>(200);

    public FlyingHazardRoutes(Player player, GameObject platform, float screenWidth,
        float platformWidthMultiplier = 1f)
    {
        this.player = player;
        inertia = GameplayPreferences.UseInertia;
        scale = GameplayPreferences.HorizontalMovementScale;
        width = screenWidth;
        step = Mathf.Clamp(Time.fixedDeltaTime, 0.005f, 0.04f);
        Vector3 catScale = player.transform.localScale;
        if (player.GetComponent<PortraitModelScaler>() == null) catScale *= GameplayPreferences.ModelScale;
        BoxCollider2D feet = player.GetComponent<BoxCollider2D>();
        SpriteRenderer cat = player.GetComponent<SpriteRenderer>();
        Bounds art = cat.sprite.bounds;
        feetHalf = Mathf.Abs(feet.size.x * catScale.x) * 0.5f;
        bodyHalf = Vector2.Scale(art.extents, catScale) * 0.65f;
        bodyOffset = new Vector2((art.center.x - feet.offset.x) * catScale.x,
            (art.center.y - feet.offset.y + feet.size.y * 0.5f) * catScale.y);
        BoxCollider2D surface = platform.GetComponent<BoxCollider2D>();
        Vector3 platformScale = platform.transform.localScale * GameplayPreferences.ModelScale;
        halfPlatform = Mathf.Abs(surface.size.x * platformScale.x) * 0.5f * platformWidthMultiplier;
        platformTop = (surface.offset.y + surface.size.y * 0.5f) * platformScale.y;
    }

    public Paths Build(Vector2 from, Vector2 to, int preparedDirection = 0)
    {
        Paths paths = new Paths();
        paths.RequirePreparedVelocity(preparedDirection);
        IEnumerator work = BuildIncrementally(from, to, paths, 0);
        while (work.MoveNext()) { }
        return paths;
    }

    public IEnumerator BuildIncrementally(Vector2 from, Vector2 to, Paths paths,
        double millisecondsPerSlice = 2.0)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        float landingTime = JumpReachability.Simulate(player, to.y - from.y, inertia, scale).LandingTime;
        if (landingTime <= 0f) yield break;
        float landingMargin = halfPlatform - feetHalf - 0.12f * GameplayPreferences.ModelScale;
        if (landingMargin <= 0.08f * GameplayPreferences.ModelScale) yield break;
        to.x = from.x + TrapGeometry.WrappedDelta(from.x, to.x, width);
        from.y += platformTop;
        to.y += platformTop;
        float maximumSpeed = player.moveSpeed * scale;
        bool complete = true;
        for (int position = -1; position <= 1; position++)
        for (int velocity = -1; velocity <= 1; velocity++)
        {
            int index = (position + 1) * 3 + velocity + 1;
            var alternatives = new List<Witness>(42);
            paths.Starts[index] = alternatives;
            Vector2 start = from + Vector2.right * (position * landingMargin * 0.65f);
            for (int delay = 0; delay <= 13; delay++)
            for (int target = -1; target <= 1; target++)
            {
                Vector2 aim = to + Vector2.right * (target * landingMargin * 0.55f);
                int detour = Mathf.Max(0, (delay - 8) / 2);
                float waypoint = delay < 8 ? start.x : start.x +
                    (delay % 2 == 0 ? -1f : 1f) * (0.8f + detour * 0.45f) * scale;
                float hold = delay < 8 ? landingTime * delay / 14f
                    : landingTime * (0.28f + detour * 0.07f);
                Witness path = Simulate(start, aim, to, velocity * maximumSpeed,
                    hold, waypoint, landingMargin);
                if (path != null)
                {
                    alternatives.Add(path);
                    if (index == paths.OrdinaryStartIndex && delay < 8)
                        paths.Ordinary.Add(path);
                }
                if (millisecondsPerSlice > 0 && watch.Elapsed.TotalMilliseconds >= millisecondsPerSlice)
                {
                    yield return null;
                    watch.Restart();
                }
            }
            if (alternatives.Count == 0 &&
                (paths.RequiredStartMask & (1 << index)) != 0) complete = false;
        }
        paths.Complete = complete;
    }

    private Witness Simulate(Vector2 start, Vector2 aim, Vector2 landing,
        float initialVelocity, float delay, float waypoint, float landingMargin)
    {
        Vector2 feet = start;
        float vx = initialVelocity, vy = player.NormalJumpForce;
        float speed = player.moveSpeed * scale;
        List<Vector2> samples = scratchSamples;
        samples.Clear();
        samples.Add(feet + bodyOffset);
        bool above = false;
        for (float elapsed = 0f; elapsed < 3f; elapsed += step)
        {
            float goal = elapsed < delay ? waypoint : aim.x;
            float desired = Mathf.Clamp((goal - feet.x) * 6f, -speed, speed);
            float input = 0f, best = float.PositiveInfinity;
            for (int command = -1; command <= 1; command++)
            {
                float next = Horizontal(vx, vy, command);
                float error = Mathf.Abs(next - desired);
                if (error < best) { best = error; input = command; }
            }
            vx = Horizontal(vx, vy, input);
            float gravityScale = Mathf.Abs(vy) <= player.apexVelocityThreshold ? player.apexGravityScale
                : vy > 0f ? player.risingGravityScale : player.fallingGravityScale;
            vy = Mathf.Max(vy, -player.maximumFallSpeed);
            vy -= Mathf.Abs(Physics2D.gravity.y) * gravityScale * step;
            Vector2 previous = feet;
            feet += new Vector2(vx, vy) * step;
            above |= feet.y >= landing.y;
            if (above && vy < 0f && feet.y <= landing.y)
            {
                feet = Vector2.Lerp(previous, feet, Mathf.InverseLerp(previous.y, feet.y, landing.y));
                if (Mathf.Abs(feet.x - landing.x) > landingMargin) return null;
                samples.Add(feet + bodyOffset);
                Vector2 min = samples[0], max = min;
                for (int i = 1; i < samples.Count; i++)
                {
                    min = Vector2.Min(min, samples[i]);
                    max = Vector2.Max(max, samples[i]);
                }
                return new Witness { Body = samples.ToArray(), LandingFeet = feet,
                    LandingVelocity = vx, Bounds = new Rect(min, max - min) };
            }
            samples.Add(feet + bodyOffset);
            if (vy < 0f && feet.y < start.y) return null;
        }
        return null;
    }

    private float Horizontal(float vx, float vy, float command)
    {
        float target = command * player.moveSpeed * scale;
        if (!inertia) return target;
        bool turning = command != 0f && Mathf.Abs(vx) > 0.1f && Mathf.Sign(target) != Mathf.Sign(vx);
        float rate = command == 0f ? player.airDeceleration * scale
            : (turning ? player.turnAcceleration : player.acceleration) *
              (vy < 0f ? player.fallingControl : player.risingControl) * scale;
        return Mathf.MoveTowards(vx, target, rate * step);
    }

    public bool HasSafeAlternatives(Paths paths, Rect proposedPatrol, IList<Rect> existingPatrols)
    {
        if (paths == null || !paths.Complete) return false;
        // Additional body-space reserve: this is not a one-pixel dodging test.
        Vector2 clearance = bodyHalf + Vector2.one * (0.12f * GameplayPreferences.ModelScale);
        for (int start = 0; start < paths.Starts.Length; start++)
        {
            if ((paths.RequiredStartMask & (1 << start)) == 0) continue;
            List<Witness> alternatives = paths.Starts[start];
            bool safe = false;
            foreach (Witness path in alternatives)
            {
                if (Intersects(path, proposedPatrol, clearance)) continue;
                bool blocked = false;
                for (int i = 0; i < existingPatrols.Count && !blocked; i++)
                    blocked = Intersects(path, existingPatrols[i], clearance);
                if (!blocked) { safe = true; break; }
            }
            if (!safe) return false;
        }
        return true;
    }

    public bool CanThreatenARealJump(Paths paths, Vector2 origin, float patrol, float size, float speed)
        => CanThreatenARealJump(paths, origin, new Vector2(patrol, 0.1f * size),
            size, speed, DoodlikHazard.FlightPattern.Horizontal);

    public bool CanThreatenARealJump(Paths paths, Vector2 origin, Vector2 amplitude,
        float size, float speed, DoodlikHazard.FlightPattern pattern)
    {
        if (paths == null || !paths.Complete) return false;
        // The ordinary centre / stationary start must have a real collision
        // counterexample, not merely an overlapping bounding rectangle.
        Vector2 wholeHalf = amplitude + Vector2.one * (0.24f * size);
        Rect wholePatrol = new Rect(origin - wholeHalf, wholeHalf * 2f);
        foreach (Witness witness in paths.Ordinary)
        {
            if (!Intersects(witness, wholePatrol, bodyHalf)) continue;
            for (int phase = 0; phase < 16; phase++)
            {
            Vector2[] path = witness.Body;
            float age = phase * Mathf.PI * 2f / (16f * speed);
            Vector2 previousSeed = DoodlikHazard.FlightPosition(origin, 0f,
                amplitude, speed, age * speed, pattern);
            for (int i = 1; i < path.Length; i++)
            {
                Vector2 seed = DoodlikHazard.FlightPosition(origin, i * step,
                    amplitude, speed, age * speed, pattern);
                Vector2 from = path[i - 1] - previousSeed;
                Vector2 to = path[i] - seed;
                from.x = TrapGeometry.WrappedDelta(0f, from.x, width);
                to.x = from.x + TrapGeometry.WrappedDelta(from.x, to.x, width);
                if (TrapGeometry.SweptBox(from, to, bodyHalf,
                    new Rect(Vector2.one * (-0.24f * size), Vector2.one * (0.48f * size)))) return true;
                previousSeed = seed;
            }
            }
        }
        return false;
    }

    public void AppendApexCandidates(Paths paths, float radiusX, float radiusY,
        Vector2 patrol,
        List<Vector2> candidates)
    {
        if (paths == null || !paths.Complete || paths.Ordinary.Count == 0) return;
        // Candidate search follows three actual jump witnesses. Narrow valid
        // windows near the apex should not depend on lucky rectangle sampling.
        for (int sample = 0; sample < 3; sample++)
        {
            Vector2[] path = paths.Ordinary[sample * (paths.Ordinary.Count - 1) / 2].Body;
            Vector2 apex = path[0];
            for (int i = 1; i < path.Length; i++) if (path[i].y > apex.y) apex = path[i];
            foreach (float side in new[] { -1f, 1f })
            foreach (float horizontal in new[] { 0.55f, 0.95f })
            foreach (float vertical in new[] { -0.35f, 0.25f, 0.75f })
                candidates.Add(apex + new Vector2(
                    side * (bodyHalf.x + radiusX + patrol.x) * horizontal,
                    (bodyHalf.y + radiusY + patrol.y) * vertical));
        }
    }

    public bool CanThreatenStatic(Paths paths, Rect damage)
    {
        if (paths == null || !paths.Complete) return false;
        foreach (Witness path in paths.Ordinary)
            if (Intersects(path, damage, bodyHalf)) return true;
        return false;
    }

    public sealed class ChainResult
    {
        public bool Passed { get; internal set; }
        public int SimulatedPaths { get; internal set; }
        public int Slices { get; internal set; }
        public double LongestSliceMilliseconds { get; internal set; }
    }

    // Preserve actual arrival velocity/position across three hops instead of
    // inventing fresh edge + opposite-speed states at every platform. Every
    // required incoming state still needs a path. Editor validation
    // drives the SAME iterator synchronously; runtime spreads it over frames.
    public bool CanTraverseChain(Vector2[] platforms, Rect proposedPatrol,
        IList<Rect> existingPatrols, out int simulatedPaths, Paths initialPaths = null)
    {
        var result = new ChainResult();
        IEnumerator work = TraverseChain(platforms, proposedPatrol, existingPatrols, result, initialPaths, 0);
        while (work.MoveNext()) { }
        simulatedPaths = result.SimulatedPaths;
        return result.Passed;
    }

    public IEnumerator TraverseChain(Vector2[] platforms, Rect proposedPatrol,
        IList<Rect> existingPatrols, ChainResult result, Paths initialPaths,
        double millisecondsPerSlice = 2.0)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        if (platforms == null || platforms.Length != 4 ||
            platforms[3].y + platformTop <= proposedPatrol.yMax + 0.12f * scale) yield break;
        Paths first = initialPaths ?? Build(platforms[0], platforms[1]);
        if (!first.Complete) yield break;
        Vector2 clearance = bodyHalf + Vector2.one * (0.12f * GameplayPreferences.ModelScale);
        for (int incoming = 0; incoming < 9; incoming++)
        {
            if ((first.RequiredStartMask & (1 << incoming)) == 0) continue;
            var exits = new ExitSelection(this, platforms[1]);
            foreach (Witness path in first.Starts[incoming])
            {
                if (!HitsPrematurePlatform(path.Body, platforms, 1) &&
                    IsSafe(path, proposedPatrol, existingPatrols, clearance)) exits.Consider(path);
                if (millisecondsPerSlice > 0 && watch.Elapsed.TotalMilliseconds >= millisecondsPerSlice)
                {
                    RecordSlice(result, watch);
                    yield return null;
                    watch.Restart();
                }
            }
            List<Witness> beam = exits.ToList();
            if (beam.Count == 0) { RecordSlice(result, watch); yield break; }
            for (int hop = 1; hop < 3; hop++)
            {
                exits = new ExitSelection(this, platforms[hop + 1]);
                bool finalExit = false;
                for (int parent = 0; parent < beam.Count && !finalExit; parent++)
                {
                    Witness previous = beam[parent];
                    Vector2 landing = platforms[hop + 1] + Vector2.up * platformTop;
                    landing.x = previous.LandingFeet.x +
                        TrapGeometry.WrappedDelta(previous.LandingFeet.x, landing.x, width);
                    float duration = JumpReachability.Simulate(player,
                        landing.y - previous.LandingFeet.y, inertia, scale).LandingTime;
                    float margin = halfPlatform - feetHalf - 0.12f * GameplayPreferences.ModelScale;
                    for (int profile = 0; profile < 14 && !finalExit; profile++)
                    for (int offset = -1; offset <= 1 && !finalExit; offset++)
                    {
                        int detour = Mathf.Max(0, (profile - 8) / 2);
                        float waypoint = profile < 8 ? previous.LandingFeet.x : previous.LandingFeet.x +
                            (profile % 2 == 0 ? -1f : 1f) * (0.8f + detour * 0.45f) * scale;
                        float hold = profile < 8 ? duration * profile / 14f : duration * (0.28f + detour * 0.07f);
                        Witness path = Simulate(previous.LandingFeet,
                            landing + Vector2.right * (offset * margin * 0.55f), landing,
                            previous.LandingVelocity, hold, waypoint, margin);
                        result.SimulatedPaths++;
                        if (path != null && !HitsPrematurePlatform(path.Body, platforms, hop + 1) &&
                            IsSafe(path, proposedPatrol, existingPatrols, clearance))
                        {
                            if (hop == 2) finalExit = true;
                            else exits.Consider(path);
                        }
                        // Check after every trajectory, not after a whole
                        // profile/start group (which can exceed a frame budget).
                        if (millisecondsPerSlice > 0 && watch.Elapsed.TotalMilliseconds >= millisecondsPerSlice)
                        {
                            RecordSlice(result, watch);
                            yield return null;
                            watch.Restart();
                        }
                    }
                }
                if (hop == 2)
                {
                    if (!finalExit) { RecordSlice(result, watch); yield break; }
                    continue;
                }
                beam = exits.ToList();
                if (beam.Count == 0) { RecordSlice(result, watch); yield break; }
            }
        }
        RecordSlice(result, watch);
        result.Passed = true;
    }

    private static void RecordSlice(ChainResult result, System.Diagnostics.Stopwatch watch)
    {
        result.Slices++;
        result.LongestSliceMilliseconds = System.Math.Max(result.LongestSliceMilliseconds, watch.Elapsed.TotalMilliseconds);
    }

    private sealed class ExitSelection
    {
        private readonly FlyingHazardRoutes owner;
        private readonly Vector2 target;
        private Witness stable, left, right;
        private float stability = float.PositiveInfinity;

        internal ExitSelection(FlyingHazardRoutes owner, Vector2 target)
        { this.owner = owner; this.target = target; }

        internal void Consider(Witness candidate)
        {
            float cost = Mathf.Abs(candidate.LandingVelocity) +
                Mathf.Abs(TrapGeometry.WrappedDelta(target.x, candidate.LandingFeet.x, owner.width)) * 3f;
            if (cost < stability) { stable = candidate; stability = cost; }
            if (left == null || candidate.LandingFeet.x + candidate.LandingVelocity * 0.15f <
                left.LandingFeet.x + left.LandingVelocity * 0.15f) left = candidate;
            if (right == null || candidate.LandingFeet.x + candidate.LandingVelocity * 0.15f >
                right.LandingFeet.x + right.LandingVelocity * 0.15f) right = candidate;
        }
        internal List<Witness> ToList()
        {
            var selected = new List<Witness>(3);
            if (stable != null) selected.Add(stable);
            if (left != null && left != stable) selected.Add(left);
            if (right != null && right != stable && right != left) selected.Add(right);
            return selected;
        }
    }

    private bool IsSafe(Witness candidate, Rect proposed, IList<Rect> existing, Vector2 clearance)
    {
        if (Intersects(candidate, proposed, clearance)) return false;
        for (int i = 0; i < existing.Count; i++)
            if (Intersects(candidate, existing[i], clearance)) return false;
        return true;
    }

    private bool HitsPrematurePlatform(Vector2[] body, Vector2[] platforms, int intendedLanding)
    {
        for (int target = intendedLanding + 1; target < platforms.Length; target++)
        {
            float top = platforms[target].y + platformTop;
            for (int i = 1; i < body.Length; i++)
            {
                Vector2 from = body[i - 1] - bodyOffset, to = body[i] - bodyOffset;
                if (to.y >= from.y || from.y < top || to.y > top) continue;
                float x = Mathf.Lerp(from.x, to.x, (from.y - top) / (from.y - to.y));
                if (Mathf.Abs(TrapGeometry.WrappedDelta(platforms[target].x, x, width)) < halfPlatform + feetHalf)
                    return true;
            }
        }
        return false;
    }

    private bool Intersects(Witness witness, Rect hazard, Vector2 clearance)
    {
        if (witness.Bounds.yMax + clearance.y < hazard.yMin ||
            witness.Bounds.yMin - clearance.y > hazard.yMax) return false;
        Vector2[] path = witness.Body;
        // A chained witness retains its unwrapped arrival position. After
        // several screen crossings its nearest world copy need not be zero.
        int nearestCopy = Mathf.RoundToInt((witness.Bounds.center.x - hazard.center.x) / width);
        for (int copy = nearestCopy - 1; copy <= nearestCopy + 1; copy++)
        {
            Rect shifted = hazard;
            shifted.x += copy * width;
            if (witness.Bounds.xMax + clearance.x < shifted.xMin ||
                witness.Bounds.xMin - clearance.x > shifted.xMax) continue;
            for (int i = 1; i < path.Length; i++)
            {
                if (Mathf.Max(path[i - 1].y, path[i].y) + clearance.y < shifted.yMin ||
                    Mathf.Min(path[i - 1].y, path[i].y) - clearance.y > shifted.yMax) continue;
                if (TrapGeometry.SweptBox(path[i - 1], path[i], clearance, shifted)) return true;
            }
        }
        return false;
    }
}
