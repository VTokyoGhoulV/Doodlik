using System.Collections.Generic;
using UnityEngine;

// Warm ink, folded paper and small crayon facets, matching the existing pickup
// meshes. These cached drawings contain no textures, physics or gameplay RNG.
public static class AdventureArt
{
    private static Mesh rocketIcon, goldenFish, rocket, bee, star;
    private static readonly Color32 Ink = new Color32(74, 46, 29, 255);
    private static readonly Color32 Paper = new Color32(255, 248, 220, 255);
    private static readonly Color32 Fold = new Color32(221, 216, 181, 255);
    private static readonly Color32 Orange = new Color32(234, 127, 50, 255);
    private static readonly Color32 Gold = new Color32(255, 202, 65, 255);

    public static Mesh PickupIcon(int kind) => kind == 4
        ? rocketIcon != null ? rocketIcon : rocketIcon = BuildRocketIcon()
        : kind == 5 ? goldenFish != null ? goldenFish : goldenFish = BuildGoldenFish() : null;
    public static Mesh RocketBody => rocket != null ? rocket : rocket = BuildRocket();
    public static Mesh BeeBody => bee != null ? bee : bee = BuildBee();
    public static Mesh Star => star != null ? star : star = BuildStar();

    // Only the dense body, not decorative wings, feet, antennae or motion lines.
    // The bee faces local +X; callers mirror this centre with the visible mesh.
    public static Rect BeeDamageRect => new Rect(-0.28f, -0.16f, 0.58f, 0.31f);
    public const float RocketSeatY = 0.10f;

    private static Mesh BuildRocketIcon()
    {
        var p = new Painter();
        Medallion(p, new Color32(191, 220, 221, 255));
        p.Polygon(new Color32(255, 239, 183, 190), new Vector2(-.17f, -.11f), new Vector2(-.13f, -.17f), new Vector2(-.03f, -.06f));
        p.Stroke(new Vector2(-.14f, -.12f), new Vector2(-.09f, -.07f), .018f, Orange);
        p.Outline(new[] { new Vector2(-.145f, -.065f), new Vector2(.13f, .145f),
            new Vector2(.09f, -.115f), new Vector2(-.035f, -.055f) }, Paper, .87f);
        p.Polygon(Fold, new Vector2(-.035f, -.045f), new Vector2(.113f, .12f), new Vector2(.075f, -.094f));
        p.Stroke(new Vector2(-.04f, -.04f), new Vector2(.106f, .118f), .010f, Ink);
        p.Outline(new[] { new Vector2(-.12f, -.05f), new Vector2(-.145f, .02f), new Vector2(-.045f, .008f) }, Orange, .75f);
        p.Outline(new[] { new Vector2(.015f, -.055f), new Vector2(.034f, -.135f), new Vector2(.085f, -.103f) }, Orange, .75f);
        p.Stroke(new Vector2(-.15f, .11f), new Vector2(-.15f, .15f), .01f, Paper);
        p.Stroke(new Vector2(-.17f, .13f), new Vector2(-.13f, .13f), .01f, Paper);
        return p.Finish("Pickup 4 — folded paper rocket");
    }

    private static Mesh BuildGoldenFish()
    {
        var p = new Painter();
        Medallion(p, new Color32(255, 226, 136, 255));
        p.Oval(new Vector2(-.018f, -.016f), new Vector2(.162f, .133f), new Color32(255, 246, 193, 100), 12);
        p.Outline(new[] { new Vector2(.075f, -.015f), new Vector2(.176f, .072f),
            new Vector2(.158f, -.095f) }, Orange, .76f);
        p.Outline(new[] { new Vector2(-.163f, -.01f), new Vector2(-.083f, .080f), new Vector2(.025f, .071f),
            new Vector2(.095f, -.008f), new Vector2(.014f, -.087f), new Vector2(-.095f, -.079f) }, Gold, .86f);
        p.Polygon(new Color32(255, 231, 125, 255), new Vector2(-.135f, .005f), new Vector2(-.074f, .055f),
            new Vector2(.008f, .052f), new Vector2(.048f, .006f));
        p.Polygon(Orange, new Vector2(-.01f, -.03f), new Vector2(.045f, -.015f), new Vector2(.011f, -.066f));
        p.Stroke(new Vector2(-.035f, .045f), new Vector2(-.048f, -.052f), .010f, new Color32(170, 96, 35, 255));
        p.Oval(new Vector2(-.109f, .008f), Vector2.one * .017f, Ink, 8);
        p.Oval(new Vector2(-.113f, .014f), Vector2.one * .0055f, Paper, 6);
        p.Outline(new[] { new Vector2(-.085f, .096f), new Vector2(-.101f, .147f), new Vector2(-.054f, .125f),
            new Vector2(-.025f, .175f), new Vector2(.004f, .122f), new Vector2(.043f, .151f), new Vector2(.027f, .095f) }, Gold, .78f);
        p.Stroke(new Vector2(-.074f, .108f), new Vector2(.020f, .108f), .009f, Paper);
        p.Spark(new Vector2(.126f, .127f), .026f, Paper);
        p.Spark(new Vector2(-.12f, -.138f), .020f, Paper);
        return p.Finish("Pickup 5 — crowned golden fish");
    }

    private static void Medallion(Painter p, Color32 fill)
    {
        // Exactly the old pickups' 0.23 radius and sixteen-sided outline.
        p.Oval(Vector2.zero, Vector2.one * .23f, Ink, 16);
        p.Oval(Vector2.zero, Vector2.one * .202f, fill, 16);
        p.Stroke(new Vector2(-.147f, .117f), new Vector2(-.105f, .158f), .009f, new Color32(255, 250, 220, 200));
        p.Stroke(new Vector2(.087f, -.168f), new Vector2(.132f, -.135f), .007f, new Color32(164, 115, 53, 95));
    }

    private static Mesh BuildRocket()
    {
        var p = new Painter();
        // A compact paper dart, not a metal spaceship enclosing the cat.
        p.Outline(new[] { new Vector2(-.45f, .074f), new Vector2(-.50f, .220f), new Vector2(-.235f, .115f) }, Orange, .82f);
        p.Outline(new[] { new Vector2(-.47f, .090f), new Vector2(.50f, .225f),
            new Vector2(.252f, -.115f), new Vector2(-.370f, -.140f) }, Paper, .94f);
        p.Polygon(new Color32(249, 240, 207, 255), new Vector2(-.43f, .075f), new Vector2(.465f, .202f),
            new Vector2(-.055f, -.066f), new Vector2(-.34f, -.120f));
        p.Polygon(Fold, new Vector2(-.055f, -.066f), new Vector2(.465f, .202f), new Vector2(.233f, -.098f));
        p.Stroke(new Vector2(-.055f, -.065f), new Vector2(.465f, .202f), .013f, Ink);
        p.Stroke(new Vector2(-.32f, -.111f), new Vector2(-.055f, -.065f), .010f, new Color32(145, 121, 83, 230));
        p.Outline(new[] { new Vector2(-.065f, -.060f), new Vector2(-.150f, -.225f), new Vector2(.190f, -.115f) }, Orange, .84f);
        p.Stroke(new Vector2(-.115f, -.180f), new Vector2(.09f, -.118f), .009f, new Color32(255, 210, 132, 255));
        for (int i = 0; i < 6; i++)
        {
            float x = -.31f + i * .046f;
            p.Stroke(new Vector2(x, -.052f), new Vector2(x + .020f, -.008f), .0045f,
                new Color32(146, 116, 76, 85));
        }
        p.Stroke(new Vector2(.19f, .148f), new Vector2(.39f, .178f), .005f, new Color32(255, 255, 244, 255));
        return p.Finish("Paper rocket — warm folds and orange fins");
    }

    private static Mesh BuildBee()
    {
        var p = new Painter();
        Color32 fur = new Color32(247, 183, 58, 255), dark = new Color32(87, 57, 37, 255);
        // Wings sit behind the body. Their pale paper facets never count as hit area.
        p.Outline(new[] { new Vector2(-.15f, .080f), new Vector2(-.29f, .230f), new Vector2(-.24f, .290f),
            new Vector2(-.10f, .245f), new Vector2(.025f, .080f) }, new Color32(225, 242, 226, 235), .88f);
        p.Outline(new[] { new Vector2(-.015f, .080f), new Vector2(.02f, .255f), new Vector2(.11f, .280f),
            new Vector2(.17f, .220f), new Vector2(.11f, .071f) }, new Color32(247, 248, 225, 245), .87f);
        p.Stroke(new Vector2(-.14f, .115f), new Vector2(-.225f, .235f), .008f, new Color32(159, 187, 173, 200));
        p.Stroke(new Vector2(.042f, .110f), new Vector2(.075f, .232f), .008f, new Color32(159, 187, 173, 200));
        // Two short rear legs and a tail point: all deliberately outside damage.
        p.Stroke(new Vector2(-.16f, -.11f), new Vector2(-.22f, -.218f), .021f, Ink);
        p.Stroke(new Vector2(-.22f, -.218f), new Vector2(-.16f, -.237f), .019f, Ink);
        p.Stroke(new Vector2(.055f, -.12f), new Vector2(.08f, -.215f), .021f, Ink);
        p.Stroke(new Vector2(.08f, -.215f), new Vector2(.14f, -.220f), .019f, Ink);
        p.Polygon(Ink, new Vector2(-.28f, -.01f), new Vector2(-.395f, -.065f), new Vector2(-.29f, -.088f));
        p.Oval(new Vector2(-.045f, -.015f), new Vector2(.285f, .177f), Ink, 14);
        p.Oval(new Vector2(-.045f, -.012f), new Vector2(.260f, .150f), fur, 14);
        // Warm ink bands taper with the abdomen, rather than floating rectangles.
        p.Polygon(dark, new Vector2(-.212f, .110f), new Vector2(-.148f, .135f), new Vector2(-.135f, -.156f), new Vector2(-.205f, -.129f));
        p.Polygon(dark, new Vector2(-.075f, .137f), new Vector2(-.012f, .135f), new Vector2(.008f, -.153f), new Vector2(-.063f, -.162f));
        p.Oval(new Vector2(.205f, .0f), new Vector2(.153f, .152f), Ink, 12);
        p.Oval(new Vector2(.204f, .002f), new Vector2(.128f, .125f), fur, 12);
        p.Stroke(new Vector2(.150f, .117f), new Vector2(.115f, .220f), .016f, Ink);
        p.Stroke(new Vector2(.115f, .220f), new Vector2(.155f, .241f), .014f, Ink);
        p.Stroke(new Vector2(.250f, .118f), new Vector2(.286f, .208f), .016f, Ink);
        p.Oval(new Vector2(.288f, .213f), Vector2.one * .024f, Ink, 8);
        // White eye triangles, ink pupils and sloping brows make intent readable.
        p.Polygon(Paper, new Vector2(.114f, .067f), new Vector2(.185f, .039f), new Vector2(.155f, -.005f));
        p.Polygon(Paper, new Vector2(.209f, .038f), new Vector2(.283f, .069f), new Vector2(.267f, -.002f));
        p.Oval(new Vector2(.164f, .026f), new Vector2(.012f, .023f), Ink, 7);
        p.Oval(new Vector2(.239f, .026f), new Vector2(.012f, .023f), Ink, 7);
        p.Stroke(new Vector2(.106f, .079f), new Vector2(.189f, .047f), .017f, Ink);
        p.Stroke(new Vector2(.203f, .047f), new Vector2(.286f, .079f), .017f, Ink);
        p.Stroke(new Vector2(.195f, -.074f), new Vector2(.255f, -.063f), .013f, Ink);
        for (int i = 0; i < 8; i++)
        {
            float x = -.274f + .033f * i;
            p.Stroke(new Vector2(x, .075f), new Vector2(x + .010f, .102f), .0045f, new Color32(255, 233, 161, 150));
        }
        return p.Finish("Angry paper bumblebee — harmless wings, dense body");
    }

    private static Mesh BuildStar()
    {
        var p = new Painter();
        p.Spark(Vector2.zero, .5f, Ink);
        p.Spark(Vector2.zero, .34f, Paper);
        p.Polygon(Gold, Vector2.zero, new Vector2(.34f, 0), new Vector2(.09f, -.09f), new Vector2(0, -.34f));
        p.Stroke(new Vector2(-.04f, .13f), new Vector2(-.03f, .25f), .028f, new Color32(255, 255, 244, 255));
        return p.Finish("Folded paper star mote");
    }

    private sealed class Painter
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> triangles = new List<int>();
        public void Polygon(Color32 color, params Vector2[] points)
        {
            int first = vertices.Count;
            foreach (Vector2 point in points) Add(point, color);
            // The crown / paper darts are concave. A centre fan would fill in
            // their notches; triangulate the real outline once, during caching.
            var remaining = new List<int>();
            float area = 0;
            for (int i = 0; i < points.Length; i++)
            { remaining.Add(i); area += Cross(points[i], points[(i + 1) % points.Length]); }
            float winding = area >= 0 ? 1f : -1f;
            int guard = points.Length * points.Length;
            while (remaining.Count > 3 && guard-- > 0)
            {
                bool cut = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int a = remaining[(i + remaining.Count - 1) % remaining.Count], b = remaining[i], c = remaining[(i + 1) % remaining.Count];
                    if (Cross(points[b] - points[a], points[c] - points[b]) * winding <= .00000001f) continue;
                    bool occupied = false;
                    foreach (int point in remaining)
                    {
                        if (point == a || point == b || point == c) continue;
                        Vector2 v = points[point];
                        if (Cross(points[b] - points[a], v - points[a]) * winding >= -.00000001f &&
                            Cross(points[c] - points[b], v - points[b]) * winding >= -.00000001f &&
                            Cross(points[a] - points[c], v - points[c]) * winding >= -.00000001f) { occupied = true; break; }
                    }
                    if (occupied) continue;
                    triangles.Add(first + a); triangles.Add(first + b); triangles.Add(first + c);
                    remaining.RemoveAt(i); cut = true; break;
                }
                if (!cut) throw new System.InvalidOperationException("Adventure artwork contains a self-intersecting polygon.");
            }
            if (remaining.Count == 3)
            { triangles.Add(first + remaining[0]); triangles.Add(first + remaining[1]); triangles.Add(first + remaining[2]); }
        }
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        public void Outline(Vector2[] points, Color32 fill, float innerScale)
        {
            Polygon(Ink, points);
            Vector2 center = Vector2.zero;
            foreach (Vector2 point in points) center += point;
            center /= points.Length;
            Vector2[] inner = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++) inner[i] = center + (points[i] - center) * innerScale;
            Polygon(fill, inner);
        }
        public void Oval(Vector2 center, Vector2 radius, Color32 color, int segments)
        {
            Vector2[] points = new Vector2[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                points[i] = center + Vector2.Scale(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), radius);
            }
            Polygon(color, points);
        }
        public void Stroke(Vector2 from, Vector2 to, float width, Color32 color)
        {
            Vector2 side = new Vector2(from.y - to.y, to.x - from.x).normalized * width * .5f;
            Polygon(color, from - side, from + side, to + side, to - side);
        }
        public void Spark(Vector2 center, float radius, Color32 color)
        {
            Vector2[] points = new Vector2[8];
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * .25f;
                float length = i % 2 == 0 ? radius : radius * .26f;
                points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length;
            }
            Polygon(color, points);
        }
        private void Add(Vector2 point, Color32 color) { vertices.Add(point); colors.Add(color); }
        public Mesh Finish(string name)
        {
            Mesh mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            return mesh;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Release(rocketIcon); Release(goldenFish); Release(rocket); Release(bee); Release(star);
        rocketIcon = goldenFish = rocket = bee = star = null;
    }
    private static void Release(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
    }
}
