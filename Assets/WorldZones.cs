using UnityEngine;

public static class WorldZones
{
    public enum Zone { Meadow, Forest, Clouds, Sunset, Night, Moon }
    private static JourneySettings journeySettings;
    private static bool settingsLoaded;
    public static int GoalScore
    {
        get
        {
            if (!settingsLoaded)
            {
                journeySettings = Resources.Load<JourneySettings>(JourneySettings.ResourcePath);
                settingsLoaded = true;
            }
            return journeySettings != null ? journeySettings.GoalScore : JourneySettings.DefaultGoalScore;
        }
    }
    public const int ZoneCount = 6;
    public static Zone Current { get; private set; }
    public static int CurrentScore { get; private set; }
    // Legacy 'Score' APIs in the generator still mean unmultiplied height units.
    // Awarded run points belong to Player.CurrentScore and must not enter here.
    public static int CurrentHeight => CurrentScore;
    public static int GoalHeight => GoalScore;
    public static float RunStartHeight { get; private set; }
    public static float PointsPerUnit { get; private set; } = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        journeySettings = null;
        settingsLoaded = false;
        ConfigureRun(0f, 1f);
    }

    public static void ConfigureRun(float startHeight, float pointsPerUnit)
    {
        RunStartHeight = startHeight;
        PointsPerUnit = Mathf.Max(0f, pointsPerUnit);
        CurrentScore = 0;
        Current = Zone.Meadow;
    }

    public static void SetHeight(float height)
    {
        SetScore(ScoreAtHeight(height));
    }

    public static void SetScore(int score)
    {
        CurrentScore = Mathf.Max(0, score);
        Zone next = AtScore(CurrentScore);
        if (next == Current) return;
        Zone previous = Current;
        Current = next;
        if (RunFlow.IsPlaying && (int)next > (int)previous)
            GameAudio.Play(GameAudio.Cue.Zone);
    }

    public static void SetHeightProgress(int height) => SetScore(height);

    public static int ScoreAtHeight(float height) => ScoreForHeight(height, RunStartHeight, PointsPerUnit);

    // Keep float arithmetic/rounding for ordinary runs, but clamp before the
    // integer conversion: long runs and high multipliers must not wrap negative.
    public static int ScoreForHeight(float height, float startHeight, float pointsPerUnit)
    {
        float points = (height - startHeight) * Mathf.Max(0f, pointsPerUnit);
        if (!(points > 0f)) return 0; // Includes an invalid NaN height/rate.
        if (points >= int.MaxValue) return int.MaxValue;
        return Mathf.FloorToInt(points);
    }

    public static float HeightAtScore(int score) => RunStartHeight +
        (PointsPerUnit > 0f ? Mathf.Max(0, score) / PointsPerUnit : 0f);

    public static Zone AtHeight(float height) => AtScore(ScoreAtHeight(height));

    public static Zone AtScore(int score) => score >= StartScore(Zone.Moon) ? Zone.Moon
        : score >= StartScore(Zone.Night) ? Zone.Night : score >= StartScore(Zone.Sunset) ? Zone.Sunset
        : score >= StartScore(Zone.Clouds) ? Zone.Clouds : score >= StartScore(Zone.Forest) ? Zone.Forest : Zone.Meadow;

    public static int StartScore(Zone zone)
    {
        double ratio = zone == Zone.Moon ? 1.0 : zone == Zone.Night ? 0.75
            : zone == Zone.Sunset ? 0.45 : zone == Zone.Clouds ? 0.275 : zone == Zone.Forest ? 0.125 : 0;
        return (int)System.Math.Round(GoalScore * ratio, System.MidpointRounding.AwayFromZero);
    }

    public static float StartHeight(Zone zone) => HeightAtScore(StartScore(zone));

    public static float ProgressForScore(int score) => Mathf.Clamp01(score / (float)GoalScore);

    public static bool IsCloudZone(Zone zone) => zone == Zone.Clouds || zone == Zone.Sunset;

    public static bool IsNightZone(Zone zone) => zone == Zone.Night || zone == Zone.Moon;

    public static bool IsDarkBackground(Zone zone) => IsNightZone(zone);

    public static string Name(Zone zone) => zone == Zone.Moon ? "Космос"
        : zone == Zone.Night ? "Граница космоса" : zone == Zone.Sunset ? "Высокое небо"
        : zone == Zone.Clouds ? "Облачные тропинки" : zone == Zone.Forest ? "Над деревьями" : "Цветочный луг";


    public static string Challenge(Zone zone) => IsNightZone(zone) ? "Осторожно: колючие семена"
        : IsCloudZone(zone) ? "Движущиеся и хрупкие платформы" : "Берегись колючек";

    // Palette reference for UI/legacy editor validation; illustrated backgrounds
    // themselves render white and are never recoloured with this palette.
    public static Color BackgroundTint(Zone zone) => zone == Zone.Moon
        ? new Color(0.27f, 0.42f, 0.56f, 1f)
        : zone == Zone.Night ? new Color(0.37f, 0.39f, 0.61f, 1f)
        : zone == Zone.Sunset ? new Color(0.86f, 0.57f, 0.47f, 1f)
        : zone == Zone.Clouds ? new Color(0.60f, 0.74f, 0.88f, 1f)
        : zone == Zone.Forest ? new Color(0.51f, 0.68f, 0.53f, 1f)
        : new Color(0.70f, 0.74f, 0.70f, 1f);
}
