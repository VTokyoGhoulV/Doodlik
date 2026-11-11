using System.Collections.Generic;
using UnityEngine;

// The world pickups and bag UI share the very same warm-ink drawings.
// No textures, physics or gameplay randomness are used to build the meshes.
public static class ExpeditionArt
{
    private static readonly Color32 Ink = new Color32(83, 54, 34, 255);
    private static readonly Color32 Paper = new Color32(255, 246, 212, 255);
    private static Mesh jar, wool, shard, sparks;

    public static Mesh FindMesh(ExpeditionFindKind kind)
    {
        switch (kind)
        {
            case ExpeditionFindKind.FireflyJar: return jar != null ? jar : jar = BuildJar();
            case ExpeditionFindKind.CloudWool: return wool != null ? wool : wool = BuildWool();
            case ExpeditionFindKind.StarShard: return shard != null ? shard : shard = BuildShard();
            case ExpeditionFindKind.SparkDust: return sparks != null ? sparks : sparks = BuildSparks();
            default: return null;
        }
    }

    private static Mesh BuildJar()
    {
        var p = new Painter();
        p.Outline(new[] { new Vector2(-.13f, .15f), new Vector2(.13f, .15f),
            new Vector2(.18f, .09f), new Vector2(.165f, -.17f), new Vector2(.10f, -.22f),
            new Vector2(-.10f, -.22f), new Vector2(-.165f, -.17f), new Vector2(-.18f, .09f) },
            new Color32(174, 213, 177, 255), .85f);
        p.Polygon(new Color32(206, 234, 191, 255), new Vector2(-.13f, .06f),
            new Vector2(-.10f, -.15f), new Vector2(-.06f, -.17f), new Vector2(-.08f, .07f));
        p.Outline(new[] { new Vector2(-.13f, .145f), new Vector2(-.13f, .215f),
            new Vector2(.13f, .215f), new Vector2(.13f, .145f) }, new Color32(207, 146, 69, 255), .75f);
        p.Stroke(new Vector2(-.085f, .177f), new Vector2(.09f, .177f), .013f, Paper);
        foreach (Vector2 point in new[] { new Vector2(-.055f, .045f), new Vector2(.065f, -.045f), new Vector2(-.035f, -.115f) })
        {
            p.Oval(point, Vector2.one * .039f, new Color32(245, 220, 111, 255), 8);
            p.Diamond(point, .017f, Paper);
            p.Stroke(point + new Vector2(-.025f, .012f), point + new Vector2(-.04f, .023f), .009f, Ink);
            p.Stroke(point + new Vector2(.025f, .012f), point + new Vector2(.04f, .023f), .009f, Ink);
        }
        return p.Finish("Expedition find — jar of fireflies");
    }

    private static Mesh BuildWool()
    {
        var p = new Painter();
        Vector2[] centers = { new Vector2(-.11f, -.015f), new Vector2(0f, .055f), new Vector2(.11f, -.015f) };
        foreach (Vector2 point in centers) p.Oval(point, new Vector2(.12f, .115f), Ink, 12);
        foreach (Vector2 point in centers) p.Oval(point, new Vector2(.096f, .09f), Paper, 12);
        p.Oval(new Vector2(0f, -.045f), new Vector2(.14f, .08f), Paper, 12);
        Color32 fold = new Color32(168, 179, 164, 255);
        p.Stroke(new Vector2(-.135f, -.018f), new Vector2(-.08f, .038f), .013f, fold);
        p.Stroke(new Vector2(-.08f, .038f), new Vector2(-.02f, .023f), .013f, fold);
        p.Stroke(new Vector2(.02f, .083f), new Vector2(.067f, .04f), .013f, fold);
        p.Stroke(new Vector2(.067f, .04f), new Vector2(.048f, -.016f), .013f, fold);
        p.Outline(new[] { new Vector2(-.12f, -.10f), new Vector2(.12f, -.10f),
            new Vector2(.105f, -.17f), new Vector2(-.10f, -.17f) }, new Color32(222, 139, 66, 255), .70f);
        p.Stroke(new Vector2(-.015f, -.11f), new Vector2(.025f, -.16f), .012f, Paper);
        return p.Finish("Expedition find — tied cloud wool");
    }

    private static Mesh BuildShard()
    {
        var p = new Painter();
        p.Outline(new[] { new Vector2(0f, .23f), new Vector2(.19f, .03f),
            new Vector2(.08f, -.23f), new Vector2(-.12f, -.18f), new Vector2(-.19f, .03f) },
            new Color32(164, 192, 225, 255), .82f);
        p.Polygon(new Color32(231, 233, 248, 255), new Vector2(0f, .18f), new Vector2(-.145f, .025f), Vector2.zero);
        p.Polygon(new Color32(200, 162, 210, 255), Vector2.zero, new Vector2(.06f, -.18f), new Vector2(.145f, .025f));
        p.Stroke(new Vector2(0f, .18f), new Vector2(0f, -.005f), .014f, Paper);
        p.Stroke(new Vector2(0f, -.005f), new Vector2(-.085f, -.135f), .014f, Paper);
        p.Diamond(new Vector2(.065f, .095f), .035f, new Color32(255, 218, 106, 255));
        p.Diamond(new Vector2(.065f, .095f), .014f, Paper);
        return p.Finish("Expedition find — folded star shard");
    }

    private static Mesh BuildSparks()
    {
        var p = new Painter();
        p.Outline(new[] { new Vector2(-.135f, .145f), new Vector2(.135f, .145f),
            new Vector2(.18f, .06f), new Vector2(.13f, -.16f), new Vector2(0f, -.205f),
            new Vector2(-.13f, -.16f), new Vector2(-.18f, .06f) }, new Color32(174, 220, 215, 255), .82f);
        p.Outline(new[] { new Vector2(-.12f, .14f), new Vector2(-.10f, .205f),
            new Vector2(.10f, .205f), new Vector2(.12f, .14f) }, new Color32(205, 153, 93, 255), .75f);
        p.Stroke(new Vector2(-.10f, .17f), new Vector2(.10f, .17f), .015f, Ink);
        p.Diamond(new Vector2(0f, .015f), .09f, Ink);
        p.Diamond(new Vector2(0f, .015f), .064f, new Color32(255, 203, 74, 255));
        p.Diamond(new Vector2(0f, .026f), .025f, Paper);
        p.Diamond(new Vector2(-.09f, -.09f), .025f, Paper);
        p.Diamond(new Vector2(.10f, -.07f), .022f, Paper);
        return p.Finish("Expedition find — spark dust pouch");
    }

    private sealed class Painter
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> triangles = new List<int>();
        // All the outlined silhouettes here are convex. Overlapping convex
        // facets keep the paper shapes simple and cannot fill a concave notch.
        public void Polygon(Color32 color, params Vector2[] points)
        {
            int start = vertices.Count;
            foreach (Vector2 point in points) { vertices.Add(point); colors.Add(color); }
            for (int i = 1; i < points.Length - 1; i++)
            { triangles.Add(start); triangles.Add(start + i); triangles.Add(start + i + 1); }
        }
        public void Outline(Vector2[] points, Color32 fill, float innerScale)
        {
            Polygon(Ink, points);
            Vector2 center = Vector2.zero;
            foreach (Vector2 point in points) center += point;
            center /= points.Length;
            Vector2[] inside = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++) inside[i] = center + (points[i] - center) * innerScale;
            Polygon(fill, inside);
        }
        public void Oval(Vector2 center, Vector2 radius, Color32 color, int segments)
        {
            Vector2[] points = new Vector2[segments];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / points.Length;
                points[i] = center + Vector2.Scale(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), radius);
            }
            Polygon(color, points);
        }
        public void Stroke(Vector2 from, Vector2 to, float width, Color32 color)
        {
            Vector2 side = new Vector2(from.y - to.y, to.x - from.x).normalized * width * .5f;
            Polygon(color, from - side, from + side, to + side, to - side);
        }
        public void Diamond(Vector2 center, float radius, Color32 color) => Polygon(color,
            center + Vector2.up * radius, center + Vector2.right * radius,
            center + Vector2.down * radius, center + Vector2.left * radius);
        public Mesh Finish(string name)
        {
            Mesh result = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            result.SetVertices(vertices); result.SetColors(colors); result.SetTriangles(triangles, 0); result.RecalculateBounds();
            return result;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Release(jar); Release(wool); Release(shard); Release(sparks); jar = wool = shard = sparks = null;
    }
    private static void Release(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
    }
}
