#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RogueValidation
{
    public static void RunCore()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/"))
            throw new InvalidOperationException("Disposable batch QA projects only.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikRogueCore_20261102";
        AudioListener.volume = 0f; AudioListener.pause = true;
        RunFlow.RunActive = RunFlow.IsTransitioning = false;
        GameplayPreferences.SetRunKind(GameRunKind.Classic);
        GameplayPreferences.SetMode(false, false, false);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RogueEconomyValidation.Validate();
        RogueRunValidation.Validate();
        ExpeditionValidation.RunCore();
        Type enemy = typeof(RogueValidation).Assembly.GetType("ExpeditionEnemyValidation");
        if (enemy == null) throw new InvalidOperationException("Enemy rules/placement validator must be present.");
        enemy.GetMethod("ValidateRules").Invoke(null, null);
        enemy.GetMethod("ValidatePlacement").Invoke(null, null);
        enemy.GetMethod("CaptureEnemy").Invoke(null, null);
        Debug.Log("ROGUE_CORE_PASS economy=true training=true recipes=true talents=true combatHealth=true ordinaryRegression=true enemyRules=true enemyPlacement=true");
    }
}
#endif
