using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// A home presenter only. Wallet, recipe costs and durable transactions stay in
// their respective progression owners; hiding this panel cannot purchase items.
public sealed class ExpeditionWorkshopUI : MonoBehaviour
{
    private static readonly Color Ink = new Color32(94, 49, 24, 255);
    private static readonly Color Paper = new Color32(255, 232, 179, 255);
    private static readonly Color Amber = new Color32(255, 182, 48, 255);
    private TMP_FontAsset font;
    private Action back;
    private Func<bool> canEdit;
    private GameObject trainingPage, recipesPage;
    private TMP_Text wallet, hint, wardDescription, wardToggleLabel, hunterDescription, hunterAction;
    private readonly TMP_Text[] levels = new TMP_Text[3], actions = new TMP_Text[3];
    private readonly UnityEngine.UI.Button[] trainingButtons = new UnityEngine.UI.Button[3];
    private UnityEngine.UI.Button trainingTab, recipesTab, craftWard, takeWard, unlockHunter, backButton;
    private string feedback;
    public bool IsVisible => gameObject.activeSelf;
    public bool IsTrainingVisible => IsVisible && trainingPage.activeSelf;
    public RectTransform Card => (RectTransform)transform;

    public static ExpeditionWorkshopUI Create(Transform parent, TMP_FontAsset typeface, Action onBack, Func<bool> editAllowed)
    {
        GameObject root = RectObject("Expedition workshop", parent, new Vector2(780f, 900f), Vector2.zero);
        ExpeditionWorkshopUI view = root.AddComponent<ExpeditionWorkshopUI>();
        view.font = typeface; view.back = onBack; view.canEdit = editAllowed;
        view.Build();
        PlayerProgress.Changed += view.Refresh;
        ExpeditionProgress.Changed += view.Refresh;
        root.SetActive(false);
        return view;
    }

    public void Show()
    {
        gameObject.SetActive(true); ShowTraining();
        Focus(trainingTab);
    }
    public void Hide() => gameObject.SetActive(false);
    public void ApplyScale(float value) => transform.localScale = Vector3.one * value;

    public void ShowTraining()
    {
        trainingPage.SetActive(true); recipesPage.SetActive(false); feedback = null;
        Refresh();
    }
    public void ShowRecipes()
    {
        trainingPage.SetActive(false); recipesPage.SetActive(true); feedback = null;
        Refresh();
    }

    public void Refresh()
    {
        if (wallet == null || !IsVisible) return;
        GameObject selectedBefore = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        bool ownedFocus = selectedBefore != null && selectedBefore.transform.IsChildOf(transform);
        wallet.text = "Монетки: " + PlayerProgress.Coins + " · запас оберегов: " + ExpeditionProgress.PreparedWards;
        bool home = canEdit != null && canEdit();
        bool train = home && PlayerProgress.ExpeditionTrainingAvailable;
        for (int i = 0; i < trainingButtons.Length; i++)
        {
            ExpeditionTrainingKind kind = (ExpeditionTrainingKind)i;
            int level = PlayerProgress.GetExpeditionTrainingLevel(kind);
            int cap = ExpeditionTraining.MaxLevel(kind), price = PlayerProgress.ExpeditionTrainingPrice(kind);
            levels[i].text = level + " / " + cap;
            actions[i].text = level >= cap ? "Максимум" : price + " монет";
            trainingButtons[i].interactable = train && level < cap && price > 0 && PlayerProgress.Coins >= price;
        }
        bool materials = home && !PlayerProgress.AdminSandboxActive && !ExpeditionProgress.IsReadOnly;
        wardDescription.text = "Поглощает один удар врага.\nНе спасает от колючек и падения.\nЦена: 2 банки светлячков + 1 шерсть.\nВ кладовой: " +
            ExpeditionProgress.Count(ExpeditionFindKind.FireflyJar) + " / " + ExpeditionProgress.Count(ExpeditionFindKind.CloudWool);
        craftWard.interactable = materials && ExpeditionProgress.PreparedWards < 9 &&
            ExpeditionProgress.Count(ExpeditionFindKind.FireflyJar) >= 2 && ExpeditionProgress.Count(ExpeditionFindKind.CloudWool) >= 1;
        wardToggleLabel.text = "Взять оберег в следующий забег: " + (ExpeditionProgress.TakeWardNextRun ? "ДА" : "НЕТ");
        takeWard.interactable = materials && (ExpeditionProgress.PreparedWards > 0 || ExpeditionProgress.TakeWardNextRun);
        hunterDescription.text = "Постоянно +10 п.п. к шансу добычи с врагов.\nЦена: 3 порции пыли + 1 осколок звезды.\nВ кладовой: " +
            ExpeditionProgress.Count(ExpeditionFindKind.SparkDust) + " / " + ExpeditionProgress.Count(ExpeditionFindKind.StarShard);
        hunterAction.text = ExpeditionProgress.HunterCharmUnlocked ? "Открыто" : "Открыть";
        unlockHunter.interactable = materials && !ExpeditionProgress.HunterCharmUnlocked &&
            ExpeditionProgress.Count(ExpeditionFindKind.SparkDust) >= 3 && ExpeditionProgress.Count(ExpeditionFindKind.StarShard) >= 1;
        trainingTab.GetComponent<UnityEngine.UI.Image>().color = trainingPage.activeSelf ? Amber : new Color32(245, 208, 138, 255);
        recipesTab.GetComponent<UnityEngine.UI.Image>().color = recipesPage.activeSelf ? Amber : new Color32(245, 208, 138, 255);
        hint.text = !string.IsNullOrEmpty(feedback) ? feedback : ExpeditionProgress.IsReadOnly ?
            "Кладовая защищена: неизвестная версия сохранения.\nМатериалы не будут потрачены." :
            trainingPage.activeSelf ? "Всё здесь действует только в экспедициях.\nПрыжок, скорость и классика не меняются." :
            "Один оберег расходуется на старте экспедиции.\nПосле перезапуска или возврата он не возвращается.";
        RefreshNavigation(ownedFocus);
    }

    private void RefreshNavigation(bool ownedFocus)
    {
        var available = new System.Collections.Generic.List<UnityEngine.UI.Button>(3);
        if (trainingPage.activeSelf)
        {
            foreach (UnityEngine.UI.Button action in trainingButtons) if (action.interactable) available.Add(action);
        }
        else
        {
            foreach (UnityEngine.UI.Button action in new[] { craftWard, takeWard, unlockHunter }) if (action.interactable) available.Add(action);
        }
        UnityEngine.UI.Button tab = trainingPage.activeSelf ? trainingTab : recipesTab;
        UnityEngine.UI.Button first = available.Count > 0 ? available[0] : backButton;
        trainingTab.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit, selectOnRight = recipesTab, selectOnDown = first };
        recipesTab.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit, selectOnLeft = trainingTab, selectOnDown = first };
        for (int i = 0; i < available.Count; i++)
            available[i].navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnUp = i > 0 ? available[i - 1] : tab,
                selectOnDown = i + 1 < available.Count ? available[i + 1] : backButton };
        backButton.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit,
            selectOnUp = available.Count > 0 ? available[available.Count - 1] : tab };
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        // uGUI clears EventSystem selection immediately when interactable is
        // set false. Remember ownership before updating the button states.
        if (selected == null) { if (ownedFocus) Focus(first); return; }
        if (!selected.transform.IsChildOf(transform)) return;
        UnityEngine.UI.Selectable control = selected.GetComponent<UnityEngine.UI.Selectable>();
        if (control != null && (!control.IsActive() || !control.IsInteractable())) Focus(first);
    }

    private void Purchase(ExpeditionTrainingKind kind)
    {
        feedback = canEdit != null && canEdit() && PlayerProgress.TryUpgradeExpeditionTraining(kind)
            ? "Улучшение куплено. Оно действует со следующей экспедиции."
            : "Покупка не выполнена. Проверь монеты, предел уровня и доступность сохранения.";
        Refresh();
    }
    private void CraftWard()
    {
        feedback = canEdit != null && canEdit() && ExpeditionProgress.TryCraftWard()
            ? "Оберег создан. Один будет израсходован при старте следующей экспедиции."
            : "Оберег не создан. Нужны материалы и свободное место в запасе.";
        Refresh();
    }
    private void ToggleWard()
    {
        feedback = canEdit != null && canEdit() && ExpeditionProgress.SetTakeWardNextRun(!ExpeditionProgress.TakeWardNextRun)
            ? "Подготовка к следующей экспедиции изменена." : "Подготовку сейчас изменить нельзя.";
        Refresh();
    }
    private void UnlockHunter()
    {
        feedback = canEdit != null && canEdit() && ExpeditionProgress.TryUnlockHunterCharm()
            ? "Амулет охотника открыт навсегда для экспедиций."
            : "Амулет не открыт. Проверь материалы и доступность сохранения.";
        Refresh();
    }

    private void Build()
    {
        gameObject.AddComponent<UnityEngine.UI.Image>().color = Paper;
        var outline = gameObject.AddComponent<UnityEngine.UI.Outline>(); outline.effectColor = Ink; outline.effectDistance = new Vector2(6f, -6f);
        Label(transform, "Мастерская Дудлика", new Vector2(700f, 64f), new Vector2(0f, 378f), 41f, true);
        wallet = Label(transform, "", new Vector2(700f, 42f), new Vector2(0f, 321f), 25f);
        trainingTab = Button(transform, "Прокачка", new Vector2(326f, 58f), new Vector2(-175f, 264f), ShowTraining, out _);
        recipesTab = Button(transform, "Рецепты", new Vector2(326f, 58f), new Vector2(175f, 264f), ShowRecipes, out _);
        trainingPage = RectObject("Expedition training", transform, Vector2.zero, Vector2.zero);
        recipesPage = RectObject("Expedition recipes", transform, Vector2.zero, Vector2.zero);
        for (int i = 0; i < trainingButtons.Length; i++)
        {
            ExpeditionTrainingKind kind = (ExpeditionTrainingKind)i;
            GameObject row = Row("Training " + kind, trainingPage.transform, new Vector2(714f, 144f), new Vector2(0f, 160f - i * 164f));
            Label(row.transform, ExpeditionTraining.Name(kind), new Vector2(460f, 38f), new Vector2(-90f, 39f), 28f, true);
            Label(row.transform, ExpeditionTraining.Description(kind), new Vector2(480f, 66f), new Vector2(-86f, -14f), 22f);
            levels[i] = Label(row.transform, "", new Vector2(150f, 36f), new Vector2(257f, 39f), 24f, true);
            trainingButtons[i] = Button(row.transform, "", new Vector2(164f, 58f), new Vector2(257f, -29f), () => Purchase(kind), out actions[i]);
            trainingButtons[i].gameObject.name = "Purchase training " + kind;
        }
        GameObject ward = Row("Ward recipe", recipesPage.transform, new Vector2(714f, 205f), new Vector2(0f, 124f));
        Label(ward.transform, "Плетёный оберег", new Vector2(650f, 40f), new Vector2(0f, 74f), 29f, true);
        wardDescription = Label(ward.transform, "", new Vector2(470f, 122f), new Vector2(-95f, -12f), 22f);
        craftWard = Button(ward.transform, "Создать", new Vector2(164f, 64f), new Vector2(257f, -40f), CraftWard, out _);
        craftWard.gameObject.name = "Craft expedition ward";
        takeWard = Button(recipesPage.transform, "", new Vector2(714f, 60f), new Vector2(0f, -19f), ToggleWard, out wardToggleLabel);
        takeWard.gameObject.name = "Toggle prepared ward";
        wardToggleLabel.fontSize = wardToggleLabel.fontSizeMax = 25f; wardToggleLabel.fontSizeMin = 21f;
        GameObject hunter = Row("Hunter recipe", recipesPage.transform, new Vector2(714f, 176f), new Vector2(0f, -150f));
        Label(hunter.transform, "Амулет охотника", new Vector2(650f, 40f), new Vector2(0f, 56f), 29f, true);
        hunterDescription = Label(hunter.transform, "", new Vector2(480f, 90f), new Vector2(-86f, -17f), 22f);
        unlockHunter = Button(hunter.transform, "Открыть", new Vector2(164f, 64f), new Vector2(257f, -35f), UnlockHunter, out hunterAction);
        unlockHunter.gameObject.name = "Unlock hunter charm";
        hint = Label(transform, "", new Vector2(714f, 70f), new Vector2(0f, -294f), 22f);
        backButton = Button(transform, "К находкам", new Vector2(370f, 66f), new Vector2(0f, -396f), () => back?.Invoke(), out _);
        trainingTab.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit, selectOnRight = recipesTab, selectOnDown = trainingButtons[0] };
        recipesTab.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit, selectOnLeft = trainingTab, selectOnDown = craftWard };
    }

    private GameObject Row(string name, Transform parent, Vector2 size, Vector2 position)
    {
        GameObject row = RectObject(name, parent, size, position);
        row.AddComponent<UnityEngine.UI.Image>().color = new Color32(255, 243, 207, 255);
        return row;
    }
    private TMP_Text Label(Transform parent, string text, Vector2 size, Vector2 position, float fontSize, bool bold = false)
    {
        TMP_Text label = RectObject("Label", parent, size, position).AddComponent<TextMeshProUGUI>();
        label.font = font; label.text = text; label.fontSize = label.fontSizeMax = fontSize;
        label.fontSizeMin = Mathf.Max(18f, fontSize - 3f); label.enableAutoSizing = true;
        label.color = Ink; label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        return label;
    }
    private UnityEngine.UI.Button Button(Transform parent, string text, Vector2 size, Vector2 position, UnityEngine.Events.UnityAction action, out TMP_Text label)
    {
        GameObject root = RectObject(text.Length > 0 ? text : "Action", parent, size, position);
        var image = root.AddComponent<UnityEngine.UI.Image>(); image.color = Amber;
        var outline = root.AddComponent<UnityEngine.UI.Outline>(); outline.effectColor = new Color32(164, 86, 32, 255); outline.effectDistance = new Vector2(2f, -2f);
        var button = root.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
        UnityEngine.UI.ColorBlock colors = button.colors; colors.fadeDuration = 0f; button.colors = colors;
        button.onClick.AddListener(() => { GameAudio.Play(GameAudio.Cue.Click); action(); });
        label = Label(root.transform, text, size - new Vector2(14f, 8f), Vector2.zero, 27f, true);
        return button;
    }
    private static GameObject RectObject(string name, Transform parent, Vector2 size, Vector2 position)
    {
        GameObject root = new GameObject(name, typeof(RectTransform)); RectTransform rect = (RectTransform)root.transform;
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size; rect.anchoredPosition = position; return root;
    }
    private static void Focus(UnityEngine.UI.Button button)
    {
        if (GameplayPreferences.IsDesktopBuild && EventSystem.current != null && button != null)
            EventSystem.current.SetSelectedGameObject(button.gameObject);
    }
    private void OnDestroy()
    {
        PlayerProgress.Changed -= Refresh; ExpeditionProgress.Changed -= Refresh;
    }
}
