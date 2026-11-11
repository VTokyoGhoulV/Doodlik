#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Small deterministic regression layer, run separately from the real UI loop.
public static class ExpeditionValidation
{
    public static void RunCore()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/"))
            throw new InvalidOperationException("Use an isolated batch expedition QA copy.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikExpeditionCore";
        RunFlow.RunActive = RunFlow.IsTransitioning = false;
        GameplayPreferences.SetRunKind(GameRunKind.Classic);
        GameplayPreferences.SetMode(false, false, false);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ExpeditionProgressValidation.Validate();
        ChallengeProgressValidation.Validate();
        RunStatisticsValidation.Validate();
        AdminProgressValidation.Validate();
        ExpeditionWorldValidation.ValidateRules();
        ExpeditionWorldValidation.ValidatePlacement();
        ExpeditionWorldValidation.CaptureFinds();
        BuildScriptBindingValidation.Validate();
        Debug.Log("EXPEDITION_CORE_REGRESSION_PASS classicChallenges=true classicStatistics=true admin=true cargoSave=true art=true placement=true bindings=true");
    }
}
#endif
