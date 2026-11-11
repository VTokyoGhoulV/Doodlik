using UnityEngine;

// Cached ink-and-paper illustrations for earned rewards. Both the wardrobe
// and world use these exact sprites and WardrobeArt's common fitting contract.
public static class ChallengeRewardArt
{
    private static readonly Sprite[] Sprites = new Sprite[3];
    private static readonly Texture2D[] Textures = new Texture2D[3];
    private static readonly Color32 Ink = new Color32(68, 49, 33, 255);
    private static readonly Color32 Paper = new Color32(255, 239, 192, 255);

    public static Sprite GetSprite(string id)
    {
        int index = id == "accessory_target" ? 0 : id == "accessory_patch" ? 1 : id == "clothing_explorer" ? 2 : -1;
        if (index < 0) return null;
        if (Sprites[index] != null) return Sprites[index];
        Painter drawing = index == 0 ? Target() : index == 1 ? Patch() : Explorer();
        Texture2D texture = drawing.Texture();
        texture.name = "Earned illustration " + id;
        Textures[index] = texture;
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        sprite.name = "Wardrobe " + id;
        sprite.hideFlags = HideFlags.DontSave;
        return Sprites[index] = sprite;
    }

    private static Painter Target()
    {
        Painter p = new Painter(256, 256);
        // A stitched little rosette, not a weapon or an oversized HUD reticle.
        p.Polygon(new[] { V(83, 80), V(76, 18), V(104, 36), V(123, 14), V(133, 84) }, new Color32(202, 119, 67, 255), Ink, 7f);
        p.Polygon(new[] { V(128, 82), V(143, 17), V(161, 40), V(188, 25), V(175, 98) }, new Color32(234, 171, 73, 255), Ink, 7f);
        p.Ellipse(V(128, 143), V(99, 99), Ink);
        p.Ellipse(V(128, 143), V(91, 91), new Color32(239, 181, 79, 255));
        p.Ellipse(V(128, 143), V(74, 74), Ink);
        p.Ellipse(V(128, 143), V(67, 67), Paper);
        p.Ellipse(V(128, 143), V(48, 48), new Color32(180, 80, 67, 255));
        p.Ellipse(V(128, 143), V(29, 29), Paper);
        p.Ellipse(V(128, 143), V(12, 12), new Color32(180, 80, 67, 255));
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI / 6f;
            Vector2 radial = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            p.Line(V(128, 143) + radial * 81f, V(128, 143) + radial * 86f, 2.8f, new Color32(117, 78, 40, 210));
        }
        p.Line(V(71, 187), V(86, 203), 5f, new Color32(255, 243, 190, 230));
        return p;
    }

    private static Painter Patch()
    {
        Painter p = new Painter(256, 160);
        p.Polygon(new[] { V(22, 41), V(28, 123), V(48, 140), V(216, 136), V(235, 117), V(231, 37), V(210, 21), V(43, 23) },
            new Color32(231, 192, 133, 255), Ink, 8f);
        p.Polygon(new[] { V(86, 35), V(85, 126), V(174, 125), V(177, 34) }, Paper, new Color32(160, 120, 80, 230), 3f);
        for (int side = 0; side < 2; side++)
        for (int row = 0; row < 3; row++)
        for (int column = 0; column < 2; column++)
            p.Ellipse(V((side == 0 ? 45 : 192) + column * 14, 49 + row * 28), V(3.5f, 3.5f), new Color32(143, 103, 65, 210));
        p.Line(V(111, 59), V(149, 105), 7.5f, new Color32(183, 91, 72, 255));
        p.Line(V(111, 105), V(149, 59), 7.5f, new Color32(183, 91, 72, 255));
        p.Line(V(44, 126), V(75, 127), 3f, new Color32(255, 232, 183, 230));
        return p;
    }

    private static Painter Explorer()
    {
        Painter p = new Painter(512, 256);
        Color32 cloth = new Color32(194, 177, 121, 255);
        Color32 olive = new Color32(115, 129, 80, 255);
        Color32 seam = new Color32(104, 93, 60, 255);
        p.Polygon(new[] { V(24, 236), V(488, 233), V(500, 220), V(489, 22), V(466, 13), V(42, 17), V(17, 29), V(13, 218) }, cloth, Ink, 9f);
        // Faint diagonal pencil grain stays inside the fabric, not over fur.
        for (int x = 35; x < 472; x += 15)
            p.Line(V(x, 34), V(Mathf.Min(x + 35, 478), 214), 1.1f, new Color32(234, 222, 170, 80));
        p.Polygon(new[] { V(38, 233), V(109, 234), V(108, 47), V(80, 39), V(67, 162) }, olive, Ink, 5f);
        p.Polygon(new[] { V(410, 234), V(481, 233), V(454, 162), V(440, 41), V(410, 46) }, olive, Ink, 5f);
        p.Line(V(258, 229), V(257, 30), 7f, Ink);
        p.Line(V(267, 218), V(266, 44), 2.5f, new Color32(244, 224, 163, 255));
        for (int y = 51; y < 210; y += 14) p.Line(V(254, y), V(263, y + 1), 2.6f, new Color32(210, 200, 155, 255));
        // Real patch pockets, fastening, stitched knee-height belt and a scarf
        // tab sewn into the vest (not the retired wearable scarf category).
        p.Polygon(new[] { V(51, 151), V(199, 150), V(194, 61), V(178, 48), V(69, 48), V(53, 61) },
            new Color32(218, 199, 141, 255), Ink, 5f);
        p.Polygon(new[] { V(46, 151), V(204, 150), V(194, 120), V(125, 101), V(56, 122) }, olive, Ink, 5f);
        p.Ellipse(V(126, 128), V(6, 6), new Color32(243, 210, 117, 255));
        p.Polygon(new[] { V(313, 149), V(461, 151), V(459, 61), V(441, 48), V(331, 48), V(315, 61) },
            new Color32(218, 199, 141, 255), Ink, 5f);
        p.Line(V(325, 128), V(449, 130), 4f, seam);
        for (int y = 65; y < 120; y += 12)
        {
            p.Line(V(65, y), V(65, y + 5), 2f, seam);
            p.Line(V(448, y), V(448, y + 5), 2f, seam);
        }
        p.Polygon(new[] { V(20, 48), V(492, 46), V(489, 24), V(25, 25) }, new Color32(131, 91, 58, 255), Ink, 4f);
        p.Polygon(new[] { V(226, 50), V(285, 50), V(285, 21), V(225, 21) }, new Color32(234, 195, 104, 255), Ink, 4f);
        p.Line(V(238, 35), V(275, 35), 5f, Ink);
        p.Polygon(new[] { V(201, 232), V(257, 208), V(305, 232), V(278, 177), V(255, 193), V(231, 177) },
            new Color32(186, 87, 62, 255), Ink, 5f);
        // Sewn compass patch and tiny leaf patch make this an actual outfit.
        p.Ellipse(V(365, 195), V(27, 27), Ink);
        p.Ellipse(V(365, 195), V(23, 23), Paper);
        p.Polygon(new[] { V(365, 214), V(370, 194), V(365, 176), V(360, 195) }, new Color32(175, 73, 57, 255), Ink, 2f);
        p.Line(V(348, 195), V(382, 195), 2f, seam);
        p.Polygon(new[] { V(141, 198), V(166, 220), V(183, 205), V(167, 185) }, olive, Ink, 3f);
        p.Line(V(150, 194), V(175, 211), 2f, Paper);
        return p;
    }

    private static Vector2 V(float x, float y) => new Vector2(x, y);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        for (int i = 0; i < Sprites.Length; i++)
        {
            Release(Sprites[i]); Release(Textures[i]);
            Sprites[i] = null; Textures[i] = null;
        }
    }

    private static void Release(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
    }

    private sealed class Painter
    {
        private readonly int width, height;
        private readonly Color32[] pixels;
        public Painter(int width, int height) { this.width = width; this.height = height; pixels = new Color32[width * height]; }
        public Texture2D Texture()
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            texture.SetPixels32(pixels); texture.Apply(false, false); return texture;
        }
        public void Ellipse(Vector2 center, Vector2 radius, Color32 color)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(center.x - radius.x - 1));
            int x1 = Mathf.Min(width - 1, Mathf.CeilToInt(center.x + radius.x + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(center.y - radius.y - 1));
            int y1 = Mathf.Min(height - 1, Mathf.CeilToInt(center.y + radius.y + 1));
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                float u = (x + 0.5f - center.x) / radius.x, v = (y + 0.5f - center.y) / radius.y;
                float distance = (Mathf.Sqrt(u * u + v * v) - 1f) * Mathf.Min(radius.x, radius.y);
                Put(x, y, color, Mathf.Clamp01(0.7f - distance));
            }
        }
        public void Polygon(Vector2[] points, Color32 fill, Color32 outline, float lineWidth)
        {
            Vector2 min = points[0], max = points[0];
            foreach (Vector2 point in points) { min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            for (int y = Mathf.Max(0, Mathf.FloorToInt(min.y)); y <= Mathf.Min(height - 1, Mathf.CeilToInt(max.y)); y++)
            for (int x = Mathf.Max(0, Mathf.FloorToInt(min.x)); x <= Mathf.Min(width - 1, Mathf.CeilToInt(max.x)); x++)
            {
                bool inside = false;
                for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                    if ((points[i].y > y + 0.5f) != (points[j].y > y + 0.5f) &&
                        x + 0.5f < (points[j].x - points[i].x) * (y + 0.5f - points[i].y) / (points[j].y - points[i].y) + points[i].x) inside = !inside;
                if (inside) Put(x, y, fill, 1f);
            }
            for (int i = 0; i < points.Length; i++) Line(points[i], points[(i + 1) % points.Length], lineWidth, outline);
        }
        public void Line(Vector2 from, Vector2 to, float thickness, Color32 color)
        {
            Vector2 delta = to - from;
            float length = Mathf.Max(0.0001f, delta.sqrMagnitude), radius = thickness * 0.5f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(from.x, to.x) - radius - 1));
            int x1 = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(from.x, to.x) + radius + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(from.y, to.y) - radius - 1));
            int y1 = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(from.y, to.y) + radius + 1));
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                Vector2 point = V(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(point - from, delta) / length);
                Put(x, y, color, Mathf.Clamp01(radius + 0.7f - Vector2.Distance(point, from + delta * t)));
            }
        }
        private void Put(int x, int y, Color32 color, float coverage)
        {
            if (coverage <= 0f) return;
            int index = y * width + x;
            Color old = pixels[index], next = color;
            // A stable paper grain, independent of UnityEngine.Random.
            float grain = ((x * 13 + y * 37 + x * y * 3) % 19 - 9) / 255f;
            next.r = Mathf.Clamp01(next.r + grain); next.g = Mathf.Clamp01(next.g + grain); next.b = Mathf.Clamp01(next.b + grain);
            float alpha = next.a * coverage, combined = alpha + old.a * (1f - alpha);
            if (combined <= 0f) return;
            pixels[index] = new Color((next.r * alpha + old.r * old.a * (1f - alpha)) / combined,
                (next.g * alpha + old.g * old.a * (1f - alpha)) / combined,
                (next.b * alpha + old.b * old.a * (1f - alpha)) / combined, combined);
        }
    }
}
