using System.Collections.Generic;
using UnityEngine;

// Presentation only: no timers, radii, hitboxes or gameplay state are changed.
[DisallowMultipleComponent]
public sealed class PlayerBuffVisuals : MonoBehaviour
{
    private sealed class Drawing
    {
        public Transform Root;
        public Mesh Mesh;
        public Color[] BaseColors, Colors;
        public float Alpha = -1f;

        public void SetVisible(bool visible)
        {
            if (Root.gameObject.activeSelf != visible) Root.gameObject.SetActive(visible);
        }

        public void SetAlpha(float alpha)
        {
            if (Mathf.Abs(Alpha - alpha) < 0.005f) return;
            Alpha = alpha;
            for (int i = 0; i < Colors.Length; i++)
            {
                Colors[i] = BaseColors[i];
                Colors[i].a *= alpha;
            }
            Mesh.SetColors(Colors);
        }
    }

    private RunPowerups powers;
    private Player player;
    private SpriteRenderer cat;
    private Transform effectsRoot;
    private Material material;
    private Drawing shield, magnet, pullArcs, clock;
    private Vector2 baseSize;
    private float animationTime, previousProtection, shieldBreakTime;
    private const float BreakDuration = 0.32f;

    public static PlayerBuffVisuals Attach(RunPowerups owner)
    {
        if (owner == null) return null;
        PlayerBuffVisuals visual = owner.GetComponent<PlayerBuffVisuals>();
        if (visual != null && visual.effectsRoot != null) return visual;
        SpriteRenderer renderer = null;
        foreach (SpriteRenderer candidate in owner.GetComponentsInChildren<SpriteRenderer>())
            if (candidate.enabled && candidate.sprite != null) { renderer = candidate; break; }
        if (renderer == null) return null;
        if (visual == null) visual = owner.gameObject.AddComponent<PlayerBuffVisuals>();
        visual.Initialize(owner, renderer);
        return visual;
    }

    private void Initialize(RunPowerups owner, SpriteRenderer renderer)
    {
        powers = owner;
        player = owner.GetComponent<Player>();
        cat = renderer;
        baseSize = renderer.bounds.size;
        Shader shader = Resources.Load<Shader>("HazardInk");
        if (shader == null)
        {
            Debug.LogError("Doodlik: HazardInk shader is missing for player buff visuals.", this);
            enabled = false;
            return;
        }
        material = new Material(shader) { name = "Doodlik buff ink (shared drawings)" };
        effectsRoot = new GameObject("Active bonus effects").transform;
        // Sibling of PlayerVisual: sprite flip/squash must not mirror the icons.
        effectsRoot.SetParent(owner.transform, false);
        shield = CreateDrawing("Shield bubble", BuildShield(), effectsRoot, -1);
        magnet = CreateDrawing("Magnet", BuildMagnet(), effectsRoot, 2);
        pullArcs = CreateDrawing("Attraction arcs", BuildArcs(), magnet.Root, 2);
        clock = CreateDrawing("Slow clock", BuildClock(), effectsRoot, 2);
        HideAll();
    }

    private Drawing CreateDrawing(string name, Mesh mesh, Transform parent, int sortingOffset)
    {
        GameObject root = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        root.transform.SetParent(parent, false);
        root.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = root.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingLayerID = cat.sortingLayerID;
        renderer.sortingOrder = cat.sortingOrder + sortingOffset;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        Color[] colors = mesh.colors;
        return new Drawing { Root = root.transform, Mesh = mesh, BaseColors = colors, Colors = new Color[colors.Length] };
    }

    private void LateUpdate() => RefreshPresentation(Time.deltaTime);

    private void RefreshPresentation(float scaledDeltaTime)
    {
        if (effectsRoot == null) return;
        if (powers == null || !powers.isActiveAndEnabled || cat == null || player == null ||
            player.IsDead || !RunFlow.RunActive || RunFlow.IsTransitioning)
        {
            HideAll();
            return;
        }
        float dt = Time.timeScale > 0f ? Mathf.Max(0f, scaledDeltaTime) : 0f;
        animationTime += dt;
        if (powers.ProtectionTimeRemaining > previousProtection + 0.5f) shieldBreakTime = BreakDuration;
        else shieldBreakTime = Mathf.Max(0f, shieldBreakTime - dt);
        previousProtection = powers.ProtectionTimeRemaining;

        Bounds bounds = cat.bounds;
        float width = Mathf.Clamp(bounds.size.x, baseSize.x * 0.88f, baseSize.x * 1.12f);
        float height = Mathf.Clamp(bounds.size.y, baseSize.y * 0.88f, baseSize.y * 1.12f);
        effectsRoot.position = bounds.center;
        effectsRoot.rotation = Quaternion.identity;
        Vector3 parentScale = transform.lossyScale;
        effectsRoot.localScale = new Vector3(1f / NonZero(parentScale.x), 1f / NonZero(parentScale.y), 1f);

        bool persistentShield = powers.HasShield || powers.IsRocketActive;
        bool shieldVisible = persistentShield || shieldBreakTime > 0f;
        shield.SetVisible(shieldVisible);
        if (shieldVisible)
        {
            float burst = persistentShield ? 0f : 1f - shieldBreakTime / BreakDuration;
            float breathe = persistentShield ? 1f + Mathf.Sin(animationTime * 2.2f) * 0.018f : 1f + burst * 0.24f;
            shield.Root.localScale = new Vector3(width * 1.25f, height * 1.18f, 1f) * breathe;
            shield.SetAlpha(persistentShield ? 1f : (1f - burst) * (1f - burst));
        }

        bool magnetVisible = powers.MagnetTimeRemaining > 0f;
        magnet.SetVisible(magnetVisible);
        if (magnetVisible)
        {
            float pulse = ExpiringPulse(powers.MagnetTimeRemaining);
            float size = height * 0.28f * pulse;
            magnet.Root.localPosition = new Vector3(-width * 0.5f - height * 0.26f, height * 0.42f, 0f);
            magnet.Root.localScale = Vector3.one * size;
            magnet.Root.localRotation = Quaternion.Euler(0f, 0f, -12f);
            magnet.SetAlpha(1f);
            pullArcs.SetVisible(true);
            pullArcs.Root.localScale = Vector3.one * (0.91f + 0.08f * Mathf.Sin(animationTime * 3f));
            pullArcs.SetAlpha(0.78f + 0.12f * Mathf.Sin(animationTime * 3f));
        }

        bool slowVisible = powers.SlowTimeRemaining > 0f;
        clock.SetVisible(slowVisible);
        if (slowVisible)
        {
            float size = height * 0.29f * ExpiringPulse(powers.SlowTimeRemaining);
            clock.Root.localPosition = new Vector3(width * 0.5f + height * 0.16f, height * 0.42f, 0f);
            clock.Root.localScale = Vector3.one * size;
            clock.Root.localRotation = Quaternion.Euler(0f, 0f, 8f + Mathf.Sin(animationTime * 1.6f) * 3f);
            clock.SetAlpha(1f);
        }
    }

    private float ExpiringPulse(float remaining) => remaining > 2f ? 1f :
        1.025f + 0.065f * Mathf.Sin(animationTime * Mathf.PI * 2f); // Smooth 1 Hz, never blinks off.

    private static float NonZero(float value) => Mathf.Abs(value) < 0.001f ? 0.001f : value;

    private void HideAll()
    {
        shield?.SetVisible(false);
        magnet?.SetVisible(false);
        clock?.SetVisible(false);
    }

    private void OnDisable() => HideAll();
    private void OnDestroy()
    {
        Release(shield?.Mesh); Release(magnet?.Mesh); Release(pullArcs?.Mesh); Release(clock?.Mesh);
        Release(material);
        if (effectsRoot != null) Release(effectsRoot.gameObject);
    }
    private static void Release(Object resource)
    {
        if (resource == null) return;
        if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
    }

    private static Mesh BuildShield()
    {
        var drawing = new InkDrawing();
        drawing.Arc(Vector2.zero, 0.5f, 0.038f, 0f, 360f, 64, new Color(0.58f, 0.92f, 1f, 0.16f));
        drawing.Arc(Vector2.zero, 0.5f, 0.012f, 0f, 360f, 64, new Color(0.51f, 0.86f, 1f, 0.67f));
        drawing.Arc(Vector2.zero, 0.485f, 0.010f, 48f, 105f, 12, new Color(0.9f, 1f, 1f, 0.76f));
        return drawing.Mesh("Soft shield outline");
    }

    private static Mesh BuildMagnet()
    {
        var drawing = new InkDrawing();
        Color ink = new Color32(73, 45, 42, 245), red = new Color32(238, 112, 118, 255);
        drawing.Arc(Vector2.zero, 0.30f, 0.18f, 180f, 360f, 2, ink);
        drawing.Arc(Vector2.zero, 0.30f, 0.105f, 180f, 360f, 2, red);
        foreach (float x in new[] { -0.30f, 0.30f })
        {
            drawing.Box(new Vector2(x, 0.15f), new Vector2(0.18f, 0.32f), ink);
            drawing.Box(new Vector2(x, 0.15f), new Vector2(0.105f, 0.29f), red);
            drawing.Box(new Vector2(x, 0.27f), new Vector2(0.13f, 0.08f), new Color32(255, 238, 183, 255));
        }
        return drawing.Mesh("Tiny coral magnet");
    }

    private static Mesh BuildArcs()
    {
        var drawing = new InkDrawing();
        foreach (float radius in new[] { 0.56f, 0.75f })
        {
            drawing.Arc(Vector2.zero, radius, 0.026f, -37f, 37f, 10, new Color(1f, 0.66f, 0.45f, 0.65f));
            drawing.Arc(Vector2.zero, radius, 0.026f, 143f, 217f, 10, new Color(1f, 0.66f, 0.45f, 0.65f));
        }
        return drawing.Mesh("Magnetic pull arcs");
    }

    private static Mesh BuildClock()
    {
        var drawing = new InkDrawing();
        Color ink = new Color32(52, 64, 91, 255);
        drawing.Disc(Vector2.zero, 0.44f, ink);
        drawing.Disc(Vector2.zero, 0.38f, new Color32(155, 218, 246, 240));
        drawing.Disc(Vector2.zero, 0.30f, new Color32(224, 247, 250, 245));
        drawing.Box(new Vector2(0f, 0.47f), new Vector2(0.15f, 0.12f), ink);
        drawing.Box(new Vector2(0f, 0.105f), new Vector2(0.04f, 0.23f), ink);
        drawing.Box(new Vector2(0.09f, 0f), new Vector2(0.20f, 0.04f), ink);
        drawing.Disc(Vector2.zero, 0.042f, ink);
        return drawing.Mesh("Blue slow clock");
    }

    private sealed class InkDrawing
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();
        public void Box(Vector2 center, Vector2 size, Color color)
        {
            Vector2 half = size * 0.5f;
            Quad(center + new Vector2(-half.x, -half.y), center + new Vector2(-half.x, half.y),
                center + half, center + new Vector2(half.x, -half.y), color);
        }
        public void Arc(Vector2 center, float radius, float width, float start, float end, int segments, Color color)
        {
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.Lerp(start, end, (float)i / segments) * Mathf.Deg2Rad;
                float b = Mathf.Lerp(start, end, (float)(i + 1) / segments) * Mathf.Deg2Rad;
                Vector2 da = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 db = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                Quad(center + da * (radius - width * 0.5f), center + da * (radius + width * 0.5f),
                    center + db * (radius + width * 0.5f), center + db * (radius - width * 0.5f), color);
            }
        }
        public void Disc(Vector2 center, float radius, Color color)
        {
            int first = vertices.Count;
            vertices.Add(center); colors.Add(color);
            for (int i = 0; i < 24; i++)
            {
                float angle = i * Mathf.PI / 12f;
                vertices.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius); colors.Add(color);
            }
            for (int i = 0; i < 24; i++) { triangles.Add(first); triangles.Add(first + 1 + i); triangles.Add(first + 1 + (i + 1) % 24); }
        }
        private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int first = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (int i = 0; i < 4; i++) colors.Add(color);
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
        }
        public Mesh Mesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
