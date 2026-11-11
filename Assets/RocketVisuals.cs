using UnityEngine;

// A paper seat below the paws, not a replacement cat skin. Cosmetic-only:
// this observes RunPowerups and never moves Player, modifies colliders or RNG.
[DisallowMultipleComponent]
public sealed class RocketVisuals : MonoBehaviour
{
    private const int Capacity = 12;
    private sealed class Mote
    {
        public Transform Root;
        public Mesh Mesh;
        public Color32[] BaseColors, Colors;
        public Vector3 Origin;
        public float Age, Size, Phase;
        public bool Active;
    }

    private readonly Mote[] pool = new Mote[Capacity];
    private RunPowerups powers;
    private Player player;
    private SpriteRenderer cat;
    private Transform bodyRoot, poolRoot;
    private Mesh bodyMesh;
    private Material material;
    private Color32[] bodyBaseColors, bodyColors;
    private Vector2 initialSize;
    private Vector3 previousPosition;
    private float animationTime, sinceEmission, exitFade;
    private int emitted, nextMote;
    private bool wasActive;
    private const float ExitDuration = .22f, MoteDuration = .52f;
    public int PoolCapacity => Capacity;
    public int ActiveMotes { get; private set; }
    public bool BodyVisible => bodyRoot != null && bodyRoot.gameObject.activeSelf;

    public static RocketVisuals Attach(RunPowerups owner)
    {
        if (owner == null) return null;
        RocketVisuals visual = owner.GetComponent<RocketVisuals>();
        if (visual != null && visual.bodyRoot != null) return visual;
        SpriteRenderer source = null;
        CatAppearance appearance = owner.GetComponentInChildren<CatAppearance>();
        if (appearance != null) source = appearance.GetComponent<SpriteRenderer>();
        if (source == null)
            foreach (SpriteRenderer candidate in owner.GetComponentsInChildren<SpriteRenderer>())
                if (candidate.enabled && candidate.sprite != null) { source = candidate; break; }
        if (source == null || source.sprite == null) return null;
        if (visual == null) visual = owner.gameObject.AddComponent<RocketVisuals>();
        visual.Initialize(owner, source);
        return visual;
    }

    private void Initialize(RunPowerups owner, SpriteRenderer source)
    {
        powers = owner; player = owner.GetComponent<Player>(); cat = source;
        initialSize = cat.bounds.size;
        Shader shader = Resources.Load<Shader>("HazardInk");
        if (shader == null)
        {
            Debug.LogError("Doodlik: HazardInk shader is missing for the paper rocket.", this);
            enabled = false; return;
        }
        material = new Material(shader) { name = "Doodlik paper rocket ink", hideFlags = HideFlags.DontSave };
        bodyMesh = Instantiate(AdventureArt.RocketBody); bodyMesh.name = "Paper rocket fade mesh"; bodyMesh.MarkDynamic();
        bodyBaseColors = bodyMesh.colors32; bodyColors = new Color32[bodyBaseColors.Length];
        bodyRoot = Drawing("Paper rocket seat", bodyMesh, owner.transform, -1);
        poolRoot = new GameObject("Paper rocket star pool").transform;
        // World-space motes never stretch into ribbons when Player wraps.
        for (int i = 0; i < Capacity; i++)
        {
            Mesh mesh = Instantiate(AdventureArt.Star); mesh.name = "Paper rocket pooled star " + i; mesh.MarkDynamic();
            Color32[] colors = mesh.colors32;
            Transform root = Drawing("Paper star " + i, mesh, poolRoot, -2);
            root.gameObject.SetActive(false);
            pool[i] = new Mote { Root = root, Mesh = mesh, BaseColors = colors, Colors = new Color32[colors.Length] };
        }
        previousPosition = cat.bounds.center;
        HideAll();
    }

    private Transform Drawing(string name, Mesh mesh, Transform parent, int sortingOffset)
    {
        GameObject root = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        root.transform.SetParent(parent, false);
        root.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = root.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material; renderer.sortingLayerID = cat.sortingLayerID;
        renderer.sortingOrder = cat.sortingOrder + sortingOffset;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        return root.transform;
    }

    private void LateUpdate() => RefreshPresentation(Time.deltaTime);

    private void RefreshPresentation(float scaledDeltaTime)
    {
        if (bodyRoot == null || poolRoot == null) return;
        if (powers == null || !powers.isActiveAndEnabled || player == null || player.IsDead || cat == null ||
            !RunFlow.RunActive || RunFlow.IsTransitioning)
        { HideAll(); return; }
        float dt = Time.timeScale > 0f && scaledDeltaTime > 0f &&
            !float.IsNaN(scaledDeltaTime) && !float.IsInfinity(scaledDeltaTime) ? scaledDeltaTime : 0f;
        Bounds bounds = cat.bounds;
        float width = Mathf.Clamp(bounds.size.x, initialSize.x * .88f, initialSize.x * 1.12f);
        float height = Mathf.Clamp(bounds.size.y, initialSize.y * .88f, initialSize.y * 1.12f);
        Vector3 current = bounds.center;
        if ((current - previousPosition).sqrMagnitude > width * width * 3f) ClearMotes();
        previousPosition = current;
        bool flying = powers.IsRocketFlying, active = powers.IsRocketActive;
        if (wasActive && !active) exitFade = ExitDuration;
        wasActive = active;
        if (active) exitFade = ExitDuration;
        else exitFade = Mathf.Max(0f, exitFade - dt);
        animationTime += dt;

        bool visible = active || exitFade > 0f;
        if (bodyRoot.gameObject.activeSelf != visible) bodyRoot.gameObject.SetActive(visible);
        if (visible)
        {
            float opacity = active ? 1f : exitFade / ExitDuration;
            float size = width * 1.10f;
            float facing = cat.flipX ? -1f : 1f;
            // Follow the unmodified sprite's paw level. The mesh's seat ridge
            // meets the paws; its nose and fins remain below the costume.
            float pawY = bounds.min.y + height * .075f;
            bodyRoot.position = new Vector3(bounds.center.x - width * .055f * facing,
                pawY - AdventureArt.RocketSeatY * size - (1f - opacity) * height * .07f, bounds.center.z);
            bodyRoot.rotation = Quaternion.identity;
            Vector3 parentScale = bodyRoot.parent.lossyScale;
            bodyRoot.localScale = new Vector3(size * facing / NonZero(parentScale.x), size / NonZero(parentScale.y), 1f);
            SetAlpha(bodyMesh, bodyBaseColors, bodyColors, opacity);
            sinceEmission += dt;
            if (flying && dt > 0f && sinceEmission >= .065f)
            {
                Emit(bodyRoot.position + new Vector3(-width * .34f * facing, -height * .11f, 0), width);
                sinceEmission = 0f;
            }
        }
        ActiveMotes = 0;
        for (int i = 0; i < Capacity; i++)
        {
            Mote mote = pool[i];
            if (mote == null || !mote.Active) continue;
            mote.Age += dt;
            if (mote.Age >= MoteDuration)
            {
                mote.Active = false; mote.Root.gameObject.SetActive(false); continue;
            }
            ActiveMotes++;
            float t = mote.Age / MoteDuration;
            mote.Root.position = mote.Origin + new Vector3(Mathf.Sin(mote.Phase + t * 4f) * width * .045f,
                -width * t * .20f, 0);
            mote.Root.rotation = Quaternion.Euler(0, 0, mote.Phase * 13f + t * 45f);
            mote.Root.localScale = Vector3.one * (mote.Size * (1f - .35f * t));
            SetAlpha(mote.Mesh, mote.BaseColors, mote.Colors, 1f - Mathf.SmoothStep(.1f, 1f, t));
        }
    }

    private void Emit(Vector3 origin, float width)
    {
        Mote mote = pool[nextMote]; nextMote = (nextMote + 1) % Capacity;
        int sequence = emitted++;
        mote.Origin = origin + Vector3.right * width * ((sequence % 3 - 1) * .075f);
        mote.Age = 0; mote.Size = width * (.18f + sequence % 4 * .020f); mote.Phase = sequence * 2.39996f;
        mote.Active = true; mote.Root.gameObject.SetActive(true);
    }

    private static void SetAlpha(Mesh mesh, Color32[] source, Color32[] buffer, float opacity)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            Color32 color = source[i]; color.a = (byte)Mathf.RoundToInt(color.a * opacity); buffer[i] = color;
        }
        mesh.SetColors(buffer);
    }
    private void ClearMotes()
    {
        for (int i = 0; i < Capacity; i++)
        {
            if (pool[i] == null) continue;
            pool[i].Active = false;
            if (pool[i].Root != null) pool[i].Root.gameObject.SetActive(false);
        }
        ActiveMotes = 0; sinceEmission = 0f; nextMote = 0;
    }
    private void HideAll()
    {
        if (bodyRoot != null) bodyRoot.gameObject.SetActive(false);
        ClearMotes(); exitFade = 0; wasActive = false;
        if (cat != null) previousPosition = cat.bounds.center;
    }
    private void OnDisable() => HideAll();
    private void OnDestroy()
    {
        for (int i = 0; i < Capacity; i++) if (pool[i] != null) Release(pool[i].Mesh);
        Release(bodyMesh); Release(material);
        if (bodyRoot != null) Release(bodyRoot.gameObject);
        if (poolRoot != null) { poolRoot.gameObject.SetActive(false); Release(poolRoot.gameObject); }
    }
    private static float NonZero(float value) => Mathf.Abs(value) < .001f ? .001f : value;
    private static void Release(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
