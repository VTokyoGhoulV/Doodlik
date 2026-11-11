using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Keeps the interface readable on desktop monitors and landscape phones,
/// including devices with a notch or rounded screen corners.
/// </summary>
public sealed class ResponsiveDisplay : MonoBehaviour
{
    private static readonly Vector2 LandscapeReferenceResolution =
        new Vector2(1920f, 1080f);
    private static readonly Vector2 PortraitReferenceResolution =
        new Vector2(1080f, 1920f);

    private RectTransform safeAreaRoot;
    private Rect lastSafeArea;
    private Vector2Int lastScreenSize;
    private Canvas sceneCanvas;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ObserveSceneLoads()
    {
        // RuntimeInitialize runs only once per play session. Each restarted
        // scene owns a new score canvas and needs the same setup before Start.
        SceneManager.sceneLoaded -= ConfigureSceneInterface;
        SceneManager.sceneLoaded += ConfigureSceneInterface;
    }

    private static void ConfigureSceneInterface(Scene scene, LoadSceneMode mode)
    {
        GameplayPreferences.ApplyOrientation(false);

        foreach (Canvas canvas in FindObjectsByType<Canvas>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            ConfigureScaler(canvas);
        }

        GameObject scoreCanvas = GameObject.Find("Score UI");
        if (scoreCanvas != null &&
            scoreCanvas.GetComponent<ResponsiveDisplay>() == null)
        {
            scoreCanvas.AddComponent<ResponsiveDisplay>();
        }
    }

    public static void ConfigureScaler(Canvas canvas)
    {
        if (canvas == null)
        {
            return;
        }

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = Screen.width < Screen.height
            ? PortraitReferenceResolution
            : LandscapeReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    private void Awake()
    {
        Canvas canvas = GetComponent<Canvas>();
        sceneCanvas = canvas;
        ConfigureScaler(canvas);

        safeAreaRoot = new GameObject(
            "Safe Area",
            typeof(RectTransform)
        ).GetComponent<RectTransform>();
        safeAreaRoot.SetParent(transform, false);
        safeAreaRoot.SetAsFirstSibling();

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child != safeAreaRoot)
            {
                child.SetParent(safeAreaRoot, false);
            }
        }

        ApplySafeArea(true);
    }

    private void Update()
    {
        ApplySafeArea(false);
    }

    private void ApplySafeArea(bool force)
    {
        Rect safeArea = Screen.safeArea;
        Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
        if (!force && safeArea == lastSafeArea && screenSize == lastScreenSize)
        {
            return;
        }

        lastSafeArea = safeArea;
        lastScreenSize = screenSize;
        ConfigureScaler(sceneCanvas);

        if (Screen.width <= 0 || Screen.height <= 0)
        {
            return;
        }

        Vector2 anchorMin = safeArea.position;
        Vector2 anchorMax = safeArea.position + safeArea.size;
        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;
        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;

        safeAreaRoot.anchorMin = anchorMin;
        safeAreaRoot.anchorMax = anchorMax;
        safeAreaRoot.offsetMin = Vector2.zero;
        safeAreaRoot.offsetMax = Vector2.zero;
    }
}
