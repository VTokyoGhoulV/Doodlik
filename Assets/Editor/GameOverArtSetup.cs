#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

[InitializeOnLoad]
public static class GameOverArtSetup
{
    static GameOverArtSetup()
    {
        EditorApplication.delayCall += EnsureSadCatImage;
    }

    [MenuItem("Doodlik/Refresh Game Over Cat")]
    public static void EnsureSadCatImage()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != "Assets/Scenes/SampleScene.unity")
        {
            return;
        }

        Player player = Object.FindFirstObjectByType<Player>(
            FindObjectsInactive.Include
        );
        if (player == null || player.gameOverPanel == null)
        {
            return;
        }

        Transform root = player.gameOverPanel.transform;
        Transform sadCat = root.Find("Sad Cat Image");

        if (sadCat == null)
        {
            Sprite sadCatSprite = LoadSprite("Assets/SadCat.png");
            if (sadCatSprite == null)
            {
                Debug.LogWarning("SadCat sprite was not found.");
                return;
            }

            GameObject imageObject = new GameObject(
                "Sad Cat Image",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image)
            );
            imageObject.transform.SetParent(root, false);
            sadCat = imageObject.transform;

            Image image = imageObject.GetComponent<Image>();
            image.sprite = sadCatSprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        Transform overlay = root.Find("Game Over Panel");
        if (overlay == null)
        {
            Debug.LogWarning("Game Over Panel was not found.");
            return;
        }

        Image overlayImage = overlay.GetComponent<Image>();
        overlayImage.color = new Color(0.08f, 0.12f, 0.2f, 0.78f);
        overlay.SetAsFirstSibling();

        Transform card = root.Find("Game Over Card");
        if (card == null)
        {
            GameObject cardObject = new GameObject(
                "Game Over Card",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Outline),
                typeof(Shadow)
            );
            cardObject.transform.SetParent(root, false);
            card = cardObject.transform;
        }

        RectTransform cardRect = (RectTransform)card;
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = Vector2.zero;
        cardRect.sizeDelta = new Vector2(600f, 680f);
        cardRect.localRotation = Quaternion.Euler(0f, 0f, -1.2f);

        Image cardImage = card.GetComponent<Image>();
        cardImage.color = new Color(1f, 0.91f, 0.67f, 0.97f);
        cardImage.raycastTarget = false;

        Outline cardOutline = card.GetComponent<Outline>();
        cardOutline.effectColor = new Color(0.58f, 0.25f, 0.07f, 1f);
        cardOutline.effectDistance = new Vector2(8f, -8f);

        Shadow cardShadow = card.GetComponent<Shadow>();
        cardShadow.effectColor = new Color(0.02f, 0.035f, 0.07f, 0.55f);
        cardShadow.effectDistance = new Vector2(14f, -16f);

        TMP_Text title = card.Find("GameOverText")?.GetComponent<TMP_Text>();
        if (title == null)
        {
            title = overlay.GetComponentInChildren<TMP_Text>(true);
        }

        Button restartButton = card.GetComponentInChildren<Button>(true);
        if (restartButton == null)
        {
            restartButton = overlay.GetComponentInChildren<Button>(true);
        }

        sadCat.SetParent(card, false);
        sadCat.SetAsFirstSibling();

        RectTransform rect = sadCat.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 115f);
        rect.sizeDelta = new Vector2(330f, 300f);
        rect.localRotation = Quaternion.identity;

        if (title != null)
        {
            title.transform.SetParent(card, false);
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = new Vector2(0f, -55f);
            titleRect.sizeDelta = new Vector2(500f, 90f);
            titleRect.localRotation = Quaternion.Euler(0f, 0f, 2.3f);
            title.text = "Ой! Котик упал";
            title.color = new Color(0.36f, 0.13f, 0.035f, 1f);
            title.fontSize = 48f;
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Center;
        }

        Transform stats = card.Find("Game Over Stats");
        if (stats == null)
        {
            GameObject statsObject = new GameObject(
                "Game Over Stats",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI)
            );
            statsObject.transform.SetParent(card, false);
            stats = statsObject.transform;
        }

        RectTransform statsRect = stats.GetComponent<RectTransform>();
        statsRect.anchorMin = statsRect.anchorMax = new Vector2(0.5f, 0.5f);
        statsRect.anchoredPosition = new Vector2(0f, -120f);
        statsRect.sizeDelta = new Vector2(470f, 70f);
        statsRect.localRotation = Quaternion.identity;

        TMP_Text statsText = stats.GetComponent<TMP_Text>();
        statsText.text = "Счёт: 0     Рекорд: 0";
        statsText.color = new Color(0.36f, 0.17f, 0.06f, 1f);
        statsText.fontSize = 27f;
        statsText.fontStyle = FontStyles.Bold;
        statsText.alignment = TextAlignmentOptions.Center;

        Transform celebration = card.Find("New Record Celebration");
        if (celebration == null)
        {
            GameObject celebrationObject = new GameObject(
                "New Record Celebration",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI)
            );
            celebrationObject.transform.SetParent(card, false);
            celebration = celebrationObject.transform;
            CreateConfetti(celebration);
        }

        RectTransform celebrationRect = celebration.GetComponent<RectTransform>();
        celebrationRect.anchorMin = celebrationRect.anchorMax = new Vector2(0.5f, 0.5f);
        celebrationRect.anchoredPosition = new Vector2(0f, -178f);
        celebrationRect.sizeDelta = new Vector2(500f, 60f);
        celebrationRect.localRotation = Quaternion.Euler(0f, 0f, 1.5f);

        TMP_Text celebrationText = celebration.GetComponent<TMP_Text>();
        celebrationText.text = "НОВЫЙ РЕКОРД!";
        celebrationText.color = new Color(0.92f, 0.28f, 0.045f, 1f);
        celebrationText.fontSize = 31f;
        celebrationText.fontStyle = FontStyles.Bold;
        celebrationText.alignment = TextAlignmentOptions.Center;
        celebration.gameObject.SetActive(false);

        if (restartButton != null)
        {
            restartButton.transform.SetParent(card, false);
            RectTransform buttonRect = restartButton.GetComponent<RectTransform>();
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(0f, -255f);
            buttonRect.sizeDelta = new Vector2(330f, 82f);
            buttonRect.localRotation = Quaternion.Euler(0f, 0f, -1.5f);

            Image buttonImage = restartButton.GetComponent<Image>();
            buttonImage.color = new Color(1f, 0.63f, 0.12f, 1f);
            Outline buttonOutline = GetOrAdd<Outline>(restartButton.gameObject);
            buttonOutline.effectColor = new Color(0.58f, 0.22f, 0.04f, 1f);
            buttonOutline.effectDistance = new Vector2(5f, -5f);

            TMP_Text buttonText = restartButton.GetComponentInChildren<TMP_Text>(true);
            if (buttonText != null)
            {
                buttonText.text = "Ещё раз!  (R)";
                buttonText.color = new Color(0.25f, 0.09f, 0.02f, 1f);
                buttonText.fontStyle = FontStyles.Bold;
                buttonText.fontSize = 32f;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Game Over UI styling refreshed.");
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static void CreateConfetti(Transform parent)
    {
        Color[] colors =
        {
            new Color(1f, 0.55f, 0.08f, 1f),
            new Color(0.95f, 0.22f, 0.12f, 1f),
            new Color(0.25f, 0.68f, 0.38f, 1f),
            new Color(0.25f, 0.55f, 0.9f, 1f),
            new Color(0.8f, 0.35f, 0.72f, 1f)
        };

        Vector2[] positions =
        {
            new Vector2(-235f, 32f), new Vector2(-205f, 53f),
            new Vector2(-178f, 28f), new Vector2(-228f, -8f),
            new Vector2(-190f, -25f), new Vector2(235f, 32f),
            new Vector2(205f, 53f), new Vector2(178f, 28f),
            new Vector2(228f, -8f), new Vector2(190f, -25f)
        };

        for (int i = 0; i < positions.Length; i++)
        {
            GameObject piece = new GameObject(
                $"Confetti {i + 1}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image)
            );
            piece.transform.SetParent(parent, false);
            RectTransform pieceRect = piece.GetComponent<RectTransform>();
            pieceRect.anchorMin = pieceRect.anchorMax = new Vector2(0.5f, 0.5f);
            pieceRect.anchoredPosition = positions[i];
            pieceRect.sizeDelta = new Vector2(10f, 28f);
            float side = positions[i].x < 0f ? -1f : 1f;
            pieceRect.localRotation = Quaternion.Euler(
                0f,
                0f,
                side * (25f + (i % 3) * 24f)
            );
            Image pieceImage = piece.GetComponent<Image>();
            pieceImage.color = colors[i % colors.Length];
            pieceImage.raycastTarget = false;
        }
    }

    private static Sprite LoadSprite(string path)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is Sprite sprite)
            {
                return sprite;
            }
        }

        return null;
    }
}
#endif
