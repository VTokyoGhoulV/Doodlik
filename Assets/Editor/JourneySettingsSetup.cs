using UnityEditor;
using UnityEngine;

// Create once through Unity's serializer. Existing tuning is never overwritten.
[InitializeOnLoad]
public static class JourneySettingsSetup
{
    public const string AssetPath = "Assets/Resources/Settings/JourneySettings.asset";

    static JourneySettingsSetup() => EditorApplication.delayCall += EnsureAsset;

    public static void EnsureAsset()
    {
        if (AssetDatabase.LoadAssetAtPath<JourneySettings>(AssetPath) != null) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            return;
        }
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Settings")) AssetDatabase.CreateFolder("Assets/Resources", "Settings");
        JourneySettings settings = ScriptableObject.CreateInstance<JourneySettings>();
        AssetDatabase.CreateAsset(settings, AssetPath);
        AssetDatabase.SaveAssetIfDirty(settings);
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EnsureAsset();
    }

    [MenuItem("Doodlik/Настройки путешествия")]
    public static void OpenSettings()
    {
        EnsureAsset();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<JourneySettings>(AssetPath);
    }
}
