using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// A small extension of the persistent menu Canvas. All expedition data and
// eligibility decisions belong to ExpeditionRun/Session, never the widgets.
public sealed class ExpeditionUI : MonoBehaviour
{
    private enum Page { Bag, Rest, Perks, Result }
    private static ExpeditionUI instance;
    private static int closedFrame = -1;
    private static readonly Color Ink = new Color32(94, 49, 24, 255);
    private static readonly Color Paper = new Color32(255, 232, 179, 255);
    private static readonly Color Amber = new Color32(255, 182, 48, 255);
    private GameMenuUI menu;
    private TMP_FontAsset font;
    private RectTransform safeRoot, card, hud;
    private GameObject panel;
    private TMP_Text title, subtitle, hint, bagLabel, hudStatus, combatStatus, talentActionLabel;
    private readonly TMP_Text[] names = new TMP_Text[3], details = new TMP_Text[3];
    private readonly ExpeditionFindIcon[] icons = new ExpeditionFindIcon[3], hudIcons = new ExpeditionFindIcon[3];
    private readonly UnityEngine.UI.Button[] protect = new UnityEngine.UI.Button[3], discard = new UnityEngine.UI.Button[3];
    private readonly UnityEngine.UI.Image[] rowPapers = new UnityEngine.UI.Image[3];
    private UnityEngine.UI.Button close, proceed, extract, done, chooseTalent, talentBack;
    private readonly UnityEngine.UI.Button[] perkButtons = new UnityEngine.UI.Button[3];
    private readonly TMP_Text[] perkLabels = new TMP_Text[3];
    private ExpeditionRun run;
    private ExpeditionResult result;
    private bool resultSaved;
    private Action resultContinue;
    private Page page;
    private float previousTimeScale;
    private GameObject returnFocus;
    private Vector2Int lastScreen;
    private Rect lastSafe;
    private string feedback;

    public static bool IsOpen => instance != null && instance.panel != null && instance.panel.activeSelf;
    public static bool BlocksGameplayInput => IsOpen;
    public static bool ConsumesMenuInput => IsOpen || closedFrame == Time.frameCount;
    public bool IsBagVisible => IsOpen && page == Page.Bag;
    public bool IsRestVisible => IsOpen && page == Page.Rest;
    public bool IsResultVisible => IsOpen && page == Page.Result;
    public bool IsPerkChoiceVisible => IsOpen && page == Page.Perks;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() { instance = null; closedFrame = -1; }

    public static ExpeditionUI Attach(GameMenuUI owner)
    {
        ExpeditionUI ui = owner.GetComponent<ExpeditionUI>();
        if (ui == null) ui = owner.gameObject.AddComponent<ExpeditionUI>();
        ui.menu = owner;
        return ui;
    }

    private void Awake()
    {
        instance = this;
        menu = GetComponent<GameMenuUI>();
        font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF - Fallback");
        Build();
        SceneManager.sceneLoaded += SceneLoaded;
    }

    private void OnDestroy()
    {
        if (run != null) run.Changed -= Refresh;
        SceneManager.sceneLoaded -= SceneLoaded;
        if (instance == this) instance = null;
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Hide(false);
        Bind(null);
        Refresh();
    }

    private void Update()
    {
        Layout();
        if (run != ExpeditionRun.Active) Bind(ExpeditionRun.Active);
        bool showHud = run != null && run.HasStarted && !run.IsFinished && RunFlow.RunActive;
        if (hud.gameObject.activeSelf != showHud) hud.gameObject.SetActive(showHud);
        if (AdminTools.ConsumesMenuInput || RunFlow.IsTransitioning) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (IsOpen && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (page == Page.Rest) { if (run != null && run.NeedsPerkChoice) OpenTalents(); else Continue(); }
            else if (page == Page.Perks) Show(Page.Rest);
            else if (page == Page.Result) CompleteResult();
            else Close();
        }
        else if (keyboard.bKey.wasPressedThisFrame)
        {
            if (IsBagVisible) Close();
            else if (!IsOpen) OpenBag();
        }
    }

    private void Bind(ExpeditionRun next)
    {
        if (run != null) run.Changed -= Refresh;
        run = next;
        if (run != null) run.Changed += Refresh;
        Refresh();
    }

    public void OpenBag()
    {
        if (IsOpen || AdminTools.IsOpen || RunFlow.IsTransitioning || !RunFlow.IsPlaying ||
            Time.timeScale <= 0f || ExpeditionRun.Active == null || !ExpeditionRun.Active.HasStarted ||
            ExpeditionRun.Active.IsFinished || ExpeditionRun.Active.Owner == null || ExpeditionRun.Active.Owner.IsDead) return;
        Bind(ExpeditionRun.Active);
        Show(Page.Bag);
    }

    public void ShowRest(ExpeditionRun owner)
    {
        if (owner == null || !owner.IsResting || owner.IsFinished) return;
        Bind(owner);
        Show(Page.Rest);
    }

    public void ShowResult(ExpeditionResult settlement, Action onContinue, bool rewardsSaved = true)
    {
        if (settlement == null) return;
        result = settlement;
        resultSaved = rewardsSaved;
        resultContinue = onContinue;
        Show(Page.Result);
    }

    private void Show(Page requested)
    {
        if (!IsOpen)
        {
            previousTimeScale = Time.timeScale;
            returnFocus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        }
        page = requested;
        feedback = null;
        panel.SetActive(true);
        Time.timeScale = 0f;
        Refresh(); Layout(true);
        UnityEngine.UI.Button focus = page == Page.Rest ? run != null && run.NeedsPerkChoice ? chooseTalent : proceed :
            page == Page.Perks ? perkButtons[0] : page == Page.Result ? done : close;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(focus.gameObject);
    }

    public void Close()
    {
        if (IsBagVisible) Hide(true);
    }

    private void Hide(bool restoreTime)
    {
        if (!IsOpen) return;
        panel.SetActive(false);
        closedFrame = Time.frameCount;
        if (restoreTime)
            Time.timeScale = previousTimeScale > 0f && RunFlow.IsPlaying && run != null && !run.IsFinished &&
                run.Owner != null && !run.Owner.IsDead ? AdminRuntime.GameSpeed : 0f;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(returnFocus != null && returnFocus.activeInHierarchy ? returnFocus : null);
    }

    private void Continue()
    {
        if (run != null && run.ContinueFromRest()) Hide(true);
    }

    public void OpenTalents()
    {
        if (!IsOpen || run == null || !run.IsResting || run.IsFinished || !run.NeedsPerkChoice) return;
        Show(Page.Perks);
    }

    private void ChoosePerk(int index)
    {
        if (!IsPerkChoiceVisible || run == null || index < 0 || index >= run.PerkChoices.Count) return;
        if (run.TryChoosePerk(run.PerkChoices[index])) Show(Page.Rest);
    }

    private void Extract()
    {
        if (run != null && run.TryExtract()) return;
        feedback = "Вернуться с добычей можно только на безопасном привале.";
        Refresh();
    }

    private void CompleteResult()
    {
        Action continuation = resultContinue;
        resultContinue = null;
        Hide(false);
        if (continuation != null) continuation();
        else menu?.ReturnToHome();
    }

    private void Protect(int slot)
    {
        if (run != null && run.ProtectSlot(slot))
        {
            feedback = "Находка перемещена в защищённое место.";
            Refresh();
        }
    }

    private void Discard(int slot)
    {
        if (run != null && run.DiscardSlot(slot))
        {
            feedback = "Место освобождено. Выброшенную находку вернуть нельзя.";
            Refresh();
        }
    }

    private void Refresh()
    {
        if (panel == null) return;
        ExpeditionSession session = run != null ? run.Session : null;
        bagLabel.text = "Рюкзак " + (session != null ? session.Count : 0) + "/3";
        hudStatus.text = run != null ? run.StatusText : string.Empty;
        combatStatus.text = run != null && run.HasStarted ? "Здоровье " + run.HitPoints + "/" + run.MaximumHitPoints +
            " · урон " + run.StompDamage + " · враги " + run.EnemyKills + (run.HasWard ? " · оберег" : "") : string.Empty;
        for (int i = 0; i < hudIcons.Length; i++)
            hudIcons[i].Kind = session != null ? session.Slots[i] : ExpeditionFindKind.None;
        if (!IsOpen) return;
        bool summary = page == Page.Result;
        bool talents = page == Page.Perks;
        title.text = summary ? resultSaved && result != null && result.Eligible && result.Outcome == ExpeditionOutcome.Extracted ? "Добыча дома!" : "Итоги экспедиции" :
            talents ? "Талант экспедиции" : page == Page.Rest ? "Небесный привал" : "Рюкзак Дудлика";
        subtitle.text = summary ? result != null && result.Eligible && !resultSaved ?
            "Высота: " + result.Height + " · счёт: " + result.Score + "\nНаходки не записаны — хранилище защищено." : FormatResult(result, true) : talents ?
            "Выбери один бонус до конца этой экспедиции.\nПостоянная прокачка остаётся в мастерской." : page == Page.Rest ?
            "Забрать все находки и завершить подъём?\nИли продолжить путь, рискуя незащищённой добычей?" :
            "Три места для находок. Первое — защищённое.\nПри поражении остальные находки будут потеряны.\nЗдоровье — от врагов; колючки и падение — как в классике.";
        for (int i = 0; i < icons.Length; i++)
        {
            ExpeditionFindKind kind = summary && result != null ? result.Slots[i] :
                session != null ? session.Slots[i] : ExpeditionFindKind.None;
            icons[i].Kind = kind;
            bool occupied = ExpeditionCatalog.IsValid(kind);
            bool kept = summary && resultSaved && result != null && result.Eligible && occupied &&
                (result.Outcome == ExpeditionOutcome.Extracted || i == 0);
            names[i].text = occupied ? ExpeditionCatalog.Find(kind).Name : "Пустое место";
            details[i].text = summary ? occupied ? !resultSaved || result != null && !result.Eligible ? "Не записано" : kept ? "Сохранено в кладовой" : "Не удалось донести" : "" :
                !occupied ? i == 0 ? "Защищённое место для находки" : "Пока ничего нет" :
                i == 0 ? "Защищено при поражении" : "Можно защитить вместо первой находки";
            rowPapers[i].color = summary && occupied ? kept ? new Color32(225, 235, 183, 255) :
                new Color32(240, 217, 190, 255) : i == 0 ? new Color32(255, 221, 144, 255) : new Color32(255, 243, 207, 255);
            rowPapers[i].gameObject.SetActive(!talents);
            protect[i].gameObject.SetActive(!summary);
            discard[i].gameObject.SetActive(!summary);
            protect[i].interactable = occupied && i != 0 && session != null && !session.IsSettled;
            discard[i].interactable = occupied && session != null && !session.IsSettled;
        }
        close.gameObject.SetActive(page == Page.Bag);
        proceed.gameObject.SetActive(page == Page.Rest);
        proceed.interactable = run != null && !run.NeedsPerkChoice;
        extract.gameObject.SetActive(page == Page.Rest);
        done.gameObject.SetActive(summary);
        chooseTalent.gameObject.SetActive(page == Page.Rest);
        chooseTalent.interactable = run != null && run.NeedsPerkChoice;
        talentActionLabel.text = run != null && run.NeedsPerkChoice ? "Выбрать талант · " + run.PerkChoices.Count + " варианта" : "Талант выбран";
        talentBack.gameObject.SetActive(talents);
        for (int i = 0; i < perkButtons.Length; i++)
        {
            bool offered = talents && run != null && i < run.PerkChoices.Count;
            perkButtons[i].gameObject.SetActive(offered);
            if (offered)
            {
                ExpeditionPerkKind perk = run.PerkChoices[i];
                perkLabels[i].text = "<b>" + ExpeditionPerks.Name(perk) + "</b>\n" + ExpeditionPerks.Description(perk);
                perkButtons[i].interactable = run.NeedsPerkChoice;
            }
        }
        hint.rectTransform.anchoredPosition = new Vector2(0f, page == Page.Rest ? -211f : -234f);
        hint.rectTransform.sizeDelta = new Vector2(710f, page == Page.Rest ? 52f : 74f);
        hint.text = !string.IsNullOrEmpty(feedback) ? feedback : summary ?
            result != null && !result.Eligible ? "Тестовая попытка: находки и рекорды не сохраняются." :
                !resultSaved ? "Кладовая не подтвердила запись. Прежние находки не изменены." :
                    "Обычные монеты, покупки и образы не потеряны." : session != null && !session.IsEligible ?
            "Тестовая попытка: находки не попадут в кладовую." : talents ?
            "Можно вернуться к привалу и забрать груз без выбора.\nДля продолжения выбери один из доступных талантов." : page == Page.Rest ?
            "Привал не сохраняет весь рюкзак автоматически.\n" + (run != null && run.NeedsPerkChoice ? "Для продолжения выбери талант." : run != null ? run.PerkSummary : "После продолжения риск остаётся.") :
            combatStatus.text + "\n" + (run != null ? run.PerkSummary : "Прыжок и управление от груза не меняются.");
    }

    public static string FormatResult(ExpeditionResult settlement, bool compact)
    {
        if (settlement == null) return "Последней экспедиции пока нет.\nВыбери «Экспедиция» в режиме игры.";
        string outcome = settlement.Outcome == ExpeditionOutcome.Extracted ? "Удачное возвращение" :
            settlement.Outcome == ExpeditionOutcome.Lost ? "Поражение" : "Путь прерван";
        if (!settlement.Eligible) outcome = "Тестовая экспедиция";
        if (compact && !settlement.Eligible)
            return "Высота: " + settlement.Height + " · счёт: " + settlement.Score + "\nТестовая попытка · без сохранения находок";
        return compact ? "Высота: " + settlement.Height + " · счёт: " + settlement.Score +
            "\nСохранено: " + settlement.KeptCount + " · потеряно: " + settlement.LostCount :
            "Последняя экспедиция: " + outcome + "\nВысота: " + settlement.Height + " · сохранено: " + settlement.KeptCount +
            " · потеряно: " + settlement.LostCount;
    }

    private void Build()
    {
        GameObject layer = RectObject("Expedition interface", transform, Vector2.zero, Vector2.zero);
        Stretch((RectTransform)layer.transform);
        Canvas canvas = layer.AddComponent<Canvas>(); canvas.overrideSorting = true; canvas.sortingOrder = 1500;
        layer.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        safeRoot = (RectTransform)RectObject("Expedition safe area", layer.transform, Vector2.zero, Vector2.zero).transform;
        Stretch(safeRoot);
        hud = (RectTransform)RectObject("Expedition HUD", safeRoot, new Vector2(452f, 72f), Vector2.zero).transform;
        hud.anchorMin = hud.anchorMax = new Vector2(0f, 0f); hud.pivot = new Vector2(.5f, .5f); hud.anchoredPosition = new Vector2(250f, 58f);
        Button(hud, "Рюкзак 0/3", new Vector2(210f, 60f), new Vector2(-120f, 0f), OpenBag, out bagLabel).name = "Open expedition bag";
        combatStatus = Label(hud, "", new Vector2(452f, 34f), new Vector2(0f, 68f), 20f);
        combatStatus.outlineColor = new Color32(255, 235, 188, 255); combatStatus.outlineWidth = .16f;
        hudStatus = Label(hud, "", new Vector2(452f, 48f), new Vector2(0f, 114f), 20f);
        hudStatus.outlineColor = new Color32(255, 235, 188, 255); hudStatus.outlineWidth = .16f;
        for (int i = 0; i < 3; i++)
        {
            GameObject cell = RectObject("HUD slot " + i, hud, new Vector2(62f, 62f), new Vector2(28f + i * 70f, 0f));
            cell.AddComponent<UnityEngine.UI.Image>().color = i == 0 ? new Color32(255, 221, 144, 240) : new Color32(255, 240, 203, 220);
            hudIcons[i] = RectObject("Find icon", cell.transform, new Vector2(50f, 50f), Vector2.zero).AddComponent<ExpeditionFindIcon>();
            hudIcons[i].raycastTarget = false;
        }
        hud.gameObject.SetActive(false);
        panel = RectObject("Expedition panel", safeRoot, Vector2.zero, Vector2.zero); Stretch((RectTransform)panel.transform);
        panel.AddComponent<UnityEngine.UI.Image>().color = new Color(0.13f, 0.09f, 0.05f, 0.76f);
        card = (RectTransform)RectObject("Expedition card", panel.transform, new Vector2(780f, 820f), Vector2.zero).transform;
        card.gameObject.AddComponent<UnityEngine.UI.Image>().color = Paper;
        var border = card.gameObject.AddComponent<UnityEngine.UI.Outline>(); border.effectColor = Ink; border.effectDistance = new Vector2(4f, -4f);
        title = Label(card, "", new Vector2(700f, 68f), new Vector2(0f, 345f), 43f);
        subtitle = Label(card, "", new Vector2(714f, 74f), new Vector2(0f, 267f), 24f);
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            GameObject row = RectObject("Bag slot " + i, card, new Vector2(714f, 119f), new Vector2(0f, 157f - i * 135f));
            rowPapers[i] = row.AddComponent<UnityEngine.UI.Image>();
            icons[i] = RectObject("Find icon", row.transform, new Vector2(80f, 80f), new Vector2(-304f, 0f)).AddComponent<ExpeditionFindIcon>();
            icons[i].raycastTarget = false;
            names[i] = Label(row.transform, "", new Vector2(356f, 44f), new Vector2(-65f, 27f), 27f);
            details[i] = Label(row.transform, "", new Vector2(380f, 44f), new Vector2(-53f, -23f), 20f);
            protect[i] = Button(row.transform, "Защитить", new Vector2(164f, 44f), new Vector2(257f, 28f), () => Protect(slot), out _).GetComponent<UnityEngine.UI.Button>();
            discard[i] = Button(row.transform, "Выбросить", new Vector2(164f, 44f), new Vector2(257f, -28f), () => Discard(slot), out _).GetComponent<UnityEngine.UI.Button>();
            // These actions change availability immediately after discarding or swapping.
            // Do not leave an empty slot looking actionable during a color tween.
            UnityEngine.UI.ColorBlock slotColors = protect[i].colors;
            slotColors.fadeDuration = 0f;
            protect[i].colors = slotColors;
            discard[i].colors = slotColors;
        }
        hint = Label(card, "", new Vector2(710f, 74f), new Vector2(0f, -234f), 22f);
        close = Button(card, "Закрыть · B / Esc", new Vector2(420f, 66f), new Vector2(0f, -348f), Close, out _).GetComponent<UnityEngine.UI.Button>();
        extract = Button(card, "Забрать добычу и домой", new Vector2(344f, 70f), new Vector2(-182f, -348f), Extract, out _).GetComponent<UnityEngine.UI.Button>();
        proceed = Button(card, "Продолжить подъём", new Vector2(344f, 70f), new Vector2(182f, -348f), Continue, out _).GetComponent<UnityEngine.UI.Button>();
        done = Button(card, "Домой", new Vector2(420f, 70f), new Vector2(0f, -348f), CompleteResult, out _).GetComponent<UnityEngine.UI.Button>();
        chooseTalent = Button(card, "Выбрать талант", new Vector2(520f, 52f), new Vector2(0f, -273f), OpenTalents, out talentActionLabel).GetComponent<UnityEngine.UI.Button>();
        chooseTalent.name = "Open expedition talents";
        talentBack = Button(card, "К привалу", new Vector2(420f, 66f), new Vector2(0f, -348f), () => Show(Page.Rest), out _).GetComponent<UnityEngine.UI.Button>();
        for (int i = 0; i < perkButtons.Length; i++)
        {
            int index = i;
            perkButtons[i] = Button(card, "", new Vector2(714f, 126f), new Vector2(0f, 157f - i * 135f), () => ChoosePerk(index), out perkLabels[i]).GetComponent<UnityEngine.UI.Button>();
            perkButtons[i].gameObject.name = "Choose expedition perk " + i;
            perkLabels[i].fontSize = perkLabels[i].fontSizeMax = 28f; perkLabels[i].fontSizeMin = 22f;
        }
        panel.SetActive(false);
        Layout(true); Refresh();
    }

    private void Layout(bool force = false)
    {
        if (safeRoot == null || Screen.width <= 0 || Screen.height <= 0) return;
        Vector2Int size = new Vector2Int(Screen.width, Screen.height); Rect safe = Screen.safeArea;
        if (!force && size == lastScreen && safe == lastSafe) return;
        lastScreen = size; lastSafe = safe;
        safeRoot.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
        safeRoot.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
        safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;
        Canvas.ForceUpdateCanvases();
        float fit = Mathf.Min(1f, (safeRoot.rect.width - 44f) / 780f, (safeRoot.rect.height - 44f) / 820f);
        card.localScale = Vector3.one * Mathf.Max(.1f, fit);
    }

    private TMP_Text Label(Transform parent, string value, Vector2 size, Vector2 position, float fontSize)
    {
        TextMeshProUGUI text = RectObject("Label", parent, size, position).AddComponent<TextMeshProUGUI>();
        text.font = font; text.text = value; text.fontSize = fontSize; text.fontSizeMin = Mathf.Max(18f, fontSize - 3f);
        text.fontSizeMax = fontSize; text.enableAutoSizing = true; text.color = Ink;
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        return text;
    }

    private GameObject Button(Transform parent, string value, Vector2 size, Vector2 position, UnityEngine.Events.UnityAction action, out TMP_Text label)
    {
        GameObject root = RectObject(value, parent, size, position);
        var image = root.AddComponent<UnityEngine.UI.Image>(); image.color = Amber;
        var outline = root.AddComponent<UnityEngine.UI.Outline>(); outline.effectColor = new Color32(164, 86, 32, 255); outline.effectDistance = new Vector2(2f, -2f);
        var button = root.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
        UnityEngine.UI.ColorBlock colors = button.colors; colors.fadeDuration = 0f; button.colors = colors;
        button.onClick.AddListener(() => { GameAudio.Play(GameAudio.Cue.Click); action(); });
        label = Label(root.transform, value, size - new Vector2(14f, 8f), Vector2.zero, 25f);
        return root;
    }

    private static GameObject RectObject(string name, Transform parent, Vector2 size, Vector2 position)
    {
        GameObject root = new GameObject(name, typeof(RectTransform)); RectTransform rect = (RectTransform)root.transform;
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size; rect.anchoredPosition = position;
        return root;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
