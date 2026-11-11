using System.Collections.Generic;
using UnityEngine;

// Manual, wrap-aware enemy contacts share Player's audited travel. Decorative
// wings and the tell never deal damage; top attacks use the actual paw collider.
public sealed class ExpeditionEnemy : MonoBehaviour
{
    public enum Phase { Patrol, Windup, Dash, Recovery, Defeated }
    public const int MaximumHealth = 2;
    public const float BodyHalfWidth = .27f, BodyHalfHeight = .18f;
    public const float WindupDuration = .65f, DashDuration = .38f;
    private static readonly List<ExpeditionEnemy> Active = new List<ExpeditionEnemy>();
    private static Material sharedInk;
    private Camera sceneCamera;
    private Vector2 origin, previousPosition, sampledPosition, dashStart, dashTarget;
    private Vector2 patrol;
    private Transform drawing, tell;
    private MeshFilter bodyFilter, healthFilter;
    private MeshRenderer tellRenderer;
    private float phaseTime, poseTime, contactAfter, phaseOffset;
    private uint random;
    private int lastContactFrame = -1;
    private bool resolving;
    public int Health { get; private set; } = MaximumHealth;
    public Phase CurrentPhase { get; private set; }
    public static int ActiveCount => Active.Count;
    public Rect PatrolBounds => BoundsAt(origin, patrol);
    public Vector2 Origin => origin;

    public static Rect BoundsAt(Vector2 center, Vector2 movement)
    {
        float scale = GameplayPreferences.ModelScale;
        Vector2 extent = new Vector2(Mathf.Abs(movement.x) + .54f * scale, Mathf.Abs(movement.y) + .54f * scale);
        return new Rect(center - extent, extent * 2f);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        Active.Clear();
        if (sharedInk != null) { if (Application.isPlaying) Object.Destroy(sharedInk); else Object.DestroyImmediate(sharedInk); }
        sharedInk = null;
    }

    private void OnEnable()
    {
        previousPosition = sampledPosition = transform.position;
        if (Health > 0 && !Active.Contains(this)) Active.Add(this);
    }
    private void OnDisable() => Active.Remove(this);
    private void OnDestroy() => Active.Remove(this);

    public static ExpeditionEnemy Spawn(Vector2 position, Camera camera, float patrolX, float patrolY, float phase = 0f, uint seed = 1)
    {
        if (GameplayPreferences.RunKind != GameRunKind.Expedition || camera == null || !Finite(position.x) || !Finite(position.y) ||
            !Finite(patrolX) || !Finite(patrolY) || patrolX < 0f || patrolY < 0f) return null;
        if (sharedInk == null)
        {
            Shader shader = Resources.Load<Shader>("HazardInk"); if (shader == null) return null;
            sharedInk = new Material(shader) { name = "Storm beetle paper ink", hideFlags = HideFlags.DontSave };
        }
        GameObject root = new GameObject("Грозовой жук"); root.transform.position = position;
        ExpeditionEnemy enemy = root.AddComponent<ExpeditionEnemy>();
        enemy.origin = position; enemy.sceneCamera = camera; enemy.patrol = new Vector2(patrolX, patrolY);
        enemy.phaseOffset = phase; enemy.random = seed == 0 ? 0x8a5cd789u : seed;
        enemy.CurrentPhase = Phase.Patrol; enemy.phaseTime = -.35f - Mathf.Abs(Mathf.Sin(phase)) * .8f;
        enemy.drawing = enemy.Drawing("Paper beetle", ExpeditionEnemyArt.Body, Vector2.zero, 5, out enemy.bodyFilter);
        enemy.Drawing("Two visible hit points", ExpeditionEnemyArt.Health(MaximumHealth), Vector2.up * .46f, 6, out enemy.healthFilter);
        MeshFilter ignored;
        enemy.tell = enemy.Drawing("Dash warning", ExpeditionEnemyArt.Arrow, Vector2.right * .67f, 6, out ignored);
        enemy.tellRenderer = enemy.tell.GetComponent<MeshRenderer>(); enemy.tellRenderer.enabled = false;
        if (!Active.Contains(enemy)) Active.Add(enemy);
        return enemy;
    }

    private Transform Drawing(string name, Mesh mesh, Vector2 offset, int order, out MeshFilter filter)
    {
        GameObject child = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        child.transform.SetParent(transform, false); child.transform.localPosition = offset * GameplayPreferences.ModelScale;
        child.transform.localScale = Vector3.one * GameplayPreferences.ModelScale;
        filter = child.GetComponent<MeshFilter>(); filter.sharedMesh = mesh;
        MeshRenderer renderer = child.GetComponent<MeshRenderer>(); renderer.sharedMaterial = sharedInk; renderer.sortingOrder = order;
        return child.transform;
    }

    private void Update()
    {
        previousPosition = sampledPosition;
        ExpeditionRun run = ExpeditionRun.Active;
        if (sceneCamera == null || transform.position.y < sceneCamera.transform.position.y - sceneCamera.orthographicSize - 2f)
        { Destroy(gameObject); return; }
        if (run != null && run.CanFight) Step(Time.deltaTime, run.Owner);
        sampledPosition = transform.position;
    }

    public void Step(float deltaTime, Player target)
    {
        if (Health <= 0 || target == null || target.IsDead || !RunFlow.IsPlaying || Time.timeScale <= 0f ||
            GameMenuUI.BlocksGameplayInput || !Finite(deltaTime) || deltaTime <= 0f) return;
        float elapsed = deltaTime * RunPowerups.PlatformSpeedMultiplier;
        poseTime += elapsed; phaseTime += elapsed;
        Vector2 position = transform.position;
        switch (CurrentPhase)
        {
            case Phase.Patrol:
                position = origin + new Vector2(Mathf.Sin(poseTime * 1.8f + phaseOffset) * patrol.x * .55f,
                    Mathf.Sin(poseTime * 2.1f + phaseOffset) * patrol.y);
                if (phaseTime >= 2.1f && Visible())
                {
                    dashStart = position;
                    float width = sceneCamera.orthographicSize * sceneCamera.aspect * 2f;
                    float direction = Mathf.Sign(TrapGeometry.WrappedDelta(position.x, target.HazardBodyCenter.x, width));
                    if (direction == 0f) direction = 1f;
                    dashTarget = origin + new Vector2(direction * patrol.x, 0f);
                    tell.localPosition = new Vector3(direction * .67f * GameplayPreferences.ModelScale, 0f, 0f);
                    tell.localScale = new Vector3(direction, 1f, 1f) * GameplayPreferences.ModelScale;
                    ChangePhase(Phase.Windup);
                }
                break;
            case Phase.Windup:
                position = dashStart;
                if (phaseTime >= WindupDuration) ChangePhase(Phase.Dash);
                break;
            case Phase.Dash:
                position = Vector2.Lerp(dashStart, dashTarget, Mathf.Clamp01(phaseTime / DashDuration));
                if (phaseTime >= DashDuration) ChangePhase(Phase.Recovery);
                break;
            case Phase.Recovery:
                position = Vector2.MoveTowards(position, origin, .7f * GameplayPreferences.ModelScale * elapsed);
                if (phaseTime >= 1.1f) ChangePhase(Phase.Patrol);
                break;
        }
        transform.position = new Vector2(Mathf.Clamp(position.x, origin.x - patrol.x, origin.x + patrol.x),
            Mathf.Clamp(position.y, origin.y - patrol.y, origin.y + patrol.y));
        if (drawing != null) drawing.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(poseTime * 4f) * 4f);
    }

    private bool Visible() => sceneCamera != null && transform.position.y < sceneCamera.transform.position.y + sceneCamera.orthographicSize - .7f;
    private void ChangePhase(Phase next)
    {
        CurrentPhase = next; phaseTime = 0f;
        if (bodyFilter != null) bodyFilter.sharedMesh = next == Phase.Windup ? ExpeditionEnemyArt.Warning : Health < MaximumHealth ? ExpeditionEnemyArt.Hurt : ExpeditionEnemyArt.Body;
        if (tellRenderer != null) tellRenderer.enabled = next == Phase.Windup;
    }

    public static bool ResolveTravel(ExpeditionRun run, Vector2 from, Vector2 to, Vector2 halfSize, float width, bool sweepMotion = true)
    {
        if (run == null || !run.CanFight || !Finite(width) || width <= 0f || !Finite(from.x) || !Finite(from.y) ||
            !Finite(to.x) || !Finite(to.y) || !Finite(halfSize.x) || !Finite(halfSize.y) || halfSize.x < 0f || halfSize.y < 0f) return false;
        Collider2D paws = run.Owner != null ? run.Owner.GetComponent<Collider2D>() : null;
        if (paws == null) return false;
        float feetOffset = Mathf.Max(halfSize.y, run.Owner.HazardBodyCenter.y - paws.bounds.min.y);
        float feetHalfWidth = paws.bounds.extents.x;
        to.x = from.x + TrapGeometry.WrappedDelta(from.x, to.x, width);
        float scale = GameplayPreferences.ModelScale;
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            if (i >= Active.Count) continue;
            ExpeditionEnemy enemy = Active[i];
            if (enemy == null || !enemy.isActiveAndEnabled || enemy.Health <= 0 || enemy.resolving ||
                enemy.lastContactFrame == Time.frameCount || Time.time < enemy.contactAfter) continue;
            Vector2 current = enemy.transform.position;
            Vector2 previous = sweepMotion ? enemy.previousPosition : current;
            Vector2 relativeFrom = from - previous, relativeTo = to - current;
            for (int copy = -1; copy <= 1; copy++)
            {
                Vector2 localCenter = Vector2.right * (copy * width);
                if (TryTopContact(relativeFrom - localCenter, relativeTo - localCenter, feetOffset, feetHalfWidth, scale, out float fraction))
                {
                    enemy.resolving = true;
                    try
                    {
                        enemy.lastContactFrame = Time.frameCount; enemy.contactAfter = Time.time + .35f;
                        enemy.Health = Mathf.Max(0, enemy.Health - Mathf.Max(1, run.StompDamage));
                        if (enemy.healthFilter != null) enemy.healthFilter.sharedMesh = ExpeditionEnemyArt.Health(enemy.Health);
                        Vector2 atEnemy = Vector2.Lerp(previous, current, fraction) + localCenter;
                        Vector2 atPlayer = Vector2.Lerp(from, to, fraction);
                        Vector2 contact = new Vector2(atPlayer.x, atEnemy.y + BodyHalfHeight * scale);
                        if (enemy.Health <= 0) enemy.Defeat(run);
                        else { enemy.ChangePhase(Phase.Recovery); GameAudio.Play(GameAudio.Cue.Jump); }
                        run.Owner.BounceFromExpeditionEnemy(contact);
                        return true;
                    }
                    finally { if (enemy != null) enemy.resolving = false; }
                }
                Rect body = new Rect(localCenter - new Vector2(BodyHalfWidth, BodyHalfHeight) * scale,
                    new Vector2(BodyHalfWidth, BodyHalfHeight) * (2f * scale));
                if (!TrapGeometry.SweptBox(relativeFrom, relativeTo, halfSize, body)) continue;
                enemy.lastContactFrame = Time.frameCount;
                if (run.TryTakeEnemyHit()) enemy.contactAfter = Time.time + .35f;
                if (!run.CanFight) return false;
                break;
            }
        }
        return false;
    }

    public static bool TryTopContact(Vector2 relativeFrom, Vector2 relativeTo, float feetOffset, float feetHalfWidth, float scale, out float fraction)
    {
        fraction = 0f;
        if (!Finite(relativeFrom.x) || !Finite(relativeFrom.y) || !Finite(relativeTo.x) || !Finite(relativeTo.y) ||
            !Finite(feetOffset) || !Finite(feetHalfWidth) || !Finite(scale) || feetOffset < 0f || feetHalfWidth < 0f || scale <= 0f) return false;
        float a = relativeFrom.y - feetOffset, b = relativeTo.y - feetOffset, top = BodyHalfHeight * scale;
        float tolerance = .025f * scale;
        if (b >= a - .0001f || a < top - tolerance || b > top + tolerance) return false;
        fraction = Mathf.Clamp01((a - top) / (a - b));
        float x = Mathf.Lerp(relativeFrom.x, relativeTo.x, fraction);
        return Mathf.Abs(x) <= BodyHalfWidth * scale * .85f + Mathf.Max(0f, feetHalfWidth) * .75f;
    }

    private void Defeat(ExpeditionRun run)
    {
        CurrentPhase = Phase.Defeated; Active.Remove(this);
        run.NotifyEnemyDefeated(); GameAudio.Play(GameAudio.Cue.Coin);
        random ^= random << 13; random ^= random >> 17; random ^= random << 5;
        float roll = (random & 0x00ffffffu) / 16777216f;
        if (roll < Mathf.Clamp01(run.EnemyDropChance))
        {
            Vector2 drop = transform.position;
            // A kill occurs under the player's paws in certified free air.
            // Keep cargo inside the viewport, not on a ballistic loot arc.
            if (sceneCamera != null)
            {
                float scale = GameplayPreferences.ModelScale;
                float halfWidth = sceneCamera.orthographicSize * sceneCamera.aspect;
                drop.x = Mathf.Clamp(drop.x, sceneCamera.transform.position.x - halfWidth + .35f * scale,
                    sceneCamera.transform.position.x + halfWidth - .35f * scale);
                drop.y = Mathf.Clamp(drop.y + .30f * scale, sceneCamera.transform.position.y - sceneCamera.orthographicSize + .5f * scale,
                    sceneCamera.transform.position.y + sceneCamera.orthographicSize - .35f * scale);
            }
            ExpeditionPickup.Spawn(ExpeditionFindKind.SparkDust, drop, sceneCamera, phaseOffset);
        }
        gameObject.SetActive(false); Destroy(gameObject);
    }

    public static void RemoveUnclaimed()
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            ExpeditionEnemy enemy = Active[i]; Active.RemoveAt(i);
            if (enemy == null) continue;
            enemy.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(enemy.gameObject); else DestroyImmediate(enemy.gameObject);
        }
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
