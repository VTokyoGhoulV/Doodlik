using UnityEngine;

/// <summary>
/// Applies and stores the player's frame pacing choice on every platform.
/// Phone-only quality optimizations remain limited to mobile builds.
/// </summary>
public static class MobilePerformance
{
    private const string FrameRateKey = "DoodlikMobileFrameRate";
    private const int DefaultFrameRate = 60;
    public const int UnlimitedFrameRate = -1;
    public const int VerticalSync = -2;

    public static int SelectedFrameRateMode => NormalizeMode(
        PlayerPrefs.GetInt(FrameRateKey, DefaultFrameRate));

    public static string SelectedFrameRateLabel =>
        GetFrameRateLabel(SelectedFrameRateMode);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplySavedSettings()
    {
#if UNITY_ANDROID || UNITY_IOS
        QualitySettings.SetQualityLevel(0, true);
        ScalableBufferManager.ResizeBuffers(0.9f, 0.9f);
#endif
        SetFrameRate(PlayerPrefs.GetInt(FrameRateKey, DefaultFrameRate), false);
    }

    public static void SetFrameRate(int requestedFrameRate)
    {
        SetFrameRate(requestedFrameRate, true);
    }

    private static void SetFrameRate(int requestedFrameRate, bool save)
    {
        int mode = NormalizeMode(requestedFrameRate);
        if (mode == VerticalSync)
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
        }
        else
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = mode == UnlimitedFrameRate ? -1 : mode;
        }

        if (save)
        {
            PlayerPrefs.SetInt(FrameRateKey, mode);
            PlayerPrefs.Save();
        }
    }

    public static string GetFrameRateLabel(int mode)
    {
        return NormalizeMode(mode) switch
        {
            30 => "30 FPS",
            60 => "60 FPS",
            120 => "120 FPS",
            UnlimitedFrameRate => "Без лимита",
            VerticalSync => "Вертикальная синхронизация",
            _ => "60 FPS"
        };
    }

    private static int NormalizeMode(int mode)
    {
#if UNITY_ANDROID || UNITY_IOS
        // Mobile displays control presentation timing themselves. Unity's
        // desktop-style VSync and an uncapped target produce uneven pacing on
        // many phones, so migrate those previously saved choices to 60 FPS.
        return mode == 30 || mode == 60 || mode == 120
            ? mode
            : DefaultFrameRate;
#else
        return mode == 30 || mode == 60 || mode == 120 ||
               mode == UnlimitedFrameRate || mode == VerticalSync
            ? mode
            : DefaultFrameRate;
#endif
    }
}
