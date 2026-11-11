using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class TrapSettings
{
    public bool enabled = true;
    [Min(8f), Tooltip("Высота появления первых колючек; старт остаётся безопасным.")]
    public float startingHeight = 14f;
    [Min(12f), Tooltip("Первые редкие летучие колючки после безопасного старта.")]
    public float flyingSeedStartingHeight = 16f;
    [Tooltip("Вероятность попытки размещения: в начале / на максимальной сложности.")]
    public Vector2 spawnChance = new Vector2(0.55f, 0.8f);
    [Min(4f), Tooltip("Минимальное расстояние по высоте между ловушками.")]
    public float minimumSpacing = 5.5f;
}

// Owned by the existing platform spawner. Hazards are finalized only after
// higher route segments are known, and before this height enters the camera.
public sealed class HazardSpawner
{
    private sealed class Step
    {
        public Vector2 from, to;
        public float margin;
        public bool allowFlyingDetour;
        public int preparedDirection;
        public FlyingHazardRoutes.Paths paths;
        public bool moving;
    }

    private sealed class PendingPlacement
    {
        public Rect area;
        public Vector2 center;
        public Vector2[] route;
        public FlyingHazardRoutes.Paths initialPaths;
        public Rect[] existing;
        public Step[] knownSteps;
    }

    private readonly List<Step> steps = new List<Step>();
    private readonly List<DoodlikHazard> hazards = new List<DoodlikHazard>();
    private readonly List<DoodlikHazard> thornPatches = new List<DoodlikHazard>();
    private readonly List<Rect> alternativePlatforms = new List<Rect>();
    private readonly List<Rect> bushLaunchAreas = new List<Rect>();
    private float bushReservationModelScale;
    private readonly Camera camera;
    private readonly GameObject platformPrefab;
    private readonly TrapSettings settings;
    private readonly DifficultyGenerationProfile balance;
    public GameDifficulty RunDifficulty => balance.Difficulty;
    private readonly float jumpHeight;
    private readonly float playerMargin;
    private readonly float inertiaMargin;
    private FlyingHazardRoutes flyingRoutes;
    private readonly Player routePlayer;
    private readonly List<Rect> existingPatrols = new List<Rect>();
    private readonly List<Vector2> targetedCandidates = new List<Vector2>(36);
    private int nextCandidate;
    private float lastHazardY = float.NegativeInfinity;
    private int majorHazardsPlaced;
    private int horizontalBranchesPlaced;
    private int meadowFlyingPlaced;
    private int bumblebeeEligibleFlights;
    private float allowAbove;
    private int chainAttempts;
    private Vector2[] acceptedConnectedRoute;
    private Rect[] acceptedConnectedExisting;
    private FlyingHazardRoutes.Paths acceptedConnectedInitialPaths;
    private readonly List<PendingPlacement> pendingCandidates = new List<PendingPlacement>(2);
    private object activeJob;
    private Vector2? deferredRow;
    // Optional cargo observes completed hazard planning without reserving new
    // exclusion lanes or changing the existing density/RNG decisions.
    public bool HasPendingPlanning => activeJob != null || deferredRow.HasValue;
    private int planningStage;
#if UNITY_EDITOR
    public static bool DiagnoseFlying;
    public static bool DiagnoseConnected;
    public static bool ValidateSlicedInEditor;
    private static double longestPlanningSliceMs;
    private static int planningSlices;
    private static readonly double[] stageMaximumMs = new double[5];
    private static readonly int[] flyingRejections = new int[7];
    private static readonly string[] flyingExamples = new string[7];
    private static int connectedAttempts, connectedAccepted;
    private static double connectedLongestMs;
    public static void ResetFlyingDiagnostics()
    {
        Array.Clear(flyingRejections, 0, flyingRejections.Length);
        Array.Clear(flyingExamples, 0, flyingExamples.Length);
        connectedAttempts = connectedAccepted = 0;
        connectedLongestMs = 0;
        longestPlanningSliceMs = 0;
        planningSlices = 0;
        Array.Clear(stageMaximumMs, 0, stageMaximumMs.Length);
    }
    public static string FlyingDiagnostics()
    {
        string[] names = { "otherHazard", "movingCorridor", "narrowOrAerialCorridor", "incompletePaths", "noSafePath", "irrelevant", "accepted" };
        string result = "FLYING_CANDIDATE_DIAGNOSTICS";
        for (int i = 0; i < names.Length; i++)
            result += " " + names[i] + "=" + flyingRejections[i] +
                (flyingExamples[i] == null ? "" : " [" + flyingExamples[i] + "]");
        return result + $" connectedAttempts={connectedAttempts} connectedAccepted={connectedAccepted} connectedLongestMs={connectedLongestMs:F2}" +
            $" planningSlices={planningSlices} longestPlanningSliceMs={longestPlanningSliceMs:F3}" +
            $" stagesMs=prepare:{stageMaximumMs[0]:F3},cache:{stageMaximumMs[1]:F3},candidate:{stageMaximumMs[2]:F3},chain:{stageMaximumMs[3]:F3},commit:{stageMaximumMs[4]:F3}";
    }
#endif

    private bool FlyingDecision(int reason, Step step = null)
    {
#if UNITY_EDITOR
        if (DiagnoseFlying)
        {
            flyingRejections[reason]++;
            if (step != null && flyingExamples[reason] == null)
                flyingExamples[reason] = $"from={step.from} to={step.to}";
        }
#endif
        return reason == 6;
    }

    // Runtime route proofs are spread across frames. Generate the next rows
    // well ahead of the camera so a short bush-speed ascent does not make
    // every candidate visible before its safety proof can finish.
    public float LookAhead => jumpHeight + 24f;

    public bool NeedsOpenHazardSpace(float platformY)
    {
        bool desktop = GameplayPreferences.IsDesktopBuild;
        if (!GameplayPreferences.UseInertia ||
            !GameplayPreferences.IsPortrait && !desktop)
            return false;
        int score = WorldZones.ScoreAtHeight(platformY);
        // A boosted inertia flight can occupy several future hazard rows,
        // especially in portrait. Defer the next bush, not the obstacles,
        // whenever their early density is lagging behind the other modes.
        int openingScore = Mathf.Clamp(score, 100, 1000);
        float openingRate = desktop ? 0.038f : 0.024f;
        float laterRate = desktop ? 0.020f : 0.013f;
        int desiredMajor = Mathf.FloorToInt(((openingScore - 100) * openingRate +
            Mathf.Max(0, score - 1000) * laterRate) * balance.HazardDensity);
        return majorHazardsPlaced < desiredMajor;
    }

    public HazardSpawner(Camera camera, Player player, GameObject platformPrefab,
        TrapSettings settings, GameDifficulty? difficulty = null)
    {
        this.camera = camera;
        this.platformPrefab = platformPrefab;
        this.settings = settings;
        balance = new DifficultyGenerationProfile(difficulty ?? GameplayPreferences.Difficulty);
        routePlayer = player;
        float scale = GameplayPreferences.ModelScale;
        bushReservationModelScale = scale;
        SpriteRenderer playerSprite = player.GetComponent<SpriteRenderer>();
        // Player.Start may not yet have attached the portrait scaler.
        Vector2 extents = playerSprite != null && playerSprite.sprite != null
            ? (Vector2)playerSprite.sprite.bounds.extents : new Vector2(0.4f, 0.5f);
        Vector3 baseScale = player.transform.localScale;
        if (player.GetComponent<PortraitModelScaler>() == null)
            baseScale *= scale;
        playerMargin = Mathf.Max(extents.x * baseScale.x, extents.y * baseScale.y) + 0.18f * scale;
        float gravity = Mathf.Max(0.1f, Mathf.Abs(Physics2D.gravity.y));
        float apexSpeed = Mathf.Clamp(player.apexVelocityThreshold, 0f, player.NormalJumpForce);
        jumpHeight = (player.NormalJumpForce * player.NormalJumpForce - apexSpeed * apexSpeed) /
            (2f * gravity * Mathf.Max(0.1f, player.risingGravityScale)) +
            apexSpeed * apexSpeed / (2f * gravity * Mathf.Max(0.1f, player.apexGravityScale)) + 0.25f;
        float movementScale = GameplayPreferences.HorizontalMovementScale;
        inertiaMargin = GameplayPreferences.UseInertia
            ? player.moveSpeed * player.moveSpeed * movementScale /
              (2f * Mathf.Max(1f, player.turnAcceleration * player.risingControl))
            : 0f;
        allowAbove = settings.startingHeight;
        flyingRoutes = new FlyingHazardRoutes(player, platformPrefab,
            camera.orthographicSize * camera.aspect * 2f, balance.PlatformWidth);
    }

    public void AddRoute(Vector2 from, Vector2 to, float sourceRange, float targetRange,
        bool allowFlyingDetour = true, int preparedDirection = 0)
    {
        if (!settings.enabled)
            return;
        // Reserve the route through the landing center, with room to correct
        // input. Reserving every possible edge takeoff covers the entire narrow
        // portrait screen and prevents any obstacle from ever being placed.
        float landingMargin = 0.12f * GameplayPreferences.ModelScale;
        steps.Add(new Step { from = from, to = to,
            margin = playerMargin + landingMargin + Mathf.Max(sourceRange, targetRange),
            allowFlyingDetour = allowFlyingDetour && sourceRange <= 0f && targetRange <= 0f,
            preparedDirection = preparedDirection,
            moving = sourceRange > 0f || targetRange > 0f });

        while (nextCandidate < steps.Count &&
               to.y > steps[nextCandidate].to.y + jumpHeight + 2f)
        {
            TrySpawn(steps[nextCandidate].to);
            nextCandidate++;
        }

        // Only keep geometry that can still overlap future candidates.
        while (nextCandidate > 0 && steps.Count > 0 &&
               steps[0].from.y + jumpHeight + 2f < camera.transform.position.y - camera.orthographicSize)
        {
            steps.RemoveAt(0);
            nextCandidate--;
        }
        for (int i = hazards.Count - 1; i >= 0; i--)
            if (hazards[i] == null) hazards.RemoveAt(i);
        for (int i = thornPatches.Count - 1; i >= 0; i--)
            if (thornPatches[i] == null) thornPatches.RemoveAt(i);
        for (int i = alternativePlatforms.Count - 1; i >= 0; i--)
            if (alternativePlatforms[i].yMax < camera.transform.position.y - camera.orthographicSize - jumpHeight)
                alternativePlatforms.RemoveAt(i);
        for (int i = bushLaunchAreas.Count - 1; i >= 0; i--)
            if (bushLaunchAreas[i].yMax < camera.transform.position.y - camera.orthographicSize - jumpHeight)
                bushLaunchAreas.RemoveAt(i);
    }

    public bool TryReserveBushLaunch(Rect area)
    {
        return TryReserveBushLaunch(new[] { area });
    }

    public bool TryReserveBushLaunch(IReadOnlyList<Rect> areas)
    {
        if (areas == null || areas.Count == 0) return false;
        float width = camera.orthographicSize * camera.aspect * 2f;
        foreach (Rect area in areas)
        {
            foreach (DoodlikHazard hazard in hazards)
                if (hazard != null && OverlapsWrapped(area, hazard.PatrolBounds, width))
                    return false;
            foreach (DoodlikHazard patch in thornPatches)
            {
                if (patch == null) continue;
                // Patches follow their platforms on aspect changes, so inspect
                // their live position instead of a stale reservation rectangle.
                if (OverlapsWrapped(area, patch.WarningBounds, width)) return false;
            }
        }
        for (int i = 0; i < areas.Count; i++) bushLaunchAreas.Add(areas[i]);
        return true;
    }

    public void RegisterThornPatch(DoodlikHazard patch)
    {
        if (patch != null) thornPatches.Add(patch);
    }

    public bool OverlapsBushLaunch(Rect area)
    {
        float width = camera.orthographicSize * camera.aspect * 2f;
        foreach (Rect launch in bushLaunchAreas)
            if (OverlapsWrapped(area, launch, width)) return true;
        return false;
    }

    private static bool OverlapsWrapped(Rect a, Rect b, float width)
    {
        for (int copy = -1; copy <= 1; copy++)
            if (a.Overlaps(new Rect(b.position + Vector2.right * (copy * width), b.size)))
                return true;
        return false;
    }

    public void ReserveAlternativePlatform(GameObject platform)
    {
        if (platform == null) return;
        SpriteRenderer art = platform.GetComponent<SpriteRenderer>();
        if (art == null) return;
        Bounds bounds = art.bounds;
        alternativePlatforms.Add(new Rect(
            bounds.min.x - 0.12f, bounds.min.y - 0.12f,
            bounds.size.x + 0.24f, bounds.size.y + 0.24f));
    }

    public void ReleaseAlternativePlatform(GameObject platform)
    {
        if (platform == null) return;
        SpriteRenderer art = platform.GetComponent<SpriteRenderer>();
        if (art == null) return;
        Bounds bounds = art.bounds;
        Vector2 center = bounds.center;
        for (int i = alternativePlatforms.Count - 1; i >= 0; i--)
            if (Vector2.Distance(alternativePlatforms[i].center, center) < 0.01f)
            {
                alternativePlatforms.RemoveAt(i);
                return;
            }
    }

    private void TrySpawn(Vector2 row)
    {
        if (Application.isPlaying && (routePlayer == null || routePlayer.IsDead ||
            !RunFlow.IsPlaying || Time.timeScale <= 0f)) return;
        // Do not lose the next eligible row while a proof is spread across
        // frames. Keep the newest row: older ones may enter the camera first.
        float spacing = HazardSpacing(WorldZones.AtHeight(row.y), row.y);
        if (row.y < allowAbove || row.y - lastHazardY < spacing) return;
        if (activeJob != null)
        {
            if (!deferredRow.HasValue || row.y > deferredRow.Value.y)
                deferredRow = row;
            return;
        }
#if UNITY_EDITOR
        if (!Application.isPlaying && ValidateSlicedInEditor)
        {
            object job = activeJob = new object();
            IEnumerator work = PlanRow(job, row);
            bool more;
            do
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                more = work.MoveNext();
                longestPlanningSliceMs = Math.Max(longestPlanningSliceMs, watch.Elapsed.TotalMilliseconds);
                stageMaximumMs[planningStage] = Math.Max(stageMaximumMs[planningStage], watch.Elapsed.TotalMilliseconds);
                planningSlices++;
            } while (more);
            return;
        }
#endif
        if (Application.isPlaying)
        {
            object job = activeJob = new object();
            routePlayer.StartCoroutine(PlanRow(job, row));
        }
        else
        {
            IEnumerator work = SpawnRow(null, row, false);
            while (work.MoveNext()) { }
        }
    }

    private IEnumerator PlanRow(object job, Vector2 row)
    {
        yield return null;
        try
        {
            // Warm only the few nearby ordinary-platform caches, incrementally.
            // Other rows keep spawning; this job never holds their generation.
            foreach (Step step in steps.ToArray())
            {
                if (!step.allowFlyingDetour || step.paths != null ||
                    step.from.y > row.y + jumpHeight + 2f ||
                    step.from.y + jumpHeight + playerMargin < row.y) continue;
                var paths = new FlyingHazardRoutes.Paths();
                paths.RequirePreparedVelocity(step.preparedDirection);
                IEnumerator build = flyingRoutes.BuildIncrementally(step.from, step.to, paths);
                while (true)
                {
                    if (JobCancelled(job)) yield break;
                    if (Time.timeScale <= 0f) { yield return null; continue; }
                    planningStage = 1;
                    if (!build.MoveNext()) break;
                    yield return null;
                }
                step.paths = paths;
                yield return null;
            }
            IEnumerator placement = SpawnRow(job, row, true);
            planningStage = 0;
            while (true)
            {
                if (JobCancelled(job)) yield break;
                if (Time.timeScale <= 0f) { yield return null; continue; }
                if (!placement.MoveNext()) break;
                yield return null;
            }
        }
        finally
        {
            if (ReferenceEquals(activeJob, job))
            {
                activeJob = null;
                if (deferredRow.HasValue)
                {
                    Vector2 next = deferredRow.Value;
                    deferredRow = null;
                    TrySpawn(next);
                }
            }
        }
    }

    private float HazardProgress(float height)
    {
        int startScore = WorldZones.ScoreAtHeight(settings.startingHeight);
        int score = WorldZones.ScoreAtHeight(height);
        return Mathf.Clamp01((score - startScore) /
            (float)Mathf.Max(1, WorldZones.GoalScore - startScore));
    }

    private float HazardSpacing(WorldZones.Zone zone, float height)
    {
        float modeFactor = GameplayPreferences.IsDesktopBuild
            ? (GameplayPreferences.UseInertia ? 1f : 0.9f)
            : GameplayPreferences.IsPortrait
                ? (GameplayPreferences.UseInertia ? 0.7f : 0.8f) : 0.9f;
        // A little breathing room at the meadow; shorter intervals only as
        // the ascent approaches space. Path certification still has veto power.
        bool portraitInertia = GameplayPreferences.IsPortrait && GameplayPreferences.UseInertia;
        float summitSpacing = !GameplayPreferences.IsPortrait &&
            !GameplayPreferences.UseInertia ? 0.65f : 0.82f;
        float ascentFactor = portraitInertia
            ? Mathf.Lerp(0.90f, 0.76f, HazardProgress(height))
            : Mathf.Lerp(1.12f, summitSpacing, HazardProgress(height));
        return settings.minimumSpacing * modeFactor * ascentFactor *
            (WorldZones.IsNightZone(zone) ? 0.88f : 1f) * balance.HazardSpacing;
    }

    private bool UnderDensityCeiling(float height)
    {
        // The candidate search is much more successful with precise controls.
        // A cumulative budget keeps those modes from filling the view while
        // leaving the less permissive inertia layouts free to keep trying.
        int startScore = WorldZones.ScoreAtHeight(settings.startingHeight);
        int scoreSinceStart = Mathf.Max(0, WorldZones.ScoreAtHeight(height) - startScore);
        float rampLength = Mathf.Max(1, WorldZones.GoalScore - startScore);
        float rampScore = Mathf.Min(scoreSinceStart, rampLength);
        // Integrate a linearly growing per-score allowance. Multiplying the
        // current rate by the full score would suddenly unlock old rows.
        bool precisePortrait = GameplayPreferences.IsPortrait && !GameplayPreferences.UseInertia;
        float openingRate = precisePortrait ? 0.030f : 0.033f;
        float summitRate = precisePortrait ? 0.055f : 0.072f;
        float allowance = openingRate * rampScore +
            (summitRate - openingRate) * rampScore * rampScore / (2f * rampLength) +
            summitRate * (scoreSinceStart - rampScore);
        int budget = Mathf.Max(1, Mathf.CeilToInt(allowance * balance.HazardDensity));
        return majorHazardsPlaced < budget;
    }

    private float HazardAttemptChance(float difficulty)
    {
        float attemptChance = Mathf.Lerp(settings.spawnChance.x, settings.spawnChance.y, difficulty);
        attemptChance = Mathf.Min(1f, attemptChance +
            (GameplayPreferences.IsDesktopBuild
                ? (GameplayPreferences.UseInertia
                    ? Mathf.Lerp(0.16f, 0.06f, difficulty) : 0.15f)
                : GameplayPreferences.IsPortrait
                    ? (GameplayPreferences.UseInertia ? 0.45f : 0.2f)
                    : (GameplayPreferences.UseInertia ? 0.16f : 0.22f)));
        return Mathf.Min(1f, attemptChance * balance.HazardChance);
    }

    private float HazardPatrolSpeed(float difficulty) =>
        Mathf.Lerp(0.9f, 1.55f, difficulty) * balance.HazardSpeed;

    private IEnumerator SpawnRow(object job, Vector2 row, bool sliced)
    {
        WorldZones.Zone zone = WorldZones.AtHeight(row.y);
        float zoneSpacing = HazardSpacing(zone, row.y);
        if (row.y < allowAbove || row.y - lastHazardY < zoneSpacing)
            yield break;
        if (!UnderDensityCeiling(row.y)) yield break;
        float difficulty = HazardProgress(row.y);
        float attemptChance = HazardAttemptChance(difficulty);
        // Distinct zones introduce one major source of pressure at a time.
        // Clouds focus on moving/crumbling platforms; night on flying burrs.
        // Long night patrols fail the route proof more often than static vines.
        // Keep flying hazards dominant, but leave enough certified vine rows
        // that the final climb does not become easier than the middle sky.
        float nightFlyingChance = !GameplayPreferences.IsPortrait &&
            (!GameplayPreferences.UseInertia || GameplayPreferences.IsDesktopBuild)
                ? 0.70f : 0.85f;
        float flyingChance = WorldZones.IsNightZone(zone) ? nightFlyingChance
            : WorldZones.IsCloudZone(zone) ? 0.45f
            : zone == WorldZones.Zone.Forest ? 0.38f : 0.42f;
        if (GameplayPreferences.IsPortrait)
            flyingChance = Mathf.Max(flyingChance, 0.58f);
        int rowScore = WorldZones.ScoreAtHeight(row.y);
        // Establish the horizontal branch in the early game. Otherwise the
        // flying/vertical rolls can consume every eligible row before 1000.
        bool firstHorizontalDue = horizontalBranchesPlaced == 0 && rowScore >= 150 && rowScore < 1000;
        int forestStart = WorldZones.StartScore(WorldZones.Zone.Forest);
        int firstFlyingScore = Mathf.Max(
            WorldZones.ScoreAtHeight(settings.flyingSeedStartingHeight) + 15,
            Mathf.RoundToInt(forestStart * 0.4f));
        // Give the meadow one certified flying obstacle after its first branch.
        // A rejected flying placement still gets an ordinary fallback on this
        // row, so the early hazard budget is not spent on an empty space.
        bool firstMeadowFlyingDue = !firstHorizontalDue &&
            horizontalBranchesPlaced > 0 && meadowFlyingPlaced == 0 &&
            zone == WorldZones.Zone.Meadow && rowScore >= firstFlyingScore &&
            row.y >= settings.flyingSeedStartingHeight;
        // Once due, keep trying on eligible rows. Safety checks and the same
        // cumulative density ceiling still decide whether anything can spawn.
        if (!firstMeadowFlyingDue && UnityEngine.Random.value > attemptChance)
            yield break;
        bool rolledFlying = !firstHorizontalDue && !firstMeadowFlyingDue &&
            row.y >= settings.flyingSeedStartingHeight &&
            UnityEngine.Random.value < flyingChance;
        bool rolledVertical = !firstHorizontalDue && !firstMeadowFlyingDue &&
            !rolledFlying && UnityEngine.Random.value < 0.35f;
        int passes = firstMeadowFlyingDue ? 2 : 1;
        for (int pass = 0; pass < passes; pass++)
        {
            bool flying = firstMeadowFlyingDue ? pass == 0 : rolledFlying;
            bool verticalVine = firstMeadowFlyingDue
                ? pass == 1 && UnityEngine.Random.value < 0.35f
                : rolledVertical;
            int placedBefore = majorHazardsPlaced;
            IEnumerator placement = SpawnHazardType(job, row, sliced, zone,
                difficulty, flying, verticalVine);
            while (placement.MoveNext()) yield return null;
            if (majorHazardsPlaced != placedBefore || sliced && JobCancelled(job))
                yield break;
        }
    }

    private IEnumerator SpawnHazardType(object job, Vector2 row, bool sliced,
        WorldZones.Zone zone, float difficulty, bool flying, bool verticalVine)
    {
        pendingCandidates.Clear();
        chainAttempts = 0;
        float scale = GameplayPreferences.ModelScale *
            (GameplayPreferences.IsPortrait
                ? (GameplayPreferences.UseInertia ? 0.75f : 0.85f) : 1f);
        // Reserve the whole path, then prove a detour. The first zone is
        // slower, but no longer shrinks movement until the enemy looks still.
        DoodlikHazard.FlightPattern pattern = flying
            ? (DoodlikHazard.FlightPattern)UnityEngine.Random.Range(0, 4)
            : DoodlikHazard.FlightPattern.Horizontal;
        float earlyPatrol = zone == WorldZones.Zone.Meadow ? 0.75f : 1f;
        float controlPatrol = GameplayPreferences.UseInertia ? 0.5f : 1f;
        Vector2 patrol = flying ? new Vector2(
            0.75f * scale * earlyPatrol * controlPatrol,
            0.65f * scale * earlyPatrol * controlPatrol) : Vector2.zero;
        if (pattern == DoodlikHazard.FlightPattern.Horizontal) patrol.y *= 0.35f;
        else if (pattern == DoodlikHazard.FlightPattern.Vertical) patrol.x *= 0.35f;
        // This exact speed is passed both to route relevance simulation and
        // DoodlikHazard.Initialize; difficulty never bypasses the witness.
        float patrolSpeed = HazardPatrolSpeed(difficulty);
        float patrolPhase = UnityEngine.Random.value * Mathf.PI * 2f;
        Vector2 staticDamageHalf = new Vector2(verticalVine ? DoodlikHazard.VerticalThornHalfWidth : 0.49f,
            verticalVine ? 0.49f : 0.22f) * (scale * DoodlikHazard.ThornBranchScale);
        Vector2 staticDamageOffset = verticalVine
            ? Vector2.right * (DoodlikHazard.VerticalThornCenterOffsetX * scale * DoodlikHazard.ThornBranchScale)
            : Vector2.zero;
        Vector2 reservedHalf = flying
            ? Vector2.one * (0.26f * scale) + patrol
            : staticDamageHalf + new Vector2(
                Mathf.Abs(staticDamageOffset.x) + 0.03f * scale,
                0.03f * scale);
        float halfScreen = camera.orthographicSize * camera.aspect;
        float min = camera.transform.position.x - halfScreen + reservedHalf.x + 0.2f;
        float max = camera.transform.position.x + halfScreen - reservedHalf.x - 0.2f;
        if (min >= max)
            yield break;
        float y = row.y + (flying ? Mathf.Min(jumpHeight - 0.6f, 2.1f) : 0.22f * scale);
        // Never materialize an obstacle on top of the player/in the visible view.
        if (y - reservedHalf.y <= camera.transform.position.y + camera.orthographicSize + 0.8f)
            yield break;

        targetedCandidates.Clear();
        foreach (Step route in steps)
        {
            if (!route.allowFlyingDetour || Mathf.Abs(route.from.y - row.y) > 0.1f) continue;
            if (route.paths == null)
            {
                if (sliced) continue;
                route.paths = flyingRoutes.Build(route.from, route.to,
                    route.preparedDirection);
            }
            flyingRoutes.AppendApexCandidates(route.paths,
                flying ? 0.24f * scale : staticDamageHalf.x,
                flying ? 0.24f * scale : staticDamageHalf.y,
                patrol, targetedCandidates);
            break;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int attempt = 0; attempt < 24 + targetedCandidates.Count; attempt++)
        {
            if (sliced && watch.Elapsed.TotalMilliseconds >= 2.0)
            {
                yield return null;
                watch.Restart();
            }
            Vector2 center;
            if (attempt >= 24)
            {
                center = targetedCandidates[attempt - 24];
                float left = camera.transform.position.x - halfScreen;
                center.x = left + Mathf.Repeat(center.x - left, halfScreen * 2f);
                if (center.x < min || center.x > max) continue;
            }
            else
            {
                float candidateX = attempt < 16
                    ? Mathf.Clamp(row.x + UnityEngine.Random.Range(-2.5f, 2.5f) * GameplayPreferences.ModelScale, min, max)
                    : UnityEngine.Random.Range(min, max);
                center = new Vector2(candidateX,
                    row.y + UnityEngine.Random.Range(1.2f, jumpHeight + 0.6f));
            }
            Rect area = new Rect(center - reservedHalf, reservedHalf * 2f);
            if (area.yMin <= camera.transform.position.y + camera.orthographicSize + 0.8f)
                continue;
            planningStage = 2;
            if (!CanPlaceObstacle(area, center, patrol, scale, patrolSpeed,
                pattern, flying, staticDamageHalf, staticDamageOffset, sliced))
                continue;

            planningStage = 4;
            SpawnObstacle(row, center, area, flying, verticalVine, scale, patrol, patrolSpeed,
                patrolPhase, pattern, acceptedConnectedRoute, acceptedConnectedExisting,
                acceptedConnectedInitialPaths);
            yield break;
        }
        if (sliced && pendingCandidates.Count > 0 && routePlayer != null &&
            RunFlow.IsPlaying && !routePlayer.IsDead)
        {
            IEnumerator connected = ResolvePending(job, pendingCandidates.ToArray(), row,
                flying, verticalVine, scale, patrol, patrolSpeed, patrolPhase, pattern);
            while (connected.MoveNext()) yield return null;
        }
    }

    private void SpawnObstacle(Vector2 row, Vector2 center, Rect area, bool flying,
        bool verticalVine,
        float scale, Vector2 patrol, float patrolSpeed, float patrolPhase,
        DoodlikHazard.FlightPattern pattern, Vector2[] certificate,
        Rect[] certifiedExisting, FlyingHazardRoutes.Paths certifiedInitialPaths)
    {
            // Replace a subset of already certified airborne placements; never
            // spend another density slot, reroll RNG or enlarge a reservation.
            bool bee = false;
            if (flying && WorldZones.ScoreAtHeight(center.y) >= ChargingBumblebee.StartHeight)
                bee = ChargingBumblebee.ShouldReplace(bumblebeeEligibleFlights++, ChargingBumblebee.HasActive);
            GameObject root;
            Vector2 hazardPosition;
            if (flying)
            {
                root = new GameObject(bee ? "Angry bumblebee" : "Flying thorn seed");
                root.transform.position = center;
                hazardPosition = center;
            }
            else if (verticalVine)
            {
                root = new GameObject("Vertical thorn vine");
                root.transform.position = center;
                hazardPosition = center;
            }
            else
            {
                root = new GameObject("Thorny branch (danger on every side)");
                root.transform.position = center;
                BoxCollider2D landingSurface = root.AddComponent<BoxCollider2D>();
                landingSurface.size = new Vector2(
                    0.98f * DoodlikHazard.ThornBranchScale, 0.2f) * scale;
                // Shielded paws land on the branch core, below its upper
                // thorns. The visible points can touch the cat before the
                // feet cross this surface, so it never seems to bounce in air.
                landingSurface.offset = new Vector2(0f,
                    -0.155f * DoodlikHazard.ThornBranchScale) * scale;
                // An unprotected cat takes damage on every side, including
                // below. The disabled surface only supports a shield bounce.
                landingSurface.enabled = false;
                hazardPosition = center;
            }

            GameObject hazardObject = flying || verticalVine ? root : new GameObject("Thorns");
            if (!flying && !verticalVine)
            {
                hazardObject.transform.SetParent(root.transform, true);
                hazardObject.transform.position = hazardPosition;
            }
            DoodlikHazard hazard = hazardObject.AddComponent<DoodlikHazard>();
            if (bee) hazard.InitializeBumblebee(camera, routePlayer, scale, area, balance.Difficulty);
            else hazard.Initialize(flying ? DoodlikHazard.Kind.FlyingSeed :
                    verticalVine ? DoodlikHazard.Kind.VerticalThorns : DoodlikHazard.Kind.Thorns,
                camera, scale, patrol.x, patrolSpeed, patrolPhase, area,
                pattern, patrol.y);
            if (certificate != null)
            {
                hazard.SetCertifiedRoute(certificate);
#if UNITY_EDITOR
                hazard.SetCertifiedProofInputs(certifiedExisting, certifiedInitialPaths);
#endif
            }
            hazards.Add(hazard);
            majorHazardsPlaced++;
            if (!flying && !verticalVine) horizontalBranchesPlaced++;
            if (flying && WorldZones.AtHeight(center.y) == WorldZones.Zone.Meadow)
                meadowFlyingPlaced++;
            lastHazardY = row.y;
    }

    private IEnumerator ResolvePending(object job, PendingPlacement[] candidates, Vector2 row,
        bool flying, bool verticalVine, float scale, Vector2 patrol, float speed, float phase,
        DoodlikHazard.FlightPattern pattern)
    {
        // Let the current platform-generation frame finish first.
        yield return null;
        foreach (PendingPlacement candidate in candidates)
        {
            var result = new FlyingHazardRoutes.ChainResult();
            IEnumerator work = flyingRoutes.TraverseChain(candidate.route, candidate.area,
                candidate.existing, result, candidate.initialPaths);
            while (true)
            {
                if (JobCancelled(job) || !StillOffscreen(candidate.area)) yield break;
                // Opening the pause menu freezes planning as well as play.
                if (Time.timeScale <= 0f) { yield return null; continue; }
                planningStage = 3;
                if (!work.MoveNext()) break;
                yield return null;
            }
            if (!result.Passed) { yield return null; continue; }
            // Coins can reserve an optional route while this proof yields.
            // Recheck it and the CURRENT view immediately before commit.
            if (JobCancelled(job) || !StillOffscreen(candidate.area))
                yield break;
            // Generation keeps running while the proof yields. A new bush,
            // coin route or upper route can invalidate this position; try the
            // next certified position instead of discarding the whole row.
            float width = camera.orthographicSize * camera.aspect * 2f;
            if (CoinRouteReservations.Overlaps(candidate.area, width) ||
                OverlapsBushLaunch(candidate.area) ||
                NewRouteOverlaps(candidate, width))
            {
                yield return null;
                continue;
            }
            planningStage = 4;
            SpawnObstacle(row, candidate.center, candidate.area, flying, verticalVine, scale,
                patrol, speed, phase, pattern, candidate.route, candidate.existing,
                candidate.initialPaths);
            yield break;
        }
    }

    private bool JobCancelled(object job) => !ReferenceEquals(activeJob, job) || camera == null ||
        routePlayer == null || routePlayer.IsDead || !RunFlow.RunActive || RunFlow.IsTransitioning;

    private bool StillOffscreen(Rect area) => camera != null &&
        area.yMin > camera.transform.position.y + camera.orthographicSize + 0.8f;

    private bool NewRouteOverlaps(PendingPlacement candidate, float width)
    {
        foreach (Step step in steps)
        {
            if (Array.IndexOf(candidate.knownSteps, step) >= 0) continue;
            if (step.allowFlyingDetour)
            {
                if (TrapGeometry.OverlapsRoute(candidate.area, step.from, step.to,
                    width, step.margin, jumpHeight, playerMargin, inertiaMargin))
                    return true;
            }
            else if (ProtectedRouteOverlaps(candidate.area, step, width))
                return true;
        }
        return false;
    }

    private bool CanPlaceObstacle(Rect area, Vector2 center, Vector2 patrol,
        float scale, float speed, DoodlikHazard.FlightPattern pattern,
        bool flying, Vector2 staticDamageHalf, Vector2 staticDamageOffset,
        bool deferConnected = false)
    {
        acceptedConnectedRoute = null;
        acceptedConnectedExisting = null;
        acceptedConnectedInitialPaths = null;
        float width = camera.orthographicSize * camera.aspect * 2f;
        if (CoinRouteReservations.Overlaps(area, width)) return false;
        if (OverlapsBushLaunch(area)) return false;
        foreach (Rect platform in alternativePlatforms)
            for (int copy = -1; copy <= 1; copy++)
                if (new Rect(platform.position + Vector2.right * (copy * width), platform.size).Overlaps(area))
                    return false;
        existingPatrols.Clear();
        for (int i = 0; i < hazards.Count; i++)
        {
            if (hazards[i] == null) continue;
            if (hazards[i].PatrolBounds.Overlaps(area)) return flying && FlyingDecision(0);
            existingPatrols.Add(hazards[i].PatrolBounds);
        }
        for (int i = 0; i < thornPatches.Count; i++)
        {
            DoodlikHazard patch = thornPatches[i];
            if (patch == null) continue;
            Rect patchBounds = patch.DamageBounds;
            if (OverlapsWrapped(area, patchBounds, width)) return flying && FlyingDecision(0);
            existingPatrols.Add(patchBounds);
        }
        bool relevant = false, individuallySafe = true;
        int firstAffected = -1, lastAffected = -1;
        // Cheap exclusions first: do not build trajectory caches only to learn
        // that a neighbouring moving/narrow/aerial corridor forbids this point.
        foreach (Step step in steps)
        {
            if (!step.allowFlyingDetour && ProtectedRouteOverlaps(area, step, width))
                return flying && FlyingDecision(step.moving ? 1 : 2, step);
        }
        for (int index = 0; index < steps.Count; index++)
        {
            Step step = steps[index];
            // Non-detour routes were handled by the physical guard above.
            // Their broader approximate rectangle is not a route to prove:
            // sliced planning deliberately does not build paths for them.
            if (!step.allowFlyingDetour) continue;
            if (!TrapGeometry.OverlapsRoute(area, step.from, step.to, width,
                step.margin, jumpHeight, playerMargin, inertiaMargin)) continue;
            if (firstAffected < 0) firstAffected = index;
            lastAffected = index;
            // Moving/narrow platforms and authored aerial-pickup jumps retain
            // the original full corridor. Never weaken a route we cannot prove.
            if (step.paths == null)
            {
                if (deferConnected) return false;
                step.paths = flyingRoutes.Build(step.from, step.to,
                    step.preparedDirection);
            }
            if (!step.paths.Complete) return flying && FlyingDecision(3, step);
            individuallySafe &= flyingRoutes.HasSafeAlternatives(step.paths, area, existingPatrols);
            if (!relevant)
                relevant = flying ? flyingRoutes.CanThreatenARealJump(step.paths,
                    center, patrol, scale, speed, pattern)
                    : flyingRoutes.CanThreatenStatic(step.paths,
                        new Rect(center + staticDamageOffset - staticDamageHalf,
                            staticDamageHalf * 2f));
        }
        if (!relevant) return flying && FlyingDecision(5);
        if (individuallySafe) return !flying || FlyingDecision(6);
        if (chainAttempts < 2 && firstAffected >= 0 &&
            steps[firstAffected].preparedDirection == 0 &&
            firstAffected + 2 < steps.Count && lastAffected <= firstAffected + 2)
        {
            Vector2[] nodes = { steps[firstAffected].from, steps[firstAffected].to,
                steps[firstAffected + 1].to, steps[firstAffected + 2].to };
            bool suitable = true;
            for (int i = firstAffected; i <= firstAffected + 2; i++) suitable &= steps[i].allowFlyingDetour;
            if (suitable && nodes[3].y > area.yMax + 0.12f * GameplayPreferences.ModelScale)
            {
                chainAttempts++;
                if (deferConnected)
                {
                    pendingCandidates.Add(new PendingPlacement { area = area, center = center, route = nodes,
                        initialPaths = steps[firstAffected].paths, existing = existingPatrols.ToArray(),
                        knownSteps = steps.ToArray() });
                    return false;
                }
#if UNITY_EDITOR
                connectedAttempts++;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                bool passed = flyingRoutes.CanTraverseChain(nodes, area, existingPatrols, out _, steps[firstAffected].paths);
                connectedLongestMs = Math.Max(connectedLongestMs, watch.Elapsed.TotalMilliseconds);
                if (passed)
                {
                    connectedAccepted++;
                    acceptedConnectedRoute = nodes;
                    acceptedConnectedExisting = existingPatrols.ToArray();
                    acceptedConnectedInitialPaths = steps[firstAffected].paths;
                    return !flying || FlyingDecision(6);
                }
#endif
            }
        }
        return flying && FlyingDecision(4);
    }

    private bool ProtectedRouteOverlaps(Rect area, Step step, float width)
    {
        if (!TrapGeometry.OverlapsRoute(area, step.from, step.to, width,
            step.margin, jumpHeight, playerMargin, inertiaMargin)) return false;

        // The old full-height corridor covered nearly the entire portrait
        // screen even *below* the next landing. A successful jump can visit
        // that low band only while rising from the source, not after landing
        // on the higher target. Bound its sideways reach by the latest possible
        // time to rise there. Keep the original full guard near/above landing.
        if (area.yMax >= step.to.y - playerMargin) return true;
        float rise = Mathf.Max(0f, area.yMax - step.from.y + playerMargin);
        float launchSpeed = routePlayer.NormalJumpForce;
        float gravity = Mathf.Abs(Physics2D.gravity.y) *
            Mathf.Max(0.1f, routePlayer.risingGravityScale);
        float discriminant = launchSpeed * launchSpeed - 2f * gravity * rise;
        if (discriminant <= 0f) return true;
        float latestRiseTime = (launchSpeed - Mathf.Sqrt(discriminant)) / gravity;
        float maximumTravel = routePlayer.moveSpeed *
            GameplayPreferences.HorizontalMovementScale * (latestRiseTime + 0.05f);
        float halfReach = step.margin + inertiaMargin + maximumTravel;
        for (int copy = -1; copy <= 1; copy++)
        {
            float sourceX = step.from.x + copy * width;
            if (area.xMax >= sourceX - halfReach &&
                area.xMin <= sourceX + halfReach) return true;
        }
        return false;
    }

    public bool IsAreaFree(Rect area, float screenWidth)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            Step step = steps[i];
            if (TrapGeometry.OverlapsRoute(area, step.from, step.to,
                screenWidth, step.margin, jumpHeight, playerMargin, inertiaMargin))
                return false;
        }
        for (int i = 0; i < hazards.Count; i++)
            if (hazards[i] != null && hazards[i].PatrolBounds.Overlaps(area))
                return false;
        return true;
    }

    public void InvalidateLayout(float highestY, float oldMinX, float oldMaxX,
        float newMinX, float newMaxX)
    {
        activeJob = null;
        pendingCandidates.Clear();
        // A resize remaps the platforms. Old patrol reservations no longer apply.
        for (int i = 0; i < hazards.Count; i++)
        {
            if (hazards[i] == null) continue;
            Transform root = hazards[i].transform;
            UnityEngine.Object.Destroy(root.parent != null ? root.parent.gameObject : root.gameObject);
        }
        hazards.Clear();
        steps.Clear();
        deferredRow = null;
        alternativePlatforms.Clear();
        // Existing bushes stay attached to their remapped platforms. Their
        // launch corridors must stay reserved when the screen format changes.
        float oldWidth = Mathf.Max(0.01f, oldMaxX - oldMinX);
        float newWidth = Mathf.Max(0.01f, newMaxX - newMinX);
        float widthScale = Mathf.Max(1f, newWidth / oldWidth);
        float verticalGrowth = Mathf.Max(0f,
            GameplayPreferences.ModelScale - bushReservationModelScale) * 2f;
        for (int i = 0; i < bushLaunchAreas.Count; i++)
        {
            Rect oldArea = bushLaunchAreas[i];
            float centerX = newMinX + (oldArea.center.x - oldMinX) * newWidth / oldWidth;
            float halfWidth = oldArea.width * widthScale * 0.5f;
            bushLaunchAreas[i] = new Rect(centerX - halfWidth,
                oldArea.yMin - verticalGrowth, halfWidth * 2f,
                oldArea.height + verticalGrowth * 2f);
        }
        bushReservationModelScale = GameplayPreferences.ModelScale;
        nextCandidate = 0;
        allowAbove = highestY + jumpHeight + 2f;
        flyingRoutes = new FlyingHazardRoutes(routePlayer, platformPrefab,
            camera.orthographicSize * camera.aspect * 2f, balance.PlatformWidth);
    }
}
