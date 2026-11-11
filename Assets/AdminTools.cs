using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
#endif

// Local testing tools only. Release builds keep inert flags, no UI or commands.
public sealed class AdminTools : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
    public static bool IsAvailable => true;
    public static bool CheatMode { get; private set; }
    public static bool IsOpen => instance != null && instance.panel != null && instance.panel.activeSelf;
    public static bool ConsumesMenuInput => IsOpen || closedFrame == Time.frameCount;
    private static AdminTools instance;
    private static int closedFrame = -1;
    private static readonly Color Ink = new Color32(94, 49, 24, 255);
    private static readonly Color Paper = new Color32(255, 232, 179, 255);
    private GameObject panel;
    private RectTransform safeRoot, card, launchRect;
    private Canvas canvas;
    private TMP_FontAsset font;
    private TMP_Text coins, cheatLabel, hint, launcherLabel;
    private TMP_InputField amount;
    private readonly List<GameObject> pages = new List<GameObject>();
    private readonly List<UnityEngine.UI.Image> tabImages = new List<UnityEngine.UI.Image>();
    private readonly List<SettingControl> settings = new List<SettingControl>();
    private string status = "Все вещи открыты; обычное сохранение защищено.";
    private int selectedPage;
    private GameObject returnFocus;
    private float previousTimeScale;
    private Rect lastSafeArea;
    private Vector2Int lastScreen;

    private sealed class SettingControl
    {
        public UnityEngine.UI.Slider Slider;
        public TMP_Text Value;
        public Func<float> Read;
        public bool Integer;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        instance = null;
        CheatMode = false;
        closedFrame = -1;
    }

    private void Awake()
    {
        instance = this;
        BuildInterface();
        PlayerProgress.Changed += Refresh;
        SceneManager.sceneLoaded += SceneLoaded;
        Refresh();
    }

    private void OnDestroy()
    {
        if (instance == this && IsOpen) Hide(true);
        PlayerProgress.Changed -= Refresh;
        SceneManager.sceneLoaded -= SceneLoaded;
        if (instance == this) instance = null;
    }

    private void Update()
    {
        UpdateLayout();
        AdminRuntime.ApplyGameSpeed();
        if (RunFlow.IsTransitioning) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
        {
            if (IsOpen) Close(); else Open();
        }
        else if (IsOpen && keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Close();
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // A new scene owns its pause state. Never restore an old scene's time.
        if (IsOpen) Hide(false);
        Refresh();
    }

    public void Open()
    {
        if (IsOpen || RunFlow.IsTransitioning) return;
        previousTimeScale = Time.timeScale;
        returnFocus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        PlayerProgress.BeginAdminSandbox();
        RefreshPlayer();
        panel.SetActive(true);
        Time.timeScale = 0f;
        Refresh();
        // Do not summon the phone keyboard just by opening the panel.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(tabImages[selectedPage].gameObject);
    }

    public void Close() => Hide(true);

    private void Hide(bool restoreTime)
    {
        if (!IsOpen) return;
        panel.SetActive(false);
        closedFrame = Time.frameCount;
        if (restoreTime) Time.timeScale = previousTimeScale > 0f ? AdminRuntime.GameSpeed : 0f;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(returnFocus != null && returnFocus.activeInHierarchy ? returnFocus : null);
    }

    public static void SetCheatMode(bool enabled)
    {
        CheatMode = enabled && PlayerProgress.AdminSandboxActive;
        RefreshPlayer();
        if (instance != null) instance.Refresh();
    }

    public void EndSandbox()
    {
        RefreshPlayer(); // Keep the attempt marked even after restoring normal progress.
        CheatMode = false;
        AdminRuntime.ClearPowers();
        AdminRuntime.ResetTuning();
        PlayerProgress.EndAdminSandbox();
        RefreshPlayer();
        Refresh();
        Close();
    }

    public bool ChangeCoins(int direction)
    {
        if (!PlayerProgress.AdminSandboxActive || !int.TryParse(amount.text, out int count) || count < 1 || count > 9999999)
        {
            hint.text = "Введите количество от 1 до 9 999 999.";
            return false;
        }
        PlayerProgress.AdjustAdminCoins(direction < 0 ? -count : count);
        status = direction < 0 ? "Тестовые монеты списаны." : "Тестовые монеты выданы.";
        Refresh();
        return true;
    }

    private static void RefreshPlayer()
    {
        Player player = Object.FindAnyObjectByType<Player>();
        if (player != null) player.RefreshAdminState();
    }

    private void Refresh()
    {
        if (coins == null) return;
        if (!PlayerProgress.AdminSandboxActive) CheatMode = false;
        coins.text = "Тестовые монеты: " + PlayerProgress.Coins;
        cheatLabel.text = "Бессмертие: " + (CheatMode ? "включено" : "выключено");
        hint.text = status + "\nРекорды и награды тестовой попытки не сохраняются.";
        foreach (SettingControl setting in settings)
        {
            float value = setting.Read();
            setting.Slider.SetValueWithoutNotify(value);
            setting.Value.text = setting.Integer ? Mathf.RoundToInt(value).ToString() : value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×";
            if (setting.Slider.gameObject.name == "BuffDuration")
                setting.Value.text = Mathf.RoundToInt(value) + " сек.";
        }
        Player player = Object.FindAnyObjectByType<Player>();
        launcherLabel.text = PlayerProgress.AdminSandboxActive ? "Админ · ТЕСТ" :
            player != null && player.IsTestAttempt ? "Тестовая попытка" : "Админ (F1)";
    }

    private void BuildInterface()
    {
        GameObject layer = Rect("Admin tools (cheat builds only)", transform, Vector2.zero, Vector2.zero);
        Stretch(layer.GetComponent<RectTransform>());
        canvas = layer.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 2000;
        layer.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF - Fallback");
        safeRoot = Rect("Safe area", layer.transform, Vector2.zero, Vector2.zero).GetComponent<RectTransform>();
        Stretch(safeRoot);
        GameObject launcher = Button(safeRoot, "Админ (F1)", new Vector2(220f, 54f), Vector2.zero, Open, out launcherLabel);
        launchRect = launcher.GetComponent<RectTransform>();
        launchRect.anchorMin = launchRect.anchorMax = new Vector2(1f, 0f);
        launchRect.pivot = new Vector2(1f, 0f);
        launchRect.anchoredPosition = new Vector2(-24f, 20f);

        panel = Rect("Admin panel", layer.transform, Vector2.zero, Vector2.zero);
        Stretch(panel.GetComponent<RectTransform>());
        panel.AddComponent<UnityEngine.UI.Image>().color = new Color(0.13f, 0.09f, 0.05f, 0.72f);
        GameObject panelSafe = Rect("Panel safe area", panel.transform, Vector2.zero, Vector2.zero);
        Stretch(panelSafe.GetComponent<RectTransform>());
        card = Rect("Admin card", panelSafe.transform, new Vector2(780f, 820f), Vector2.zero).GetComponent<RectTransform>();
        card.gameObject.AddComponent<UnityEngine.UI.Image>().color = Paper;
        var outline = card.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = Ink; outline.effectDistance = new Vector2(3f, -3f);
        Label(card, "Админ-панель", new Vector2(700f, 70f), new Vector2(0f, 347f), 44f);
        Label(card, "Локальная песочница · версия " + Application.version,
            new Vector2(710f, 40f), new Vector2(0f, 294f), 24f);
        string[] tabs = { "Монеты", "Физика", "Бафы", "Улучшения" };
        for (int i = 0; i < tabs.Length; i++)
        {
            int page = i;
            GameObject tab = Button(card, tabs[i], new Vector2(170f, 54f), new Vector2(-270f + 180f * i, 237f),
                () => SelectPage(page), out _);
            tab.name = "AdminTab" + i;
            tabImages.Add(tab.GetComponent<UnityEngine.UI.Image>());
            GameObject content = Rect("AdminPage" + i, card, Vector2.zero, Vector2.zero);
            Stretch(content.GetComponent<RectTransform>());
            pages.Add(content);
        }
        BuildEconomy(pages[0].transform);
        BuildPhysics(pages[1].transform);
        BuildPowers(pages[2].transform);
        BuildUpgrades(pages[3].transform);
        hint = Label(card, "", new Vector2(710f, 64f), new Vector2(0f, -223f), 21f);
        Button(card, "", new Vector2(700f, 62f), new Vector2(0f, -285f), () => SetCheatMode(!CheatMode), out cheatLabel);
        Button(card, "Обычный прогресс", new Vector2(340f, 62f), new Vector2(-180f, -362f), EndSandbox, out _);
        Button(card, "Закрыть (F1 / Esc)", new Vector2(340f, 62f), new Vector2(180f, -362f), Close, out _);
        SelectPage(0);
        panel.SetActive(false);
        UpdateLayout(true);
    }

    private void BuildEconomy(Transform page)
    {
        coins = Label(page, "", new Vector2(700f, 55f), new Vector2(0f, 155f), 32f);
        GameObject field = Rect("Coin amount", page, new Vector2(380f, 64f), new Vector2(0f, 79f));
        field.AddComponent<UnityEngine.UI.Image>().color = new Color32(255, 247, 220, 255);
        amount = field.AddComponent<TMP_InputField>();
        RectTransform viewport = Rect("Text area", field.transform, new Vector2(352f, 54f), Vector2.zero).GetComponent<RectTransform>();
        TMP_Text inputText = Label(viewport, "", new Vector2(352f, 54f), Vector2.zero, 28f);
        amount.textViewport = viewport;
        amount.textComponent = (TextMeshProUGUI)inputText;
        amount.targetGraphic = field.GetComponent<UnityEngine.UI.Image>();
        amount.contentType = TMP_InputField.ContentType.IntegerNumber;
        amount.characterLimit = 7;
        amount.text = "100";
        Button(page, "Выдать", new Vector2(220f, 64f), new Vector2(-240f, -3f), () => ChangeCoins(1), out _);
        Button(page, "Забрать", new Vector2(220f, 64f), new Vector2(0f, -3f), () => ChangeCoins(-1), out _);
        Button(page, "Обнулить", new Vector2(220f, 64f), new Vector2(240f, -3f),
            () => { PlayerProgress.AdjustAdminCoins(int.MinValue); status = "Тестовый кошелёк обнулён."; Refresh(); }, out _);
        Button(page, "+1 000", new Vector2(340f, 60f), new Vector2(-180f, -83f), () => AddPresetCoins(1000), out _);
        Button(page, "+10 000", new Vector2(340f, 60f), new Vector2(180f, -83f), () => AddPresetCoins(10000), out _);
        Label(page, "Монеты и покупки меняются только в песочнице.\nПосле выхода вернётся твой обычный прогресс.",
            new Vector2(700f, 70f), new Vector2(0f, -155f), 23f);
    }

    private void AddPresetCoins(int count)
    {
        if (!PlayerProgress.AdminSandboxActive) return;
        PlayerProgress.AdjustAdminCoins(count);
        status = "Выдано " + count + " тестовых монет.";
        Refresh();
    }

    private void BuildPhysics(Transform page)
    {
        Setting(page, "MovementMultiplier", "Скорость котика", 177f, 0.5f, 3f, false,
            () => AdminRuntime.MovementMultiplier, AdminRuntime.SetMovementMultiplier);
        Setting(page, "JumpMultiplier", "Сила обычного прыжка", 111f, 0.5f, 2f, false,
            () => AdminRuntime.JumpMultiplier, AdminRuntime.SetJumpMultiplier);
        Setting(page, "GravityMultiplier", "Сила гравитации", 45f, 0.4f, 2f, false,
            () => AdminRuntime.GravityMultiplier, AdminRuntime.SetGravityMultiplier);
        Setting(page, "BushMultiplier", "Сила прыжка с куста", -21f, 0.5f, 2f, false,
            () => AdminRuntime.BushMultiplier, AdminRuntime.SetBushMultiplier);
        Setting(page, "GameSpeed", "Скорость игры", -87f, 0.25f, 2f, false,
            () => AdminRuntime.GameSpeed, AdminRuntime.SetGameSpeed);
        Button(page, "Сбросить физику и темп", new Vector2(700f, 52f), new Vector2(0f, -164f),
            () => { AdminRuntime.ResetTuning(); status = "Физика и темп возвращены к обычным."; Refresh(); }, out _);
    }

    private void BuildPowers(Transform page)
    {
        Setting(page, "BuffDuration", "Длительность магнита и замедления", 177f, 1f, 120f, true,
            () => AdminRuntime.BuffDuration, AdminRuntime.SetBuffDuration);
        Button(page, "Выдать щит", new Vector2(340f, 62f), new Vector2(-180f, 80f), () => Grant(RunPowerups.Kind.Shield), out _);
        Button(page, "Выдать магнит", new Vector2(340f, 62f), new Vector2(180f, 80f), () => Grant(RunPowerups.Kind.Magnet), out _);
        Button(page, "Замедлить платформы", new Vector2(340f, 62f), new Vector2(-180f, -2f), () => Grant(RunPowerups.Kind.Slow), out _);
        Button(page, "Запустить ракету", new Vector2(340f, 62f), new Vector2(180f, -2f), () => Grant(RunPowerups.Kind.Rocket), out _);
        Button(page, "Убрать все бафы", new Vector2(340f, 62f), new Vector2(-180f, -84f),
            () => ActionResult(AdminRuntime.ClearPowers(), "Бафы сняты; полёт отменён."), out _);
        Button(page, "Прыгнуть сейчас", new Vector2(340f, 62f), new Vector2(180f, -84f),
            () => ActionResult(AdminRuntime.JumpNow(), "Прыжок готов — закрой панель."), out _);
        Label(page, "Команды работают во время живой попытки.\nЩит — один заряд; ракета сохраняет обычный полёт.",
            new Vector2(700f, 66f), new Vector2(0f, -159f), 22f);
    }

    private void BuildUpgrades(Transform page)
    {
        Label(page, "Улучшения выбранного формата и управления", new Vector2(700f, 48f), new Vector2(0f, 174f), 26f);
        Setting(page, "UpgradeMagnet", "Радиус сбора монет", 105f, 0f, PlayerProgress.MaxUpgradeLevel(PlayerProgress.Upgrade.Magnet), true,
            () => PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Magnet, RunFlow.ModeKey),
            value => PlayerProgress.SetAdminUpgradeLevel(PlayerProgress.Upgrade.Magnet, RunFlow.ModeKey, Mathf.RoundToInt(value)));
        Setting(page, "UpgradeDuration", "Длительность обычных бафов", 23f, 0f, PlayerProgress.MaxUpgradeLevel(PlayerProgress.Upgrade.Duration), true,
            () => PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Duration, RunFlow.ModeKey),
            value => PlayerProgress.SetAdminUpgradeLevel(PlayerProgress.Upgrade.Duration, RunFlow.ModeKey, Mathf.RoundToInt(value)));
        Button(page, "Уровень 0", new Vector2(340f, 62f), new Vector2(-180f, -74f), () => SetUpgrades(false), out _);
        Button(page, "Максимум", new Vector2(340f, 62f), new Vector2(180f, -74f), () => SetUpgrades(true), out _);
        Label(page, "Все котики, одежда, аксессуары и следы открыты.\nИх можно примерить в обычном гардеробе.",
            new Vector2(700f, 70f), new Vector2(0f, -155f), 23f);
    }

    private void SetUpgrades(bool maximum)
    {
        if (!PlayerProgress.AdminSandboxActive) return;
        foreach (PlayerProgress.Upgrade upgrade in Enum.GetValues(typeof(PlayerProgress.Upgrade)))
            PlayerProgress.SetAdminUpgradeLevel(upgrade, RunFlow.ModeKey, maximum ? PlayerProgress.MaxUpgradeLevel(upgrade) : 0);
        status = maximum ? "Тестовые улучшения: максимальные." : "Тестовые улучшения: уровень 0.";
        Refresh();
    }

    private void Grant(RunPowerups.Kind kind) => ActionResult(AdminRuntime.GrantPower(kind), "Баф выдан — закрой панель и продолжай.");

    private void ActionResult(bool success, string message)
    {
        status = success ? message : "Нужна живая попытка; активную ракету нельзя продлить.";
        Refresh();
    }

    private void SelectPage(int index)
    {
        selectedPage = Mathf.Clamp(index, 0, pages.Count - 1);
        for (int i = 0; i < pages.Count; i++)
        {
            pages[i].SetActive(i == selectedPage);
            tabImages[i].color = i == selectedPage ? new Color32(255, 185, 49, 255) : new Color32(238, 212, 158, 255);
        }
        status = selectedPage == 1 ? "Слабый прыжок может сделать маршрут непроходимым." :
            "Все вещи открыты; обычное сохранение защищено.";
        if (hint != null) Refresh();
    }

    private void Setting(Transform page, string id, string title, float y, float min, float max,
        bool integer, Func<float> read, Func<float, bool> write)
    {
        TMP_Text titleLabel = Label(page, title, new Vector2(530f, 32f), new Vector2(-85f, y), 25f);
        titleLabel.alignment = TextAlignmentOptions.MidlineLeft;
        TMP_Text valueLabel = Label(page, "", new Vector2(150f, 32f), new Vector2(275f, y), 25f);
        valueLabel.alignment = TextAlignmentOptions.MidlineRight;
        GameObject root = Rect(id, page, new Vector2(700f, 34f), new Vector2(0f, y - 33f));
        root.AddComponent<UnityEngine.UI.Image>().color = Color.clear;
        var slider = root.AddComponent<UnityEngine.UI.Slider>();
        var track = Rect("Track", root.transform, new Vector2(680f, 8f), Vector2.zero).AddComponent<UnityEngine.UI.Image>();
        track.color = new Color32(183, 153, 106, 255); track.raycastTarget = false;
        RectTransform handleArea = Rect("Handle area", root.transform, new Vector2(664f, 30f), Vector2.zero).GetComponent<RectTransform>();
        var handle = Rect("Handle", handleArea, new Vector2(34f, 34f), Vector2.zero).AddComponent<UnityEngine.UI.Image>();
        handle.color = new Color32(255, 180, 44, 255);
        var outline = handle.gameObject.AddComponent<UnityEngine.UI.Outline>(); outline.effectColor = Ink;
        outline.effectDistance = new Vector2(2f, -2f);
        slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = integer;
        slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
        // Slider drives the handle's vertical anchors to stretch. Keep only
        // horizontal sizeDelta, otherwise its height is added a second time.
        handle.rectTransform.sizeDelta = new Vector2(34f, 0f);
        slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        settings.Add(new SettingControl { Slider = slider, Value = valueLabel, Read = read, Integer = integer });
        slider.SetValueWithoutNotify(read());
        slider.onValueChanged.AddListener(value =>
        {
            if (write(value))
                status = selectedPage == 1 ? "Настройка применена; геометрию прыжков меняешь вручную." : "Тестовая настройка применена.";
            Refresh();
        });
    }

    private void UpdateLayout(bool force = false)
    {
        Rect safe = Screen.safeArea;
        Vector2Int screen = new Vector2Int(Screen.width, Screen.height);
        if (!force && safe == lastSafeArea && screen == lastScreen) return;
        lastSafeArea = safe; lastScreen = screen;
        if (screen.x <= 0 || screen.y <= 0) return;
        Vector2 min = new Vector2(safe.xMin / screen.x, safe.yMin / screen.y);
        Vector2 max = new Vector2(safe.xMax / screen.x, safe.yMax / screen.y);
        safeRoot.anchorMin = min; safeRoot.anchorMax = max;
        RectTransform panelSafe = (RectTransform)card.parent;
        panelSafe.anchorMin = min; panelSafe.anchorMax = max;
        float scale = Mathf.Max(0.01f, GetComponent<Canvas>().scaleFactor);
        float fit = Mathf.Min(1f, Mathf.Min((safe.width / scale - 40f) / 780f, (safe.height / scale - 40f) / 820f));
        card.localScale = Vector3.one * Mathf.Max(0.2f, fit);
    }

    private static GameObject Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.sizeDelta = size; rect.anchoredPosition = position;
        return go;
    }
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
    private TMP_Text Label(Transform parent, string text, Vector2 size, Vector2 position, float fontSize)
    {
        TextMeshProUGUI label = Rect("Label", parent, size, position).AddComponent<TextMeshProUGUI>();
        label.font = font; label.text = text; label.fontSize = fontSize; label.color = Ink;
        label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        return label;
    }
    private GameObject Button(Transform parent, string text, Vector2 size, Vector2 position, UnityEngine.Events.UnityAction action, out TMP_Text label)
    {
        GameObject go = Rect(text, parent, size, position);
        var image = go.AddComponent<UnityEngine.UI.Image>(); image.color = new Color32(255, 182, 48, 255);
        var outline = go.AddComponent<UnityEngine.UI.Outline>(); outline.effectColor = new Color32(155, 92, 35, 255); outline.effectDistance = new Vector2(2f, -2f);
        var button = go.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
        label = Label(go.transform, text, size - new Vector2(16f, 8f), Vector2.zero, 27f);
        return go;
    }
#else
    public static bool IsAvailable => false;
    public static bool CheatMode => false;
    public static bool IsOpen => false;
    public static bool ConsumesMenuInput => false;
#endif
}
