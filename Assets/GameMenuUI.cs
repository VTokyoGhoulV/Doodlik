using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameMenuUI : MonoBehaviour
{
    private static TMP_FontAsset interfaceFont;
    private GameObject menuPanel;
    private GameObject resetChoicePanel;
    private GameObject confirmationPanel;
    private GameObject settingsPanel;
    private GameObject menuButtonObject;
    private TMP_Text menuButtonLabel;
    private TMP_Text frameRateStatus;
    private GameObject settingsPopup;
    private GameObject popupReturnFocus;
    private GameObject menuSettingsButton, menuResetButton, resetCancelButton;
    private TMP_Text resetConfirmationMessage;
    private RectTransform menuButtonRect;
    private Canvas menuCanvas;
    private Rect lastSafeArea;
    private Vector2Int lastScreenSize;
    private float lastCanvasScaleFactor;
    private bool isOpen;
    private bool transitionInProgress;
    private bool applyOrientationDuringTransition;
    private bool resetAllRecords;
    private float previousTimeScale = 1f;
    private static GameMenuUI instance;
    private FrontEndUI frontEnd;
    private TMP_Text musicStatus, effectsStatus;
    private UnityEngine.UI.Slider musicSlider, effectsSlider;
    private TMP_Text hudTransparencyStatus;
    private UnityEngine.UI.Slider hudTransparencySlider;
    private bool hudPreferencesDirty;
    private float hudPreferencesSaveAfter;
    private bool settingsFromHome, restoreSettings, restoreModes;
    private bool startPreparationInProgress;
    private Camera openingViewportCamera;
    private Vector2Int openingViewportScreen;
    private float openingViewportAspect;
    private int openingViewportStableFrames;
    private int openingViewportObservedFrame = -1;
    public bool CanSelectGameMode => !RunFlow.RunActive &&
        !RunFlow.IsTransitioning && !transitionInProgress && !startPreparationInProgress && !ExpeditionUI.IsOpen;
    public static bool BlocksGameplayInput => AdminTools.ConsumesMenuInput || ExpeditionUI.ConsumesMenuInput || instance != null &&
        (instance.isOpen || instance.transitionInProgress ||
         (instance.frontEnd != null && instance.frontEnd.IsVisible));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateMenu()
    {
        if (FindFirstObjectByType<GameMenuUI>() != null)
        {
            return;
        }

        GameObject menuRoot = new GameObject("Game Menu UI");
        DontDestroyOnLoad(menuRoot);
        menuRoot.AddComponent<GameMenuUI>();
    }

    private void Awake()
    {
        instance = this;
        BuildInterface();
        frontEnd = gameObject.AddComponent<FrontEndUI>();
        frontEnd.Initialize(this);
        ExpeditionUI.Attach(this);
        frontEnd.ShowHome();
        menuButtonObject.SetActive(false);
        Time.timeScale = 0f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
        gameObject.AddComponent<AdminTools>();
#endif
    }

    private void Start()
    {
        // Run after BeforeSceneLoad initializers, including isolated QA storage.
        // An interrupted expedition is settled once before its stash is shown.
        ExpeditionProgress.RecoverInterruptedRun();
        frontEnd.Refresh();
    }

    private void Update()
    {
        if (!RunFlow.RunActive || startPreparationInProgress) ObserveOpeningViewport();
        if (hudPreferencesDirty && Time.realtimeSinceStartup >= hudPreferencesSaveAfter)
            SaveHudPreferences();
        UpdateMenuButtonSafeArea();
        if (AdminTools.ConsumesMenuInput || ExpeditionUI.ConsumesMenuInput) return;
        if (startPreparationInProgress) return;
        if (transitionInProgress) return;

        if (Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (confirmationPanel != null && confirmationPanel.activeSelf)
            {
                CloseResetConfirmation();
                return;
            }

            if (resetChoicePanel != null && resetChoicePanel.activeSelf)
            {
                CloseResetChoice();
                return;
            }

            if (settingsPanel != null && settingsPanel.activeSelf)
            {
                if (settingsPopup != null)
                {
                    CloseSettingsPopup();
                    return;
                }
                CloseSettings();
                return;
            }

            if (frontEnd != null && frontEnd.IsVisible) frontEnd.GoBack();
            else ToggleMenu();
        }
    }

    private void BuildInterface()
    {
        menuCanvas = gameObject.AddComponent<Canvas>();
        menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        menuCanvas.sortingOrder = 1000;

        gameObject.AddComponent<CanvasScaler>();
        ResponsiveDisplay.ConfigureScaler(menuCanvas);
        gameObject.AddComponent<GraphicRaycaster>();

        menuPanel = CreatePanel(transform);
        CreateButton(
            menuPanel.transform,
            "Выйти из игры",
            new Vector2(440f, 90f),
            new Vector2(0f, -220f),
            ExitGame,
            out _
        );
        menuResetButton = CreateButton(
            menuPanel.transform,
            "Обнулить рекорды",
            new Vector2(440f, 90f),
            new Vector2(0f, 30f),
            OpenResetChoice,
            out _
        );
        menuSettingsButton = CreateButton(
            menuPanel.transform,
            "Настройки",
            new Vector2(440f, 90f),
            new Vector2(0f, 155f),
            OpenSettings,
            out _
        );
        CreateButton(menuPanel.transform, "На стартовый экран", new Vector2(440f, 90f),
            new Vector2(0f, -95f), ReturnToHome, out _);
        menuPanel.SetActive(false);

        settingsPanel = CreatePanel(transform);
        CreateSettingsContent(settingsPanel.transform);
        settingsPanel.SetActive(false);

        resetChoicePanel = CreatePanel(transform);
        CreateResetChoiceContent(resetChoicePanel.transform);
        resetChoicePanel.SetActive(false);

        confirmationPanel = CreatePanel(transform);
        CreateConfirmationContent(confirmationPanel.transform);
        confirmationPanel.SetActive(false);

        menuButtonObject = CreateButton(
            transform,
            "Меню",
            new Vector2(170f, 64f),
            new Vector2(-35f, -35f),
            ToggleMenu,
            out menuButtonLabel
        );
        menuButtonRect = menuButtonObject.GetComponent<RectTransform>();
        menuButtonRect.anchorMin = Vector2.one;
        menuButtonRect.anchorMax = Vector2.one;
        menuButtonRect.pivot = Vector2.one;
        menuButtonRect.anchoredPosition = new Vector2(-35f, -35f);
        menuButtonRect.localRotation = Quaternion.Euler(0f, 0f, -2.5f);
        menuButtonObject.transform.SetAsLastSibling();

        UpdateMenuButtonSafeArea(true);
    }

    private void UpdateMenuButtonSafeArea(bool force = false)
    {
        if (menuButtonRect == null)
        {
            return;
        }

        Rect safeArea = Screen.safeArea;
        Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
        Canvas canvas = menuCanvas;
        float scaleFactor = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
        if (!force && safeArea == lastSafeArea && screenSize == lastScreenSize &&
            Mathf.Approximately(scaleFactor, lastCanvasScaleFactor))
        {
            return;
        }

        lastSafeArea = safeArea;
        lastScreenSize = screenSize;

        if (canvas != null)
        {
            ResponsiveDisplay.ConfigureScaler(canvas);
        }

        // CanvasScaler can update one frame after an orientation change. Recheck
        // its scale even when the safe area and screen dimensions stay the same.
        scaleFactor = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
        lastCanvasScaleFactor = scaleFactor;

        float rightInset = Mathf.Max(0f, Screen.width - safeArea.xMax);
        float topInset = Mathf.Max(0f, Screen.height - safeArea.yMax);

        menuButtonRect.anchoredPosition = new Vector2(
            -35f - rightInset / scaleFactor,
            -35f - topInset / scaleFactor
        );
    }

    private static GameObject CreatePanel(Transform parent)
    {
        GameObject panel = new GameObject("Menu Panel", typeof(RectTransform));
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image background = panel.AddComponent<Image>();
        background.color = new Color(0.05f, 0.07f, 0.1f, 0.82f);
        return panel;
    }

    private static GameObject CreateButton(
        Transform parent,
        string label,
        Vector2 size,
        Vector2 anchoredPosition,
        UnityEngine.Events.UnityAction onClick,
        out TMP_Text text
    )
    {
        GameObject buttonObject = new GameObject(label, typeof(RectTransform));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(1f, 0.72f, 0.18f, 0.98f);

        Outline border = buttonObject.AddComponent<Outline>();
        border.effectColor = new Color(0.62f, 0.25f, 0.06f, 1f);
        border.effectDistance = new Vector2(5f, -5f);

        Shadow buttonShadow = buttonObject.AddComponent<Shadow>();
        buttonShadow.effectColor = new Color(0.25f, 0.12f, 0.04f, 0.42f);
        buttonShadow.effectDistance = new Vector2(6f, -7f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.92f, 0.7f, 1f);
        colors.pressedColor = new Color(0.88f, 0.55f, 0.12f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick.AddListener(() => { GameAudio.Play(GameAudio.Cue.Click); onClick(); });

        GameObject textObject = new GameObject("Label", typeof(RectTransform));
        textObject.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 8f);
        textRect.offsetMax = new Vector2(-12f, -8f);

        text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = GetInterfaceFont();
        text.text = label;
        text.fontSize = 36f;
        text.color = new Color(0.27f, 0.12f, 0.035f, 1f);
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        text.enableAutoSizing = true;
        text.fontSizeMin = 22f;
        text.fontSizeMax = 40f;
        text.raycastTarget = false;

        Shadow textShadow = textObject.AddComponent<Shadow>();
        textShadow.effectColor = new Color(1f, 0.91f, 0.56f, 0.75f);
        textShadow.effectDistance = new Vector2(2f, -2f);

        return buttonObject;
    }

    private static TMP_FontAsset GetInterfaceFont()
    {
        if (interfaceFont == null)
        {
            interfaceFont = Resources.Load<TMP_FontAsset>(
                "Fonts & Materials/LiberationSans SDF - Fallback"
            );
        }

        return interfaceFont;
    }

    private void ToggleMenu()
    {
        if (transitionInProgress || !RunFlow.RunActive) return;
        if (settingsPanel.activeSelf) { CloseSettings(); return; }
        isOpen = !isOpen;

        if (isOpen)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            menuPanel.SetActive(true);
        }
        else
        {
            CloseSettingsPopup();
            Time.timeScale = previousTimeScale;
            settingsPanel.SetActive(false);
            resetChoicePanel.SetActive(false);
            confirmationPanel.SetActive(false);
            menuButtonObject.SetActive(true);
        }

        menuPanel.SetActive(isOpen);
        menuButtonLabel.text = isOpen ? "Назад" : "Меню";
        if (isOpen) FocusButton(menuSettingsButton);
        else EventSystem.current?.SetSelectedGameObject(null);
    }

    private void OpenResetChoice()
    {
        menuPanel.SetActive(false);
        resetChoicePanel.SetActive(true);
        menuButtonObject.SetActive(false);
        FocusFirst(resetChoicePanel);
    }

    private void OpenSettings()
    {
        UpdateFrameRateStatus();
        menuPanel.SetActive(false);
        settingsPanel.SetActive(true);
        menuButtonLabel.text = "Назад";
        UpdateAudioStatus();
        UpdateHudTransparencyStatus();
        FocusFirst(settingsPanel);
    }

    public void OpenSettingsFromHome()
    {
        frontEnd.Hide();
        settingsFromHome = true;
        isOpen = true;
        previousTimeScale = 0f;
        OpenSettings();
        menuButtonObject.SetActive(false);
    }

    public void StartRunFromHome()
    {
        if (!CanSelectGameMode) return;
        ObserveOpeningViewport();
        if (!OpeningViewportReady() || !OpeningWorldMatchesViewport())
        {
            StartCoroutine(PrepareRunFromHome());
            return;
        }
        StartPreparedRun();
    }

    private void StartPreparedRun()
    {
        frontEnd.Hide();
        settingsFromHome = false;
        isOpen = false;
        RunFlow.RunActive = true;
        Time.timeScale = 1f;
        FindFirstObjectByType<Player>()?.Journal?.BeginRun();
        FindFirstObjectByType<Player>()?.Expedition?.BeginRun();
        menuButtonLabel.text = "Меню";
        menuButtonObject.SetActive(true);
        GameAudio.Play(GameAudio.Cue.Start);
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void ObserveOpeningViewport()
    {
        Camera camera = Camera.main;
        Vector2Int screen = new Vector2Int(Screen.width, Screen.height);
        float aspect = camera != null ? camera.aspect : 0f;
        bool unchanged = camera != null && camera == openingViewportCamera &&
            screen == openingViewportScreen &&
            Mathf.Abs(aspect - openingViewportAspect) <= 0.001f;
        if (openingViewportObservedFrame == Time.frameCount && unchanged) return;
        openingViewportObservedFrame = Time.frameCount;
        openingViewportStableFrames = unchanged ? openingViewportStableFrames + 1 : 1;
        openingViewportCamera = camera;
        openingViewportScreen = screen;
        openingViewportAspect = aspect;
    }

    private bool OpeningViewportReady()
    {
        if (openingViewportStableFrames < 2 || openingViewportCamera == null ||
            openingViewportScreen.x <= 0 || openingViewportScreen.y <= 0 ||
            float.IsNaN(openingViewportAspect) || float.IsInfinity(openingViewportAspect) ||
            openingViewportAspect <= 0f) return false;
        if (GameplayPreferences.IsDesktopBuild) return true;
        bool portrait = GameplayPreferences.IsPortrait;
        return (openingViewportScreen.y > openingViewportScreen.x) == portrait &&
            (openingViewportAspect < 1f) == portrait;
    }

    private static bool OpeningWorldMatchesViewport()
    {
        Spawner spawner = FindFirstObjectByType<Spawner>();
        return spawner != null && spawner.enabled && spawner.DraftMatchesCurrentSurface;
    }

    private IEnumerator PrepareRunFromHome()
    {
        startPreparationInProgress = true;
        frontEnd.Hide();
        menuButtonObject.SetActive(false);
        RunFlow.RunActive = false;
        Time.timeScale = 0f;
        GameplayPreferences.ApplyOrientation(false);
        float deadline = Time.realtimeSinceStartup + 5f;
        int reloads = 0;
        while (Time.realtimeSinceStartup < deadline)
        {
            ObserveOpeningViewport();
            if (!OpeningViewportReady())
            {
                yield return null;
                continue;
            }
            if (OpeningWorldMatchesViewport())
            {
                startPreparationInProgress = false;
                StartPreparedRun();
                yield break;
            }
            if (reloads++ >= 2) break;
            // Reload the whole draft under the existing fade. This also
            // rebuilds bush reservations, hazard proofs, pickup clocks and
            // moving patrols together, rather than stretching their geometry.
            BeginRestartTransition();
            while (transitionInProgress) yield return null;
        }

        // A timeout never authorizes an attempt with an uncertified route.
        // The next Play press can retry once the Android surface is settled.
        startPreparationInProgress = false;
        RunFlow.RunActive = false;
        Time.timeScale = 0f;
        frontEnd.ShowHome();
        menuButtonObject.SetActive(false);
        Debug.LogWarning("Doodlik: экран ещё меняет размер; запуск отложен до следующей попытки.");
    }

    public void ReturnToHome()
    {
        if (transitionInProgress) return;
        FindFirstObjectByType<Player>()?.Expedition?.EndRun(ExpeditionOutcome.Abandoned);
        FindFirstObjectByType<Player>()?.Journal?.EndRun("В меню");
        PlayerProgress.Flush();
        ExpeditionProgress.Flush();
        RunFlow.RunActive = false;
        settingsFromHome = false;
        BeginRestartTransition();
    }

    private void CloseSettings()
    {
        SaveHudPreferences();
        CloseSettingsPopup();
        settingsPanel.SetActive(false);
        if (settingsFromHome)
        {
            settingsFromHome = false;
            isOpen = false;
            frontEnd.ShowHome();
            menuButtonObject.SetActive(false);
            return;
        }
        menuPanel.SetActive(true);
        menuButtonLabel.text = "Назад";
        FocusButton(menuSettingsButton);
    }

    private void CreateSettingsContent(Transform parent)
    {
        GameObject titleObject = new GameObject("Settings Title", typeof(RectTransform));
        titleObject.transform.SetParent(parent, false);
        RectTransform titleRect = titleObject.GetComponent<RectTransform>();
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = new Vector2(0f, 350f);
        titleRect.sizeDelta = new Vector2(720f, 90f);

        TMP_Text title = titleObject.AddComponent<TextMeshProUGUI>();
        title.font = GetInterfaceFont();
        title.text = "Настройки";
        title.fontSize = 46f;
        title.fontStyle = FontStyles.Bold;
        title.color = new Color(1f, 0.83f, 0.42f, 1f);
        title.alignment = TextAlignmentOptions.Center;

        GameObject frameRateButton = CreateButton(parent, "", new Vector2(640f, 76f),
            new Vector2(0f, 230f), OpenFrameRateOptions,
            out frameRateStatus);
        musicSlider = CreatePercentSlider(parent, "Music Volume", 120f, out musicStatus);
        effectsSlider = CreatePercentSlider(parent, "Effects Volume", -5f, out effectsStatus);
        hudTransparencySlider = CreatePercentSlider(parent, "Score Panel Transparency", -130f, out hudTransparencyStatus);
        hudTransparencyStatus.enableAutoSizing = true;
        hudTransparencyStatus.fontSizeMin = 26f;
        hudTransparencyStatus.fontSizeMax = 32f;
        musicSlider.onValueChanged.AddListener(value =>
        {
            GameAudio.SetMusicVolume(value / 100f);
            UpdateAudioStatus();
        });
        effectsSlider.onValueChanged.AddListener(value =>
        {
            GameAudio.SetEffectsVolume(value / 100f);
            UpdateAudioStatus();
        });
        hudTransparencySlider.onValueChanged.AddListener(value =>
        {
            GameplayPreferences.SetScorePanelTransparency(value / 100f);
            hudPreferencesDirty = true;
            hudPreferencesSaveAfter = Time.realtimeSinceStartup + 0.5f;
            UpdateHudTransparencyStatus();
        });
        TMP_Text hudHint = CreateSettingsLabel(parent,
            "Счёт и рекорд остаются видимыми", new Vector2(0f, -230f));
        hudHint.fontSize = 24f;
        hudHint.raycastTarget = false;
        GameObject backButton = CreateButton(
            parent,
            "Назад",
            new Vector2(300f, 105f),
            new Vector2(0f, -325f),
            CloseSettings,
            out _
        );
        // Up/down traverses settings; left/right adjusts a slider by one percent.
        UnityEngine.UI.Selectable[] settingsControls =
        {
            frameRateButton.GetComponent<UnityEngine.UI.Button>(), musicSlider,
            effectsSlider, hudTransparencySlider, backButton.GetComponent<UnityEngine.UI.Button>()
        };
        for (int i = 0; i < settingsControls.Length; i++)
        {
            settingsControls[i].navigation = new UnityEngine.UI.Navigation
            {
                mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnUp = i > 0 ? settingsControls[i - 1] : null,
                selectOnDown = i + 1 < settingsControls.Length ? settingsControls[i + 1] : null
            };
        }
        UpdateFrameRateStatus();
        UpdateAudioStatus();
        UpdateHudTransparencyStatus();
    }

    private void UpdateHudTransparencyStatus()
    {
        int percent = Mathf.RoundToInt(GameplayPreferences.ScorePanelTransparency * 100f);
        if (hudTransparencyStatus != null)
            hudTransparencyStatus.text = $"Прозрачность панели счёта: {percent}%";
        hudTransparencySlider?.SetValueWithoutNotify(percent);
    }

    private void SaveHudPreferences()
    {
        if (!hudPreferencesDirty) return;
        PlayerPrefs.Save();
        hudPreferencesDirty = false;
    }

    private void UpdateAudioStatus()
    {
        int musicPercent = Mathf.RoundToInt(GameAudio.MusicVolume * 100f);
        int effectsPercent = Mathf.RoundToInt(GameAudio.EffectsVolume * 100f);
        if (musicStatus != null) musicStatus.text = $"Музыка: {musicPercent}%";
        if (effectsStatus != null) effectsStatus.text = $"Звуки: {effectsPercent}%";
        musicSlider?.SetValueWithoutNotify(musicPercent);
        effectsSlider?.SetValueWithoutNotify(effectsPercent);
    }

    private static UnityEngine.UI.Slider CreatePercentSlider(
        Transform parent, string name, float rowY, out TMP_Text status)
    {
        status = CreateSettingsLabel(parent, "", new Vector2(0f, rowY + 31f));
        status.gameObject.name = name + " Label";
        status.rectTransform.sizeDelta = new Vector2(640f, 42f);
        status.fontSize = 32f;
        status.fontStyle = FontStyles.Bold;
        status.color = new Color(1f, 0.87f, 0.62f, 1f);
        status.alignment = TextAlignmentOptions.Left;
        status.raycastTarget = false;

        GameObject root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(640f, 64f);
        rect.anchoredPosition = new Vector2(0f, rowY - 22f);
        // The whole strip accepts mouse/touch input, not just the thin visible track.
        UnityEngine.UI.Image hitArea = root.AddComponent<UnityEngine.UI.Image>();
        hitArea.color = Color.clear;

        UnityEngine.UI.Image track = CreateSliderImage(root.transform, "Track",
            new Color(0.42f, 0.24f, 0.12f, 1f));
        SetSliderStrip(track.rectTransform, 22f, 0f, 20f);
        UnityEngine.UI.Image groove = CreateSliderImage(track.transform, "Groove",
            new Color(1f, 0.88f, 0.65f, 1f));
        groove.rectTransform.anchorMin = Vector2.zero;
        groove.rectTransform.anchorMax = Vector2.one;
        groove.rectTransform.offsetMin = new Vector2(3f, 3f);
        groove.rectTransform.offsetMax = new Vector2(-3f, -3f);

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(root.transform, false);
        SetSliderStrip(fillArea.GetComponent<RectTransform>(), 25f, 0f, 14f);
        UnityEngine.UI.Image fill = CreateSliderImage(fillArea.transform, "Fill",
            new Color(1f, 0.68f, 0.1f, 1f));
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = Vector2.one;
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;

        GameObject handleArea = new GameObject("Handle Area", typeof(RectTransform));
        handleArea.transform.SetParent(root.transform, false);
        SetSliderStrip(handleArea.GetComponent<RectTransform>(), 25f, 0f, 44f);
        UnityEngine.UI.Image handle = CreateSliderImage(handleArea.transform, "Handle",
            new Color(1f, 0.72f, 0.18f, 1f));
        // Slider stretches the handle vertically to its 44-unit container.
        handle.rectTransform.sizeDelta = new Vector2(44f, 0f);
        UnityEngine.UI.Outline border = handle.gameObject.AddComponent<UnityEngine.UI.Outline>();
        border.effectColor = new Color(0.62f, 0.25f, 0.06f, 1f);
        border.effectDistance = new Vector2(3f, -3f);
        UnityEngine.UI.Image grip = CreateSliderImage(handle.transform, "Grip",
            new Color(0.55f, 0.25f, 0.07f, 1f));
        grip.rectTransform.sizeDelta = new Vector2(4f, 20f);

        UnityEngine.UI.Slider slider = root.AddComponent<UnityEngine.UI.Slider>();
        slider.minValue = 0f;
        slider.maxValue = 100f;
        slider.wholeNumbers = true;
        slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        UnityEngine.UI.ColorBlock colors = slider.colors;
        colors.highlightedColor = new Color(1f, 0.95f, 0.75f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.88f, 0.62f, 0.2f, 1f);
        colors.fadeDuration = 0.08f;
        slider.colors = colors;
        return slider;
    }

    private static UnityEngine.UI.Image CreateSliderImage(Transform parent, string name, Color color)
    {
        GameObject element = new GameObject(name, typeof(RectTransform));
        element.transform.SetParent(parent, false);
        UnityEngine.UI.Image image = element.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void SetSliderStrip(RectTransform rect, float inset, float y, float height)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.offsetMin = new Vector2(inset, y - height * 0.5f);
        rect.offsetMax = new Vector2(-inset, y + height * 0.5f);
    }

    private static TMP_Text CreateSettingsLabel(
        Transform parent, string value, Vector2 position)
    {
        GameObject labelObject = new GameObject("Settings Status", typeof(RectTransform));
        labelObject.transform.SetParent(parent, false);
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(700f, 55f);
        TMP_Text label = labelObject.AddComponent<TextMeshProUGUI>();
        label.font = GetInterfaceFont();
        label.text = value;
        label.fontSize = 28f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        return label;
    }

    public bool SelectRunKind(GameRunKind kind)
    {
        if (!CanSelectGameMode || (kind != GameRunKind.Classic && kind != GameRunKind.Expedition)) return false;
        if (GameplayPreferences.RunKind == kind) { frontEnd.Refresh(); return true; }
        if (!GameplayPreferences.SetRunKind(kind)) return false;
        settingsFromHome = false;
        BeginRestartTransition(true);
        return true;
    }

    public bool SelectGameMode(bool portrait, bool inertia)
    {
        if (!CanSelectGameMode)
            return false;

        if (GameplayPreferences.IsDesktopBuild)
            portrait = false;

        bool orientationChanged = GameplayPreferences.IsPortrait != portrait;
        if (!orientationChanged && GameplayPreferences.UseInertia == inertia)
        {
            frontEnd.Refresh();
            return true;
        }

        // Update orientation and controls together before a new Player/Spawner
        // reads them. Apply the display change only behind the transition cover.
        GameplayPreferences.SetMode(portrait, inertia, false);
        applyOrientationDuringTransition = orientationChanged;
        settingsFromHome = false;
        BeginRestartTransition(true);
        return true;
    }

    public bool SelectDifficulty(GameDifficulty difficulty)
    {
        if (!CanSelectGameMode || difficulty < GameDifficulty.Easy || difficulty > GameDifficulty.Hard)
            return false;

        if (GameplayPreferences.Difficulty == difficulty)
        {
            frontEnd.Refresh();
            return true;
        }

        // Difficulty affects generated supports and hazards, so the prepared
        // scene must be rebuilt just like an orientation/control mode change.
        GameplayPreferences.SetDifficulty(difficulty);
        settingsFromHome = false;
        BeginRestartTransition(true);
        return true;
    }

    public static void RestartCurrentSceneSmoothly()
    {
        GameMenuUI menu = FindFirstObjectByType<GameMenuUI>();
        if (menu != null)
        {
            menu.BeginRestartTransition();
            return;
        }

        ExpeditionRun.Active?.EndRun(ExpeditionOutcome.Abandoned);
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void BeginRestartTransition(bool showModesAfterRestart = false)
    {
        if (transitionInProgress)
            return;

        FindFirstObjectByType<Player>()?.Expedition?.EndRun(ExpeditionOutcome.Abandoned);
        FindFirstObjectByType<Player>()?.Journal?.EndRun(showModesAfterRestart ? "Смена режима" : "Перезапуск");
        transitionInProgress = true;
        RunFlow.IsTransitioning = true;
        restoreModes = showModesAfterRestart;
        restoreSettings = !restoreModes && settingsPanel.activeSelf;
        StartCoroutine(RestartWithTransition());
    }

    private IEnumerator RestartWithTransition()
    {
        isOpen = false;
        menuPanel.SetActive(false);
        settingsPanel.SetActive(false);
        resetChoicePanel.SetActive(false);
        confirmationPanel.SetActive(false);
        menuButtonObject.SetActive(false);
        menuButtonLabel.text = "Меню";
        CloseSettingsPopup();
        frontEnd.Hide();
        Time.timeScale = 0f;
        PlayerPrefs.Save();

        GameObject transition = new GameObject(
            "Scene Transition", typeof(RectTransform));
        transition.transform.SetParent(transform, false);
        RectTransform transitionRect = transition.GetComponent<RectTransform>();
        transitionRect.anchorMin = Vector2.zero;
        transitionRect.anchorMax = Vector2.one;
        transitionRect.offsetMin = Vector2.zero;
        transitionRect.offsetMax = Vector2.zero;
        Image transitionImage = transition.AddComponent<Image>();
        transitionImage.color = new Color(0.035f, 0.055f, 0.08f, 1f);
        CanvasGroup transitionGroup = transition.AddComponent<CanvasGroup>();
        transitionGroup.alpha = 0f;
        transitionGroup.blocksRaycasts = true;
        transition.transform.SetAsLastSibling();

        const float fadeOutDuration = 0.2f;
        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            transitionGroup.alpha = Mathf.Clamp01(elapsed / fadeOutDuration);
            yield return null;
        }
        transitionGroup.alpha = 1f;

        if (applyOrientationDuringTransition)
        {
            Vector2Int previousScreenSize = new Vector2Int(
                Screen.width,
                Screen.height
            );
            GameplayPreferences.ApplyOrientation(true);

            // Android меняет размер поверхности не в тот же кадр. Держим
            // затемнение, пока выбранный формат действительно не применится.
            const float orientationTimeout = 1.25f;
            float orientationElapsed = 0f;
            while (orientationElapsed < orientationTimeout)
            {
                bool portraitApplied = Screen.height >= Screen.width;
                if (portraitApplied == GameplayPreferences.IsPortrait &&
                    new Vector2Int(Screen.width, Screen.height) !=
                    previousScreenSize)
                {
                    break;
                }

                orientationElapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        AsyncOperation loading = SceneManager.LoadSceneAsync(
            SceneManager.GetActiveScene().buildIndex);
        while (!loading.isDone)
            yield return null;

        // Let orientation and responsive canvases settle behind the cover.
        yield return null;
        yield return null;

        if (restoreModes)
        {
            frontEnd.ShowModes();
        }
        else if (restoreSettings)
        {
            isOpen = true;
            previousTimeScale = RunFlow.RunActive ? 1f : 0f;
            settingsFromHome = !RunFlow.RunActive;
            OpenSettings();
        }
        else if (!RunFlow.RunActive && !startPreparationInProgress) frontEnd.ShowHome();
        transition.transform.SetAsLastSibling();

        const float fadeInDuration = 0.28f;
        elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            transitionGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeInDuration);
            yield return null;
        }

        Destroy(transition);
        menuButtonObject.SetActive(RunFlow.RunActive);
        UpdateFrameRateStatus();
        applyOrientationDuringTransition = false;
        restoreModes = false;
        transitionInProgress = false;
        RunFlow.IsTransitioning = false;
        Time.timeScale = RunFlow.RunActive && !isOpen ? 1f : 0f;
        frontEnd.Refresh();
    }

    private void SelectFrameRate(int frameRate)
    {
        CloseSettingsPopup();
        MobilePerformance.SetFrameRate(frameRate);
        UpdateFrameRateStatus();
    }

    private void UpdateFrameRateStatus()
    {
        if (frameRateStatus != null)
        {
            frameRateStatus.text = "Кадры: " + MobilePerformance.SelectedFrameRateLabel + "  ▼";
        }
    }

    private void OpenFrameRateOptions()
    {
        int[] modes = GameplayPreferences.IsDesktopBuild
            ? new[]
            {
                30, 60, 120,
                MobilePerformance.UnlimitedFrameRate,
                MobilePerformance.VerticalSync
            }
            : new[]
        {
            30, 60, 120
        };
        string[] labels = new string[modes.Length];
        for (int i = 0; i < modes.Length; i++)
            labels[i] = MobilePerformance.GetFrameRateLabel(modes[i]);

        OpenSettingsPopup(
            "Частота кадров",
            labels,
            index => SelectFrameRate(modes[index]));
    }

    private void OpenSettingsPopup(
        string title, string[] options,
        UnityEngine.Events.UnityAction<int> onSelected)
    {
        CloseSettingsPopup();
        popupReturnFocus = EventSystem.current != null
            ? EventSystem.current.currentSelectedGameObject : null;

        settingsPopup = new GameObject("Settings Popup", typeof(RectTransform));
        settingsPopup.transform.SetParent(settingsPanel.transform, false);
        RectTransform overlayRect = settingsPopup.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        Image overlay = settingsPopup.AddComponent<Image>();
        overlay.color = new Color(0.02f, 0.03f, 0.05f, 0.72f);
        Button overlayButton = settingsPopup.AddComponent<Button>();
        overlayButton.targetGraphic = overlay;
        overlayButton.navigation = new UnityEngine.UI.Navigation
        {
            mode = UnityEngine.UI.Navigation.Mode.None
        };
        overlayButton.onClick.AddListener(CloseSettingsPopup);

        GameObject window = new GameObject("Popup Window", typeof(RectTransform));
        window.transform.SetParent(settingsPopup.transform, false);
        RectTransform windowRect = window.GetComponent<RectTransform>();
        windowRect.anchorMin = windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.sizeDelta = new Vector2(600f, 125f + options.Length * 82f);
        Image windowImage = window.AddComponent<Image>();
        windowImage.color = new Color(0.12f, 0.15f, 0.19f, 0.98f);
        Outline outline = window.AddComponent<Outline>();
        outline.effectColor = new Color(0.62f, 0.25f, 0.06f, 1f);
        outline.effectDistance = new Vector2(6f, -6f);

        TMP_Text popupTitle = CreateSettingsLabel(
            window.transform, title,
            new Vector2(0f, windowRect.sizeDelta.y * 0.5f - 55f));
        popupTitle.fontStyle = FontStyles.Bold;
        popupTitle.color = new Color(1f, 0.83f, 0.42f, 1f);

        float firstY = windowRect.sizeDelta.y * 0.5f - 125f;
        UnityEngine.UI.Button[] optionButtons = new UnityEngine.UI.Button[options.Length];
        for (int i = 0; i < options.Length; i++)
        {
            int selectedIndex = i;
            optionButtons[i] = CreateButton(window.transform, options[i], new Vector2(500f, 66f),
                new Vector2(0f, firstY - i * 82f),
                () => onSelected(selectedIndex), out _).GetComponent<UnityEngine.UI.Button>();
        }

        // Raycast blocking does not constrain keyboard navigation: automatic
        // search can otherwise select a settings slider behind this popup.
        for (int i = 0; i < optionButtons.Length; i++)
        {
            optionButtons[i].navigation = new UnityEngine.UI.Navigation
            {
                mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnUp = i > 0 ? optionButtons[i - 1] : null,
                selectOnDown = i + 1 < optionButtons.Length ? optionButtons[i + 1] : null
            };
        }

        settingsPopup.transform.SetAsLastSibling();
        Transform popupWindow = settingsPopup.transform.Find("Popup Window");
        if (popupWindow != null) FocusFirst(popupWindow.gameObject);
    }

    private static void FocusFirst(GameObject panel)
    {
        foreach (Button button in panel.GetComponentsInChildren<Button>())
        {
            if (!button.IsInteractable()) continue;
            FocusButton(button.gameObject);
            return;
        }
    }

    private static void FocusButton(GameObject target)
    {
        if (target == null || !target.activeInHierarchy || EventSystem.current == null) return;
        Button button = target.GetComponent<Button>();
        if (button != null && button.IsInteractable())
            EventSystem.current.SetSelectedGameObject(target);
    }

    private void CloseSettingsPopup()
    {
        if (settingsPopup == null)
            return;

        // Destroy is deferred until the end of the frame. Hide the popup first
        // so navigation cannot select an option that is about to disappear.
        settingsPopup.SetActive(false);
        Destroy(settingsPopup);
        settingsPopup = null;
        if (settingsPanel.activeInHierarchy)
        {
            if (popupReturnFocus != null && popupReturnFocus.activeInHierarchy &&
                popupReturnFocus.transform.IsChildOf(settingsPanel.transform))
                FocusButton(popupReturnFocus);
            else FocusFirst(settingsPanel);
        }
        popupReturnFocus = null;
    }

    private void CloseResetConfirmation()
    {
        confirmationPanel.SetActive(false);
        resetChoicePanel.SetActive(true);
        FocusFirst(resetChoicePanel);
    }

    private void CloseResetChoice()
    {
        resetChoicePanel.SetActive(false);
        menuPanel.SetActive(true);
        menuButtonObject.SetActive(true);
        menuButtonLabel.text = "Назад";
        FocusButton(menuResetButton);
    }

    private void OpenResetConfirmation(bool resetAll)
    {
        resetAllRecords = resetAll;
        resetChoicePanel.SetActive(false);
        confirmationPanel.SetActive(true);
        resetConfirmationMessage.text = resetAll
            ? "Обнулить высоту и счёт во всех режимах?\nЭто действие нельзя отменить."
            : "Обнулить рекорды высоты и счёта?\n" +
              GameplayPreferences.CurrentModeDisplayName + " · " +
              DifficultyRules.Name(GameplayPreferences.Difficulty);
        // Enter must never confirm deletion just because this panel opened.
        FocusButton(resetCancelButton);
    }

    private void ConfirmResetHighScore()
    {
        if (resetAllRecords)
        {
            RunRecords.ResetAll();
        }
        else
        {
            RunRecords.ResetCurrent();
        }
        PlayerPrefs.Save();
        isOpen = false;
        resetChoicePanel.SetActive(false);
        confirmationPanel.SetActive(false);
        menuPanel.SetActive(false);
        menuButtonObject.SetActive(true);
        menuButtonLabel.text = "Меню";
        RestartCurrentSceneSmoothly();
    }

    private void CreateConfirmationContent(Transform parent)
    {
        GameObject messageObject = new GameObject(
            "Confirmation Message",
            typeof(RectTransform)
        );
        messageObject.transform.SetParent(parent, false);
        RectTransform messageRect = messageObject.GetComponent<RectTransform>();
        messageRect.anchorMin = messageRect.anchorMax = new Vector2(0.5f, 0.5f);
        messageRect.anchoredPosition = new Vector2(0f, 105f);
        messageRect.sizeDelta = new Vector2(720f, 150f);

        resetConfirmationMessage = messageObject.AddComponent<TextMeshProUGUI>();
        resetConfirmationMessage.font = GetInterfaceFont();
        resetConfirmationMessage.text = "Обнулить рекорд?";
        resetConfirmationMessage.fontSize = 36f;
        resetConfirmationMessage.enableAutoSizing = true;
        resetConfirmationMessage.fontSizeMin = 26f;
        resetConfirmationMessage.fontSizeMax = 36f;
        resetConfirmationMessage.fontStyle = FontStyles.Bold;
        resetConfirmationMessage.color = new Color(1f, 0.83f, 0.42f, 1f);
        resetConfirmationMessage.alignment = TextAlignmentOptions.Center;

        CreateButton(
            parent,
            "Да, обнулить",
            new Vector2(300f, 105f),
            new Vector2(-175f, -65f),
            ConfirmResetHighScore,
            out _
        );
        resetCancelButton = CreateButton(
            parent,
            "Отмена",
            new Vector2(300f, 105f),
            new Vector2(175f, -65f),
            CloseResetConfirmation,
            out _
        );
    }

    private void CreateResetChoiceContent(Transform parent)
    {
        TMP_Text title = CreateSettingsLabel(
            parent, "Какие рекорды обнулить?", new Vector2(0f, 245f));
        title.fontSize = 42f;
        title.fontStyle = FontStyles.Bold;
        title.color = new Color(1f, 0.83f, 0.42f, 1f);

        CreateButton(
            parent,
            "Текущий режим",
            new Vector2(440f, 105f),
            new Vector2(0f, 85f),
            () => OpenResetConfirmation(false),
            out _
        );
        CreateButton(
            parent,
            "Все рекорды",
            new Vector2(440f, 105f),
            new Vector2(0f, -55f),
            () => OpenResetConfirmation(true),
            out _
        );
        CreateButton(
            parent,
            "Назад",
            new Vector2(300f, 95f),
            new Vector2(0f, -210f),
            CloseResetChoice,
            out _
        );
    }

    public void ExitGame()
    {
        FindFirstObjectByType<Player>()?.Expedition?.EndRun(ExpeditionOutcome.Abandoned);
        FindFirstObjectByType<Player>()?.Journal?.EndRun("Выход");
        PlayerProgress.Flush();
        ExpeditionProgress.Flush();
        Time.timeScale = 1f;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) SaveHudPreferences();
    }

    private void OnApplicationQuit() => SaveHudPreferences();

    private void OnDestroy()
    {
        SaveHudPreferences();
        if (instance == this) instance = null;
        if (isOpen)
        {
            Time.timeScale = previousTimeScale;
        }
    }
}
