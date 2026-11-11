using System.Collections.Generic;
using UnityEngine;

// Tiny ink drawings shared by the wardrobe card and the world-space trail.
// Geometry is cached once; the runtime pool owns only each mote's fading colors.
public static class JumpTrailArt
{
    public sealed class Drawing
    {
        public readonly Vector3[] Vertices;
        public readonly Color32[] Colors;
        public readonly int[] Triangles;

        internal Drawing(List<Vector3> vertices, List<Color32> colors, List<int> triangles)
        { Vertices = vertices.ToArray(); Colors = colors.ToArray(); Triangles = triangles.ToArray(); }
    }

    private static Drawing stars, petals, fireflies, paws, meteor;
    private static readonly Color32 Ink = new Color32(102, 70, 48, 235);

    public static Drawing Get(string id)
    {
        switch (id)
        {
            case "trail_stardust": return stars ?? (stars = BuildStars());
            case "trail_blossom": return petals ?? (petals = BuildPetals());
            case "trail_fireflies": return fireflies ?? (fireflies = BuildFirefly());
            case "trail_ninelives": return paws ?? (paws = BuildPaw());
            case "trail_meteor": return meteor ?? (meteor = BuildMeteor());
            default: return null;
        }
    }

    private static Drawing BuildStars()
    {
        Painter paint = new Painter();
        Vector2[] points = new Vector2[10];
        for (int i = 0; i < points.Length; i++)
        {
            float angle = (90f + i * 36f) * Mathf.Deg2Rad;
            float radius = i % 2 == 0 ? 0.47f : 0.22f;
            points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        paint.Polygon(points, Ink);
        paint.Polygon(points, new Color32(255, 207, 95, 255), 0.78f);
        paint.Stroke(new Vector2(-0.11f, 0.12f), new Vector2(0.02f, 0.22f), 0.045f,
            new Color32(255, 241, 174, 255));
        return paint.Finish();
    }

    private static Drawing BuildPetals()
    {
        Painter paint = new Painter();
        // An asymmetric pointed petal, rather than a round particle recolor.
        Vector2[] petal = { new Vector2(-0.05f, -0.42f), new Vector2(-0.31f, -0.1f),
            new Vector2(-0.34f, 0.19f), new Vector2(-0.15f, 0.4f), new Vector2(0.12f, 0.44f),
            new Vector2(0.33f, 0.26f), new Vector2(0.29f, -0.03f), new Vector2(0.1f, -0.28f) };
        paint.Polygon(petal, new Color32(146, 84, 87, 230));
        paint.Polygon(petal, new Color32(237, 164, 178, 255), 0.82f);
        paint.Stroke(new Vector2(-0.025f, -0.22f), new Vector2(0.09f, 0.25f), 0.028f,
            new Color32(255, 219, 204, 255));
        return paint.Finish();
    }

    private static Drawing BuildFirefly()
    {
        Painter paint = new Painter();
        paint.Oval(new Vector2(0f, -0.14f), new Vector2(0.31f, 0.3f),
            new Color32(249, 210, 91, 48));
        paint.Oval(new Vector2(-0.21f, 0.1f), new Vector2(0.22f, 0.14f), Ink);
        paint.Oval(new Vector2(0.21f, 0.1f), new Vector2(0.22f, 0.14f), Ink);
        paint.Oval(new Vector2(-0.21f, 0.1f), new Vector2(0.175f, 0.095f),
            new Color32(249, 239, 198, 235));
        paint.Oval(new Vector2(0.21f, 0.1f), new Vector2(0.175f, 0.095f),
            new Color32(249, 239, 198, 235));
        paint.Oval(new Vector2(0f, -0.05f), new Vector2(0.145f, 0.28f), Ink);
        paint.Oval(new Vector2(0f, -0.14f), new Vector2(0.105f, 0.15f),
            new Color32(255, 218, 91, 255));
        paint.Oval(new Vector2(-0.025f, -0.13f), new Vector2(0.035f, 0.065f),
            new Color32(255, 249, 192, 255));
        return paint.Finish();
    }

    private static Drawing BuildPaw()
    {
        Painter paint = new Painter();
        Color32 pad = new Color32(210, 153, 102, 255);
        Vector2[] sole = { new Vector2(-0.29f, -0.31f), new Vector2(-0.32f, -0.12f),
            new Vector2(-0.15f, 0.08f), new Vector2(0.02f, 0.12f), new Vector2(0.21f, 0.01f),
            new Vector2(0.32f, -0.18f), new Vector2(0.25f, -0.36f), new Vector2(0.02f, -0.32f) };
        paint.Polygon(sole, Ink);
        paint.Polygon(sole, pad, 0.78f);
        Vector2[] toes = { new Vector2(-0.36f, 0.14f), new Vector2(-0.14f, 0.34f),
            new Vector2(0.13f, 0.36f), new Vector2(0.36f, 0.16f) };
        foreach (Vector2 toe in toes)
        {
            paint.Oval(toe, new Vector2(0.11f, 0.145f), Ink);
            paint.Oval(toe, new Vector2(0.075f, 0.105f), pad);
        }
        paint.Stroke(new Vector2(-0.15f, -0.15f), new Vector2(-0.06f, -0.07f), 0.035f,
            new Color32(255, 226, 168, 255));
        return paint.Finish();
    }

    private static Drawing BuildMeteor()
    {
        Painter paint = new Painter();
        Vector2[] flame = { new Vector2(-0.49f, 0.45f), new Vector2(0.21f, 0.19f),
            new Vector2(0.43f, -0.22f), new Vector2(0.13f, -0.39f), new Vector2(-0.19f, -0.11f) };
        paint.Polygon(flame, Ink);
        paint.Polygon(flame, new Color32(226, 129, 62, 255), 0.84f);
        paint.Polygon(new[] { new Vector2(-0.34f, 0.31f), new Vector2(0.14f, 0.08f),
            new Vector2(0.30f, -0.22f), new Vector2(0.04f, -0.24f) }, new Color32(255, 213, 105, 255));
        Vector2[] rock = { new Vector2(-0.01f, -0.03f), new Vector2(0.20f, 0.02f),
            new Vector2(0.39f, -0.13f), new Vector2(0.38f, -0.32f), new Vector2(0.18f, -0.45f),
            new Vector2(-0.02f, -0.36f), new Vector2(-0.10f, -0.18f) };
        paint.Polygon(rock, Ink);
        // Scale around the rock's centre instead of moving its outline.
        Vector2[] inset = new Vector2[rock.Length];
        Vector2 centre = new Vector2(0.15f, -0.20f);
        for (int i = 0; i < rock.Length; i++) inset[i] = centre + (rock[i] - centre) * 0.77f;
        paint.Polygon(inset, new Color32(181, 140, 108, 255));
        paint.Oval(new Vector2(0.23f, -0.16f), new Vector2(0.065f, 0.055f), new Color32(102, 70, 48, 255));
        paint.Oval(new Vector2(0.10f, -0.30f), new Vector2(0.045f, 0.035f), new Color32(119, 82, 57, 255));
        paint.Stroke(new Vector2(0.02f, -0.13f), new Vector2(0.10f, -0.07f), 0.035f,
            new Color32(255, 223, 156, 255));
        return paint.Finish();
    }

    private sealed class Painter
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> triangles = new List<int>();

        public void Polygon(Vector2[] points, Color32 color, float scale = 1f)
        {
            int first = vertices.Count;
            Vector2 center = Vector2.zero;
            foreach (Vector2 point in points) center += point;
            center /= points.Length;
            Add(center * scale, color);
            foreach (Vector2 point in points) Add(point * scale, color);
            for (int i = 0; i < points.Length; i++)
            {
                triangles.Add(first); triangles.Add(first + 1 + i);
                triangles.Add(first + 1 + (i + 1) % points.Length);
            }
        }

        public void Oval(Vector2 center, Vector2 radius, Color32 color)
        {
            Vector2[] points = new Vector2[10];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / points.Length;
                points[i] = center + Vector2.Scale(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), radius);
            }
            Polygon(points, color);
        }

        public void Stroke(Vector2 from, Vector2 to, float width, Color32 color)
        {
            Vector2 side = new Vector2(from.y - to.y, to.x - from.x).normalized * width * 0.5f;
            Polygon(new[] { from - side, from + side, to + side, to - side }, color);
        }

        private void Add(Vector2 point, Color32 color) { vertices.Add(point); colors.Add(color); }
        public Drawing Finish() => new Drawing(vertices, colors, triangles);
    }
}
