using System.Collections.Generic;
using UnityEngine;

// Cargo is collected by the visible body/swept travel, never by a magnet.
// A full bag leaves the world item unclaimed so the player can decide what to discard.
public sealed class ExpeditionPickup : MonoBehaviour
{
    private static readonly List<ExpeditionPickup> Active = new List<ExpeditionPickup>();
    private static Material ink;
    private Camera sceneCamera;
    private Transform drawing;
    private float phase, retryAfter;
    private Vector2 previousPosition, sampledPosition;
    private bool collected, claiming;
    public ExpeditionFindKind Kind { get; private set; }
    public const float PickupRadius = .16f;
    public static int ActiveCount => Active.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        Active.Clear();
        if (ink != null)
        {
            if (Application.isPlaying) Object.Destroy(ink); else Object.DestroyImmediate(ink);
        }
        ink = null;
    }
    private void OnEnable()
    {
        previousPosition = sampledPosition = transform.position;
        if (!collected && !Active.Contains(this)) Active.Add(this);
    }
    private void OnDisable() => Active.Remove(this);
    private void OnDestroy() => Active.Remove(this);

    public static ExpeditionPickup Spawn(ExpeditionFindKind kind, Vector2 position, Camera camera, float phase = 0f)
    {
        if (GameplayPreferences.RunKind != GameRunKind.Expedition || !ExpeditionCatalog.IsValid(kind) ||
            !Finite(position.x) || !Finite(position.y)) return null;
        Mesh mesh = ExpeditionArt.FindMesh(kind);
        if (mesh == null) return null;
        if (ink == null)
        {
            Shader shader = Resources.Load<Shader>("HazardInk");
            if (shader == null) shader = Shader.Find("Doodlik/HazardInk");
            if (shader == null) return null;
            ink = new Material(shader) { name = "Expedition cargo ink", hideFlags = HideFlags.DontSave };
        }
        GameObject root = new GameObject("Expedition find — " + kind);
        root.transform.position = position;
        ExpeditionPickup pickup = root.AddComponent<ExpeditionPickup>();
        pickup.Kind = kind; pickup.sceneCamera = camera; pickup.phase = phase;
        // EditMode proof replays do not dispatch ordinary OnEnable reliably.
        // Runtime registration is already present, so keep this idempotent.
        if (!Active.Contains(pickup)) Active.Add(pickup);
        GameObject drawing = new GameObject("Paper find", typeof(MeshFilter), typeof(MeshRenderer));
        drawing.transform.SetParent(root.transform, false);
        drawing.transform.localScale = Vector3.one * GameplayPreferences.ModelScale;
        drawing.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = drawing.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = ink; renderer.sortingOrder = 5;
        pickup.drawing = drawing.transform;
        return pickup;
    }

    private void Update()
    {
        if (collected) return;
        previousPosition = sampledPosition; sampledPosition = transform.position;
        if (drawing != null) drawing.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 1.7f + phase) * 5f);
        if (sceneCamera != null && transform.position.y < sceneCamera.transform.position.y - sceneCamera.orthographicSize - 2f)
            Destroy(gameObject);
    }

    public static void CollectNear(Vector2 center, Vector2 bodyHalfSize, float width)
        => CollectAlong(center, center, bodyHalfSize, width, false);

    public static void CollectAlong(Vector2 from, Vector2 to, Vector2 bodyHalfSize, float width, bool sweepMotion = true)
    {
        ExpeditionRun expedition = ExpeditionRun.Active;
        if (expedition == null || !expedition.CanCollect || !RunFlow.IsPlaying || Time.timeScale <= 0f) return;
        if (!Finite(width) || width <= 0f || !Finite(from.x) || !Finite(from.y) || !Finite(to.x) || !Finite(to.y) ||
            !Finite(bodyHalfSize.x) || !Finite(bodyHalfSize.y) || bodyHalfSize.x < 0f || bodyHalfSize.y < 0f) return;
        to.x = from.x + TrapGeometry.WrappedDelta(from.x, to.x, width);
        float radius = PickupRadius * GameplayPreferences.ModelScale;
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            if (i >= Active.Count) continue;
            ExpeditionPickup pickup = Active[i];
            if (pickup == null || pickup.collected || pickup.claiming || !pickup.isActiveAndEnabled ||
                Time.unscaledTime < pickup.retryAfter) continue;
            Vector2 center = pickup.transform.position;
            Vector2 relativeFrom = from - (sweepMotion ? pickup.previousPosition : center);
            Vector2 relativeTo = to - center;
            bool hit = false;
            for (int copy = -1; copy <= 1 && !hit; copy++)
            {
                Vector2 point = Vector2.right * (copy * width);
                hit = TrapGeometry.SweptBox(relativeFrom, relativeTo, bodyHalfSize,
                    new Rect(point - Vector2.one * radius, Vector2.one * radius * 2f));
            }
            if (!hit) continue;
            bool accepted;
            // Guard callbacks against a second synchronous collection call.
            // Claim permanently only after the bag accepts the find.
            pickup.claiming = true;
            try { accepted = expedition.TryCollect(pickup.Kind); }
            finally { if (pickup != null) pickup.claiming = false; }
            if (pickup == null) continue;
            if (!accepted) { pickup.retryAfter = Time.unscaledTime + .35f; continue; }
            pickup.collected = true; Active.Remove(pickup);
            pickup.gameObject.SetActive(false);
            Destroy(pickup.gameObject);
        }
    }

    public static void RemoveUnclaimed()
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            ExpeditionPickup pickup = Active[i];
            if (pickup == null || pickup.collected) continue;
            Active.Remove(pickup);
            pickup.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(pickup.gameObject); else DestroyImmediate(pickup.gameObject);
        }
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
