#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Check the objects Unity will serialize, rather than only compiled C# types.
public sealed class BuildScriptBindingValidation : IPreprocessBuildWithReport, IProcessSceneWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report) => Validate();

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (report != null) ValidateScene(scene);
    }

    public static void Validate()
    {
        int scenes = 0, prefabs = 0, settings = 0;
        foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
        {
            if (!entry.enabled) continue;
            Scene scene = EditorSceneManager.OpenPreviewScene(entry.path);
            try { ValidateScene(scene); scenes++; }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        if (scenes == 0) throw new BuildFailedException("No enabled gameplay scene.");
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Assets/TextMesh Pro/Examples & Extras/", StringComparison.Ordinal)) continue;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            ValidateHierarchy(prefab, path);
            prefabs++;
        }
        foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(asset is ScriptableObject value)) continue;
                MonoScript script = MonoScript.FromScriptableObject(value);
                RequireBinding(script, value.GetType(), path);
                settings++;
            }
        }
        Debug.Log($"BUILD_SCRIPT_BINDINGS_PASS scenes={scenes} prefabs={prefabs} settings={settings}");
    }

    public static void Probe()
    {
        foreach (string path in new[] { "Assets/Player.cs", "Assets/Spawner.cs", "Assets/Camera.cs", "Assets/CameraFollow.cs", "Assets/JourneySettings.cs", "Assets/Bush.cs", "Assets/Platform.cs" })
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null)
                Debug.Log($"SCRIPT_BINDING_PROBE path={path} guid={AssetDatabase.AssetPathToGUID(path)} type={script.GetClass()?.AssemblyQualifiedName ?? "NULL"}");
        }
        Validate();
    }

    private static void ValidateScene(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects()) ValidateHierarchy(root, scene.path);
        if (scene.path != "Assets/Scenes/SampleScene.unity") return;
        int players = 0, spawners = 0, cameras = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            players += root.GetComponentsInChildren<Player>(true).Length;
            spawners += root.GetComponentsInChildren<Spawner>(true).Length;
            cameras += root.GetComponentsInChildren<CameraFollow>(true).Length;
        }
        if (players != 1 || spawners != 1 || cameras != 1)
            throw new BuildFailedException($"Gameplay script binding is invalid: Player={players}, Spawner={spawners}, CameraFollow={cameras} in {scene.path}.");
    }

    private static void ValidateHierarchy(GameObject root, string path)
    {
        if (root == null) throw new BuildFailedException("Missing prefab: " + path);
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0)
                throw new BuildFailedException("Missing MonoBehaviour in " + path + "/" + child.name);
            foreach (MonoBehaviour behaviour in child.GetComponents<MonoBehaviour>())
                RequireBinding(MonoScript.FromMonoBehaviour(behaviour), behaviour.GetType(), path + "/" + child.name);
        }
    }

    private static void RequireBinding(MonoScript script, Type expected, string path)
    {
        if (script == null || script.GetClass() != expected)
            throw new BuildFailedException($"Invalid MonoScript binding at {path}: expected {expected.FullName}, got {script?.GetClass()?.FullName ?? "NULL"}.");
    }
}
#endif
