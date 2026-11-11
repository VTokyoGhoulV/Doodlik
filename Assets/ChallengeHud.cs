using TMPro;
using UnityEngine;

// A non-interactive ink annotation below the existing paper HUD. It never
// changes its size/position and cannot intercept touch input.
public sealed class ChallengeHud : MonoBehaviour
{
    private Player owner;
    private RectTransform safeParent, paper, rect;
    private TMP_Text label;
    private CanvasGroup visibility;
    private int completionCount;
    private float completionUntil;
    private string completionText, displayed;
    private ChallengeDefinition cachedDefinition;
    private string cachedProgress, cachedSelectionText;
    private float previousAvailable = -1f;

    public static ChallengeHud Attach(Player player, TMP_Text scoreText)
    {
        if (player == null || scoreText == null || scoreText.canvas == null) return null;
        ChallengeHud existing = scoreText.canvas.GetComponentInChildren<ChallengeHud>(true);
        if (existing != null) return existing;
        GameHudFrame frame = scoreText.GetComponentInParent<GameHudFrame>();
        Transform parent = frame != null ? frame.transform.parent : scoreText.transform.parent;
        GameObject root = new GameObject("Challenge HUD", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        ChallengeHud hud = root.AddComponent<ChallengeHud>();
        hud.owner = player;
        hud.safeParent = parent as RectTransform;
        hud.paper = frame != null ? frame.GetComponent<RectTransform>() : null;
        hud.rect = root.GetComponent<RectTransform>();
        hud.rect.anchorMin = hud.rect.anchorMax = hud.rect.pivot = new Vector2(0f, 1f);
        hud.visibility = root.AddComponent<CanvasGroup>();
        hud.visibility.interactable = hud.visibility.blocksRaycasts = false;
        hud.visibility.alpha = 0f;
        hud.label = root.AddComponent<TextMeshProUGUI>();
        hud.label.font = scoreText.font;
        hud.label.color = new Color32(94, 49, 24, 255);
        hud.label.fontSize = hud.label.fontSizeMax = 22f;
        hud.label.fontSizeMin = 18f;
        hud.label.enableAutoSizing = true;
        hud.label.alignment = TextAlignmentOptions.TopLeft;
        hud.label.textWrappingMode = TextWrappingModes.Normal;
        hud.label.overflowMode = TextOverflowModes.Ellipsis;
        hud.label.raycastTarget = false;
        hud.label.outlineColor = new Color32(255, 239, 200, 255);
        hud.label.outlineWidth = 0.16f;
        hud.UpdateLayout();
        return hud;
    }

    private void LateUpdate()
    {
        if (owner == null || label == null) return;
        RunChallenges run = RunChallenges.Active;
        bool visible = RunFlow.RunActive && !RunFlow.IsTransitioning && run != null && run.IsEligible;
        ChallengeDefinition selected = visible ? run.SelectedDefinition : null;
        if (visible && run.CompletedThisRun.Count > completionCount)
        {
            completionCount = run.CompletedThisRun.Count;
            ChallengeDefinition completed = ChallengeCatalog.Find(run.CompletedThisRun[completionCount - 1]);
            if (completed != null)
            {
                completionText = "Выполнено: «" + completed.Name + "»\n" +
                    (completed.RewardCoins > 0 ? "+" + completed.RewardCoins + " монет" : "Эксклюзивный предмет открыт!");
                completionUntil = Time.unscaledTime + 4f;
            }
        }
        if (selected != null)
        {
            string progress = run.ProgressText;
            if (selected != cachedDefinition || progress != cachedProgress)
            {
                cachedDefinition = selected;
                cachedProgress = progress;
                cachedSelectionText = progress;
            }
        }
        string nextText = !visible ? string.Empty :
            !string.IsNullOrEmpty(completionText) && (Time.unscaledTime < completionUntil || owner.IsDead)
                ? completionText : selected != null ? cachedSelectionText : string.Empty;
        visibility.alpha = string.IsNullOrEmpty(nextText) ? 0f : 1f;
        if (nextText != displayed)
        {
            displayed = nextText;
            label.text = nextText;
        }
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        if (safeParent == null || rect == null) return;
        float available = safeParent.rect.width;
        if (Mathf.Approximately(previousAvailable, available)) return;
        previousAvailable = available;
        rect.anchoredPosition = paper != null
            ? new Vector2(paper.anchoredPosition.x + 4f, paper.anchoredPosition.y - paper.rect.height - 12f)
            : new Vector2(18f, -184f);
        rect.sizeDelta = new Vector2(Mathf.Clamp(available - 250f, 180f, 460f), 62f);
    }
}
