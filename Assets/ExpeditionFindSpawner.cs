using System.Collections.Generic;
using UnityEngine;

// Optional cargo follows independent raw-height bands. Existing platforms are
// references for a collect-and-return proof, not parents or new route branches.
public sealed class ExpeditionFindSpawner
{
    private struct Route { public Collider2D Source, Target; public float SourcePatrol, TargetPatrol; }
    private struct Surface { public Collider2D Collider; public Rect Patrol; }
    private readonly Player player;
    private readonly Camera camera;
    private readonly float maximumPatrol, jumpApex;
    private readonly List<Route> routes = new List<Route>(32);
    private readonly List<Rect> surfaces = new List<Rect>(64), hazards = new List<Rect>(24), earlyLandings = new List<Rect>(64);
    private readonly List<Surface> landingSurfaces = new List<Surface>(64);
    private readonly List<Vector2> witness = new List<Vector2>(96);
    private Collider2D previousPlatform;
    private float previousPatrol, retryAt;
    private uint random;
    private int nextBandHeight, bandSeed;
    private bool exhausted;
    public const int MaximumAlive = 2;
    public const int HalfBandHeight = 55;
    public int SpawnedCount { get; private set; }
    public int PlacementAttempts { get; private set; }
    public int NextBandHeight => nextBandHeight;

    public ExpeditionFindSpawner(Player player, Camera camera, float maximumPatrol, uint seed)
    {
        this.player = player; this.camera = camera; this.maximumPatrol = maximumPatrol;
        random = seed ^ 0xd1b54a35u;
        if (random == 0) random = 0x94d049bbu;
        nextBandHeight = 60 + Range(0, 40);
        bandSeed = Range(0, 1000000);
        jumpApex = player != null ? JumpReachability.Simulate(player, 0f, GameplayPreferences.UseInertia,
            GameplayPreferences.HorizontalMovementScale).Apex : 0f;
    }

    public void RegisterPlatform(GameObject platform, float patrol)
    {
        Collider2D current = platform != null ? platform.GetComponent<Collider2D>() : null;
        if (current == null) return;
        if (previousPlatform != null) routes.Add(new Route { Source = previousPlatform, Target = current,
            SourcePatrol = previousPatrol, TargetPatrol = patrol });
        previousPlatform = current; previousPatrol = patrol;
    }

    public void Advance(float generatedHeight, bool hazardsEnabled, bool planningComplete = true)
    {
        if (GameplayPreferences.RunKind != GameRunKind.Expedition || player == null || player.IsDead ||
            camera == null || !RunFlow.IsPlaying || Time.timeScale <= 0f || exhausted) return;
        float cameraTop = camera.transform.position.y + camera.orthographicSize;
        float bottom = camera.transform.position.y - camera.orthographicSize - 1f;
        for (int i = routes.Count - 1; i >= 0; i--)
            if (routes[i].Source == null || routes[i].Target == null || routes[i].Target.bounds.max.y < bottom)
                routes.RemoveAt(i);
        // Never reserve an additional no-hazard lane. Wait for the existing
        // sliced planner to finish, then inspect its actual committed patrols.
        if (!planningComplete) return;
        int visibleHeight = WorldZones.ScoreAtHeight(cameraTop + .8f);
        while (!exhausted && nextBandHeight <= visibleHeight - HalfBandHeight) NextBand();
        if (exhausted || ExpeditionPickup.ActiveCount >= MaximumAlive || Time.unscaledTime < retryAt) return;
        float finalizedHeight = generatedHeight - (hazardsEnabled ? jumpApex + 2.5f : .5f);
        float knownTop = Mathf.Min(cameraTop + 4f, finalizedHeight - 2f);
        if (WorldZones.ScoreAtHeight(knownTop) < nextBandHeight) return;
        retryAt = Time.unscaledTime + .2f;
        PlacementAttempts++;
        if (TrySpawnBand(cameraTop, knownTop, finalizedHeight)) { SpawnedCount++; NextBand(); }
        // A failed opportunity stays available until the band reaches the
        // viewport. It is then skipped, never queued into a later cluster.
    }

    public void InvalidateLayout()
    {
        ExpeditionPickup.RemoveUnclaimed();
        // Horizontal remapping may precede the next physics sync. Wait at
        // least one fixed step before proving a new collect-and-return path.
        retryAt = Time.unscaledTime + .15f;
    }

    private bool TrySpawnBand(float cameraTop, float knownTop, float finalizedHeight)
    {
        float width = camera.orthographicSize * camera.aspect * 2f;
        float scale = GameplayPreferences.ModelScale;
        Collider2D feet = player.GetComponent<Collider2D>();
        SpriteRenderer body = player.GetComponent<SpriteRenderer>();
        if (feet == null || body == null || width <= 0f || routes.Count == 0) return false;
        float aboveFeet = body.bounds.center.y - feet.bounds.min.y;
        Vector2 halfSize = (Vector2)body.bounds.extents * .65f;
        ReadAirspace(scale);
        int offset = bandSeed % routes.Count;
        for (int i = 0; i < routes.Count; i++)
        {
            Route route = routes[(i + offset) % routes.Count];
            if (!HasSafeSurface(route.Source, route.SourcePatrol) || !HasSafeSurface(route.Target, route.TargetPatrol)) continue;
            Vector2 from = new Vector2(route.Source.bounds.center.x, route.Source.bounds.max.y);
            Vector2 to = new Vector2(route.Target.bounds.center.x, route.Target.bounds.max.y);
            if (to.y < WorldZones.HeightAtScore(Mathf.Max(30, nextBandHeight - HalfBandHeight)) - 4.5f ||
                from.y > WorldZones.HeightAtScore(nextBandHeight + HalfBandHeight)) continue;
            earlyLandings.Clear();
            foreach (Surface surface in landingSurfaces)
                if (surface.Collider != route.Target) earlyLandings.Add(surface.Patrol);
            for (int attempt = 0; attempt < 18; attempt++)
            {
                if (!AerialPickupPlacement.TryDetour(player, from, to, route.Target.bounds.extents.x,
                    width, aboveFeet, halfSize, bandSeed + attempt, out Vector2 point, null, 1)) continue;
                int height = WorldZones.ScoreAtHeight(point.y);
                if (height < Mathf.Max(30, nextBandHeight - HalfBandHeight) || height > nextBandHeight + HalfBandHeight ||
                    point.y <= cameraTop + .8f || point.y > knownTop) continue;
                if (!AirCoinSpawner.IsAirClear(point, scale, width, surfaces, hazards)) continue;
                float delta = TrapGeometry.WrappedDelta(from.x, to.x, width);
                Vector2 localPoint = point - from;
                float speed = player.moveSpeed * GameplayPreferences.HorizontalMovementScale;
                bool safe = true;
                for (int start = 0; start <= (GameplayPreferences.UseInertia ? 1 : 0); start++)
                {
                    if (!AerialPickupPlacement.CanCollectAndLand(player, to.y - from.y, delta,
                        route.Target.bounds.extents.x, aboveFeet, localPoint, start * Mathf.Sign(localPoint.x) * speed,
                        GameplayPreferences.UseInertia, GameplayPreferences.HorizontalMovementScale, witness) ||
                        !AirCoinSpawner.IsWitnessClear(witness, from, halfSize, width, finalizedHeight, hazards) ||
                        !AirCoinSpawner.IsLandingPathClear(witness, from, aboveFeet, feet.bounds.extents.x, width, earlyLandings))
                    { safe = false; break; }
                }
                if (!safe) continue;
                point.x = camera.transform.position.x + TrapGeometry.WrappedDelta(camera.transform.position.x, point.x, width);
                ExpeditionFindKind kind = KindAtHeight(height);
                ExpeditionPickup pickup = ExpeditionPickup.Spawn(kind, point, camera, bandSeed * .001f);
                return pickup != null;
            }
        }
        return false;
    }

    private static bool HasSafeSurface(Collider2D surface, float patrol) =>
        surface != null && surface.enabled && surface.gameObject.activeInHierarchy && patrol <= 0f &&
        surface.GetComponent<CrumblingPlatform>() == null && surface.GetComponentInChildren<Bush>() == null &&
        surface.GetComponentInChildren<DoodlikHazard>() == null;

    public static ExpeditionFindKind KindAtHeight(int height)
    {
        ExpeditionFindKind best = ExpeditionFindKind.None;
        foreach (ExpeditionCatalog.Definition definition in ExpeditionCatalog.All)
            if (!definition.EnemyOnly && height >= definition.MinimumHeight) best = definition.Kind;
        return best;
    }

    private void ReadAirspace(float scale)
    {
        surfaces.Clear(); hazards.Clear(); landingSurfaces.Clear();
        foreach (GameObject platform in GameObject.FindGameObjectsWithTag("Platform"))
        {
            Collider2D collider = platform.GetComponent<Collider2D>();
            if (collider == null || !collider.enabled) continue;
            Bounds bounds = collider.bounds;
            float patrol = platform.GetComponent<MovingPlatform>() != null ? maximumPatrol * 2f : 0f;
            landingSurfaces.Add(new Surface { Collider = collider,
                Patrol = new Rect(bounds.min.x - patrol, bounds.min.y, bounds.size.x + patrol * 2f, bounds.size.y) });
            SpriteRenderer sprite = platform.GetComponent<SpriteRenderer>();
            if (sprite != null) bounds.Encapsulate(sprite.bounds);
            surfaces.Add(new Rect(bounds.min.x - patrol, bounds.min.y, bounds.size.x + patrol * 2f, bounds.size.y));
        }
        foreach (DoodlikHazard hazard in Object.FindObjectsByType<DoodlikHazard>()) hazards.Add(hazard.PatrolBounds);
        foreach (ExpeditionEnemy enemy in Object.FindObjectsByType<ExpeditionEnemy>()) hazards.Add(enemy.PatrolBounds);
        foreach (RunPickup pickup in Object.FindObjectsByType<RunPickup>())
            AddPickupAirspace(pickup.transform.position, scale);
        foreach (ExpeditionPickup pickup in Object.FindObjectsByType<ExpeditionPickup>())
            AddPickupAirspace(pickup.transform.position, scale);
    }
    private void AddPickupAirspace(Vector2 point, float scale) =>
        surfaces.Add(new Rect(point - Vector2.one * (.35f * scale), Vector2.one * (.70f * scale)));

    private void NextBand()
    {
        if (nextBandHeight > int.MaxValue - 220 - HalfBandHeight) { exhausted = true; return; }
        nextBandHeight += Range(150, 220); bandSeed = Range(0, 1000000);
    }
    private int Range(int minimum, int maximum)
    {
        random ^= random << 13; random ^= random >> 17; random ^= random << 5;
        return minimum + (int)(random % (uint)(maximum - minimum + 1));
    }
}
