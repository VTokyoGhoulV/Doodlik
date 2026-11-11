using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// One paged board on the existing front-end Canvas. No independent save or
// EventSystem: progression and rewards belong exclusively to PlayerProgress.
public sealed class ChallengeBoardUI : MonoBehaviour
{
    private static readonly Color Ink = new Color32(94, 49, 24, 255);
    private static readonly Color Amber = new Color32(255, 182, 48, 255);
    private static readonly Color MutedPaper = new Color32(245, 208, 138, 255);
    private const int RowsPerPage = 3;
    private readonly Row[] rows = new Row[RowsPerPage];
    private TMP_FontAsset font;
    private TMP_Text subtitle, selection, pageText, feedback;
    private UnityEngine.UI.Button trialTab, achievementTab, previous, next, none, back;
    private Func<bool> canSelect;
    private bool achievements;
    private int page;

    private sealed class Row
    {
        public GameObject Root;
        public TMP_Text Title, Detail, Reward, ActionText;
        public UnityEngine.UI.Button Action;
        public string Id;
    }

    public bool IsVisible => gameObject.activeSelf;
    public bool ShowingAchievements => achievements;
    public int CurrentPage => page;
    public int PageCount => Mathf.Max(1, (Count() + RowsPerPage - 1) / RowsPerPage);

    public static ChallengeBoardUI Create(Transform parent, TMP_FontAsset interfaceFont,
        Action goBack, Func<bool> selectionAllowed)
    {
        GameObject root = RectObject("Challenges", parent, new Vector2(780f, 900f), Vector2.zero);
        ChallengeBoardUI board = root.AddComponent<ChallengeBoardUI>();
        board.font = interfaceFont;
        board.canSelect = selectionAllowed;
        board.Build(goBack);
        root.SetActive(false);
        return board;
    }

    private void Build(Action goBack)
    {
        UnityEngine.UI.Image paper = gameObject.AddComponent<UnityEngine.UI.Image>();
        paper.color = new Color32(255, 232, 179, 255);
        UnityEngine.UI.Outline border = gameObject.AddComponent<UnityEngine.UI.Outline>();
        border.effectColor = Ink;
        border.effectDistance = new Vector2(6f, -6f);
        UnityEngine.UI.Shadow shadow = gameObject.AddComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = new Color(0.12f, 0.08f, 0.04f, 0.32f);
        shadow.effectDistance = new Vector2(10f, -11f);

        Label(transform, "Испытания", new Vector2(700f, 66f), new Vector2(0f, 391f), 46f, true);
        subtitle = Label(transform, "", new Vector2(700f, 44f), new Vector2(0f, 337f), 23f);
        trialTab = Button(transform, "На забег", new Vector2(324f, 54f), new Vector2(-170f, 279f),
            () => ChangeSection(false), out _);
        achievementTab = Button(transform, "Достижения", new Vector2(324f, 54f), new Vector2(170f, 279f),
            () => ChangeSection(true), out _);
        selection = Label(transform, "", new Vector2(700f, 36f), new Vector2(0f, 230f), 22f, true);

        for (int i = 0; i < rows.Length; i++)
        {
            int index = i;
            Row row = new Row();
            row.Root = RectObject("Challenge row " + i, transform,
                new Vector2(700f, 170f), new Vector2(0f, 129f - i * 183f));
            UnityEngine.UI.Image background = row.Root.AddComponent<UnityEngine.UI.Image>();
            background.color = new Color32(255, 242, 206, 255);
            background.raycastTarget = false;
            UnityEngine.UI.Outline rowBorder = row.Root.AddComponent<UnityEngine.UI.Outline>();
            rowBorder.effectColor = new Color(0.55f, 0.34f, 0.16f, 0.4f);
            rowBorder.effectDistance = new Vector2(2f, -2f);
            row.Title = Label(row.Root.transform, "", new Vector2(490f, 36f), new Vector2(-80f, 51f), 27f, true);
            row.Detail = Label(row.Root.transform, "", new Vector2(490f, 62f), new Vector2(-80f, 2f), 21f);
            row.Reward = Label(row.Root.transform, "", new Vector2(490f, 43f), new Vector2(-80f, -54f), 20f);
            row.Title.alignment = row.Detail.alignment = row.Reward.alignment = TextAlignmentOptions.MidlineLeft;
            row.Action = Button(row.Root.transform, "", new Vector2(126f, 104f), new Vector2(270f, 0f),
                () => SelectRow(index), out row.ActionText);
            row.ActionText.fontSizeMax = 24f;
            row.ActionText.fontSizeMin = 20f;
            rows[i] = row;
        }

        previous = Button(transform, "<", new Vector2(76f, 42f), new Vector2(-158f, -351f),
            () => ChangePage(-1), out _);
        pageText = Label(transform, "", new Vector2(180f, 42f), new Vector2(0f, -351f), 24f);
        next = Button(transform, ">", new Vector2(76f, 42f), new Vector2(158f, -351f),
            () => ChangePage(1), out _);
        back = Button(transform, "Назад", new Vector2(260f, 56f), new Vector2(-192f, -409f),
            () => goBack(), out _);
        none = Button(transform, "Без испытания", new Vector2(354f, 56f), new Vector2(143f, -409f),
            ClearSelection, out TMP_Text noneLabel);
        noneLabel.fontSizeMax = 27f;
        feedback = Label(transform, "", new Vector2(700f, 30f), new Vector2(0f, 230f), 21f, true);
        feedback.gameObject.SetActive(false);
    }

    public void Show()
    {
        achievements = false;
        page = 0;
        string selected = PlayerProgress.SelectedChallengeId;
        int index = 0;
        foreach (ChallengeDefinition definition in ChallengeCatalog.All)
        {
            if (definition.IsAchievement) continue;
            if (definition.Id == selected) { page = index / RowsPerPage; break; }
            index++;
        }
        gameObject.SetActive(true);
        Refresh();
        Focus(trialTab);
    }

    public void Hide() => gameObject.SetActive(false);

    public void ApplyScale(float scale) => transform.localScale = Vector3.one * Mathf.Max(0.1f, scale);

    public void Refresh()
    {
        if (!IsVisible) return;
        feedback.gameObject.SetActive(false);
        selection.gameObject.SetActive(true);
        bool selectable = !RunFlow.RunActive && (canSelect == null || canSelect());
        ChallengeDefinition chosen = ChallengeCatalog.Find(PlayerProgress.SelectedChallengeId);
        selection.text = chosen == null ? "На забег: без испытания" :
            PlayerProgress.IsChallengeCompleted(chosen.Id) ? "Выполнено: " + chosen.Name : "На забег: " + chosen.Name;
        subtitle.text = achievements ? "Считаются в фоне. Монеты и редкие вещи." :
            "Одно задание на попытку. Награда выдаётся один раз.";
        trialTab.GetComponent<UnityEngine.UI.Image>().color = achievements ? MutedPaper : Amber;
        achievementTab.GetComponent<UnityEngine.UI.Image>().color = achievements ? Amber : MutedPaper;
        page = Mathf.Clamp(page, 0, PageCount - 1);
        int entry = 0;
        int visible = 0;
        foreach (ChallengeDefinition definition in ChallengeCatalog.All)
        {
            if (definition.IsAchievement != achievements) continue;
            if (entry++ < page * RowsPerPage) continue;
            if (visible >= rows.Length) break;
            Row row = rows[visible++];
            row.Root.SetActive(true);
            row.Id = definition.Id;
            bool completed = PlayerProgress.IsChallengeCompleted(definition.Id);
            bool selected = PlayerProgress.SelectedChallengeId == definition.Id;
            int progress = Mathf.Clamp(PlayerProgress.ChallengeProgressValue(definition.Id), 0, definition.Target);
            row.Title.text = definition.Name;
            row.Detail.text = definition.Description;
            row.Reward.text = $"{progress}/{definition.Target} · " + RewardText(definition);
            row.ActionText.text = completed ? "Готово!" : achievements ? "В фоне" : selected ? "Выбрано" : "Выбрать";
            row.Action.interactable = selectable && !achievements && !completed && !selected;
            row.Root.GetComponent<UnityEngine.UI.Image>().color = completed
                ? new Color32(229, 239, 195, 255) : selected
                    ? new Color32(255, 222, 150, 255) : new Color32(255, 242, 206, 255);
        }
        for (int i = visible; i < rows.Length; i++) rows[i].Root.SetActive(false);
        pageText.text = $"{page + 1} / {PageCount}";
        previous.interactable = page > 0;
        next.interactable = page + 1 < PageCount;
        none.interactable = selectable && !string.IsNullOrEmpty(PlayerProgress.SelectedChallengeId);
    }

    private int Count()
    {
        int count = 0;
        foreach (ChallengeDefinition definition in ChallengeCatalog.All)
            if (definition.IsAchievement == achievements) count++;
        return count;
    }

    private static string RewardText(ChallengeDefinition definition)
    {
        if (string.IsNullOrEmpty(definition.RewardItemId)) return $"Награда: {definition.RewardCoins} монет";
        foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
            if (item.Id == definition.RewardItemId) return "Награда: «" + item.Name + "»";
        return "Награда: эксклюзивный предмет";
    }

    private void ChangeSection(bool showAchievements)
    {
        achievements = showAchievements;
        page = 0;
        Refresh();
        Focus(achievements ? achievementTab : trialTab);
    }

    private void ChangePage(int delta)
    {
        page = Mathf.Clamp(page + delta, 0, PageCount - 1);
        Refresh();
        Focus(delta > 0 && next.interactable ? next : previous.interactable ? previous : back);
    }

    private void SelectRow(int index)
    {
        if (index < 0 || index >= rows.Length || !rows[index].Root.activeSelf ||
            achievements || RunFlow.RunActive || canSelect != null && !canSelect()) return;
        if (!PlayerProgress.SelectChallenge(rows[index].Id))
        {
            ShowUnavailable();
            return;
        }
        Refresh();
        Focus(none);
    }

    private void ClearSelection()
    {
        if (RunFlow.RunActive || canSelect != null && !canSelect()) return;
        if (!PlayerProgress.SelectChallenge(string.Empty)) { ShowUnavailable(); return; }
        Refresh();
        Focus(trialTab);
    }

    private void ShowUnavailable()
    {
        selection.gameObject.SetActive(false);
        feedback.text = "Сейчас выбор недоступен. Прогресс сохранён.";
        feedback.gameObject.SetActive(true);
    }

    private TMP_Text Label(Transform parent, string text, Vector2 size, Vector2 position,
        float fontSize, bool bold = false)
    {
        TMP_Text label = RectObject("Label", parent, size, position).AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = text;
        label.color = Ink;
        label.fontSize = label.fontSizeMax = fontSize;
        label.fontSizeMin = Mathf.Max(18f, fontSize - 3f);
        label.enableAutoSizing = true;
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    private UnityEngine.UI.Button Button(Transform parent, string text, Vector2 size, Vector2 position,
        Action callback, out TMP_Text label)
    {
        GameObject target = RectObject(text.Length == 0 ? "Action" : text, parent, size, position);
        UnityEngine.UI.Image image = target.AddComponent<UnityEngine.UI.Image>();
        image.color = Amber;
        UnityEngine.UI.Outline border = target.AddComponent<UnityEngine.UI.Outline>();
        border.effectColor = new Color32(164, 86, 32, 255);
        border.effectDistance = new Vector2(3f, -3f);
        UnityEngine.UI.Button button = target.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        UnityEngine.UI.ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.96f, 0.78f);
        colors.pressedColor = new Color(0.88f, 0.75f, 0.55f);
        colors.disabledColor = new Color(0.78f, 0.76f, 0.70f, 1f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick.AddListener(() => { GameAudio.Play(GameAudio.Cue.Click); callback(); });
        label = Label(target.transform, text, size - new Vector2(16f, 12f), Vector2.zero, 28f, true);
        return button;
    }

    private static GameObject RectObject(string name, Transform parent, Vector2 size, Vector2 position)
    {
        GameObject result = new GameObject(name, typeof(RectTransform));
        RectTransform rect = result.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return result;
    }

    private static void Focus(UnityEngine.UI.Button button)
    {
        if (GameplayPreferences.IsDesktopBuild && EventSystem.current != null && button != null)
            EventSystem.current.SetSelectedGameObject(button.gameObject);
    }
}
