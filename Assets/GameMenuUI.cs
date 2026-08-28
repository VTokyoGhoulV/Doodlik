using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameMenuUI : MonoBehaviour
{
    private GameObject menuPanel;
    private GameObject confirmationPanel;
    private GameObject menuButtonObject;
    private TMP_Text menuButtonLabel;
    private bool isOpen;
    private float previousTimeScale = 1f;

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
        BuildInterface();
    }

    private void Update()
    {
        if (Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (confirmationPanel != null && confirmationPanel.activeSelf)
            {
                CloseResetConfirmation();
                return;
            }

            ToggleMenu();
        }
    }

    private void BuildInterface()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        menuPanel = CreatePanel(transform);
        CreateButton(
            menuPanel.transform,
            "Выйти из игры",
            new Vector2(420f, 120f),
            new Vector2(0f, -90f),
            ExitGame,
            out _
        );
        CreateButton(
            menuPanel.transform,
            "Обнулить рекорд",
            new Vector2(420f, 120f),
            new Vector2(0f, 70f),
            OpenResetConfirmation,
            out _
        );
        menuPanel.SetActive(false);

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
        RectTransform menuRect = menuButtonObject.GetComponent<RectTransform>();
        menuRect.anchorMin = Vector2.one;
        menuRect.anchorMax = Vector2.one;
        menuRect.pivot = Vector2.one;
        menuRect.anchoredPosition = new Vector2(-35f, -35f);
        menuRect.localRotation = Quaternion.Euler(0f, 0f, -2.5f);
        menuButtonObject.transform.SetAsLastSibling();
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
        button.onClick.AddListener(onClick);

        GameObject textObject = new GameObject("Label", typeof(RectTransform));
        textObject.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 8f);
        textRect.offsetMax = new Vector2(-12f, -8f);

        text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 36f;
        text.color = new Color(0.27f, 0.12f, 0.035f, 1f);
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        text.enableAutoSizing = true;
        text.fontSizeMin = 22f;
        text.fontSizeMax = 40f;

        Shadow textShadow = textObject.AddComponent<Shadow>();
        textShadow.effectColor = new Color(1f, 0.91f, 0.56f, 0.75f);
        textShadow.effectDistance = new Vector2(2f, -2f);

        return buttonObject;
    }

    private void ToggleMenu()
    {
        isOpen = !isOpen;

        if (isOpen)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }
        else
        {
            Time.timeScale = previousTimeScale;
        }

        menuPanel.SetActive(isOpen);
        menuButtonLabel.text = isOpen ? "Назад" : "Меню";
    }

    private void OpenResetConfirmation()
    {
        menuPanel.SetActive(false);
        confirmationPanel.SetActive(true);
        menuButtonObject.SetActive(false);
    }

    private void CloseResetConfirmation()
    {
        confirmationPanel.SetActive(false);
        menuPanel.SetActive(true);
        menuButtonObject.SetActive(true);
        menuButtonLabel.text = "Назад";
    }

    private void ConfirmResetHighScore()
    {
        PlayerPrefs.DeleteKey(Player.HighScoreKey);
        PlayerPrefs.Save();
        isOpen = false;
        confirmationPanel.SetActive(false);
        menuPanel.SetActive(false);
        menuButtonObject.SetActive(true);
        menuButtonLabel.text = "Меню";
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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
        messageRect.sizeDelta = new Vector2(720f, 130f);

        TMP_Text message = messageObject.AddComponent<TextMeshProUGUI>();
        message.text = "Обнулить рекорд?\nЭто действие нельзя отменить.";
        message.fontSize = 42f;
        message.fontStyle = FontStyles.Bold;
        message.color = new Color(1f, 0.83f, 0.42f, 1f);
        message.alignment = TextAlignmentOptions.Center;

        CreateButton(
            parent,
            "Да, обнулить",
            new Vector2(300f, 105f),
            new Vector2(-175f, -65f),
            ConfirmResetHighScore,
            out _
        );
        CreateButton(
            parent,
            "Отмена",
            new Vector2(300f, 105f),
            new Vector2(175f, -65f),
            CloseResetConfirmation,
            out _
        );
    }

    private void ExitGame()
    {
        Time.timeScale = 1f;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnDestroy()
    {
        if (isOpen)
        {
            Time.timeScale = previousTimeScale;
        }
    }
}
