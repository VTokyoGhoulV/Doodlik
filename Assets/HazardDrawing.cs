using System.Collections.Generic;
using UnityEngine;

// Small shared vector meshes: uneven ink outlines, warm thorns and berry-red
// seeds fit the flower platforms without large textures or particle systems.
public sealed class HazardDrawing : MonoBehaviour
{
    private static Mesh thornMesh, seedMesh, patchMesh;
    private static Material inkMaterial;
    private static int users;
    private bool ownsReference;

    private static readonly Color Ink = new Color32(60, 34, 34, 255);
    private static readonly Color Thorn = new Color32(248, 170, 107, 255);
    private static readonly Color Light = new Color32(255, 222, 158, 255);
    private static readonly Color Berry = new Color32(184, 67, 89, 255);
    private static readonly Color Leaf = new Color32(97, 118, 64, 255);
    private static readonly Color BrambleInk = new Color32(48, 54, 39, 255);
    private static readonly Color BrambleGreen = new Color32(63, 119, 48, 255);
    private static readonly Color BrambleLight = new Color32(118, 170, 69, 255);
    private static readonly Color BrambleThorn = new Color32(115, 35, 58, 255);
    private static readonly Color BrambleTip = new Color32(219, 91, 122, 255);

    public static Transform Attach(Transform parent, DoodlikHazard.Kind type)
    {
        if (type == DoodlikHazard.Kind.Thorns || type == DoodlikHazard.Kind.VerticalThorns)
        {
            Sprite branch = Resources.Load<Sprite>("Hazards/ThornBranch");
            if (branch != null)
            {
                GameObject spriteArt = new GameObject("One-piece thorn branch",
                    typeof(SpriteRenderer));
                spriteArt.transform.SetParent(parent, false);
                SpriteRenderer spriteRenderer = spriteArt.GetComponent<SpriteRenderer>();
                spriteRenderer.sprite = branch;
                spriteRenderer.sortingOrder = 3;
                return spriteArt.transform;
            }
        }
        if (inkMaterial == null)
        {
            Shader shader = Resources.Load<Shader>("HazardInk");
            inkMaterial = new Material(shader) { name = "Hazard ink (shared)" };
        }
        if (thornMesh == null) thornMesh = BuildThorns();
        if (seedMesh == null) seedMesh = BuildSeed();
        if (patchMesh == null) patchMesh = BuildPatch();
        GameObject art = new GameObject("Hand drawn hazard", typeof(MeshFilter), typeof(MeshRenderer));
        art.transform.SetParent(parent, false);
        art.GetComponent<MeshFilter>().sharedMesh =
            type == DoodlikHazard.Kind.Thorns || type == DoodlikHazard.Kind.VerticalThorns
            ? thornMesh : type == DoodlikHazard.Kind.ThornPatch ? patchMesh : seedMesh;
        MeshRenderer renderer = art.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = inkMaterial;
        renderer.sortingOrder = 3;
        art.AddComponent<HazardDrawing>().ownsReference = true;
        users++;
        return art.transform;
    }

    private void OnDestroy()
    {
        if (!ownsReference || --users > 0) return;
        Release(thornMesh);
        Release(seedMesh);
        Release(patchMesh);
        Release(inkMaterial);
        thornMesh = seedMesh = patchMesh = null;
        inkMaterial = null;
        users = 0;
    }

    private static void Release(Object resource)
    {
        if (Application.isPlaying) Destroy(resource);
        else DestroyImmediate(resource);
    }

    private static Mesh BuildThorns()
    {
        InkMesh mesh = new InkMesh();
        // A dark, flowering bramble belongs to the meadow palette, while its
        // uneven burgundy barbs read as dangerous from above and below.
        for (int i = 0; i < 6; i++)
        {
            float x = -0.35f + i * 0.14f;
            float topX = x + (i % 2 == 0 ? -0.025f : 0.032f);
            float bottomX = x + (i % 2 == 0 ? 0.038f : -0.022f);
            float topHeight = i % 3 == 0 ? 0.205f : 0.16f;
            float bottomHeight = i % 3 == 1 ? 0.19f : 0.145f;
            DrawBrambleThorn(mesh, x, topX, topHeight, 1f);
            DrawBrambleThorn(mesh, x + 0.06f, bottomX, bottomHeight, -1f);
        }
        mesh.Polygon(new[] { new Vector2(-0.39f, 0.02f), new Vector2(-0.495f, 0.11f),
            new Vector2(-0.455f, -0.055f) }, BrambleInk);
        mesh.Polygon(new[] { new Vector2(-0.402f, 0.01f), new Vector2(-0.48f, 0.082f),
            new Vector2(-0.452f, -0.031f) }, BrambleThorn);
        mesh.Polygon(new[] { new Vector2(0.395f, 0.01f), new Vector2(0.495f, -0.105f),
            new Vector2(0.445f, 0.058f) }, BrambleInk);
        mesh.Polygon(new[] { new Vector2(0.405f, 0.008f), new Vector2(0.48f, -0.077f),
            new Vector2(0.446f, 0.035f) }, BrambleThorn);

        mesh.Line(new Vector2(-0.44f, -0.024f), new Vector2(-0.14f, 0.018f),
            0.126f, BrambleInk);
        mesh.Line(new Vector2(-0.14f, 0.018f), new Vector2(0.17f, -0.018f),
            0.13f, BrambleInk);
        mesh.Line(new Vector2(0.17f, -0.018f), new Vector2(0.44f, 0.025f),
            0.12f, BrambleInk);
        mesh.Line(new Vector2(-0.44f, -0.024f), new Vector2(-0.14f, 0.018f),
            0.091f, BrambleGreen);
        mesh.Line(new Vector2(-0.14f, 0.018f), new Vector2(0.17f, -0.018f),
            0.094f, BrambleGreen);
        mesh.Line(new Vector2(0.17f, -0.018f), new Vector2(0.44f, 0.025f),
            0.084f, BrambleGreen);
        mesh.Line(new Vector2(-0.39f, -0.007f), new Vector2(-0.17f, 0.035f),
            0.017f, BrambleLight);
        mesh.Line(new Vector2(-0.08f, 0.035f), new Vector2(0.12f, 0.005f),
            0.014f, BrambleLight);
        mesh.Line(new Vector2(0.23f, 0.001f), new Vector2(0.4f, 0.04f),
            0.014f, BrambleLight);

        DrawBrambleLeaf(mesh, new Vector2(-0.24f, 0.02f), new Vector2(-0.3f, 0.115f));
        DrawBrambleLeaf(mesh, new Vector2(0.28f, -0.015f), new Vector2(0.34f, -0.12f));
        DrawBrambleFlower(mesh, new Vector2(-0.065f, 0.018f));
        DrawBrambleFlower(mesh, new Vector2(0.3f, 0.028f));
        return mesh.Create("Doodlik flowering thorn bramble");
    }

    private static void DrawBrambleThorn(InkMesh mesh, float baseX,
        float tipX, float height, float side)
    {
        mesh.Polygon(new[] {
            new Vector2(baseX - 0.044f, side * 0.035f),
            new Vector2(tipX, side * height),
            new Vector2(baseX + 0.048f, side * 0.035f)
        }, BrambleInk);
        mesh.Polygon(new[] {
            new Vector2(baseX - 0.023f, side * 0.06f),
            new Vector2(tipX, side * (height - 0.018f)),
            new Vector2(baseX + 0.025f, side * 0.06f)
        }, BrambleThorn);
        mesh.Line(new Vector2(baseX - 0.008f, side * 0.09f),
            new Vector2(tipX, side * (height - 0.04f)), 0.01f, BrambleTip);
    }

    private static void DrawBrambleLeaf(InkMesh mesh, Vector2 basePoint, Vector2 tip)
    {
        Vector2 across = new Vector2(tip.y - basePoint.y,
            basePoint.x - tip.x).normalized * 0.042f;
        mesh.Polygon(new[] { basePoint - across, tip,
            basePoint + across }, BrambleInk);
        mesh.Polygon(new[] { basePoint - across * 0.65f, tip * 0.78f + basePoint * 0.22f,
            basePoint + across * 0.65f }, BrambleLight);
    }

    private static void DrawBrambleFlower(InkMesh mesh, Vector2 center)
    {
        for (int i = 0; i < 5; i++)
        {
            float angle = i * Mathf.PI * 2f / 5f;
            Vector2 petal = center + new Vector2(Mathf.Cos(angle),
                Mathf.Sin(angle)) * 0.034f;
            mesh.Oval(petal, new Vector2(0.025f, 0.024f), BrambleInk);
            mesh.Oval(petal, new Vector2(0.018f, 0.017f), BrambleTip);
        }
        mesh.Oval(center, new Vector2(0.021f, 0.021f), BrambleInk);
        mesh.Oval(center, new Vector2(0.013f, 0.013f), Light);
    }

    private static Mesh BuildPatch()
    {
        InkMesh mesh = new InkMesh();
        // A compact hooked offshoot, not a carpet of spikes: its silhouette
        // stays readable on the bright flower platform at phone resolution.
        mesh.Line(new Vector2(-0.47f, -0.09f), new Vector2(-0.14f, -0.045f),
            0.18f, Ink);
        mesh.Line(new Vector2(-0.14f, -0.045f), new Vector2(0.46f, -0.07f),
            0.18f, Ink);
        mesh.Line(new Vector2(-0.46f, -0.09f), new Vector2(-0.14f, -0.045f),
            0.12f, BrambleGreen);
        mesh.Line(new Vector2(-0.14f, -0.045f), new Vector2(0.45f, -0.07f),
            0.12f, BrambleGreen);
        mesh.Line(new Vector2(-0.4f, -0.06f), new Vector2(0.35f, -0.043f),
            0.016f, BrambleLight);
        DrawPatchSpike(mesh, -0.29f, -0.34f, 0.57f);
        DrawPatchSpike(mesh, 0f, 0.025f, 0.74f);
        DrawPatchSpike(mesh, 0.29f, 0.36f, 0.61f);
        DrawBrambleLeaf(mesh, new Vector2(-0.4f, -0.07f),
            new Vector2(-0.49f, 0.07f));
        DrawBrambleFlower(mesh, new Vector2(0.42f, -0.055f));
        return mesh.Create("Doodlik platform thorn patch");
    }

    private static void DrawPatchSpike(InkMesh mesh, float baseX, float tipX, float height)
    {
        mesh.Polygon(new[] {
            new Vector2(baseX - 0.125f, -0.03f),
            new Vector2(tipX, height),
            new Vector2(baseX + 0.13f, -0.03f)
        }, BrambleInk);
        mesh.Polygon(new[] {
            new Vector2(baseX - 0.088f, 0.018f),
            new Vector2(tipX, height - 0.05f),
            new Vector2(baseX + 0.092f, 0.018f)
        }, BrambleThorn);
        mesh.Polygon(new[] {
            new Vector2(tipX - 0.033f, height - 0.14f),
            new Vector2(tipX, height - 0.05f),
            new Vector2(tipX + 0.033f, height - 0.14f)
        }, Light);
    }

    private static Mesh BuildSeed()
    {
        InkMesh mesh = new InkMesh();
        for (int i = 0; i < 10; i++)
        {
            float angle = i * Mathf.PI * 0.2f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 across = new Vector2(-dir.y, dir.x);
            Vector2 a = dir * 0.22f - across * 0.09f;
            Vector2 b = dir * (i % 2 == 0 ? 0.42f : 0.39f) + across * 0.015f;
            Vector2 c = dir * 0.22f + across * 0.09f;
            mesh.Polygon(new[] { a, b, c }, Ink);
            mesh.Polygon(new[] { dir * 0.25f - across * 0.04f,
                b - dir * 0.075f, dir * 0.25f + across * 0.04f }, Thorn);
        }
        mesh.Oval(Vector2.zero, new Vector2(0.29f, 0.285f), Ink);
        mesh.Oval(new Vector2(0f, 0.008f), new Vector2(0.252f, 0.248f), Berry);
        mesh.Oval(new Vector2(-0.115f, 0.13f), new Vector2(0.055f, 0.04f), new Color32(235, 127, 119, 255));
        mesh.Oval(new Vector2(-0.087f, 0.01f), new Vector2(0.033f, 0.045f), Ink);
        mesh.Oval(new Vector2(0.087f, 0.01f), new Vector2(0.033f, 0.045f), Ink);
        mesh.Line(new Vector2(-0.15f, 0.105f), new Vector2(-0.045f, 0.065f), 0.026f, Ink);
        mesh.Line(new Vector2(0.15f, 0.105f), new Vector2(0.045f, 0.065f), 0.026f, Ink);
        mesh.Line(new Vector2(-0.055f, -0.11f), new Vector2(0f, -0.082f), 0.026f, Ink);
        mesh.Line(new Vector2(0f, -0.082f), new Vector2(0.06f, -0.11f), 0.026f, Ink);
        return mesh.Create("Doodlik flying burr");
    }

    private sealed class InkMesh
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();

        public void Polygon(Vector2[] points, Color color)
        {
            int start = vertices.Count;
            for (int i = 0; i < points.Length; i++)
            {
                vertices.Add(points[i]);
                colors.Add(color);
            }
            for (int i = 1; i < points.Length - 1; i++)
            {
                triangles.Add(start);
                triangles.Add(start + i);
                triangles.Add(start + i + 1);
            }
        }

        public void Line(Vector2 from, Vector2 to, float width, Color color)
        {
            Vector2 direction = (to - from).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) * width * 0.5f;
            Polygon(new[] { from - normal, from + normal, to + normal, to - normal }, color);
        }

        public void Oval(Vector2 center, Vector2 radius, Color color)
        {
            Vector2[] points = new Vector2[18];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / points.Length;
                float wobble = 1f + 0.025f * Mathf.Sin(i * 2.3f);
                points[i] = center + new Vector2(Mathf.Cos(angle) * radius.x,
                    Mathf.Sin(angle) * radius.y) * wobble;
            }
            Polygon(points, color);
        }

        public Mesh Create(string name)
        {
            Mesh mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
