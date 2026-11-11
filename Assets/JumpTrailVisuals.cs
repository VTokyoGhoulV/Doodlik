using UnityEngine;

// Bounded presentation pool: no gameplay RNG, physics, Instantiate or managed
// allocations in the frame loop. Drawings stay in world space after emission.
[DisallowMultipleComponent]
public sealed class JumpTrailVisuals : MonoBehaviour
{
    private const int Capacity = 12;
    private sealed class Mote
    {
        public Transform Transform;
        public Mesh Mesh;
        public Color32[] Colors;
        public Vector3 Origin;
        public float Age, Lifetime, Size, Phase, Turn;
        public bool Active;
    }

    private readonly Mote[] pool = new Mote[Capacity];
    private Transform poolRoot;
    private SpriteRenderer cat;
    private Player player;
    private Material material;
    private JumpTrailArt.Drawing art;
    private string itemId;
    private Vector3 previousPosition;
    private float sinceEmission;
    private int nextMote, emitted;
    public int ActiveCount { get; private set; }
    public int PoolCapacity => Capacity;

    public void Initialize(SpriteRenderer renderer, Material ink)
    {
        cat = renderer;
        material = ink;
        player = GetComponentInParent<Player>();
        previousPosition = transform.position;
    }

    public void Configure(string id)
    {
        if (itemId == id) return;
        itemId = id;
        Clear();
        ReleasePool();
        art = JumpTrailArt.Get(id);
        if (art == null || cat == null || material == null) return;
        poolRoot = new GameObject("Hand drawn jump trail").transform;
        for (int i = 0; i < Capacity; i++)
        {
            GameObject root = new GameObject("Trail mote " + i, typeof(MeshFilter), typeof(MeshRenderer));
            root.transform.SetParent(poolRoot, false);
            Mesh mesh = new Mesh { name = id + " pooled mote " + i };
            mesh.MarkDynamic();
            mesh.vertices = art.Vertices;
            mesh.colors32 = art.Colors;
            mesh.triangles = art.Triangles;
            mesh.RecalculateBounds();
            root.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingLayerID = cat.sortingLayerID;
            renderer.sortingOrder = cat.sortingOrder - 1;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            root.SetActive(false);
            pool[i] = new Mote { Transform = root.transform, Mesh = mesh, Colors = new Color32[art.Colors.Length] };
        }
        previousPosition = transform.position;
    }

    private void LateUpdate() => Refresh(Time.deltaTime);

    private void Refresh(float deltaTime)
    {
        Vector3 current = transform.position;
        Vector3 movement = current - previousPosition;
        previousPosition = current;
        if (art == null || poolRoot == null || cat == null) return;
        if (player == null || player.IsDead || !RunFlow.IsPlaying || !cat.enabled)
        { Clear(); return; }
        if (Time.timeScale <= 0f || deltaTime <= 0f) return;
        float width = Mathf.Max(0.1f, cat.bounds.size.x);
        if (movement.sqrMagnitude > width * width * 2.25f)
        { Clear(); return; }

        sinceEmission += deltaTime;
        // Only the rising part of a jump emits. Existing motes finish their
        // short fade while falling, leaving the next platform unobscured.
        if (movement.y > 0.001f && sinceEmission >= 0.075f)
        {
            Emit(cat.bounds.center + Vector3.down * cat.bounds.size.y * 0.38f, width);
            sinceEmission = 0f;
        }
        ActiveCount = 0;
        foreach (Mote mote in pool)
        {
            if (mote == null || !mote.Active) continue;
            mote.Age += deltaTime;
            if (mote.Age >= mote.Lifetime)
            {
                mote.Active = false;
                mote.Transform.gameObject.SetActive(false);
                continue;
            }
            ActiveCount++;
            float progress = mote.Age / mote.Lifetime;
            float flutter = Mathf.Sin(mote.Phase + progress * 4f) - Mathf.Sin(mote.Phase);
            float drift = itemId == "trail_fireflies" ? 0.2f : itemId == "trail_meteor" ? 0.055f : 0.12f;
            mote.Transform.position = mote.Origin + new Vector3(flutter * width * drift,
                -width * progress * 0.22f, 0f);
            mote.Transform.rotation = Quaternion.Euler(0f, 0f, mote.Turn + progress *
                (itemId == "trail_blossom" ? 100f : 28f));
            float scale = mote.Size * (1f - 0.35f * progress);
            mote.Transform.localScale = Vector3.one * scale;
            float opacity = 1f - Mathf.SmoothStep(0.1f, 1f, progress);
            for (int vertex = 0; vertex < mote.Colors.Length; vertex++)
            {
                Color32 color = art.Colors[vertex];
                color.a = (byte)Mathf.RoundToInt(color.a * opacity);
                mote.Colors[vertex] = color;
            }
            mote.Mesh.SetColors(mote.Colors);
        }
    }

    private void Emit(Vector3 origin, float width)
    {
        Mote mote = pool[nextMote];
        nextMote = (nextMote + 1) % Capacity;
        // A deterministic little wobble must not consume generator Random.
        int index = emitted++;
        float side = (index % 3 - 1) * 0.11f;
        mote.Origin = origin + Vector3.right * width * side;
        mote.Age = 0f;
        mote.Lifetime = itemId == "trail_fireflies" ? 0.64f : itemId == "trail_ninelives" ? 0.56f : 0.5f;
        mote.Size = width * (itemId == "trail_fireflies" ? 0.21f : 0.24f) *
            (0.86f + (index % 4) * 0.08f);
        mote.Phase = index * 2.39996f;
        mote.Turn = itemId == "trail_ninelives" ? (index % 2 == 0 ? -12f : 12f) : (index % 5 - 2) * 17f;
        mote.Active = true;
        mote.Transform.gameObject.SetActive(true);
    }

    public void Clear()
    {
        foreach (Mote mote in pool)
        {
            if (mote == null) continue;
            mote.Active = false;
            if (mote.Transform != null) mote.Transform.gameObject.SetActive(false);
        }
        ActiveCount = 0;
        nextMote = emitted = 0;
        sinceEmission = 0f;
        previousPosition = transform.position;
    }

    private void OnEnable() => previousPosition = transform.position;
    private void OnDisable() => Clear();
    private void OnDestroy() => ReleasePool();

    private void ReleasePool()
    {
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] != null) Release(pool[i].Mesh);
            pool[i] = null;
        }
        if (poolRoot != null)
        {
            poolRoot.gameObject.SetActive(false);
            Release(poolRoot.gameObject);
        }
        poolRoot = null;
    }

    private static void Release(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
}
