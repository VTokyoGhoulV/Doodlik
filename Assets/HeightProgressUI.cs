using TMPro;
using UnityEngine;

// A narrow, non-interactive trail on the right edge of the existing score canvas.
// Uses the run's maximum physical height, never difficulty-multiplied score.
public sealed class HeightProgressUI : MonoBehaviour
{
    private Player owner;
    private Canvas canvas;
    private GameObject content;
    private RectTransform root, rail, fill, marker;
    private UnityEngine.UI.Image fillImage, markerImage;
    private TMP_Text bottomLabel, topLabel;
    private readonly UnityEngine.UI.Image[] milestones = new UnityEngine.UI.Image[WorldZones.ZoneCount];
    private int lastHeight = -1;
    private int lastGoal = -1;
    private WorldZones.Zone lastZone;
    private Rect lastSafeArea;
    private Vector2Int lastScreen;
    private static int DisplayGoalHeight => WorldZones.GoalScore;

    public float DisplayedProgress { get; private set; }
    public bool IsVisible => content != null && content.activeSelf;

    public static HeightProgressUI Create(Player player, TMP_Text scoreText)
    {
        if (player == null || scoreText == null || scoreText.canvas == null) return null;
        Canvas canvas = scoreText.canvas;
        HeightProgressUI existing = canvas.GetComponentInChildren<HeightProgressUI>(true);
        if (existing != null) return existing;
        GameObject root = new GameObject("Height Progress", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);
        HeightProgressUI progress = root.AddComponent<HeightProgressUI>();
        progress.Initialize(player, canvas, scoreText.font);
        return progress;
    }

    private void Initialize(Player player, Canvas parentCanvas, TMP_FontAsset font)
    {
        owner = player;
        canvas = parentCanvas;
        root = GetComponent<RectTransform>();
        root.pivot = new Vector2(1f, 0.5f);
        content = new GameObject("Progress Graphics", typeof(RectTransform));
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.SetParent(root, false);
        Stretch(contentRect);
        CanvasGroup group = content.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        UnityEngine.UI.Image track = Rectangle("Trail", contentRect, new Vector2(12f, 400f),
            new Color(0.20f, 0.12f, 0.08f, 0.48f));
        rail = track.rectTransform;
        UnityEngine.UI.Outline outline = track.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(1f, 0.88f, 0.61f, 0.65f);
        outline.effectDistance = new Vector2(2f, -2f);
        fillImage = Rectangle("Reached height", rail, Vector2.zero, new Color(1f, 0.69f, 0.15f, 1f));
        fill = fillImage.rectTransform;
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(1f, 0f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        for (int i = 0; i < milestones.Length; i++)
        {
            milestones[i] = Rectangle("Zone " + i, rail, new Vector2(20f, 4f), new Color(1f, 0.92f, 0.73f, 0.95f));
            RectTransform tick = milestones[i].rectTransform;
            tick.anchorMin = tick.anchorMax = new Vector2(0.5f,
                Mathf.Clamp01(WorldZones.StartScore((WorldZones.Zone)i) / (float)DisplayGoalHeight));
            tick.anchoredPosition = Vector2.zero;
        }
        markerImage = Rectangle("Current height", rail, new Vector2(21f, 21f), new Color(1f, 0.76f, 0.22f));
        marker = markerImage.rectTransform;
        marker.localRotation = Quaternion.Euler(0f, 0f, 45f);
        UnityEngine.UI.Outline markerOutline = marker.gameObject.AddComponent<UnityEngine.UI.Outline>();
        markerOutline.effectColor = new Color(0.31f, 0.16f, 0.06f, 1f);
        markerOutline.effectDistance = new Vector2(2f, -2f);
        bottomLabel = Label("Start", "0", rail, font, false);
        topLabel = Label("Goal", "До космоса\n" + DisplayGoalHeight, rail, font, true);
        topLabel.rectTransform.anchoredPosition = new Vector2(-30f, 43f);
        topLabel.rectTransform.sizeDelta = new Vector2(120f, 60f);
        topLabel.fontSize = topLabel.fontSizeMax = 20f;
        UpdateLayout(true);
        Update();
    }

    private void Update()
    {
        if (content == null) return;
        bool visible = owner != null && RunFlow.RunActive && !RunFlow.IsTransitioning && !owner.IsDead;
        if (content.activeSelf != visible) content.SetActive(visible);
        if (!visible) return;
        UpdateLayout(false);
        int height = owner.CurrentHeight;
        int goal = DisplayGoalHeight;
        if (goal != lastGoal)
        {
            topLabel.text = "До космоса\n" + goal;
            for (int i = 0; i < milestones.Length; i++)
            {
                RectTransform tick = milestones[i].rectTransform;
                tick.anchorMin = tick.anchorMax = new Vector2(0.5f,
                    Mathf.Clamp01(WorldZones.StartScore((WorldZones.Zone)i) / (float)goal));
                tick.anchoredPosition = Vector2.zero;
            }
        }
        if (height == lastHeight && lastZone == WorldZones.Current && goal == lastGoal) return;
        lastGoal = goal;
        lastHeight = height;
        lastZone = WorldZones.Current;
        DisplayedProgress = Mathf.Clamp01(height / (float)goal);
        fill.anchorMax = new Vector2(1f, DisplayedProgress);
        marker.anchorMin = marker.anchorMax = new Vector2(0.5f, DisplayedProgress);
        marker.anchoredPosition = Vector2.zero;
        bool dark = WorldZones.IsDarkBackground(lastZone);
        Color labelColor = dark ? new Color(1f, 0.92f, 0.75f) : new Color(0.28f, 0.14f, 0.055f);
        bottomLabel.color = topLabel.color = labelColor;
        fillImage.color = markerImage.color = height >= goal
            ? new Color(0.85f, 0.92f, 0.42f) : new Color(1f, 0.72f, 0.18f);
        for (int i = 0; i < milestones.Length; i++)
            milestones[i].color = height >= WorldZones.StartScore((WorldZones.Zone)i)
                ? new Color(1f, 0.77f, 0.31f) : new Color(1f, 0.95f, 0.84f, 0.9f);
    }

    private void UpdateLayout(bool force)
    {
        Rect safe = Screen.safeArea;
        Vector2Int screen = new Vector2Int(Screen.width, Screen.height);
        if (!force && safe == lastSafeArea && screen == lastScreen) return;
        lastSafeArea = safe;
        lastScreen = screen;
        float width = Mathf.Max(1f, Screen.width), height = Mathf.Max(1f, Screen.height);
        float safeHeight = safe.height / Mathf.Max(0.01f, canvas.scaleFactor);
        float railHeight = Mathf.Clamp(safeHeight * 0.42f, 180f, 460f);
        // Centered below the top-right Menu button, including narrow landscape
        // phones and cutouts. Nothing in this hierarchy catches touch input.
        Vector2 anchor = new Vector2(safe.xMax / width, safe.center.y / height);
        root.anchorMin = root.anchorMax = anchor;
        root.anchoredPosition = new Vector2(-12f, -12f);
        root.sizeDelta = new Vector2(64f, railHeight + 72f);
        rail.sizeDelta = new Vector2(12f, railHeight);
    }

    private static UnityEngine.UI.Image Rectangle(string name, Transform parent, Vector2 size, Color color)
    {
        GameObject item = new GameObject(name, typeof(RectTransform));
        item.transform.SetParent(parent, false);
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        UnityEngine.UI.Image image = item.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text Label(string name, string value, Transform parent, TMP_FontAsset font, bool top)
    {
        GameObject item = new GameObject(name, typeof(RectTransform));
        item.transform.SetParent(parent, false);
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, top ? 1f : 0f);
        rect.anchoredPosition = new Vector2(0f, top ? 29f : -29f);
        rect.sizeDelta = new Vector2(64f, 35f);
        TMP_Text label = item.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = value;
        label.fontSize = 22f;
        label.enableAutoSizing = true;
        label.fontSizeMax = 22f;
        label.fontSizeMin = 16f;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
