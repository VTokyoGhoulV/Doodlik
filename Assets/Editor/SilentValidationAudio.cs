#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Silence only disposable batch Editors, not the user's Editor or release game.
// Interface cues deliberately ignore AudioListener.pause, so pause alone is
// insufficient. The Editor's master mute also covers previews and new sources.
[InitializeOnLoad]
public static class SilentValidationAudio
{
    private const string Key = "Doodlik.SilentValidationAudio";
    private static bool IsValidation => Application.isBatchMode &&
        Application.dataPath.Replace('\\', '/').Contains("/.utmp/");

    static SilentValidationAudio()
    {
        if (!IsValidation) return;
        if (!SessionState.GetBool(Key + ".saved", false))
        {
            SessionState.SetBool(Key + ".previousMute", EditorUtility.audioMasterMute);
            SessionState.SetBool(Key + ".saved", true);
        }
        Mute();
        EditorApplication.update += Mute;
        EditorApplication.quitting += RestoreEditorPreference;
        Debug.Log("SILENT_VALIDATION_AUDIO masterMute=True listenerVolume=0 batchOnly=True");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    private static void BeforeSplash() => Mute();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BeforeScene() => Mute();

    private static void Mute()
    {
        if (!IsValidation) return;
        EditorUtility.audioMasterMute = true;
        AudioListener.volume = 0f;
        AudioListener.pause = true;
    }

    private static void RestoreEditorPreference()
    {
        EditorApplication.update -= Mute;
        // Runtime volume stays zero until process exit; restore only the shared
        // Editor preference so the next normal Editor launch is not muted.
        EditorUtility.audioMasterMute = SessionState.GetBool(Key + ".previousMute", false);
        SessionState.EraseBool(Key + ".saved");
    }
}
#endif
