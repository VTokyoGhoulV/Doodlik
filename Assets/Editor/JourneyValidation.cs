#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class JourneyValidation
{
    [MenuItem("Doodlik/Validate Journey Update")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before running validation.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        WorldGenerationValidation.Run();
        PlayerProgressValidation.Run();
        HazardValidation.Run();
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0)
                throw new Exception("Missing script: " + child.name);
        Player player = UnityEngine.Object.FindAnyObjectByType<Player>();
        if (player == null || player.scoreText == null || player.gameOverPanel == null || Camera.main == null)
            throw new Exception("Required scene reference is missing");
        Debug.Log("JOURNEY_EDITMODE_PASS: generator, hazards, wallet, scene references");
    }
}
#endif
