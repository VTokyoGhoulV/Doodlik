using UnityEngine;

// The first landing always bounces normally; only the return landing is lost.
public sealed class CrumblingPlatform : MonoBehaviour
{
    private SpriteRenderer sprite;
    private Collider2D surface;
    private Color originalColor;
    private float timeAfterLanding = -1f;
    private Mesh crackMesh;
    private Material crackMaterial;

    private void Awake()
    {
        sprite = GetComponent<SpriteRenderer>();
        surface = GetComponent<Collider2D>();
        if (sprite == null || surface == null) { enabled = false; return; }
        originalColor = new Color(0.90f, 0.72f, 0.47f, 1f);
        sprite.color = originalColor;
        DrawCrack();
    }

    public void OnLanded()
    {
        if (timeAfterLanding < 0f)
            timeAfterLanding = 0f;
    }

    private void Update()
    {
        if (timeAfterLanding < 0f || !RunFlow.IsPlaying)
            return;
        timeAfterLanding += Time.deltaTime;
        if (timeAfterLanding < 0.32f)
        {
            sprite.color = Color.Lerp(originalColor, Color.white,
                0.3f + 0.25f * Mathf.Sin(timeAfterLanding * 48f));
            return;
        }
        surface.enabled = false;
        transform.position += Vector3.down * (timeAfterLanding - 0.32f) * 12f * Time.deltaTime;
        sprite.color = new Color(originalColor.r, originalColor.g, originalColor.b,
            Mathf.Clamp01(1f - (timeAfterLanding - 0.32f) * 3f));
        if (timeAfterLanding > 0.7f)
            Destroy(gameObject);
    }

    private void DrawCrack()
    {
        Shader shader = Resources.Load<Shader>("HazardInk");
        if (shader == null) return;
        Bounds bounds = sprite.bounds;
        Vector2[] points = { new Vector2(-0.22f, 0.48f), new Vector2(0.02f, 0.12f),
            new Vector2(-0.11f, -0.08f), new Vector2(0.18f, -0.43f) };
        Vector3[] vertices = new Vector3[12];
        Color[] colors = new Color[12];
        int[] indices = new int[18];
        for (int i = 0; i < 3; i++)
        {
            Vector2 a = Vector2.Scale(points[i], bounds.size);
            Vector2 b = Vector2.Scale(points[i + 1], bounds.size);
            Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * 0.012f * GameplayPreferences.ModelScale;
            Vector2[] corners = { a - normal, a + normal, b + normal, b - normal };
            for (int j = 0; j < 4; j++)
            {
                vertices[i * 4 + j] = transform.InverseTransformPoint(bounds.center + (Vector3)corners[j]);
                colors[i * 4 + j] = new Color(0.35f, 0.21f, 0.12f);
            }
            int v = i * 4, t = i * 6;
            indices[t] = v; indices[t + 1] = v + 1; indices[t + 2] = v + 2;
            indices[t + 3] = v; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
        }
        crackMesh = new Mesh { name = "Paper crack", vertices = vertices, colors = colors, triangles = indices };
        crackMesh.RecalculateBounds();
        crackMaterial = new Material(shader);
        GameObject crack = new GameObject("Visible paper crack", typeof(MeshFilter), typeof(MeshRenderer));
        crack.transform.SetParent(transform, false);
        crack.GetComponent<MeshFilter>().sharedMesh = crackMesh;
        MeshRenderer renderer = crack.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = crackMaterial;
        renderer.sortingOrder = sprite.sortingOrder + 1;
    }

    private void OnDestroy()
    {
        if (Application.isPlaying) { Destroy(crackMesh); Destroy(crackMaterial); }
        else { DestroyImmediate(crackMesh); DestroyImmediate(crackMaterial); }
    }
}
