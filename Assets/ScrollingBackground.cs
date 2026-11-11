using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class ScrollingBackground : MonoBehaviour
{
    public const string PanoramaResourcePath = "WorldZones/AscentPanorama";
    private const float WindowHeight = 0.18f;
    private readonly Vector2[] uv = new Vector2[4];
    private Camera mainCamera;
    private ZoneAtmosphere atmosphere;
    private Transform artwork;
    private Texture2D panorama;
    private Mesh mesh;
    private Material material;
    private float displayedProgress;
    private Rect previousUv;
    private bool ready, usingPanorama;

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        mainCamera = Camera.main;
        // The menu's existing fade covers scene reload. Reset to the bottom of
        // the same drawing here instead of scrolling backwards through it.
        displayedProgress = 0f;
        atmosphere?.ResetAppearance();
        UpdateAppearance();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForScene()
    {
        if (FindAnyObjectByType<ScrollingBackground>() != null)
        {
            return;
        }

        GameObject background = new GameObject("Scrolling Background");
        DontDestroyOnLoad(background);
        background.AddComponent<ScrollingBackground>();
    }

    private void Start()
    {
        mainCamera = Camera.main;
        panorama = Resources.Load<Texture2D>(PanoramaResourcePath);
        usingPanorama = panorama != null;
        if (!usingPanorama)
        {
            Sprite fallback = Resources.Load<Sprite>("DoodlikBackground");
            if (fallback != null) panorama = fallback.texture;
            Debug.LogWarning("Doodlik: панорама WorldZones/AscentPanorama не найдена; используется прежний фон.");
        }
        Shader shader = Shader.Find("Sprites/Default");
        if (mainCamera == null || panorama == null || shader == null)
        {
            enabled = false;
            return;
        }
        // One shared texture, one four-vertex quad, no render textures or tiled
        // sheets. The sprite shader is unlit; the artwork's own colours remain.
        material = new Material(shader) { name = "Unlit ascent panorama", mainTexture = panorama, color = Color.white };
        mesh = new Mesh { name = "Ascent panorama viewport" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f), new Vector3(-0.5f, 0.5f),
            new Vector3(0.5f, 0.5f), new Vector3(0.5f, -0.5f)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        mesh.RecalculateBounds();
        GameObject surface = new GameObject("Continuous ascent artwork", typeof(MeshFilter), typeof(MeshRenderer));
        artwork = surface.transform;
        artwork.SetParent(transform, false);
        surface.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = surface.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = -100;
        atmosphere = gameObject.AddComponent<ZoneAtmosphere>();
        ready = true;
        UpdateAppearance();
    }

    private void LateUpdate()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null || !ready) return;
        float target = WorldZones.ProgressForScore(WorldZones.CurrentScore);
        displayedProgress = Mathf.Lerp(displayedProgress, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 4f));
        if (Mathf.Abs(displayedProgress - target) < 0.00001f) displayedProgress = target;
        UpdateAppearance();
    }

    private void UpdateAppearance()
    {
        if (!ready || mainCamera == null) return;
        transform.position = new Vector3(mainCamera.transform.position.x, mainCamera.transform.position.y, 5f);
        float viewHeight = mainCamera.orthographicSize * 2f;
        float viewWidth = viewHeight * mainCamera.aspect;
        artwork.localScale = new Vector3(viewWidth, viewHeight, 1f);
        Rect crop = GetPanoramaUv(mainCamera.aspect, (float)panorama.width / panorama.height,
            usingPanorama ? displayedProgress : 0f, usingPanorama ? WindowHeight : 1f);
        if (crop != previousUv)
        {
            uv[0] = new Vector2(crop.xMin, crop.yMin);
            uv[1] = new Vector2(crop.xMin, crop.yMax);
            uv[2] = new Vector2(crop.xMax, crop.yMax);
            uv[3] = new Vector2(crop.xMax, crop.yMin);
            mesh.uv = uv;
            previousUv = crop;
        }
        atmosphere.OpacityMultiplier = usingPanorama ? 0.38f : 1f;
        atmosphere.Refresh(mainCamera, WorldZones.Current);
    }

    public static Rect GetPanoramaUv(float viewportAspect, float textureAspect, float progress, float requestedHeight = WindowHeight)
    {
        viewportAspect = Mathf.Max(0.01f, viewportAspect);
        textureAspect = Mathf.Max(0.01f, textureAspect);
        // The physical texel ratio matches the viewport ratio in portrait,
        // normal landscape and ultrawide. No stretching or letterbox edges.
        float height = Mathf.Min(Mathf.Clamp01(requestedHeight), textureAspect / viewportAspect);
        float width = Mathf.Min(1f, viewportAspect * height / textureAspect);
        return new Rect((1f - width) * 0.5f, Mathf.Clamp01(progress) * (1f - height), width, height);
    }

    private void OnDestroy()
    {
        if (Application.isPlaying) { Destroy(material); Destroy(mesh); }
        else { DestroyImmediate(material); DestroyImmediate(mesh); }
    }
}

// Restrained paper-cloud/star silhouettes. Four reused meshes, no particles or
// large textures; decoration always renders behind the gameplay sprites.
internal sealed class ZoneAtmosphere : MonoBehaviour
{
    public float OpacityMultiplier { get; set; } = 1f;
    private readonly Transform[] strips = new Transform[4];
    private readonly Mesh[] meshes = new Mesh[4];
    private readonly Color[][] colors = new Color[4][];
    private readonly float[][] opacity = new float[4][];
    private Material material;
    private Material starMaterial;
    private float cloudAlpha, starAlpha, drift;
    private float starAspect = 1f;

    private void Awake()
    {
        Shader shader = Resources.Load<Shader>("HazardInk");
        if (shader == null) { enabled = false; return; }
        material = new Material(shader) { name = "World atmosphere ink" };
        Shader softStarShader = Resources.Load<Shader>("SoftStarInk");
        starMaterial = new Material(softStarShader != null ? softStarShader : shader) { name = "Antialiased star ink" };
        for (int i = 0; i < strips.Length; i++)
        {
            SetMesh(i, Build(i >= 2, 731 + i * 997, starAspect));
            GameObject strip = new GameObject(i < 2 ? "Paper clouds" : "Paper stars",
                typeof(MeshFilter), typeof(MeshRenderer));
            strip.transform.SetParent(transform, false);
            strips[i] = strip.transform;
            strip.GetComponent<MeshFilter>().sharedMesh = meshes[i];
            MeshRenderer renderer = strip.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = i >= 2 ? starMaterial : material;
            renderer.sortingOrder = -90;
        }
    }

    public void Refresh(Camera camera, WorldZones.Zone zone)
    {
        if (material == null) return;
        if (Mathf.Abs(starAspect - camera.aspect) > 0.001f)
        {
            starAspect = camera.aspect;
            for (int i = 2; i < 4; i++)
            {
                Mesh old = meshes[i];
                SetMesh(i, Build(true, 731 + i * 997, starAspect));
                strips[i].GetComponent<MeshFilter>().sharedMesh = meshes[i];
                if (Application.isPlaying) Destroy(old); else DestroyImmediate(old);
            }
        }
        float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 1.2f);
        cloudAlpha = Mathf.Lerp(cloudAlpha, WorldZones.IsCloudZone(zone) ? 0.2f * OpacityMultiplier : 0f, blend);
        starAlpha = Mathf.Lerp(starAlpha, WorldZones.IsNightZone(zone) ? 0.26f * OpacityMultiplier : 0f, blend);
        drift = Mathf.Repeat(drift + Time.deltaTime * 0.014f, 1f);
        float height = camera.orthographicSize * 2f;
        for (int i = 0; i < strips.Length; i++)
        {
            strips[i].localScale = new Vector3(height * camera.aspect, height, 1f);
            strips[i].localPosition = new Vector3(0f, ((i % 2) - 1 + drift) * height, -0.05f);
        }
        SetAlpha(0, new Color(0.84f, 0.91f, 0.93f, cloudAlpha));
        SetAlpha(1, new Color(1f, 0.87f, 0.56f, starAlpha));
    }

    public void ResetAppearance()
    {
        cloudAlpha = starAlpha = drift = 0f;
        if (material == null) return;
        SetAlpha(0, Color.clear);
        SetAlpha(1, Color.clear);
    }

    private void SetAlpha(int kind, Color color)
    {
        for (int strip = kind * 2; strip < kind * 2 + 2; strip++)
        {
            for (int i = 0; i < colors[strip].Length; i++)
            {
                colors[strip][i] = color;
                colors[strip][i].a *= opacity[strip][i];
            }
            meshes[strip].SetColors(colors[strip]);
        }
    }

    private void SetMesh(int index, Mesh mesh)
    {
        meshes[index] = mesh;
        colors[index] = mesh.colors;
        opacity[index] = new float[mesh.vertexCount];
        for (int i = 0; i < mesh.vertexCount; i++) opacity[index][i] = colors[index][i].a;
    }

    private static Mesh Build(bool stars, int seed, float aspect)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Color> tints = new List<Color>();
        System.Random random = new System.Random(seed);
        List<Vector2> centers = new List<Vector2>();
        int count = stars ? 27 : 6;
        for (int i = 0; i < count; i++)
        {
            float x = Mathf.Repeat(i * 0.381966f + 0.08f, 0.84f) - 0.42f;
            float y = (i + 0.5f) / count - 0.5f;
            if (stars)
            {
                // Independent samples, not an arithmetic sequence: no diagonal rows.
                // Fixed local seed keeps decoration stable and never changes platform RNG.
                for (int attempt = 0; attempt < 32; attempt++)
                {
                    x = (float)random.NextDouble() * 0.9f - 0.45f;
                    y = (float)random.NextDouble() * 0.9f - 0.45f;
                    bool clear = true;
                    foreach (Vector2 other in centers)
                        if ((new Vector2(x, y) - other).sqrMagnitude < 0.0036f) { clear = false; break; }
                    if (clear) break;
                }
                centers.Add(new Vector2(x, y));
                float radius = Mathf.Lerp(0.0045f, 0.012f, (float)random.NextDouble());
                float rotation = (float)random.NextDouble() * Mathf.PI * 2f;
                Vector3[] points = new Vector3[8];
                for (int j = 0; j < 8; j++)
                {
                    float angle = j * Mathf.PI / 4f + rotation;
                    float r = j % 2 == 0 ? radius : radius * 0.28f;
                    points[j] = new Vector3(x + Mathf.Cos(angle) * r / Mathf.Max(0.1f, aspect), y + Mathf.Sin(angle) * r);
                }
                AddFan(points, vertices, triangles);
                float alpha = Mathf.Lerp(0.36f, 1f, (float)random.NextDouble());
                while (tints.Count < vertices.Count) tints.Add(new Color(1f, 1f, 1f, alpha));
            }
            else
            {
                for (int puff = 0; puff < 3; puff++)
                {
                    Vector3[] points = new Vector3[16];
                    for (int j = 0; j < points.Length; j++)
                    {
                        float angle = j * Mathf.PI * 2f / points.Length;
                        points[j] = new Vector3(x + (puff - 1) * 0.037f + Mathf.Cos(angle) * 0.046f,
                            y + (puff == 1 ? 0.012f : 0f) + Mathf.Sin(angle) * 0.024f);
                    }
                    AddFan(points, vertices, triangles);
                    while (tints.Count < vertices.Count) tints.Add(Color.white);
                }
            }
        }
        Mesh mesh = new Mesh { name = stars ? "Paper stars" : "Paper clouds" };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
        mesh.SetColors(tints);
        if (stars)
        {
            Vector2[] edgeWeights = new Vector2[vertices.Count];
            for (int i = 0; i < edgeWeights.Length; i += 9) edgeWeights[i] = Vector2.one;
            mesh.uv = edgeWeights;
        }
        return mesh;
    }

    private static void AddFan(Vector3[] points, List<Vector3> vertices, List<int> triangles)
    {
        int start = vertices.Count;
        Vector3 center = Vector3.zero;
        foreach (Vector3 point in points) center += point;
        vertices.Add(center / points.Length);
        vertices.AddRange(points);
        // A star is concave. A fan from a tip overlaps its notches and looks like
        // an arrow; a fan from its centre fills each arm without crossing edges.
        for (int j = 0; j < points.Length; j++)
        { triangles.Add(start); triangles.Add(start + 1 + j); triangles.Add(start + 1 + (j + 1) % points.Length); }
    }

    private void OnDestroy()
    {
        if (Application.isPlaying) { Destroy(material); Destroy(starMaterial); foreach (Mesh mesh in meshes) Destroy(mesh); }
        else { DestroyImmediate(material); DestroyImmediate(starMaterial); foreach (Mesh mesh in meshes) DestroyImmediate(mesh); }
    }
}
