using System.Collections.Generic;
using UnityEngine;

// Shared paper-cut silhouettes: collision deliberately excludes the soft wings,
// antennae and tell arrow. No art construction consumes gameplay randomness.
public static class ExpeditionEnemyArt
{
    private static readonly Color32 Ink = new Color32(75, 51, 37, 255);
    private static readonly Color32 Paper = new Color32(255, 242, 202, 255);
    private static Mesh calm, warning, hurt, arrow, twoHearts, oneHeart;
    public static Mesh Body => calm != null ? calm : calm = BuildBody(false, false);
    public static Mesh Warning => warning != null ? warning : warning = BuildBody(true, false);
    public static Mesh Hurt => hurt != null ? hurt : hurt = BuildBody(false, true);
    public static Mesh Arrow => arrow != null ? arrow : arrow = BuildArrow();
    public static Mesh Health(int hp) => hp > 1 ? twoHearts != null ? twoHearts : twoHearts = BuildHealth(2) :
        oneHeart != null ? oneHeart : oneHeart = BuildHealth(1);

    private static Mesh BuildBody(bool windup, bool damaged)
    {
        var p = new Painter();
        Color32 wing = new Color32(235, 233, 207, 225);
        p.Outline(wing, new Vector2(-.19f, .10f), new Vector2(-.48f, .24f), new Vector2(-.51f, .06f), new Vector2(-.28f, -.04f));
        p.Outline(wing, new Vector2(.19f, .10f), new Vector2(.48f, .24f), new Vector2(.51f, .06f), new Vector2(.28f, -.04f));
        p.Stroke(new Vector2(-.26f, .07f), new Vector2(-.44f, .16f), .014f, Paper);
        p.Stroke(new Vector2(.26f, .07f), new Vector2(.44f, .16f), .014f, Paper);
        p.Stroke(new Vector2(-.12f, .15f), new Vector2(-.19f, .32f), .026f, Ink);
        p.Stroke(new Vector2(.12f, .15f), new Vector2(.19f, .32f), .026f, Ink);
        Color32 shell = damaged ? new Color32(164, 162, 121, 255) : windup ? new Color32(232, 169, 70, 255) : new Color32(92, 145, 144, 255);
        p.Outline(shell, new Vector2(-.12f, .23f), new Vector2(.12f, .23f), new Vector2(.29f, .10f),
            new Vector2(.30f, -.10f), new Vector2(.15f, -.25f), new Vector2(-.15f, -.25f), new Vector2(-.30f, -.10f), new Vector2(-.29f, .10f));
        p.Polygon(new Color32(130, 178, 169, 255), new Vector2(-.16f, .16f), new Vector2(-.22f, -.08f), new Vector2(-.08f, -.18f), new Vector2(-.08f, .18f));
        p.Stroke(new Vector2(0f, -.02f), new Vector2(0f, -.21f), .020f, Ink);
        p.Polygon(Paper, new Vector2(-.20f, .07f), new Vector2(-.05f, .025f), new Vector2(-.08f, -.045f), new Vector2(-.20f, -.01f));
        p.Polygon(Paper, new Vector2(.20f, .07f), new Vector2(.05f, .025f), new Vector2(.08f, -.045f), new Vector2(.20f, -.01f));
        p.Stroke(new Vector2(-.105f, .025f), new Vector2(-.107f, -.018f), .030f, Ink);
        p.Stroke(new Vector2(.105f, .025f), new Vector2(.107f, -.018f), .030f, Ink);
        Color32 spark = new Color32(255, 210, 91, 255);
        p.Stroke(new Vector2(.025f, .18f), new Vector2(-.035f, .085f), .033f, spark);
        p.Stroke(new Vector2(-.035f, .085f), new Vector2(.035f, .085f), .033f, spark);
        p.Stroke(new Vector2(.035f, .085f), new Vector2(-.025f, .005f), .033f, spark);
        for (int side = -1; side <= 1; side += 2)
        for (int leg = 0; leg < 2; leg++)
            p.Stroke(new Vector2(side * .22f, -.10f - leg * .07f), new Vector2(side * (.35f - leg * .015f), -.15f - leg * .09f), .025f, Ink);
        return p.Finish("Storm beetle — " + (windup ? "windup" : damaged ? "wounded" : "patrol"));
    }

    private static Mesh BuildArrow()
    {
        var p = new Painter();
        Color32 amber = new Color32(255, 190, 65, 255);
        p.Outline(amber, new Vector2(-.16f, -.045f), new Vector2(.03f, -.045f), new Vector2(.03f, .045f), new Vector2(-.16f, .045f));
        p.Outline(amber, new Vector2(.02f, -.11f), new Vector2(.20f, 0f), new Vector2(.02f, .11f));
        return p.Finish("Storm beetle — dash warning");
    }

    private static Mesh BuildHealth(int hp)
    {
        var p = new Painter();
        for (int i = 0; i < 2; i++)
        {
            Vector2 c = new Vector2((i - .5f) * .14f, 0f);
            p.Outline(i < hp ? new Color32(244, 170, 91, 255) : new Color32(211, 204, 177, 255),
                c + Vector2.up * .055f, c + Vector2.right * .055f, c + Vector2.down * .055f, c + Vector2.left * .055f);
        }
        return p.Finish("Storm beetle — health " + hp);
    }

    private sealed class Painter
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> triangles = new List<int>();
        public void Polygon(Color32 color, params Vector2[] points)
        {
            int first = vertices.Count;
            foreach (Vector2 point in points) { vertices.Add(point); colors.Add(color); }
            for (int i = 1; i < points.Length - 1; i++) { triangles.Add(first); triangles.Add(first + i); triangles.Add(first + i + 1); }
        }
        public void Outline(Color32 fill, params Vector2[] points)
        {
            Polygon(Ink, points); Vector2 center = Vector2.zero;
            foreach (Vector2 point in points) center += point;
            center /= points.Length;
            var inner = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++) inner[i] = center + (points[i] - center) * .82f;
            Polygon(fill, inner);
        }
        public void Stroke(Vector2 a, Vector2 b, float width, Color32 color)
        {
            Vector2 side = new Vector2(a.y - b.y, b.x - a.x).normalized * width * .5f;
            Polygon(color, a - side, a + side, b + side, b - side);
        }
        public Mesh Finish(string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); return mesh;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        foreach (Mesh mesh in new[] { calm, warning, hurt, arrow, twoHearts, oneHeart })
            if (mesh != null) { if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh); }
        calm = warning = hurt = arrow = twoHearts = oneHeart = null;
    }
}
