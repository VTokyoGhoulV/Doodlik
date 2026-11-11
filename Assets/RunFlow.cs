using UnityEngine;

// Session state, never saved: a fresh launch always opens the home screen.
public static class RunFlow
{
    public static bool RunActive { get; set; }
    public static bool IsTransitioning { get; set; }
    public static bool IsPlaying => RunActive && !IsTransitioning;
    public static string ModeKey => (GameplayPreferences.IsPortrait ? "portrait" : "landscape") +
        (GameplayPreferences.UseInertia ? "_inertia" : "_precise");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { RunActive = false; IsTransitioning = false; }
}
