using System.Collections.Generic;
using UnityEngine;

// The camera opens sparse height bands; platforms are only landing references
// used to prove optional detours. No coin is created by a platform spawn.
public sealed class AirCoinSpawner
{
    private struct Route
    {
        public Collider2D Source, Target;
        public float SourcePatrol, TargetPatrol;
    }
    private struct Surface
    {
        public Collider2D Collider;
        public Rect Patrol;
    }
    private struct PendingBonus
    {
        public Route Route;
        public RunPickup Pickup;
        public int PlatformIndex;
    }
    private readonly List<Route> routes = new List<Route>();
    private readonly List<PendingBonus> pendingBonuses = new List<PendingBonus>();
    private readonly List<Rect> surfaces = new List<Rect>();
    private readonly List<Rect> hazards = new List<Rect>();
    private readonly List<Vector2> witness = new List<Vector2>(96);
    private readonly List<Surface> landingSurfaces = new List<Surface>();
    private readonly List<Rect> earlyLandings = new List<Rect>();
    private readonly Player player;
    private readonly Camera camera;
    private readonly PickupSpawnSchedule schedule;
    private readonly float maximumPatrol;
    private readonly float jumpApex;
    private Collider2D previousPlatform;
    private float previousPatrol;
    private int eligibleCoinRewards;
    public const int GoldenFishMinimumHeight = 200;
    public const int GoldenFishInterval = 8;

    public AirCoinSpawner(Player player, Camera camera, PickupSpawnSchedule schedule, float maximumPatrol)
    {
        this.player = player;
        this.camera = camera;
        this.schedule = schedule;
        this.maximumPatrol = maximumPatrol;
        jumpApex = JumpReachability.Simulate(player, 0f, GameplayPreferences.UseInertia,
            GameplayPreferences.HorizontalMovementScale).Apex;
    }

    public void RegisterPlatform(GameObject platform, float patrol)
    {
        Collider2D surface = platform.GetComponent<Collider2D>();
        if (surface == null) return;
        if (previousPlatform != null)
            routes.Add(new Route { Source = previousPlatform, Target = surface,
                SourcePatrol = previousPatrol, TargetPatrol = patrol });
        previousPlatform = surface;
        previousPatrol = patrol;
    }

    public void TryPlaceScheduledBonus(RunPickup bonus, int platformIndex)
    {
        if (bonus == null || routes.Count == 0) return;
        Route route = routes[routes.Count - 1];
        if (!HasLiveSurfaces(route.Source, route.Target) ||
            route.SourcePatrol > 0f || route.TargetPatrol > 0f) return;
        pendingBonuses.Add(new PendingBonus { Route = route, Pickup = bonus,
            PlatformIndex = platformIndex });
    }

    public static bool HasLiveSurfaces(Collider2D source, Collider2D target) =>
        source != null && target != null && source.enabled && target.enabled &&
        source.gameObject.activeInHierarchy && target.gameObject.activeInHierarchy;

    private void ProcessPendingBonuses(float finalizedHeight, float cameraTop)
    {
        Collider2D feet = player != null ? player.GetComponent<Collider2D>() : null;
        SpriteRenderer body = player != null ? player.GetComponent<SpriteRenderer>() : null;
        if (feet == null || body == null) return;
        float bodyAboveFeet = body.bounds.center.y - feet.bounds.min.y;
        float bodyHalfHeight = body.bounds.extents.y * 0.65f;
        for (int i = pendingBonuses.Count - 1; i >= 0; i--)
        {
            PendingBonus pending = pendingBonuses[i];
            if (pending.Pickup == null || pending.Pickup.transform.parent == null ||
                !HasLiveSurfaces(pending.Route.Source, pending.Route.Target))
            { pendingBonuses.RemoveAt(i); continue; }
            // Wait until the whole jump is below the hazard/platform frontier.
            // Moving the icon while its route is visible would look like a pop.
            float targetTop = pending.Route.Target.bounds.max.y;
            float wholeJumpTop = pending.Route.Source.bounds.max.y + jumpApex +
                bodyAboveFeet + bodyHalfHeight;
            if (wholeJumpTop > finalizedHeight && targetTop > cameraTop) continue;
            if (wholeJumpTop <= finalizedHeight && targetTop > cameraTop + 0.8f)
                TryPlaceBonusInAir(pending.Route.Source, pending.Route.Target,
                    pending.Pickup, pending.PlatformIndex, cameraTop, finalizedHeight);
            pendingBonuses.RemoveAt(i);
        }
    }

    private void TryPlaceBonusInAir(Collider2D source, Collider2D target,
        RunPickup bonus, int platformIndex, float cameraTop, float finalizedHeight)
    {
        Collider2D feet = player != null ? player.GetComponent<Collider2D>() : null;
        SpriteRenderer body = player != null ? player.GetComponent<SpriteRenderer>() : null;
        if (camera == null || feet == null || body == null) return;

        float width = camera.orthographicSize * camera.aspect * 2f;
        float scale = GameplayPreferences.ModelScale;
        float aboveFeet = body.bounds.center.y - feet.bounds.min.y;
        Vector2 bodyHalfSize = (Vector2)body.bounds.extents * 0.65f;
        Vector2 from = new Vector2(source.bounds.center.x, source.bounds.max.y);
        Vector2 to = new Vector2(target.bounds.center.x, target.bounds.max.y);
        float delta = TrapGeometry.WrappedDelta(from.x, to.x, width);
        float speed = player.moveSpeed * GameplayPreferences.HorizontalMovementScale;
        ReadAirspace(scale, bonus);
        earlyLandings.Clear();
        foreach (Surface surface in landingSurfaces)
            if (surface.Collider != target) earlyLandings.Add(surface.Patrol);

        // A scheduled bonus remains above its platform unless a genuine
        // optional excursion and safe return can be certified. Scheduling is
        // unchanged, so aerial rewards do not inflate the bonus economy.
        for (int attempt = 0; attempt < 18; attempt++)
        {
            if (!AerialPickupPlacement.TryDetour(player, from, to, target.bounds.extents.x,
                width, aboveFeet, bodyHalfSize, platformIndex + attempt, out Vector2 point, null, 1) ||
                point.y - 0.25f * scale <= cameraTop + 0.8f ||
                !IsAirClear(point, scale, width, surfaces, hazards)) continue;

            Vector2 localPoint = point - from;
            bool safe = true;
            for (int start = 0; start <= (GameplayPreferences.UseInertia ? 1 : 0); start++)
            {
                if (!AerialPickupPlacement.CanCollectAndLand(player, to.y - from.y, delta,
                    target.bounds.extents.x, aboveFeet, localPoint,
                    start * Mathf.Sign(localPoint.x) * speed, GameplayPreferences.UseInertia,
                    GameplayPreferences.HorizontalMovementScale, witness) ||
                    !IsWitnessClear(witness, from, bodyHalfSize, width, finalizedHeight, hazards) ||
                    !IsLandingPathClear(witness, from, aboveFeet, feet.bounds.extents.x,
                        width, earlyLandings))
                { safe = false; break; }
            }
            if (!safe) continue;

            point.x = camera.transform.position.x +
                TrapGeometry.WrappedDelta(camera.transform.position.x, point.x, width);
            bonus.PlaceInAir(point);
            for (int start = 0; start <= (GameplayPreferences.UseInertia ? 1 : 0); start++)
            {
                AerialPickupPlacement.CanCollectAndLand(player, to.y - from.y, delta,
                    target.bounds.extents.x, aboveFeet, localPoint,
                    start * Mathf.Sign(localPoint.x) * speed, GameplayPreferences.UseInertia,
                    GameplayPreferences.HorizontalMovementScale, witness);
                CoinRouteReservations.Reserve(camera, witness, from, bodyHalfSize);
            }
            return;
        }
    }

    public void Advance(float generatedHeight, bool hazardsEnabled)
    {
        if (player == null || camera == null || schedule == null || !RunFlow.IsPlaying || Time.timeScale <= 0f) return;
        float cameraTop = camera.transform.position.y + camera.orthographicSize;
        // HazardSpawner has finalized all patrols below generatedHeight-apex-2.
        // Keep the coin and its whole jump below that frontier, not just its icon.
        float finalizedHeight = generatedHeight - (hazardsEnabled ? jumpApex + 2.5f : 0.5f);
        ProcessPendingBonuses(finalizedHeight, cameraTop);
        float knownHeight = Mathf.Min(cameraTop + 4f, finalizedHeight - 2f);
        while (schedule.TryTakeCoinBand(knownHeight, out PickupSpawnSchedule.CoinBand band))
            TrySpawnBand(band, finalizedHeight);
        float bottom = camera.transform.position.y - camera.orthographicSize - 1f;
        for (int i = routes.Count - 1; i >= 0; i--)
            if (routes[i].Source == null || routes[i].Target == null || routes[i].Target.bounds.max.y < bottom)
                routes.RemoveAt(i);
    }

    public void InvalidateLayout()
    {
        pendingBonuses.Clear();
        RunPickup.RemoveUnclaimedCoins();
        RunPickup.RemoveUnclaimedAerialBonuses();
        CoinRouteReservations.ClearForCamera(camera);
    }

    private void TrySpawnBand(PickupSpawnSchedule.CoinBand band, float finalizedHeight)
    {
        float width = camera.orthographicSize * camera.aspect * 2f;
        float scale = GameplayPreferences.ModelScale;
        Collider2D feet = player.GetComponent<Collider2D>();
        SpriteRenderer body = player.GetComponent<SpriteRenderer>();
        if (feet == null || body == null || routes.Count == 0) return;
        float aboveFeet = body.bounds.center.y - feet.bounds.min.y;
        Vector2 bodyHalfSize = (Vector2)body.bounds.extents * 0.65f;
        ReadAirspace(scale);
        int offset = Mathf.Abs(band.Seed % routes.Count);
        for (int i = 0; i < routes.Count; i++)
        {
            Route route = routes[(i + offset) % routes.Count];
            if (route.Source == null || route.Target == null || !route.Source.enabled || !route.Target.enabled ||
                route.SourcePatrol > 0f || route.TargetPatrol > 0f) continue;
            Vector2 from = new Vector2(route.Source.bounds.center.x, route.Source.bounds.max.y);
            Vector2 to = new Vector2(route.Target.bounds.center.x, route.Target.bounds.max.y);
            if (to.y < band.Minimum - 4.5f || from.y > band.Maximum) continue;
            earlyLandings.Clear();
            foreach (Surface surface in landingSurfaces)
                if (surface.Collider != route.Target) earlyLandings.Add(surface.Patrol);
            // Enumerate each of the 18 deterministic candidates only once;
            // do not repeat a complete 18-candidate search for every rejection.
            for (int attempt = 0; attempt < 18; attempt++)
            {
                if (!AerialPickupPlacement.TryDetour(player, from, to, route.Target.bounds.extents.x,
                    width, aboveFeet, bodyHalfSize, band.Seed + attempt, out Vector2 point, null, 1)) continue;
                if (point.y < band.Minimum || point.y > band.Maximum) continue;
                if (!IsAirClear(point, scale, width, surfaces, hazards)) continue;
                float delta = TrapGeometry.WrappedDelta(from.x, to.x, width);
                Vector2 localPoint = point - from;
                bool safe = true;
                float speed = player.moveSpeed * GameplayPreferences.HorizontalMovementScale;
                for (int start = 0; start <= (GameplayPreferences.UseInertia ? 1 : 0); start++)
                {
                    if (!AerialPickupPlacement.CanCollectAndLand(player, to.y - from.y, delta,
                        route.Target.bounds.extents.x, aboveFeet, localPoint, start * Mathf.Sign(localPoint.x) * speed,
                        GameplayPreferences.UseInertia, GameplayPreferences.HorizontalMovementScale, witness) ||
                        !IsWitnessClear(witness, from, bodyHalfSize, width, finalizedHeight, hazards) ||
                        !IsLandingPathClear(witness, from, aboveFeet, feet.bounds.extents.x, width, earlyLandings))
                    { safe = false; break; }
                }
                if (!safe) continue;
                point.x = camera.transform.position.x + TrapGeometry.WrappedDelta(camera.transform.position.x, point.x, width);
                // Replace, never add: every eighth successful eligible coin
                // becomes a five-coin fish. Failed routes do not advance this
                // clock or cause an unreachable lure / a later reward cluster.
                bool eligible = WorldZones.ScoreAtHeight(point.y) >= GoldenFishMinimumHeight;
                bool golden = eligible && eligibleCoinRewards == GoldenFishInterval - 1;
                RunPickup reward = golden ? RunPickup.SpawnGoldenFish(point, camera, band.Seed * 0.001f)
                    : RunPickup.SpawnAirCoin(point, camera, band.Seed * 0.001f);
                if (reward != null)
                {
                    if (eligible) eligibleCoinRewards = (eligibleCoinRewards + 1) % GoldenFishInterval;
                    for (int start = 0; start <= (GameplayPreferences.UseInertia ? 1 : 0); start++)
                    {
                        AerialPickupPlacement.CanCollectAndLand(player, to.y - from.y, delta,
                            route.Target.bounds.extents.x, aboveFeet, localPoint, start * Mathf.Sign(localPoint.x) * speed,
                            GameplayPreferences.UseInertia, GameplayPreferences.HorizontalMovementScale, witness);
                        CoinRouteReservations.Reserve(camera, witness, from, bodyHalfSize);
                    }
                }
                return;
            }
        }
        // Skipping a band is intentional. Never turn an invalid optional coin
        // into an on-platform reward, an unreachable lure, or a later cluster.
    }

    private void ReadAirspace(float scale, RunPickup ignoredPickup = null)
    {
        surfaces.Clear();
        hazards.Clear();
        landingSurfaces.Clear();
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
            surfaces.Add(new Rect(bounds.min.x - patrol, bounds.min.y,
                bounds.size.x + patrol * 2f, bounds.size.y));
        }
        foreach (DoodlikHazard hazard in Object.FindObjectsByType<DoodlikHazard>(FindObjectsSortMode.None))
            hazards.Add(hazard.PatrolBounds);
        foreach (ExpeditionEnemy enemy in Object.FindObjectsByType<ExpeditionEnemy>(FindObjectsSortMode.None))
            hazards.Add(enemy.PatrolBounds);
        foreach (RunPickup pickup in Object.FindObjectsByType<RunPickup>(FindObjectsSortMode.None))
        {
            if (pickup == ignoredPickup) continue;
            surfaces.Add(new Rect((Vector2)pickup.transform.position - Vector2.one * (0.3f * scale), Vector2.one * (0.6f * scale)));
        }
    }

    public static bool IsAirClear(Vector2 point, float scale, float width, IList<Rect> platforms, IList<Rect> danger)
    {
        // Larger than the 0.23*scale icon: clear air separates coins visually
        // from every surface, including the whole patrol of moving platforms.
        Vector2 padding = new Vector2(0.38f, 0.75f) * scale;
        foreach (Rect surface in platforms)
            if (WrappedOverlap(point, padding, surface, width)) return false;
        foreach (Rect hazard in danger)
            if (WrappedOverlap(point, Vector2.one * (0.42f * scale), hazard, width)) return false;
        return true;
    }

    public static bool IsWitnessClear(IList<Vector2> path, Vector2 origin, Vector2 halfSize,
        float width, float finalizedHeight, IList<Rect> danger)
    {
        for (int i = 0; i < path.Count; i++)
        {
            Vector2 current = origin + path[i];
            if (current.y + halfSize.y > finalizedHeight) return false;
            if (i == 0) continue;
            Vector2 previous = origin + path[i - 1];
            foreach (Rect hazard in danger)
            for (int copy = -1; copy <= 1; copy++)
            {
                Rect shifted = hazard;
                shifted.x += width * copy;
                if (TrapGeometry.SweptBox(previous, current, halfSize + Vector2.one * 0.08f, shifted)) return false;
            }
        }
        return true;
    }

    public static bool IsLandingPathClear(IList<Vector2> path, Vector2 origin, float bodyAboveFeet,
        float feetHalfWidth, float width, IList<Rect> otherPlatforms)
    {
        for (int i = 1; i < path.Count; i++)
        {
            Vector2 previous = origin + path[i - 1] - Vector2.up * bodyAboveFeet;
            Vector2 current = origin + path[i] - Vector2.up * bodyAboveFeet;
            if (current.y >= previous.y) continue;
            foreach (Rect platform in otherPlatforms)
            {
                if (previous.y < platform.yMax || current.y > platform.yMax) continue;
                float x = Mathf.Lerp(previous.x, current.x,
                    Mathf.InverseLerp(previous.y, current.y, platform.yMax));
                for (int copy = -1; copy <= 1; copy++)
                    if (x + feetHalfWidth >= platform.xMin + width * copy &&
                        x - feetHalfWidth <= platform.xMax + width * copy) return false;
            }
        }
        return true;
    }

    private static bool WrappedOverlap(Vector2 point, Vector2 padding, Rect obstacle, float width)
    {
        for (int copy = -1; copy <= 1; copy++)
        {
            Rect shifted = obstacle;
            shifted.x += width * copy;
            if (TrapGeometry.SweptBox(point, point, padding, shifted)) return true;
        }
        return false;
    }
}
