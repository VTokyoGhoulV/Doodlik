using UnityEngine;

public enum GameRunKind { Classic = 0, Expedition = 1 }

public static class GameplayPreferences
{
    public const string RunKindKey = "DoodlikRunKind";
    public const string OrientationKey = "DoodlikOrientation";
    public const string InertiaKey = "DoodlikInertia";
    public const string DifficultyKey = "DoodlikDifficulty";
    public const string ScorePanelTransparencyKey = "DoodlikScorePanelTransparency";

    public static GameRunKind RunKind => PlayerPrefs.GetInt(RunKindKey, 0) == (int)GameRunKind.Expedition
        ? GameRunKind.Expedition : GameRunKind.Classic;

    public static bool SetRunKind(GameRunKind kind, bool save = true)
    {
        if (RunFlow.RunActive || RunFlow.IsTransitioning ||
            kind != GameRunKind.Classic && kind != GameRunKind.Expedition) return false;
        PlayerPrefs.SetInt(RunKindKey, (int)kind);
        if (save) PlayerPrefs.Save();
        return true;
    }

    public static GameDifficulty Difficulty => DifficultyRules.Sanitize(
        (GameDifficulty)PlayerPrefs.GetInt(DifficultyKey, (int)GameDifficulty.Normal));

    public static void SetDifficulty(GameDifficulty difficulty)
    {
        PlayerPrefs.SetInt(DifficultyKey, (int)DifficultyRules.Sanitize(difficulty));
        PlayerPrefs.Save();
    }

    public static float ScorePanelTransparency => ValidTransparency(
        PlayerPrefs.GetFloat(ScorePanelTransparencyKey, 0f));

    // The menu flushes after dragging, on close and when the app is suspended.
    public static void SetScorePanelTransparency(float value)
    {
        PlayerPrefs.SetFloat(ScorePanelTransparencyKey, ValidTransparency(value));
    }

    private static float ValidTransparency(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);

    // Uses the selected editor build target so Play Mode matches the active profile.
    public static bool IsDesktopBuild
    {
        get
        {
#if UNITY_STANDALONE
            return true;
#elif UNITY_EDITOR
            UnityEditor.BuildTarget target =
                UnityEditor.EditorUserBuildSettings.activeBuildTarget;
            return target == UnityEditor.BuildTarget.StandaloneWindows ||
                   target == UnityEditor.BuildTarget.StandaloneWindows64 ||
                   target == UnityEditor.BuildTarget.StandaloneOSX ||
                   target == UnityEditor.BuildTarget.StandaloneLinux64;
#else
            return false;
#endif
        }
    }

    public static bool IsPortrait
    {
        get
        {
            if (IsDesktopBuild)
                return false;

            return PlayerPrefs.GetInt(OrientationKey, 0) == 1;
        }
    }

    public static bool UseInertia =>
        PlayerPrefs.GetInt(InertiaKey, 1) == 1;

    public static float ModelScale => IsPortrait ? 0.64f : 1f;

    public static float HorizontalMovementScale => IsPortrait ? 0.64f : 1f;

    public static string CurrentModeDisplayName => IsDesktopBuild
        ? (UseInertia ? "С инерцией" : "Точный контроль")
        : (IsPortrait ? "Книжный" : "Альбомный") +
          (UseInertia ? " · с инерцией" : " · точный контроль");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplySavedOrientation()
    {
        ApplyOrientation(false);

#if UNITY_ANDROID || UNITY_IOS
        if (Object.FindFirstObjectByType<MobileOrientationController>() == null)
        {
            GameObject controller = new GameObject("Mobile Orientation Controller");
            Object.DontDestroyOnLoad(controller);
            controller.AddComponent<MobileOrientationController>();
        }
#endif
    }

    public static void SetPortrait(bool portrait, bool applyImmediately = true)
    {
        if (IsDesktopBuild)
            portrait = false;

        PlayerPrefs.SetInt(OrientationKey, portrait ? 1 : 0);
        PlayerPrefs.Save();
        if (applyImmediately)
            ApplyOrientation(true);
    }

    public static void SetInertia(bool enabled)
    {
        PlayerPrefs.SetInt(InertiaKey, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    // Both parts of a mode share one save operation, retaining the existing
    // preference and record keys. UI session restrictions belong to GameMenuUI.
    public static void SetMode(bool portrait, bool inertia, bool applyImmediately = true)
    {
        if (IsDesktopBuild)
            portrait = false;

        PlayerPrefs.SetInt(OrientationKey, portrait ? 1 : 0);
        PlayerPrefs.SetInt(InertiaKey, inertia ? 1 : 0);
        PlayerPrefs.Save();
        if (applyImmediately)
            ApplyOrientation(true);
    }

    public static void ApplyOrientation(bool refreshSceneObjects)
    {
#if UNITY_ANDROID || UNITY_IOS
        // Применяем выбранный формат принудительно. В отличие от AutoRotation
        // это работает даже при выключенном системном автоповороте Android.
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = false;
        Screen.autorotateToLandscapeRight = false;
        ApplyDeviceOrientation(Input.deviceOrientation);
#endif

        if (refreshSceneObjects)
        {
            PortraitModelScaler.RefreshAll();
        }
    }

    public static void ApplyDeviceOrientation(DeviceOrientation deviceOrientation)
    {
#if UNITY_ANDROID || UNITY_IOS
        ScreenOrientation requestedOrientation;

        if (IsPortrait)
        {
            requestedOrientation =
                deviceOrientation == DeviceOrientation.PortraitUpsideDown
                    ? ScreenOrientation.PortraitUpsideDown
                    : ScreenOrientation.Portrait;
        }
        else
        {
            requestedOrientation =
                deviceOrientation == DeviceOrientation.LandscapeRight
                    ? ScreenOrientation.LandscapeRight
                    : ScreenOrientation.LandscapeLeft;
        }

        if (Screen.orientation != requestedOrientation)
        {
            Screen.orientation = requestedOrientation;
        }
#endif
    }
}

public sealed class MobileOrientationController : MonoBehaviour
{
    private DeviceOrientation lastAppliedOrientation = DeviceOrientation.Unknown;
    private DeviceOrientation pendingOrientation = DeviceOrientation.Unknown;
    private float pendingSince;
    private const float OrientationSettleTime = 0.18f;

    private void Start()
    {
        GameplayPreferences.ApplyDeviceOrientation(Input.deviceOrientation);
    }

    private void Update()
    {
        // The scene transition applies the chosen format once its cover is
        // opaque. Sensor changes must not rotate the surface during fade-out.
        if (RunFlow.IsTransitioning)
            return;

        DeviceOrientation current = Input.deviceOrientation;
        bool belongsToSelectedFormat = GameplayPreferences.IsPortrait
            ? current == DeviceOrientation.Portrait ||
              current == DeviceOrientation.PortraitUpsideDown
            : current == DeviceOrientation.LandscapeLeft ||
              current == DeviceOrientation.LandscapeRight;

        if (!belongsToSelectedFormat || current == lastAppliedOrientation)
        {
            return;
        }

        if (current != pendingOrientation)
        {
            pendingOrientation = current;
            pendingSince = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - pendingSince < OrientationSettleTime)
        {
            return;
        }

        GameplayPreferences.ApplyDeviceOrientation(current);
        lastAppliedOrientation = current;
    }
}

public sealed class PortraitModelScaler : MonoBehaviour
{
    private Vector3 originalScale;

    private void Awake()
    {
        originalScale = transform.localScale;
        Apply();
    }

    public void Apply()
    {
        transform.localScale = originalScale * GameplayPreferences.ModelScale;
    }

    public static void Attach(GameObject target)
    {
        if (target != null && target.GetComponent<PortraitModelScaler>() == null)
        {
            target.AddComponent<PortraitModelScaler>();
        }
    }

    public static void RefreshAll()
    {
        foreach (PortraitModelScaler scaler in
                 FindObjectsByType<PortraitModelScaler>(
                     FindObjectsInactive.Include))
        {
            scaler.Apply();
        }
    }
}
