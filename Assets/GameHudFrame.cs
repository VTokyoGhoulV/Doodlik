using TMPro;
using UnityEngine;

// One compact paper label for the existing score/mode/wallet texts. The parent
// is already managed by ResponsiveDisplay, so the card inherits its safe area.
public sealed class GameHudFrame : MonoBehaviour
{
    private TMP_Text score, journey;
    private RectTransform rect;
    private CanvasGroup group;
    private RectTransform divider;
    private UnityEngine.UI.Image paperImage, dividerImage;
    private GameHudPanelBorder paperBorder;
    private float lastTransparency = -1f;
    private string lastScore, lastJourney;
    private float lastAvailableWidth = -1f;
    private static readonly Color Ink = new Color32(94, 49, 24, 255);

    public static void Attach(TMP_Text scoreText, TMP_Text journeyText)
    {
        if (scoreText == null || journeyText == null || scoreText.GetComponentInParent<GameHudFrame>() != null) return;
        GameObject paper = new GameObject("Journey paper frame", typeof(RectTransform));
        RectTransform root = paper.GetComponent<RectTransform>();
        root.SetParent(scoreText.transform.parent, false);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = new Vector2(14f, -14f);
        UnityEngine.UI.Image image = paper.AddComponent<UnityEngine.UI.Image>();
        image.color = new Color32(255, 235, 188, 243);
        image.raycastTarget = false;
        GameObject border = new GameObject("Paper border", typeof(RectTransform));
        RectTransform borderRect = border.GetComponent<RectTransform>();
        borderRect.SetParent(root, false);
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = new Vector2(-3f, -3f);
        borderRect.offsetMax = new Vector2(3f, 3f);
        GameHudPanelBorder outline = border.AddComponent<GameHudPanelBorder>();
        outline.color = new Color32(104, 65, 36, 240);
        outline.raycastTarget = false;
        UnityEngine.UI.Shadow shadow = paper.AddComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = new Color(0.19f, 0.1f, 0.04f, 0.22f);
        shadow.effectDistance = new Vector2(5f, -6f);
        GameHudFrame frame = paper.AddComponent<GameHudFrame>();
        frame.rect = root;
        frame.paperImage = image;
        frame.paperBorder = outline;
        frame.score = scoreText;
        frame.journey = journeyText;
        frame.group = paper.AddComponent<CanvasGroup>();
        frame.group.interactable = false;
        frame.group.blocksRaycasts = false;
        PlaceText(scoreText, root, new Vector2(18f, -12f), 94f, 30f);
        PlaceText(journeyText, root, new Vector2(60f, -111f), 36f, 25f);
        GameObject coin = new GameObject("Wallet coin", typeof(RectTransform));
        RectTransform coinRect = coin.GetComponent<RectTransform>();
        coinRect.SetParent(root, false);
        coinRect.anchorMin = coinRect.anchorMax = new Vector2(0f, 1f);
        coinRect.anchoredPosition = new Vector2(34f, -126f);
        coinRect.sizeDelta = new Vector2(32f, 32f);
        coin.AddComponent<GameHudCoinIcon>().raycastTarget = false;
        GameObject line = new GameObject("Paper divider", typeof(RectTransform));
        frame.divider = line.GetComponent<RectTransform>();
        frame.divider.SetParent(root, false);
        frame.divider.anchorMin = frame.divider.anchorMax = frame.divider.pivot = new Vector2(0f, 1f);
        frame.divider.anchoredPosition = new Vector2(18f, -103f);
        UnityEngine.UI.Image lineImage = line.AddComponent<UnityEngine.UI.Image>();
        lineImage.color = new Color(0.53f, 0.30f, 0.11f, 0.2f);
        lineImage.raycastTarget = false;
        frame.dividerImage = lineImage;
        frame.LateUpdate();
    }

    private static void PlaceText(TMP_Text text, Transform parent, Vector2 position, float height, float fontSize)
    {
        RectTransform textRect = text.rectTransform;
        textRect.SetParent(parent, false);
        textRect.anchorMin = textRect.anchorMax = textRect.pivot = new Vector2(0f, 1f);
        textRect.anchoredPosition = position;
        textRect.sizeDelta = new Vector2(600f, height);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.color = Ink;
        text.raycastTarget = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = 18f;
        text.fontSizeMax = fontSize;
        text.fontSize = fontSize;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void LateUpdate()
    {
        if (score == null || journey == null || group == null) return;
        group.alpha = RunFlow.RunActive ? 1f : 0f;
        score.color = journey.color = Ink;
        ApplyTransparency();
        float available = ((RectTransform)rect.parent).rect.width;
        if (lastScore == score.text && lastJourney == journey.text && Mathf.Approximately(lastAvailableWidth, available)) return;
        lastScore = score.text;
        lastJourney = journey.text;
        lastAvailableWidth = available;
        float desired = Mathf.Max(score.GetPreferredValues(score.text).x + 38f, journey.GetPreferredValues(journey.text).x + 80f);
        float width = Mathf.Clamp(desired, 330f, Mathf.Max(330f, available - 236f));
        rect.sizeDelta = new Vector2(width, 156f);
        score.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width - 36f);
        journey.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width - 78f);
        divider.sizeDelta = new Vector2(width - 36f, 1.5f);
    }

    private void ApplyTransparency()
    {
        float transparency = GameplayPreferences.ScorePanelTransparency;
        if (Mathf.Approximately(lastTransparency, transparency)) return;
        lastTransparency = transparency;
        float opacity = 1f - transparency;
        Color paperColor = paperImage.color;
        paperColor.a = (243f / 255f) * opacity;
        paperImage.color = paperColor;
        // Shadow inherits the paper's alpha. The hollow border has no overlapping
        // interior quads, unlike UI.Outline which darkens translucent panels.
        Color borderColor = paperBorder.color;
        borderColor.a = (240f / 255f) * opacity;
        paperBorder.color = borderColor;
        Color dividerColor = dividerImage.color;
        dividerColor.a = 0.2f * opacity;
        dividerImage.color = dividerColor;
        // A light glyph outline keeps the dark ink legible over both sky and space.
        Color32 outlineColor = new Color32(255, 235, 188, 255);
        score.outlineColor = journey.outlineColor = outlineColor;
        score.outlineWidth = journey.outlineWidth = 0.16f * transparency;
    }
}

[RequireComponent(typeof(CanvasRenderer))]
public sealed class GameHudPanelBorder : UnityEngine.UI.MaskableGraphic
{
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        Rect r = rectTransform.rect;
        const float thickness = 3f;
        AddStrip(mesh, new Rect(r.xMin, r.yMin, r.width, thickness));
        AddStrip(mesh, new Rect(r.xMin, r.yMax - thickness, r.width, thickness));
        AddStrip(mesh, new Rect(r.xMin, r.yMin + thickness, thickness, r.height - 2f * thickness));
        AddStrip(mesh, new Rect(r.xMax - thickness, r.yMin + thickness, thickness, r.height - 2f * thickness));
    }

    private void AddStrip(UnityEngine.UI.VertexHelper mesh, Rect r)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(new Vector3(r.xMin, r.yMin), color, Vector2.zero);
        mesh.AddVert(new Vector3(r.xMin, r.yMax), color, Vector2.zero);
        mesh.AddVert(new Vector3(r.xMax, r.yMax), color, Vector2.zero);
        mesh.AddVert(new Vector3(r.xMax, r.yMin), color, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }
}

[RequireComponent(typeof(CanvasRenderer))]
public sealed class GameHudCoinIcon : UnityEngine.UI.MaskableGraphic
{
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();
        Color ink = new Color32(136, 85, 31, 255);
        Disc(vh, 0.49f, ink);
        Disc(vh, 0.41f, new Color32(255, 207, 79, 255));
        Polygon(vh, ink, new Vector2(-0.28f, 0f), new Vector2(-0.02f, 0.18f),
            new Vector2(0.22f, 0f), new Vector2(-0.02f, -0.18f));
        Polygon(vh, ink, new Vector2(0.16f, 0f), new Vector2(0.34f, 0.15f), new Vector2(0.34f, -0.15f));
    }

    private void Disc(UnityEngine.UI.VertexHelper vh, float radius, Color tint)
    {
        int start = vh.currentVertCount;
        Vert(vh, Vector2.zero, tint);
        const int edges = 32;
        for (int i = 0; i <= edges; i++)
        {
            float angle = i * Mathf.PI * 2f / edges;
            Vert(vh, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint);
            if (i > 0) vh.AddTriangle(start, start + i, start + i + 1);
        }
    }

    private void Polygon(UnityEngine.UI.VertexHelper vh, Color tint, params Vector2[] points)
    {
        int start = vh.currentVertCount;
        foreach (Vector2 point in points) Vert(vh, point, tint);
        for (int i = 1; i < points.Length - 1; i++) vh.AddTriangle(start, start + i, start + i + 1);
    }

    private void Vert(UnityEngine.UI.VertexHelper vh, Vector2 point, Color tint)
    {
        Rect area = rectTransform.rect;
        vh.AddVert(area.center + Vector2.Scale(point, area.size), tint, Vector2.zero);
    }
}
