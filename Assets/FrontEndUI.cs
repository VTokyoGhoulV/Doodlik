using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

// Lives on the existing persistent menu canvas; no additional EventSystem.
public sealed class FrontEndUI : MonoBehaviour
{
    private static readonly Color Ink = new Color32(94, 49, 24, 255);
    private static readonly Color Paper = new Color32(255, 232, 179, 255);
    private static readonly Color Amber = new Color32(255, 182, 48, 255);
    private static TMP_FontAsset font;

    private GameMenuUI menu;
    private GameObject overlay, home, shop, modes, modeOptions, expeditionStash;
    private RectTransform safeRoot, homeCard, shopCard, modesCard, stashCard;
    private TMP_Text modeValue, modeDescription, modeRecord, difficultyDescription;
    private GameObject difficultySection;
    private readonly UnityEngine.UI.Button[] difficultyButtons = new UnityEngine.UI.Button[3];
    private UnityEngine.UI.Button modeButton, homeModeButton, modesDone;
    private Coroutine dropdownAnimation;
    private TMP_Text homeCoins, homeMode, homeDifficulty, homeRecord, shopCoins, shopDescription, shopHint;
    private UnityEngine.UI.Button playButton, shopBack;
    private readonly UnityEngine.UI.Button[] tabs = new UnityEngine.UI.Button[5];
    private readonly Row[] rows = new Row[3];
    private Preview homePreview;
    private Sprite catSprite;
    private int selectedTab;
    private int itemPage;
    private GameObject pageControls;
    private TMP_Text pageLabel;
    private UnityEngine.UI.Button previousPage, nextPage;
    private UnityEngine.UI.Button clearAccessories, promoButton, promoRedeem, promoClose;
    private GameObject promo;
    private RectTransform promoCard;
    private CanvasGroup shopInteraction;
    private TMP_InputField promoInput;
    private TMP_Text promoFeedback;
    private Rect lastSafeArea;
    private Vector2Int lastScreen;
    private bool initialized;
    private ChallengeBoardUI challengeBoard;
    private RunStatisticsUI statistics;
    private ExpeditionWorkshopUI expeditionWorkshop;
    private TMP_Text homeChallenge;
    private TMP_Text kindDescription, stashTotals, stashLastResult;
    private readonly UnityEngine.UI.Button[] kindButtons = new UnityEngine.UI.Button[2];
    private readonly TMP_Text[] stashCounts = new TMP_Text[ExpeditionCatalog.All.Count];
    private TMP_Text modesNotes;

    private sealed class Row
    {
        public GameObject Root;
        public TMP_Text Title, Detail, ActionLabel;
        public UnityEngine.UI.Button Action;
        public Preview Preview;
        public string ItemId;
        public PlayerProgress.Upgrade Upgrade;
        public bool IsUpgrade;
    }

    private sealed class Preview
    {
        public UnityEngine.UI.Image Cat;
        public DoodlikCosmeticPreview Accessory;
        public DoodlikCosmeticPreview Trail;
        public readonly List<DoodlikCosmeticPreview> Accessories = new List<DoodlikCosmeticPreview>();
        public readonly List<DoodlikCosmeticPreview> DrawOrder = new List<DoodlikCosmeticPreview>();
    }

    public bool IsVisible => overlay != null && overlay.activeSelf;
    public bool IsModeScreenVisible => IsVisible && modes != null && modes.activeSelf;
    public bool IsChallengeScreenVisible => IsVisible && challengeBoard != null && challengeBoard.IsVisible;
    public bool IsStatisticsScreenVisible => IsVisible && statistics != null && statistics.IsVisible;
    public bool IsExpeditionStashVisible => IsVisible && expeditionStash != null && expeditionStash.activeSelf;
    public bool IsExpeditionWorkshopVisible => IsVisible && expeditionWorkshop != null && expeditionWorkshop.IsVisible;

    public void Initialize(GameMenuUI owner)
    {
        if (initialized) return;
        initialized = true;
        menu = owner;
        HomeScreenBackdrop.AttachTo(transform);
        font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF - Fallback");
        overlay = RectObject("Start screen", transform, Vector2.zero, Vector2.zero);
        Stretch(overlay.GetComponent<RectTransform>());
        UnityEngine.UI.Image shade = overlay.AddComponent<UnityEngine.UI.Image>();
        shade.color = Color.clear;
        shade.raycastTarget = true;

        safeRoot = RectObject("Safe Area", overlay.transform, Vector2.zero, Vector2.zero).GetComponent<RectTransform>();
        Stretch(safeRoot);
        home = RectObject("Home", safeRoot, new Vector2(780f, 900f), Vector2.zero);
        homeCard = home.GetComponent<RectTransform>();
        PaperCard(home);
        Label(home.transform, "Doodlik", new Vector2(650f, 90f), new Vector2(0f, 365f), 65f, true);
        homePreview = CreatePreview(home.transform, new Vector2(184f, 166f), new Vector2(0f, 242f));
        homeCoins = Label(home.transform, "", new Vector2(650f, 44f), new Vector2(0f, 129f), 29f, true);
        homeModeButton = Button(home.transform, "Режим игры", new Vector2(650f, 54f), new Vector2(0f, 77f), ShowModes, out homeMode);
        homeMode.fontSize = 25f;
        homeMode.fontSizeMin = 21f;
        homeModeButton.GetComponent<UnityEngine.UI.Image>().color = new Color32(245, 208, 138, 255);
        homeDifficulty = Label(home.transform, "", new Vector2(650f, 34f), new Vector2(0f, 29f), 24f, true);
        homeRecord = Label(home.transform, "", new Vector2(700f, 34f), new Vector2(0f, -7f), 23f);
        homeChallenge = Label(home.transform, "", new Vector2(700f, 26f), new Vector2(0f, -35f), 20f);
        playButton = Button(home.transform, "Играть", new Vector2(470f, 68f), new Vector2(0f, -94f),
            () => menu.StartRunFromHome(), out _);
        Button(home.transform, "Испытания", new Vector2(226f, 64f), new Vector2(-122f, -170f), ShowChallenges, out TMP_Text tasksLabel);
        tasksLabel.fontSize = tasksLabel.fontSizeMax = 27f;
        tasksLabel.fontSizeMin = 23f;
        Button(home.transform, "Находки", new Vector2(226f, 64f), new Vector2(122f, -170f), ShowExpeditionStash, out TMP_Text findsLabel);
        findsLabel.fontSize = findsLabel.fontSizeMax = 27f;
        findsLabel.fontSizeMin = 23f;
        Button(home.transform, "Гардероб", new Vector2(470f, 64f), new Vector2(0f, -246f), ShowShop, out _);
        Button(home.transform, "Настройки", new Vector2(226f, 64f), new Vector2(-122f, -322f),
            () => menu.OpenSettingsFromHome(), out _);
        Button(home.transform, "Статистика", new Vector2(226f, 64f), new Vector2(122f, -322f), ShowStatistics, out _);
        Button(home.transform, "Выход", new Vector2(470f, 58f), new Vector2(0f, -398f), () => menu.ExitGame(), out _);
        challengeBoard = ChallengeBoardUI.Create(safeRoot, font, ShowHome, () => menu.CanSelectGameMode);
        statistics = RunStatisticsUI.Create(safeRoot, font, ShowHome);

        shop = RectObject("Wardrobe", safeRoot, new Vector2(780f, 900f), Vector2.zero);
        shopCard = shop.GetComponent<RectTransform>();
        PaperCard(shop);
        shopInteraction = shop.AddComponent<CanvasGroup>();
        Label(shop.transform, "Гардероб", new Vector2(650f, 68f), new Vector2(0f, 391f), 45f, true);
        shopCoins = Label(shop.transform, "", new Vector2(650f, 42f), new Vector2(0f, 339f), 29f, true);
        string[] tabLabels = { "Котики", "Одежда", "Аксессуары", "Следы", "Улучшения" };
        float[] tabWidths = { 108f, 116f, 174f, 108f, 166f };
        float tabLeft = -352f;
        for (int i = 0; i < tabs.Length; i++)
        {
            int tabIndex = i;
            tabs[i] = Button(shop.transform, tabLabels[i], new Vector2(tabWidths[i], 63f),
                new Vector2(tabLeft + tabWidths[i] * 0.5f, 273f), () => SelectTab(tabIndex), out TMP_Text tabText);
            tabText.fontSize = 23f;
            tabText.fontSizeMin = 20f;
            tabLeft += tabWidths[i] + 8f;
        }
        shopDescription = Label(shop.transform, "", new Vector2(700f, 69f), new Vector2(0f, 196f), 23f);
        clearAccessories = Button(shop.transform, "Снять всё", new Vector2(166f, 52f), new Vector2(267f, 196f),
            ClearAccessories, out TMP_Text clearLabel);
        clearLabel.fontSize = 23f;
        clearLabel.fontSizeMin = 21f;
        clearAccessories.gameObject.SetActive(false);
        for (int i = 0; i < rows.Length; i++) rows[i] = CreateRow(i);
        shopHint = Label(shop.transform, "", new Vector2(700f, 38f), new Vector2(0f, -290f), 22f);
        pageControls = RectObject("Wardrobe pages", shop.transform, new Vector2(500f, 46f), new Vector2(0f, -335f));
        previousPage = Button(pageControls.transform, "<", new Vector2(76f, 42f), new Vector2(-145f, 0f), () => ChangePage(-1), out _);
        pageLabel = Label(pageControls.transform, "", new Vector2(180f, 42f), Vector2.zero, 24f);
        nextPage = Button(pageControls.transform, ">", new Vector2(76f, 42f), new Vector2(145f, 0f), () => ChangePage(1), out _);
        shopBack = Button(shop.transform, "Назад", new Vector2(300f, 66f), new Vector2(-173f, -399f), ShowHome, out _);
        promoButton = Button(shop.transform, "Промокод", new Vector2(300f, 66f), new Vector2(173f, -399f), ShowPromoCode, out _);
        shop.SetActive(false);
        CreateModes();
        CreateExpeditionStash();
        expeditionWorkshop = ExpeditionWorkshopUI.Create(safeRoot, font, ShowExpeditionStash, () => menu.CanSelectGameMode);
        CreatePromoCode();
        overlay.SetActive(false);
        UpdateLayout(true);
        PlayerProgress.Changed += Refresh;
        ExpeditionProgress.Changed += Refresh;
    }

    public void ShowHome()
    {
        if (!initialized) return;
        ClosePromoCode(false);
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        home.SetActive(true);
        shop.SetActive(false);
        modes.SetActive(false);
        expeditionStash.SetActive(false);
        expeditionWorkshop?.Hide();
        challengeBoard.Hide();
        statistics.Hide();
        CloseModeOptions();
        Refresh();
        UpdateLayout(true);
        Focus(playButton);
        StartCoroutine(RefreshAfterSceneStart());
    }

    public void Hide()
    {
        if (overlay == null) return;
        ClosePromoCode(false);
        CloseModeOptions();
        challengeBoard?.Hide();
        statistics?.Hide();
        if (expeditionStash != null) expeditionStash.SetActive(false);
        expeditionWorkshop?.Hide();
        overlay.SetActive(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    public bool GoBack()
    {
        if (!IsVisible) return false;
        if (promo != null && promo.activeSelf) { ClosePromoCode(); return true; }
        if (modeOptions != null && modeOptions.activeSelf) { CloseModeOptions(); Focus(modeButton); return true; }
        if (IsExpeditionWorkshopVisible) { ShowExpeditionStash(); return true; }
        if (!shop.activeSelf && !modes.activeSelf && !IsExpeditionStashVisible && !challengeBoard.IsVisible && !statistics.IsVisible) return false;
        ShowHome();
        return true;
    }

    private void ShowShop()
    {
        ClosePromoCode(false);
        challengeBoard.Hide();
        statistics.Hide();
        home.SetActive(false);
        modes.SetActive(false);
        expeditionStash.SetActive(false);
        expeditionWorkshop?.Hide();
        shop.SetActive(true);
        selectedTab = 0;
        itemPage = 0;
        shopHint.text = "Монетки собираются во время игры.";
        shopHint.color = Ink;
        Refresh();
        Focus(tabs[0]);
    }

    public void ShowChallenges()
    {
        if (!initialized || !menu.CanSelectGameMode) return;
        ClosePromoCode(false);
        CloseModeOptions();
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        home.SetActive(false);
        shop.SetActive(false);
        modes.SetActive(false);
        expeditionStash.SetActive(false);
        expeditionWorkshop?.Hide();
        statistics.Hide();
        challengeBoard.Show();
        UpdateLayout(true);
    }

    public void ShowStatistics()
    {
        if (!initialized || !menu.CanSelectGameMode) return;
        ClosePromoCode(false);
        CloseModeOptions();
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        home.SetActive(false);
        shop.SetActive(false);
        modes.SetActive(false);
        expeditionStash.SetActive(false);
        expeditionWorkshop?.Hide();
        challengeBoard.Hide();
        statistics.Show();
        UpdateLayout(true);
    }

    public void ShowExpeditionStash()
    {
        if (!initialized || !menu.CanSelectGameMode) return;
        ClosePromoCode(false); CloseModeOptions();
        overlay.SetActive(true); overlay.transform.SetAsLastSibling();
        home.SetActive(false); shop.SetActive(false); modes.SetActive(false);
        challengeBoard.Hide(); statistics.Hide(); expeditionStash.SetActive(true);
        expeditionWorkshop?.Hide();
        Refresh(); UpdateLayout(true);
        Focus(expeditionStash.GetComponentInChildren<UnityEngine.UI.Button>());
    }

    public void ShowExpeditionWorkshop()
    {
        if (!initialized || !menu.CanSelectGameMode || expeditionWorkshop == null) return;
        ClosePromoCode(false); CloseModeOptions();
        overlay.SetActive(true); overlay.transform.SetAsLastSibling();
        home.SetActive(false); shop.SetActive(false); modes.SetActive(false); expeditionStash.SetActive(false);
        challengeBoard.Hide(); statistics.Hide(); expeditionWorkshop.Show();
        UpdateLayout(true);
    }

    private void CreateExpeditionStash()
    {
        expeditionStash = RectObject("Expedition stash", safeRoot, new Vector2(780f, 900f), Vector2.zero);
        stashCard = expeditionStash.GetComponent<RectTransform>(); PaperCard(expeditionStash);
        Label(expeditionStash.transform, "Кладовая находок", new Vector2(700f, 72f), new Vector2(0f, 375f), 43f, true);
        stashTotals = Label(expeditionStash.transform, "", new Vector2(700f, 70f), new Vector2(0f, 293f), 25f);
        for (int i = 0; i < stashCounts.Length; i++)
        {
            GameObject row = RectObject("Stored find " + i, expeditionStash.transform,
                new Vector2(690f, 85f), new Vector2(0f, 174f - i * 96f));
            row.AddComponent<UnityEngine.UI.Image>().color = new Color32(255, 243, 207, 255);
            ExpeditionFindKind kind = (ExpeditionFindKind)(i + 1);
            ExpeditionFindIcon icon = RectObject("Find illustration", row.transform, new Vector2(74f, 74f), new Vector2(-285f, 0f)).AddComponent<ExpeditionFindIcon>();
            icon.Kind = kind; icon.raycastTarget = false;
            stashCounts[i] = Label(row.transform, "", new Vector2(490f, 74f), new Vector2(43f, 0f), 25f, true);
        }
        stashLastResult = Label(expeditionStash.transform, "", new Vector2(700f, 90f), new Vector2(0f, -216f), 23f);
        Label(expeditionStash.transform,
            "Материалы можно потратить в мастерской.\nУлучшения действуют только в экспедициях.",
            new Vector2(700f, 64f), new Vector2(0f, -303f), 22f);
        Button(expeditionStash.transform, "Назад", new Vector2(340f, 66f), new Vector2(-180f, -396f), ShowHome, out _);
        Button(expeditionStash.transform, "Мастерская", new Vector2(340f, 66f), new Vector2(180f, -396f), ShowExpeditionWorkshop, out _);
        expeditionStash.SetActive(false);
    }

    private void RefreshExpeditionStash()
    {
        if (stashTotals == null) return;
        bool protectedStorage = ExpeditionProgress.IsReadOnly;
        stashTotals.text = protectedStorage ? "Кладовая защищена: неизвестная версия сохранения.\nНовые находки сейчас не записываются." :
            "Сохранено находок: " + ExpeditionProgress.TotalCount + "\nУдачных возвращений: " + ExpeditionProgress.ExtractedRuns;
        for (int i = 0; i < stashCounts.Length; i++)
        {
            ExpeditionFindKind kind = (ExpeditionFindKind)(i + 1);
            stashCounts[i].text = ExpeditionCatalog.Find(kind).Name + (protectedStorage ? "\nЗапись недоступна" : "\nВ кладовой: " + ExpeditionProgress.Count(kind));
        }
        stashLastResult.text = protectedStorage ? "Прежний файл не изменён.\nДля чтения нужна совместимая версия игры." :
            ExpeditionUI.FormatResult(ExpeditionProgress.LastResult, false);
    }

    private void SelectRunKind(GameRunKind kind)
    {
        if (!menu.CanSelectGameMode) return;
        CloseModeOptions();
        if (menu.SelectRunKind(kind)) Refresh();
    }

    private void SelectTab(int index)
    {
        selectedTab = index;
        itemPage = 0;
        shopHint.text = index == 4 ? "Покупка действует только в выбранном режиме." : "Внешний вид не меняет скорость и прыжок.";
        shopHint.color = Ink;
        Refresh();
    }

    private void ChangePage(int delta)
    {
        itemPage = Mathf.Max(0, itemPage + delta);
        Refresh();
        Focus(delta > 0 && nextPage.interactable ? nextPage : previousPage.interactable ? previousPage : tabs[selectedTab]);
    }

    public void Refresh()
    {
        if (!initialized || !IsVisible) return;
        if (catSprite == null)
        {
            Player player = FindAnyObjectByType<Player>();
            if (player != null)
            {
                SpriteRenderer renderer = player.GetComponentInChildren<SpriteRenderer>();
                if (renderer != null) catSprite = renderer.sprite;
            }
        }
        homeCoins.text = shopCoins.text = $"Монетки: {PlayerProgress.Coins}";
        bool expedition = GameplayPreferences.RunKind == GameRunKind.Expedition;
        homeMode.text = (expedition ? "Экспедиция" : "Классика") + " · " + GameplayPreferences.CurrentModeDisplayName + "  >";
        homeDifficulty.text = DifficultyRules.Name(GameplayPreferences.Difficulty) + " · " +
            DifficultyRules.MultiplierLabel(GameplayPreferences.Difficulty);
        homeRecord.text = CurrentRecordsText();
        ChallengeDefinition selectedChallenge = ChallengeCatalog.Find(PlayerProgress.SelectedChallengeId);
        homeChallenge.text = expedition ? "Рюкзак · враги · таланты · мастерская" : selectedChallenge == null ? "Испытание на забег не выбрано" :
            PlayerProgress.IsChallengeCompleted(selectedChallenge.Id) ? "Испытание выполнено — можно выбрать другое" :
                "Испытание: " + selectedChallenge.Name;
        challengeBoard.Refresh();
        statistics.Refresh();
        RefreshModeDetails();
        RefreshExpeditionStash();
        expeditionWorkshop?.Refresh();
        SetPreview(homePreview, PlayerProgress.EquippedId(PlayerProgress.Category.Cat), true);
        bool upgrades = selectedTab == 4;
        shopDescription.text = upgrades
            ? "Улучшения: " + GameplayPreferences.CurrentModeDisplayName + "\nСкорость и высота прыжка остаются прежними."
            : selectedTab == 0 ? "Знакомься с котиками и собирай новые образы."
            : selectedTab == 1 ? "Наряды для приключений. Окрас котика выбирается отдельно."
            : selectedTab == 2 ? "До 3 аксессуаров одновременно.\nКосмошлем подходит любому котику."
            : "Красочные следы за каждым прыжком.";
        bool accessories = selectedTab == 2;
        shopDescription.rectTransform.sizeDelta = new Vector2(accessories ? 496f : 700f, 69f);
        shopDescription.rectTransform.anchoredPosition = new Vector2(accessories ? -96f : 0f, 196f);
        clearAccessories.gameObject.SetActive(accessories);
        clearAccessories.interactable = PlayerProgress.EquippedAccessories.Count > 0;
        for (int i = 0; i < tabs.Length; i++)
            tabs[i].GetComponent<UnityEngine.UI.Image>().color = i == selectedTab ? Amber : new Color32(245, 208, 138, 255);

        if (upgrades)
        {
            pageControls.SetActive(false);
            shopHint.gameObject.SetActive(true);
            for (int i = 0; i < rows.Length; i++)
            {
                Row row = rows[i];
                row.Root.SetActive(i < 2);
                if (i >= 2) continue;
                row.IsUpgrade = true;
                row.Upgrade = (PlayerProgress.Upgrade)i;
                int level = PlayerProgress.GetUpgradeLevel(row.Upgrade, RunFlow.ModeKey);
                int max = PlayerProgress.MaxUpgradeLevel(row.Upgrade);
                int price = PlayerProgress.UpgradePrice(row.Upgrade, RunFlow.ModeKey);
                row.Title.text = (i == 0 ? "Ловкие лапки" : "Долгий эффект") + $"  {level}/{max}";
                System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
                string currentBonus = i == 0 ? "Радиус: +" + (level * 0.12f).ToString("0.00", culture) + " ед." : "Длительность: +" + (level * 0.5f).ToString("0.0", culture) + " с";
                string nextBonus = level >= max ? "Максимальный уровень" : i == 0
                    ? "Далее: +" + ((level + 1) * 0.12f).ToString("0.00", culture) + " ед." : "Далее: +" + ((level + 1) * 0.5f).ToString("0.0", culture) + " с";
                row.Detail.text = currentBonus + "\n" + nextBonus;
                row.ActionLabel.text = level >= max ? "Максимум" : $"Улучшить\n{price} монет";
                row.Action.interactable = level < max && PlayerProgress.Coins >= price;
                SetPreview(row.Preview, i == 0 ? "upgrade_magnet" : "upgrade_duration", false);
            }
        }
        else
        {
            int count = 0;
            foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
                if ((int)item.Category == selectedTab && IsVisibleInWardrobe(item)) count++;
            int pages = Mathf.Max(1, Mathf.CeilToInt(count / (float)rows.Length));
            itemPage = Mathf.Clamp(itemPage, 0, pages - 1);
            pageControls.SetActive(pages > 1);
            shopHint.gameObject.SetActive(true);
            pageLabel.text = (itemPage + 1) + " / " + pages;
            previousPage.interactable = itemPage > 0;
            nextPage.interactable = itemPage < pages - 1;
            int rowIndex = 0;
            int itemIndex = 0;
            foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
            {
                if ((int)item.Category != selectedTab || !IsVisibleInWardrobe(item)) continue;
                if (itemIndex++ < itemPage * rows.Length) continue;
                if (rowIndex >= rows.Length) break;
                Row row = rows[rowIndex++];
                row.Root.SetActive(true);
                row.ItemId = item.Id;
                row.IsUpgrade = false;
                row.Title.text = item.Name;
                bool owned = PlayerProgress.Owns(item.Id);
                bool equipped = PlayerProgress.IsEquipped(item.Id);
                bool locked = !owned && item.Price < 0;
                bool removable = equipped && item.Category == PlayerProgress.Category.Accessory && item.Id != "accessory_none";
                row.Detail.text = locked ? PlayerProgress.UnlockDescription(item.Id)
                    : equipped ? "Надето" : owned ? "Уже в гардеробе" : $"Цена: {item.Price} монет";
                row.ActionLabel.text = locked ? "Закрыто" : removable ? "Снять" : equipped ? "Надето"
                    : owned ? "Надеть" : PlayerProgress.Coins < item.Price ? "Не хватает\nмонет" : $"Купить\n{item.Price} монет";
                if (item.Id.EndsWith("_none") && equipped) row.ActionLabel.text = "Выбрано";
                if (item.Id == "accessory_none" && !equipped) row.ActionLabel.text = "Снять всё";
                row.Action.interactable = !locked && (removable || !equipped && (owned || PlayerProgress.Coins >= item.Price));
                SetPreview(row.Preview, item.Id, false);
            }
            for (; rowIndex < rows.Length; rowIndex++) rows[rowIndex].Root.SetActive(false);
        }
    }

    private static bool IsVisibleInWardrobe(PlayerProgress.Item item)
    {
        // The code-only outfit is a surprise, not a locked shop preview.
        // Ownership is saved by PlayerProgress, so the whole set reappears
        // after redemption and on every subsequent launch.
        switch (item.Id)
        {
            case "cat_ghoul":
            case "clothing_ghoul":
            case "accessory_ghoul_mask":
            case "accessory_ghoul_hair":
                return PlayerProgress.Owns(item.Id);
            default:
                return true;
        }
    }

    private IEnumerator RefreshAfterSceneStart()
    {
        yield return null;
        if (!IsVisible) yield break;
        Refresh();
        // BeforeSceneLoad creates the home screen before the scene EventSystem.
        if (home.activeSelf) Focus(playButton);
    }

    private Row CreateRow(int index)
    {
        Row row = new Row();
        row.Root = RectObject("Item " + index, shop.transform, new Vector2(704f, 132f), new Vector2(0f, 104f - 152f * index));
        UnityEngine.UI.Image paper = row.Root.AddComponent<UnityEngine.UI.Image>();
        paper.color = new Color32(255, 244, 214, 255);
        paper.raycastTarget = false;
        row.Preview = CreatePreview(row.Root.transform, new Vector2(104f, 103f), new Vector2(-279f, 0f));
        row.Title = Label(row.Root.transform, "", new Vector2(289f, 47f), new Vector2(-65f, 29f), 27f, true);
        row.Title.alignment = TextAlignmentOptions.Left;
        row.Title.fontSizeMin = 20f;
        row.Title.textWrappingMode = TextWrappingModes.NoWrap;
        row.Detail = Label(row.Root.transform, "", new Vector2(289f, 66f), new Vector2(-65f, -27f), 22f);
        row.Detail.alignment = TextAlignmentOptions.Left;
        row.Action = Button(row.Root.transform, "", new Vector2(189f, 73f), new Vector2(234f, 0f), () => ActivateRow(row), out row.ActionLabel);
        row.ActionLabel.fontSize = 24f;
        row.ActionLabel.fontSizeMin = 21f;
        return row;
    }

    private void ActivateRow(Row row)
    {
        bool succeeded;
        if (row.IsUpgrade)
        {
            succeeded = PlayerProgress.TryUpgrade(row.Upgrade, RunFlow.ModeKey);
            shopHint.text = succeeded ? "Улучшение куплено для текущего режима." : "Недостаточно монет.";
        }
        else
        {
            bool wasEquipped = PlayerProgress.IsEquipped(row.ItemId);
            bool accessory = row.ItemId.StartsWith("accessory_");
            succeeded = PlayerProgress.Owns(row.ItemId) || PlayerProgress.TryPurchase(row.ItemId);
            if (succeeded) succeeded = PlayerProgress.Equip(row.ItemId);
            shopHint.text = succeeded
                ? row.ItemId == "accessory_none" ? "Все аксессуары сняты."
                : accessory && wasEquipped ? "Аксессуар снят."
                : accessory ? "Аксессуар надет. Детали в одном месте заменяют друг друга."
                : "Готово! Новый образ уже надет."
                : "Недостаточно монет или предмет ещё не открыт.";
        }
        shopHint.color = succeeded ? Ink : new Color32(145, 52, 35, 255);
        Refresh();
        if (succeeded) GameAudio.Play(GameAudio.Cue.Purchase);
        if (!row.Action.interactable) Focus(tabs[selectedTab]);
    }

    private void ClearAccessories()
    {
        if (!PlayerProgress.Equip("accessory_none")) return;
        shopHint.text = "Все аксессуары сняты. Остальной образ сохранён.";
        shopHint.color = Ink;
        Refresh();
        Focus(tabs[2]);
    }

    private void CreatePromoCode()
    {
        promo = RectObject("Promo Code", safeRoot, Vector2.zero, Vector2.zero);
        Stretch(promo.GetComponent<RectTransform>());
        UnityEngine.UI.Image shade = promo.AddComponent<UnityEngine.UI.Image>();
        shade.color = new Color(0.18f, 0.10f, 0.05f, 0.48f);
        shade.raycastTarget = true;
        GameObject card = RectObject("Promo paper", promo.transform, new Vector2(700f, 430f), Vector2.zero);
        promoCard = card.GetComponent<RectTransform>();
        PaperCard(card);
        Label(card.transform, "Промокод", new Vector2(620f, 62f), new Vector2(0f, 166f), 43f, true);
        Label(card.transform, "Есть код? Введи его и получи подарок.", new Vector2(620f, 49f), new Vector2(0f, 107f), 25f);

        GameObject field = RectObject("Promo input", card.transform, new Vector2(612f, 76f), new Vector2(0f, 37f));
        UnityEngine.UI.Image fieldPaper = field.AddComponent<UnityEngine.UI.Image>();
        fieldPaper.color = new Color32(255, 249, 231, 255);
        UnityEngine.UI.Outline outline = field.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = Ink;
        outline.effectDistance = new Vector2(2f, -2f);
        GameObject viewport = RectObject("Text Area", field.transform, Vector2.zero, Vector2.zero);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewportRect.offsetMin = new Vector2(18f, 8f);
        viewportRect.offsetMax = new Vector2(-18f, -8f);
        viewport.AddComponent<UnityEngine.UI.RectMask2D>();
        TMP_Text inputText = Label(viewport.transform, "", Vector2.zero, Vector2.zero, 30f);
        Stretch(inputText.rectTransform);
        inputText.name = "Entered code";
        inputText.enableAutoSizing = false;
        inputText.richText = false;
        inputText.textWrappingMode = TextWrappingModes.NoWrap;
        inputText.alignment = TextAlignmentOptions.MidlineLeft;
        TMP_Text placeholder = Label(viewport.transform, "Введи промокод", Vector2.zero, Vector2.zero, 28f);
        Stretch(placeholder.rectTransform);
        placeholder.name = "Placeholder";
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        placeholder.color = new Color32(142, 112, 82, 255);
        promoInput = field.AddComponent<TMP_InputField>();
        promoInput.targetGraphic = fieldPaper;
        promoInput.textViewport = viewportRect;
        promoInput.textComponent = (TextMeshProUGUI)inputText;
        promoInput.placeholder = placeholder;
        promoInput.contentType = TMP_InputField.ContentType.Standard;
        promoInput.lineType = TMP_InputField.LineType.SingleLine;
        promoInput.characterLimit = 48;
        promoInput.richText = false;
        promoInput.keyboardType = TouchScreenKeyboardType.ASCIICapable;
        promoInput.shouldHideSoftKeyboard = false;
        promoInput.caretColor = Ink;
        promoInput.customCaretColor = true;
        promoInput.selectionColor = new Color(1f, 0.71f, 0.19f, 0.45f);
        promoInput.onValueChanged.AddListener(_ =>
        {
            promoRedeem.interactable = !string.IsNullOrWhiteSpace(promoInput.text);
            promoFeedback.color = Ink;
            promoFeedback.text = "Каждый подарок по коду можно получить один раз.";
        });
        promoInput.onSubmit.AddListener(_ => RedeemPromoCode());
        promoFeedback = Label(card.transform, "", new Vector2(622f, 90f), new Vector2(0f, -52f), 24f);
        promoFeedback.richText = false;
        promoRedeem = Button(card.transform, "Получить", new Vector2(290f, 70f), new Vector2(-160f, -153f), RedeemPromoCode, out _);
        promoClose = Button(card.transform, "Закрыть", new Vector2(266f, 70f), new Vector2(162f, -153f), () => ClosePromoCode(), out _);
        UnityEngine.UI.Navigation fieldNavigation = promoInput.navigation;
        fieldNavigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
        fieldNavigation.selectOnDown = promoRedeem;
        fieldNavigation.selectOnUp = promoClose;
        promoInput.navigation = fieldNavigation;
        UnityEngine.UI.Navigation redeemNavigation = promoRedeem.navigation;
        redeemNavigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
        redeemNavigation.selectOnUp = promoInput;
        redeemNavigation.selectOnLeft = redeemNavigation.selectOnRight = promoClose;
        promoRedeem.navigation = redeemNavigation;
        UnityEngine.UI.Navigation closeNavigation = promoClose.navigation;
        closeNavigation.mode = UnityEngine.UI.Navigation.Mode.Explicit;
        closeNavigation.selectOnUp = promoInput;
        closeNavigation.selectOnLeft = closeNavigation.selectOnRight = promoRedeem;
        promoClose.navigation = closeNavigation;
        promo.SetActive(false);
    }

    private void ShowPromoCode()
    {
        if (!IsVisible || !shop.activeSelf) return;
        promo.SetActive(true);
        promo.transform.SetAsLastSibling();
        shopInteraction.interactable = false;
        shopInteraction.blocksRaycasts = false;
        promoInput.SetTextWithoutNotify("");
        promoRedeem.interactable = false;
        promoFeedback.text = "Каждый подарок по коду можно получить один раз.";
        promoFeedback.color = Ink;
        UpdatePromoLayout();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(promoInput.gameObject);
        // TMP opens the native keyboard on touch devices and accepts the same
        // onSubmit flow for Return / mobile Done without a separate EventSystem.
        promoInput.ActivateInputField();
    }

    private void RedeemPromoCode()
    {
        if (promo == null || !promo.activeSelf || string.IsNullOrWhiteSpace(promoInput.text)) return;
        bool redeemed = PlayerProgress.TryRedeemCode(promoInput.text, out string message);
        promoFeedback.text = message;
        promoFeedback.color = redeemed ? new Color32(65, 105, 43, 255) : new Color32(151, 52, 35, 255);
        if (redeemed)
        {
            promoInput.SetTextWithoutNotify("");
            promoRedeem.interactable = false;
            promoInput.DeactivateInputField();
            shopHint.text = "Подарок получен! Новые вещи появились в гардеробе.";
            shopHint.color = Ink;
            GameAudio.Play(GameAudio.Cue.Purchase);
            Focus(promoClose);
        }
        Refresh();
    }

    private void ClosePromoCode(bool restoreFocus = true)
    {
        if (promo == null || !promo.activeSelf) return;
        promoInput.DeactivateInputField();
        promo.SetActive(false);
        shopInteraction.interactable = true;
        shopInteraction.blocksRaycasts = true;
        if (restoreFocus) Focus(promoButton);
    }

    private void UpdatePromoLayout()
    {
        if (promoCard == null || safeRoot == null) return;
        Vector2 available = safeRoot.rect.size;
        float covered = 0f;
        if (TouchScreenKeyboard.visible && Screen.height > 0 && Screen.safeArea.height > 0f)
        {
            Rect keyboard = TouchScreenKeyboard.area;
            // Some Android keyboards do not report their native inset. Keep
            // the input and actions in the upper half in that case.
            float keyboardTop = keyboard.height > 0f ? keyboard.yMax : Screen.height * 0.45f;
            covered = Mathf.Clamp01((keyboardTop - Screen.safeArea.yMin) / Screen.safeArea.height) * available.y;
        }
        float height = Mathf.Max(80f, available.y - covered);
        float scale = Mathf.Min(1f, (available.x - 44f) / 700f, (height - 36f) / 430f);
        promoCard.localScale = Vector3.one * Mathf.Max(0.1f, scale);
        promoCard.anchoredPosition = Vector2.up * (covered * 0.5f);
    }

    private void Update()
    {
        if (IsVisible)
        {
            UpdateLayout(false);
            if (promo != null && promo.activeSelf) UpdatePromoLayout();
        }
    }

    private void UpdateLayout(bool force)
    {
        if (safeRoot == null || Screen.width <= 0 || Screen.height <= 0) return;
        Vector2Int size = new Vector2Int(Screen.width, Screen.height);
        Rect safe = Screen.safeArea;
        if (!force && size == lastScreen && safe == lastSafeArea) return;
        lastScreen = size;
        lastSafeArea = safe;
        safeRoot.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
        safeRoot.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
        safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;
        Canvas.ForceUpdateCanvases();
        Vector2 available = safeRoot.rect.size;
        float scale = Mathf.Min(1f, (available.x - 44f) / 780f, (available.y - 44f) / 900f);
        homeCard.localScale = shopCard.localScale = modesCard.localScale = Vector3.one * Mathf.Max(0.1f, scale);
        if (stashCard != null) stashCard.localScale = Vector3.one * Mathf.Max(0.1f, scale);
        challengeBoard.ApplyScale(scale);
        statistics.ApplyScale(scale);
        expeditionWorkshop?.ApplyScale(Mathf.Max(.1f, scale));
    }

    private void CreateModes()
    {
        modes = RectObject("Game Modes", safeRoot, new Vector2(780f, 900f), Vector2.zero);
        modesCard = modes.GetComponent<RectTransform>();
        PaperCard(modes);
        Label(modes.transform, "Твоё путешествие", new Vector2(690f, 70f), new Vector2(0f, 385f), 43f, true);
        for (int i = 0; i < kindButtons.Length; i++)
        {
            GameRunKind kind = (GameRunKind)i;
            kindButtons[i] = Button(modes.transform, i == 0 ? "Классика" : "Экспедиция",
                new Vector2(326f, 62f), new Vector2(i == 0 ? -175f : 175f, 311f), () => SelectRunKind(kind), out TMP_Text kindLabel);
            kindButtons[i].gameObject.name = "Run kind " + kind;
            kindLabel.fontSize = kindLabel.fontSizeMax = 28f; kindLabel.fontSizeMin = 24f;
        }
        kindDescription = Label(modes.transform, "", new Vector2(680f, 60f), new Vector2(0f, 239f), 23f);
        modeButton = Button(modes.transform, "", new Vector2(676f, 66f), new Vector2(0f, 162f), ToggleModeOptions, out modeValue);
        modeValue.fontSize = 29f;
        modeValue.fontSizeMin = 25f;
        modeDescription = Label(modes.transform, "", new Vector2(658f, 58f), new Vector2(0f, 86f), 23f);
        difficultySection = RectObject("Difficulty choices", modes.transform, new Vector2(682f, 176f), new Vector2(0f, -61f));
        Label(difficultySection.transform, "Сложность", new Vector2(650f, 34f), new Vector2(0f, 70f), 27f, true);
        for (int i = 0; i < difficultyButtons.Length; i++)
        {
            GameDifficulty difficulty = (GameDifficulty)i;
            difficultyButtons[i] = Button(difficultySection.transform,
                DifficultyRules.Name(difficulty) + "\n" + DifficultyRules.MultiplierLabel(difficulty),
                new Vector2(212f, 76f), new Vector2((i - 1) * 226f, 0f),
                () => SelectDifficulty(difficulty), out TMP_Text difficultyLabel);
            difficultyButtons[i].gameObject.name = "Difficulty " + difficulty;
            difficultyLabel.fontSize = difficultyLabel.fontSizeMax = 25f;
            difficultyLabel.fontSizeMin = 21f;
        }
        difficultyDescription = Label(difficultySection.transform, "", new Vector2(660f, 62f),
            new Vector2(0f, -76f), 22f);
        modeRecord = Label(modes.transform, "", new Vector2(650f, 64f), new Vector2(0f, -215f), 26f, true);
        modesNotes = Label(modes.transform, "", new Vector2(690f, 72f), new Vector2(0f, -291f), 22f);
        modesDone = Button(modes.transform, "Готово", new Vector2(370f, 73f), new Vector2(0f, -386f), ShowHome, out _);

        int count = GameplayPreferences.IsDesktopBuild ? 2 : 4;
        float height = 24f + count * 78f;
        modeOptions = RectObject("Mode options", modes.transform, new Vector2(682f, height), new Vector2(0f, 122f - height * 0.5f));
        PaperCard(modeOptions);
        modeOptions.AddComponent<CanvasGroup>();
        for (int i = 0; i < count; i++)
        {
            bool portrait = i >= 2;
            bool inertia = i % 2 == 0;
            string title = GameplayPreferences.IsDesktopBuild ? (inertia ? "С инерцией" : "Точный контроль") :
                (portrait ? "Книжный · " : "Альбомный · ") + (inertia ? "с инерцией" : "точный контроль");
            UnityEngine.UI.Button option = Button(modeOptions.transform, title,
                new Vector2(642f, 66f), new Vector2(0f, height * 0.5f - 51f - i * 78f),
                () => SelectMode(portrait, inertia), out TMP_Text text);
            option.gameObject.name = "Mode " + i;
            text.fontSize = 28f;
            text.fontSizeMin = 25f;
        }
        modeOptions.SetActive(false);
        modes.SetActive(false);
    }

    public void ShowModes()
    {
        if (!initialized || RunFlow.RunActive) return;
        ClosePromoCode(false);
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        home.SetActive(false);
        shop.SetActive(false);
        challengeBoard.Hide();
        statistics.Hide();
        modes.SetActive(true);
        expeditionStash.SetActive(false);
        expeditionWorkshop?.Hide();
        CloseModeOptions();
        Refresh();
        UpdateLayout(true);
        Focus(kindButtons[(int)GameplayPreferences.RunKind]);
    }

    private void RefreshModeDetails()
    {
        if (modeValue == null) return;
        modeValue.text = GameplayPreferences.CurrentModeDisplayName + "  ▼";
        bool expedition = GameplayPreferences.RunKind == GameRunKind.Expedition;
        kindDescription.text = expedition ? "Бей жуков прыжком сверху. На привале выбирай талант.\nПринеси груз домой и стань сильнее в мастерской." :
            "Знакомый подъём к космосу: платформы, ловушки и монеты.\nФизика и управление одинаковые в обоих путешествиях.";
        modesNotes.text = expedition ? "Находки и рекорды — отдельно.\nИспытания и награда за " + PlayerProgress.AstronautHeight + " работают.\nОбщий журнал учитывает только классику." :
            "Высота открывает новые зоны; счёт учитывает сложность.\nРекорды отдельные для каждого сочетания.";
        for (int i = 0; i < kindButtons.Length; i++)
        {
            kindButtons[i].GetComponent<UnityEngine.UI.Image>().color = i == (int)GameplayPreferences.RunKind ? Amber : new Color32(245, 208, 138, 255);
            kindButtons[i].interactable = menu.CanSelectGameMode;
            kindButtons[i].navigation = new UnityEngine.UI.Navigation
            {
                mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnLeft = i > 0 ? kindButtons[i - 1] : null,
                selectOnRight = i + 1 < kindButtons.Length ? kindButtons[i + 1] : null,
                selectOnDown = modeButton
            };
        }
        modeDescription.text = GameplayPreferences.UseInertia
            ? "Плавный разгон и воздушная инерция.\nПланируй повороты заранее."
            : "Мгновенный отклик на движение.\nМеняй направление точно и быстро.";
        modeRecord.text = CurrentRecordsText(true);
        GameDifficulty difficulty = GameplayPreferences.Difficulty;
        difficultyDescription.text = difficulty == GameDifficulty.Easy
            ? "Шире платформы, спокойнее ловушки.\nБольше запаса на прыжках."
            : difficulty == GameDifficulty.Hard
                ? "Уже платформы, чаще длинные прыжки.\nЛовушек больше, и они быстрее."
                : "Прежний баланс платформ и ловушек.\nВысота и счёт равны.";
        for (int i = 0; i < difficultyButtons.Length; i++)
        {
            difficultyButtons[i].GetComponent<UnityEngine.UI.Image>().color = i == (int)difficulty
                ? Amber : new Color32(245, 208, 138, 255);
            difficultyButtons[i].interactable = menu.CanSelectGameMode;
            difficultyButtons[i].navigation = new UnityEngine.UI.Navigation
            {
                mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnLeft = i > 0 ? difficultyButtons[i - 1] : null,
                selectOnRight = i + 1 < difficultyButtons.Length ? difficultyButtons[i + 1] : null,
                selectOnUp = modeButton,
                selectOnDown = modesDone
            };
        }
        UnityEngine.UI.Button selectedDifficulty = difficultyButtons[(int)difficulty];
        modeButton.navigation = new UnityEngine.UI.Navigation
        {
            mode = UnityEngine.UI.Navigation.Mode.Explicit,
            selectOnUp = kindButtons[(int)GameplayPreferences.RunKind],
            selectOnDown = selectedDifficulty
        };
        modesDone.navigation = new UnityEngine.UI.Navigation
        {
            mode = UnityEngine.UI.Navigation.Mode.Explicit,
            selectOnUp = selectedDifficulty
        };
        UnityEngine.UI.Button[] options = modeOptions.GetComponentsInChildren<UnityEngine.UI.Button>(true);
        int selected = (GameplayPreferences.IsPortrait ? 2 : 0) + (GameplayPreferences.UseInertia ? 0 : 1);
        for (int i = 0; i < options.Length; i++)
            options[i].GetComponent<UnityEngine.UI.Image>().color = i == selected ? Amber : new Color32(245, 208, 138, 255);
    }

    private void ToggleModeOptions()
    {
        if (!menu.CanSelectGameMode) return;
        if (modeOptions.activeSelf) { CloseModeOptions(); return; }
        modeOptions.SetActive(true);
        modeDescription.gameObject.SetActive(false);
        modeRecord.gameObject.SetActive(false);
        difficultySection.SetActive(false);
        dropdownAnimation = StartCoroutine(AnimateModeOptions());
        UnityEngine.UI.Button[] options = modeOptions.GetComponentsInChildren<UnityEngine.UI.Button>();
        int selected = (GameplayPreferences.IsPortrait ? 2 : 0) + (GameplayPreferences.UseInertia ? 0 : 1);
        if (selected < options.Length) Focus(options[selected]);
    }

    private IEnumerator AnimateModeOptions()
    {
        CanvasGroup group = modeOptions.GetComponent<CanvasGroup>();
        float elapsed = 0f;
        while (elapsed < 0.14f)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 0.14f));
            group.alpha = t;
            modeOptions.transform.localScale = new Vector3(1f, Mathf.Lerp(0.94f, 1f, t), 1f);
            yield return null;
        }
        group.alpha = 1f;
        modeOptions.transform.localScale = Vector3.one;
        dropdownAnimation = null;
    }

    private void CloseModeOptions()
    {
        if (dropdownAnimation != null) { StopCoroutine(dropdownAnimation); dropdownAnimation = null; }
        if (modeOptions != null) modeOptions.SetActive(false);
        if (modeDescription != null) modeDescription.gameObject.SetActive(true);
        if (modeRecord != null) modeRecord.gameObject.SetActive(true);
        if (difficultySection != null) difficultySection.SetActive(true);
    }

    private void SelectMode(bool portrait, bool inertia)
    {
        if (!menu.CanSelectGameMode) return;
        CloseModeOptions();
        menu.SelectGameMode(portrait, inertia);
        Refresh();
        Focus(modeButton);
    }

    private void SelectDifficulty(GameDifficulty difficulty)
    {
        if (!menu.CanSelectGameMode) return;
        CloseModeOptions();
        if (menu.SelectDifficulty(difficulty)) Refresh();
    }

    private static string CurrentRecordsText(bool multiline = false)
    {
        int height = Mathf.Max(0, PlayerPrefs.GetInt(RunRecords.CurrentHeightKey, 0));
        int score = Mathf.Max(0, PlayerPrefs.GetInt(RunRecords.CurrentScoreKey, 0));
        return multiline
            ? $"Рекорд высоты: {height}\nРекорд счёта: {score}"
            : $"Рекорды · высота: {height} · счёт: {score}";
    }

    private void SetPreview(Preview preview, string id, bool equippedAccessories)
    {
        bool cat = id.StartsWith("cat_");
        bool clothing = id.StartsWith("clothing_");
        bool astronautHelmet = id == "accessory_astronaut_helmet";
        preview.Cat.sprite = catSprite;
        preview.Cat.enabled = catSprite != null && (cat || clothing || astronautHelmet);
        CatAppearance.ConfigurePreview(preview.Cat, cat ? id : PlayerProgress.EquippedId(PlayerProgress.Category.Cat),
            clothing ? id : PlayerProgress.EquippedId(PlayerProgress.Category.Clothing));
        IReadOnlyList<string> equipped = equippedAccessories ? PlayerProgress.EquippedAccessories : null;
        int accessories = equipped != null ? equipped.Count : 1;
        while (preview.Accessories.Count < accessories)
            preview.Accessories.Add(CreateAccessoryGraphic(preview.Cat.transform.parent, "Accessory " + preview.Accessories.Count));
        for (int i = 0; i < preview.Accessories.Count; i++)
        {
            string accessoryId = equipped != null ? i < equipped.Count ? equipped[i] : "accessory_none"
                : i == 0 && !cat && !clothing ? id : "accessory_none";
            preview.Accessories[i].Configure(accessoryId, equippedAccessories || astronautHelmet);
            preview.Accessories[i].enabled = !accessoryId.EndsWith("_none");
        }
        preview.DrawOrder.Clear();
        preview.DrawOrder.AddRange(preview.Accessories);
        preview.DrawOrder.Sort((a, b) => WardrobeArt.SortLayer(a.ItemId).CompareTo(WardrobeArt.SortLayer(b.ItemId)));
        foreach (DoodlikCosmeticPreview graphic in preview.DrawOrder) graphic.transform.SetAsLastSibling();
        preview.Trail.Configure(equippedAccessories ? PlayerProgress.EquippedId(PlayerProgress.Category.Trail) : "trail_none", equippedAccessories);
    }

    private static Preview CreatePreview(Transform parent, Vector2 size, Vector2 position)
    {
        GameObject root = RectObject("Preview", parent, size, position);
        Preview preview = new Preview();
        GameObject ribbon = RectObject("Ribbon", root.transform, Vector2.zero, Vector2.zero);
        Stretch(ribbon.GetComponent<RectTransform>());
        preview.Trail = ribbon.AddComponent<DoodlikCosmeticPreview>();
        preview.Trail.raycastTarget = false;
        GameObject cat = RectObject("Cat", root.transform, Vector2.zero, Vector2.zero);
        Stretch(cat.GetComponent<RectTransform>());
        preview.Cat = cat.AddComponent<UnityEngine.UI.Image>();
        preview.Cat.preserveAspect = true;
        preview.Cat.raycastTarget = false;
        preview.Accessory = CreateAccessoryGraphic(root.transform, "Accessory");
        preview.Accessories.Add(preview.Accessory);
        return preview;
    }

    private static DoodlikCosmeticPreview CreateAccessoryGraphic(Transform parent, string name)
    {
        GameObject accessory = RectObject(name, parent, Vector2.zero, Vector2.zero);
        Stretch(accessory.GetComponent<RectTransform>());
        DoodlikCosmeticPreview graphic = accessory.AddComponent<DoodlikCosmeticPreview>();
        graphic.raycastTarget = false;
        return graphic;
    }

    private static void PaperCard(GameObject card)
    {
        UnityEngine.UI.Image paper = card.AddComponent<UnityEngine.UI.Image>();
        paper.color = Paper;
        paper.raycastTarget = true;
        UnityEngine.UI.Outline outline = card.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = Ink;
        outline.effectDistance = new Vector2(6f, -6f);
        UnityEngine.UI.Shadow shadow = card.AddComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = new Color(0.12f, 0.08f, 0.04f, 0.32f);
        shadow.effectDistance = new Vector2(10f, -11f);
    }

    private static TMP_Text Label(Transform parent, string text, Vector2 size, Vector2 position, float fontSize, bool bold = false)
    {
        TMP_Text label = RectObject("Label", parent, size, position).AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = text;
        label.fontSize = fontSize;
        label.fontSizeMin = Mathf.Max(18f, fontSize - 4f);
        label.fontSizeMax = fontSize;
        label.enableAutoSizing = true;
        label.color = Ink;
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.raycastTarget = false;
        return label;
    }

    private static UnityEngine.UI.Button Button(Transform parent, string text, Vector2 size, Vector2 position, UnityAction callback, out TMP_Text label)
    {
        GameObject target = RectObject(text.Length > 0 ? text : "Action", parent, size, position);
        UnityEngine.UI.Image image = target.AddComponent<UnityEngine.UI.Image>();
        image.color = Amber;
        UnityEngine.UI.Outline outline = target.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color32(164, 86, 32, 255);
        outline.effectDistance = new Vector2(3f, -3f);
        UnityEngine.UI.Button button = target.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        UnityEngine.UI.ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.96f, 0.78f);
        colors.pressedColor = new Color(0.88f, 0.75f, 0.55f);
        colors.disabledColor = new Color(0.72f, 0.70f, 0.66f, 1f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick.AddListener(() =>
        {
            GameAudio.Play(GameAudio.Cue.Click);
            callback();
        });
        label = Label(target.transform, text, size - new Vector2(18f, 12f), Vector2.zero, 32f, true);
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

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void Focus(UnityEngine.UI.Button button)
    {
        if (GameplayPreferences.IsDesktopBuild && EventSystem.current != null && button != null)
            EventSystem.current.SetSelectedGameObject(button.gameObject);
    }

    private void OnDestroy()
    {
        PlayerProgress.Changed -= Refresh;
        ExpeditionProgress.Changed -= Refresh;
    }
}

// Illustrated accessories share their art with the in-game cat. Trails retain
// lightweight geometry, so changing tabs also refreshes the graphic's texture.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DoodlikCosmeticPreview : UnityEngine.UI.MaskableGraphic
{
    private string itemId = "accessory_none";
    private bool overCat;
    private int visualPart = -1;
    private UnityEngine.UI.Image catImage;
    public string ItemId => itemId;

    public override Texture mainTexture
    {
        get
        {
            Sprite artwork = WardrobeArt.GetSprite(itemId);
            return artwork != null ? artwork.texture : base.mainTexture;
        }
    }

    public void Configure(string id, bool onCat, int part = -1)
    {
        if (itemId == id && overCat == onCat && visualPart == part) return;
        itemId = id;
        overCat = onCat;
        visualPart = part;
        if (overCat && catImage == null && transform.parent != null)
        {
            Transform cat = transform.parent.Find("Cat");
            if (cat != null) catImage = cat.GetComponent<UnityEngine.UI.Image>();
        }
        SetVerticesDirty();
        SetMaterialDirty();
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
    {
        vh.Clear();
        if (string.IsNullOrEmpty(itemId) || itemId.EndsWith("_none")) return;
        Sprite artwork = WardrobeArt.GetSprite(itemId);
        if (artwork != null)
        {
            DrawArtwork(vh, artwork);
            return;
        }
        Color tint = new Color32(240, 181, 68, 255);
        foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
            if (item.Id == itemId) tint = item.Color;
        if (itemId.StartsWith("trail_"))
        {
            JumpTrailArt.Drawing drawing = JumpTrailArt.Get(itemId);
            if (drawing != null)
            {
                DrawTrailMotifs(vh, drawing);
                return;
            }
            for (int i = 0; i < 7; i++)
            {
                float x = -0.35f + i * 0.105f;
                float y = overCat ? -0.36f - 0.03f * i : -0.2f + i * 0.06f;
                float radius = 0.023f + i * 0.009f;
                Color fade = tint;
                fade.a *= 0.3f + i * 0.1f;
                Quad(vh, new Vector2(x - radius, y - radius), new Vector2(x + radius, y + radius), fade);
            }
        }
    }

    private void DrawTrailMotifs(UnityEngine.UI.VertexHelper vh, JumpTrailArt.Drawing drawing)
    {
        Rect rect = rectTransform.rect;
        float unit = Mathf.Min(rect.width, rect.height);
        for (int i = 0; i < 6; i++)
        {
            float step = i / 5f;
            Vector2 center = rect.center + new Vector2(Mathf.Lerp(-0.34f, 0.25f, step) * unit,
                (overCat ? Mathf.Lerp(-0.51f, -0.31f, step) : Mathf.Lerp(-0.23f, 0.22f, step)) * unit);
            float size = unit * Mathf.Lerp(0.065f, 0.16f, step);
            Quaternion turn = Quaternion.Euler(0f, 0f, (i % 3 - 1) * 24f);
            int first = vh.currentVertCount;
            for (int vertex = 0; vertex < drawing.Vertices.Length; vertex++)
            {
                Color32 tint = drawing.Colors[vertex];
                tint.a = (byte)Mathf.RoundToInt(tint.a * Mathf.Lerp(0.35f, 1f, step));
                vh.AddVert((Vector3)center + turn * drawing.Vertices[vertex] * size, tint, Vector2.zero);
            }
            for (int triangle = 0; triangle < drawing.Triangles.Length; triangle += 3)
                vh.AddTriangle(first + drawing.Triangles[triangle], first + drawing.Triangles[triangle + 1],
                    first + drawing.Triangles[triangle + 2]);
        }
    }

    private void DrawArtwork(UnityEngine.UI.VertexHelper vh, Sprite artwork)
    {
        Rect available = rectTransform.rect;
        Rect draw;
        if (overCat && itemId.StartsWith("accessory_"))
        {
            Vector2 catSize = available.size;
            if (catImage != null && catImage.sprite != null)
            {
                Vector2 source = catImage.sprite.rect.size;
                catSize = source * Mathf.Min(available.width / source.x, available.height / source.y);
            }
            int count = WardrobeArt.InstanceCount(itemId);
            for (int i = 0; i < count; i++)
            {
                if (visualPart >= 0 && i != visualPart) continue;
                Sprite part = WardrobeArt.GetAccessorySprite(itemId, i);
                if (part == null) continue;
                draw = WardrobeArt.AccessoryRect(itemId, catSize, i);
                draw.position += available.center;
                ArtworkQuad(vh, part, draw, WardrobeArt.AccessoryAngle(itemId, i), WardrobeArt.AccessoryFlipX(itemId, i));
            }
            return;
        }
        else
        {
            Vector2 source = artwork.rect.size;
            Vector2 size = source * Mathf.Min(available.width * 0.91f / source.x, available.height * 0.91f / source.y);
            draw = new Rect(available.center - size * 0.5f, size);
        }
        ArtworkQuad(vh, artwork, draw, 0f);
    }

    private void ArtworkQuad(UnityEngine.UI.VertexHelper vh, Sprite artwork, Rect draw, float angle, bool flipX = false)
    {
        Vector4 uv = UnityEngine.Sprites.DataUtility.GetOuterUV(artwork);
        if (flipX) { float left = uv.x; uv.x = uv.z; uv.z = left; }
        Quaternion rotation = Quaternion.Euler(0f, 0f, angle);
        Vector3 center = draw.center;
        Vector3 half = draw.size * 0.5f;
        int start = vh.currentVertCount;
        vh.AddVert(center + rotation * new Vector3(-half.x, -half.y), color, new Vector2(uv.x, uv.y));
        vh.AddVert(center + rotation * new Vector3(-half.x, half.y), color, new Vector2(uv.x, uv.w));
        vh.AddVert(center + rotation * new Vector3(half.x, half.y), color, new Vector2(uv.z, uv.w));
        vh.AddVert(center + rotation * new Vector3(half.x, -half.y), color, new Vector2(uv.z, uv.y));
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    private void Quad(UnityEngine.UI.VertexHelper vh, Vector2 min, Vector2 max, Color tint)
    {
        Rect rect = rectTransform.rect;
        Vector2 center = rect.center;
        int start = vh.currentVertCount;
        vh.AddVert(center + new Vector2(min.x * rect.width, min.y * rect.height), tint, Vector2.zero);
        vh.AddVert(center + new Vector2(min.x * rect.width, max.y * rect.height), tint, Vector2.zero);
        vh.AddVert(center + new Vector2(max.x * rect.width, max.y * rect.height), tint, Vector2.zero);
        vh.AddVert(center + new Vector2(max.x * rect.width, min.y * rect.height), tint, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}
