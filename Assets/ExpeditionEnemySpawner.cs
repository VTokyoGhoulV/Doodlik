using System.Collections.Generic;
using UnityEngine;

// Enemies are optional detours. They observe committed route/hazard geometry,
// never consume Unity's generator RNG and never reserve or remove trap lanes.
public sealed class ExpeditionEnemySpawner
{
    private struct Route { public Collider2D Source, Target; public float SourcePatrol, TargetPatrol; }
    private readonly Player player;
    private readonly Camera camera;
    private readonly float maximumPatrol, jumpApex;
    private readonly List<Route> routes = new List<Route>(32);
    private readonly List<Rect> surfaces = new List<Rect>(64), hazards = new List<Rect>(32), otherLandings = new List<Rect>(64);
    private readonly List<Collider2D> landingColliders = new List<Collider2D>(64);
    private readonly List<Rect> landingBounds = new List<Rect>(64);
    private readonly List<Vector2> witness = new List<Vector2>(128);
    private Collider2D previousPlatform;
    private float previousPatrol, retryAt;
    private uint random;
    private int nextBandHeight, bandSeed;
    private bool exhausted;
    private readonly int[] rejections = new int[7];
    public const int HalfBandHeight = 60;
    public int SpawnedCount { get; private set; }
    public int PlacementAttempts { get; private set; }
    public int NextBandHeight => nextBandHeight;
    public string Diagnostics => "route=" + rejections[0] + " band=" + rejections[1] + " view=" + rejections[2] +
        " air=" + rejections[3] + " column=" + rejections[4] + " witness=" + rejections[5] + " accepted=" + rejections[6];

    public ExpeditionEnemySpawner(Player player, Camera camera, float maximumPatrol, uint seed)
    {
        this.player = player; this.camera = camera; this.maximumPatrol = maximumPatrol;
        random = seed ^ 0x9e3779b9u; if (random == 0) random = 0x85ebca6bu;
        nextBandHeight = Range(100, 160); bandSeed = Range(0, 1000000);
        jumpApex = player != null ? JumpReachability.Simulate(player, 0f, GameplayPreferences.UseInertia,
            GameplayPreferences.HorizontalMovementScale).Apex : 0f;
    }

    public void RegisterPlatform(GameObject platform, float patrol)
    {
        Collider2D next = platform != null ? platform.GetComponent<Collider2D>() : null;
        if (next == null) return;
        if (previousPlatform != null) routes.Add(new Route { Source = previousPlatform, Target = next,
            SourcePatrol = previousPatrol, TargetPatrol = patrol });
        previousPlatform = next; previousPatrol = patrol;
    }

    public void Advance(float generatedHeight, bool hazardsEnabled, bool planningComplete = true)
    {
        if (GameplayPreferences.RunKind != GameRunKind.Expedition || player == null || player.IsDead || camera == null ||
            !RunFlow.IsPlaying || Time.timeScale <= 0f || exhausted) return;
        float top = camera.transform.position.y + camera.orthographicSize;
        float bottom = camera.transform.position.y - camera.orthographicSize - 1f;
        for (int i = routes.Count - 1; i >= 0; i--)
            if (routes[i].Source == null || routes[i].Target == null || routes[i].Target.bounds.max.y < bottom) routes.RemoveAt(i);
        if (!planningComplete) return;
        int visibleHeight = WorldZones.ScoreAtHeight(top + .8f);
        while (!exhausted && nextBandHeight <= visibleHeight - HalfBandHeight) NextBand();
        int cap = WorldZones.ScoreAtHeight(camera.transform.position.y) < 700 ? 1 : 2;
        if (exhausted || ExpeditionEnemy.ActiveCount >= cap || Time.unscaledTime < retryAt) return;
        float finalized = generatedHeight - (hazardsEnabled ? jumpApex + 2.5f : .5f);
        float knownTop = Mathf.Min(top + 4f, finalized - jumpApex - 1.5f);
        if (WorldZones.ScoreAtHeight(knownTop) < nextBandHeight) return;
        retryAt = Time.unscaledTime + .3f; PlacementAttempts++;
        if (TrySpawnBand(top, knownTop, finalized)) { SpawnedCount++; NextBand(); }
    }

    public void InvalidateLayout()
    {
        ExpeditionEnemy.RemoveUnclaimed(); retryAt = Time.unscaledTime + .15f;
    }

    private bool TrySpawnBand(float cameraTop, float knownTop, float finalizedHeight)
    {
        float width = camera.orthographicSize * camera.aspect * 2f;
        float scale = GameplayPreferences.ModelScale;
        Collider2D feet = player.GetComponent<Collider2D>(); SpriteRenderer body = player.GetComponent<SpriteRenderer>();
        if (feet == null || body == null || routes.Count == 0 || width <= 0f) return false;
        float aboveFeet = body.bounds.center.y - feet.bounds.min.y;
        Vector2 halfSize = (Vector2)body.bounds.extents * .65f;
        Vector2 movement = new Vector2(.28f, .08f) * scale;
        ReadAirspace();
        int offset = bandSeed % routes.Count;
        for (int i = 0; i < routes.Count; i++)
        {
            Route route = routes[(i + offset) % routes.Count];
            if (!SafeSurface(route.Source, route.SourcePatrol) || !SafeSurface(route.Target, route.TargetPatrol)) continue;
            Vector2 from = new Vector2(route.Source.bounds.center.x, route.Source.bounds.max.y);
            Vector2 to = new Vector2(route.Target.bounds.center.x, route.Target.bounds.max.y);
            float landingX = from.x + TrapGeometry.WrappedDelta(from.x, to.x, width);
            float lowestTop = to.y + .28f * scale, highestTop = from.y + jumpApex - .45f;
            if (highestTop <= lowestTop || to.y <= from.y || WorldZones.ScoreAtHeight(highestTop) < nextBandHeight - HalfBandHeight ||
                WorldZones.ScoreAtHeight(lowestTop) > nextBandHeight + HalfBandHeight) { rejections[0]++; continue; }
            otherLandings.Clear();
            for (int surface = 0; surface < landingColliders.Count; surface++)
                if (landingColliders[surface] != route.Target) otherLandings.Add(landingBounds[surface]);
            float sideOffset = Mathf.Max(halfSize.x + .54f * scale + movement.x + .16f,
                Mathf.Max(route.Source.bounds.extents.x, route.Target.bounds.extents.x) + halfSize.x + movement.x + .25f);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int variant = (attempt + bandSeed % 6) % 6;
                bool left = variant % 2 == 0;
                Vector2 point = new Vector2(left ? Mathf.Min(from.x, landingX) - sideOffset : Mathf.Max(from.x, landingX) + sideOffset,
                    Mathf.Lerp(lowestTop, highestTop, .20f + .27f * (variant / 2)) - ExpeditionEnemy.BodyHalfHeight * scale);
                point.x = camera.transform.position.x + TrapGeometry.WrappedDelta(camera.transform.position.x, point.x, width);
                int height = WorldZones.ScoreAtHeight(point.y);
                if (height < Mathf.Max(70, nextBandHeight - HalfBandHeight) || height > nextBandHeight + HalfBandHeight ||
                    point.y <= cameraTop + .8f || point.y > knownTop) { rejections[1]++; continue; }
                Rect area = ExpeditionEnemy.BoundsAt(point, movement);
                if (area.xMin < camera.transform.position.x - width * .5f + .10f ||
                    area.xMax > camera.transform.position.x + width * .5f - .10f ||
                    !OffMainRoute(area, from, landingX, halfSize.x, width)) { rejections[2]++; continue; }
                if (!AreaClear(area, width)) { rejections[3]++; continue; }
                // A stationary repeat stomp is possible even for a base 1-damage
                // cat. It must not bounce into a trap above the beetle.
                Rect retryColumn = new Rect(point.x - movement.x - halfSize.x - .10f,
                    point.y + ExpeditionEnemy.BodyHalfHeight * scale + aboveFeet - halfSize.y,
                    (movement.x + halfSize.x + .10f) * 2f, jumpApex + halfSize.y * 2f + .15f);
                if (retryColumn.yMax > finalizedHeight || !ClearOf(retryColumn, hazards, width) || !ClearOf(retryColumn, surfaces, width)) { rejections[4]++; continue; }
                bool safe = true;
                for (int patrolSide = -1; patrolSide <= 1 && safe; patrolSide++)
                for (int initial = 0; initial <= (GameplayPreferences.UseInertia ? 1 : 0); initial++)
                {
                    Vector2 target = point + Vector2.right * (patrolSide * movement.x);
                    if (!TryStompAndReturn(player, from, to, route.Target.bounds.extents.x, target,
                        width, aboveFeet, feet.bounds.extents.x, initial != 0, witness) ||
                        !AirCoinSpawner.IsWitnessClear(witness, from, halfSize, width, finalizedHeight, hazards) ||
                        !AirCoinSpawner.IsLandingPathClear(witness, from, aboveFeet, feet.bounds.extents.x, width, otherLandings))
                    { safe = false; break; }
                }
                if (!safe) { rejections[5]++; continue; }
                rejections[6]++;
                return ExpeditionEnemy.Spawn(point, camera, movement.x, movement.y, bandSeed * .001f, NextRandom()) != null;
            }
        }
        return false;
    }

    // Deterministic steering witness: descend onto the top, rebound with exactly
    // NormalJumpForce, then land well inside the existing target platform.
    public static bool TryStompAndReturn(Player player, Vector2 source, Vector2 landing, float targetHalfWidth,
        Vector2 enemy, float width, float aboveFeet, float feetHalfWidth, bool preparedStart, List<Vector2> path)
    {
        path?.Clear();
        if (player == null || width <= 0f || targetHalfWidth <= 0f) return false;
        float scale = GameplayPreferences.HorizontalMovementScale;
        bool inertia = GameplayPreferences.UseInertia;
        float step = Mathf.Clamp(Time.fixedDeltaTime, .005f, .04f);
        float enemyX = TrapGeometry.WrappedDelta(source.x, enemy.x, width);
        float enemyTop = enemy.y + ExpeditionEnemy.BodyHalfHeight * GameplayPreferences.ModelScale - source.y;
        float landingX = TrapGeometry.WrappedDelta(source.x, landing.x, width), gap = landing.y - source.y;
        float x = 0f, y = 0f, speed = player.moveSpeed * scale;
        float vx = preparedStart ? Mathf.Sign(enemyX) * speed : 0f, vy = player.NormalJumpForce;
        bool hit = false, roseAboveLanding = false;
        path?.Add(new Vector2(x, y + aboveFeet));
        for (float elapsed = 0f; elapsed < 6f; elapsed += step)
        {
            float control = vy < 0f ? player.fallingControl : player.risingControl;
            float input = AerialPickupPlacement.SteeringInput(player, (hit ? landingX : enemyX) - x, vx, control, scale, inertia, step);
            if (!inertia) vx = input * speed;
            else
            {
                bool turning = input != 0f && Mathf.Abs(vx) > .1f && Mathf.Sign(input) != Mathf.Sign(vx);
                float rate = input == 0f ? player.airDeceleration * scale : (turning ? player.turnAcceleration : player.acceleration) * control * scale;
                vx = Mathf.MoveTowards(vx, input * speed, rate * step);
            }
            float previousX = x, previousY = y;
            float gravity = Mathf.Abs(vy) <= player.apexVelocityThreshold ? player.apexGravityScale : vy > 0f ? player.risingGravityScale : player.fallingGravityScale;
            vy = Mathf.Max(vy, -player.maximumFallSpeed); vy -= Mathf.Abs(Physics2D.gravity.y) * gravity * step;
            y += vy * step; x += vx * step;
            if (!hit && vy < 0f && previousY >= enemyTop && y <= enemyTop)
            {
                float fraction = Mathf.InverseLerp(previousY, y, enemyTop);
                x = Mathf.Lerp(previousX, x, fraction);
                if (Mathf.Abs(x - enemyX) > ExpeditionEnemy.BodyHalfWidth * GameplayPreferences.ModelScale * .85f + feetHalfWidth * .75f) return false;
                y = enemyTop; vy = player.NormalJumpForce; hit = true;
            }
            path?.Add(new Vector2(x, y + aboveFeet));
            roseAboveLanding |= y >= gap;
            if (hit && vy < 0f && y <= gap && roseAboveLanding)
            {
                float fraction = Mathf.InverseLerp(previousY, y, gap);
                return Mathf.Abs(Mathf.Lerp(previousX, x, fraction) - landingX) <= targetHalfWidth * .60f;
            }
            if (vy < 0f && y < 0f) return false;
        }
        return false;
    }

    private void ReadAirspace()
    {
        surfaces.Clear(); hazards.Clear(); landingColliders.Clear(); landingBounds.Clear();
        foreach (GameObject platform in GameObject.FindGameObjectsWithTag("Platform"))
        {
            Collider2D collider = platform.GetComponent<Collider2D>(); if (collider == null || !collider.enabled) continue;
            Bounds bounds = collider.bounds;
            float patrol = platform.GetComponent<MovingPlatform>() != null ? maximumPatrol * 2f : 0f;
            Rect landing = new Rect(bounds.min.x - patrol, bounds.min.y, bounds.size.x + patrol * 2f, bounds.size.y);
            landingColliders.Add(collider); landingBounds.Add(landing);
            SpriteRenderer sprite = platform.GetComponent<SpriteRenderer>(); if (sprite != null) bounds.Encapsulate(sprite.bounds);
            surfaces.Add(new Rect(bounds.min.x - patrol, bounds.min.y, bounds.size.x + patrol * 2f, bounds.size.y));
        }
        foreach (DoodlikHazard danger in Object.FindObjectsByType<DoodlikHazard>()) hazards.Add(danger.PatrolBounds);
        foreach (ExpeditionEnemy enemy in Object.FindObjectsByType<ExpeditionEnemy>()) hazards.Add(enemy.PatrolBounds);
        float separation = .40f * GameplayPreferences.ModelScale;
        foreach (RunPickup pickup in Object.FindObjectsByType<RunPickup>()) surfaces.Add(new Rect((Vector2)pickup.transform.position - Vector2.one * separation, Vector2.one * (separation * 2f)));
        foreach (ExpeditionPickup pickup in Object.FindObjectsByType<ExpeditionPickup>()) surfaces.Add(new Rect((Vector2)pickup.transform.position - Vector2.one * separation, Vector2.one * (separation * 2f)));
    }
    private bool AreaClear(Rect area, float width) => ClearOf(area, surfaces, width) && ClearOf(area, hazards, width);
    private static bool ClearOf(Rect area, List<Rect> obstacles, float width)
    {
        Rect expanded = area; expanded.xMin -= .10f; expanded.xMax += .10f; expanded.yMin -= .10f; expanded.yMax += .10f;
        foreach (Rect obstacle in obstacles)
        for (int copy = -1; copy <= 1; copy++) { Rect shifted = obstacle; shifted.x += copy * width; if (expanded.Overlaps(shifted)) return false; }
        return true;
    }
    private static bool OffMainRoute(Rect area, Vector2 source, float landingX, float bodyHalfWidth, float width)
    {
        float left = Mathf.Min(source.x, landingX) - bodyHalfWidth - .08f;
        float right = Mathf.Max(source.x, landingX) + bodyHalfWidth + .08f;
        for (int copy = -1; copy <= 1; copy++)
            if (area.xMax + copy * width >= left && area.xMin + copy * width <= right) return false;
        return true;
    }
    private static bool SafeSurface(Collider2D surface, float patrol) => surface != null && surface.enabled &&
        surface.gameObject.activeInHierarchy && !surface.isTrigger && patrol <= 0f && surface.GetComponent<CrumblingPlatform>() == null &&
        surface.GetComponentInChildren<Bush>() == null && surface.GetComponentInChildren<DoodlikHazard>() == null;
    private void NextBand()
    {
        if (nextBandHeight > int.MaxValue - 190 - HalfBandHeight) { exhausted = true; return; }
        nextBandHeight += Range(120, 190); bandSeed = Range(0, 1000000);
    }
    private uint NextRandom() { random ^= random << 13; random ^= random >> 17; random ^= random << 5; return random; }
    private int Range(int min, int max) => min + (int)(NextRandom() % (uint)(max - min + 1));
}
