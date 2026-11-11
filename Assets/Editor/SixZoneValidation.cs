using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class SixZoneValidation
{
    public static void ValidateScoreBoundaries()
    {
        float start = WorldZones.RunStartHeight, multiplier = WorldZones.PointsPerUnit;
        int score = WorldZones.CurrentScore;
        bool runActive = RunFlow.RunActive;
        int checks = 0;
        try
        {
            RunFlow.RunActive = false;
            WorldZones.ConfigureRun(0f, 1f);
            Check(WorldZones.ScoreAtHeight((float)int.MaxValue) == int.MaxValue,
                "The first float above the integer limit saturates instead of wrapping to zero.", ref checks);
            Check(WorldZones.ScoreAtHeight(float.MaxValue) == int.MaxValue,
                "Very large finite heights preserve the maximum score.", ref checks);
            Check(WorldZones.ScoreAtHeight(123.99f) == 123 && WorldZones.ScoreAtHeight(-100f) == 0,
                "Normal scores keep floor rounding and zero lower bound.", ref checks);
            WorldZones.ConfigureRun(10f, 100f);
            Check(WorldZones.ScoreAtHeight(30000000f) == int.MaxValue &&
                WorldZones.AtHeight(30000000f) == WorldZones.Zone.Moon,
                "Configurable score multipliers cannot wrap the score back to the meadow.", ref checks);
            WorldZones.ConfigureRun(-float.MaxValue, float.MaxValue);
            Check(WorldZones.ScoreAtHeight(float.MaxValue) == int.MaxValue,
                "Overflow in height multiplication saturates to the maximum score.", ref checks);
            WorldZones.ConfigureRun(0f, 0f);
            Check(WorldZones.ScoreAtHeight(float.MaxValue) == 0,
                "Zero score multiplier still awards no points.", ref checks);
            Debug.Log($"SCORE_BOUNDARIES_PASS checks={checks}");
        }
        finally
        {
            WorldZones.ConfigureRun(start, multiplier);
            WorldZones.SetScore(score);
            RunFlow.RunActive = runActive;
        }
    }

    [MenuItem("Doodlik/Validate Six Height Zones")]
    public static void ValidateRules()
    {
        float start = WorldZones.RunStartHeight, multiplier = WorldZones.PointsPerUnit;
        int score = WorldZones.CurrentScore;
        bool runActive = RunFlow.RunActive;
        FieldInfo settingsField = typeof(WorldZones).GetField("journeySettings", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo loadedField = typeof(WorldZones).GetField("settingsLoaded", BindingFlags.Static | BindingFlags.NonPublic);
        object originalSettings = settingsField.GetValue(null), originalLoaded = loadedField.GetValue(null);
        JourneySettings temporary = ScriptableObject.CreateInstance<JourneySettings>();
        temporary.hideFlags = HideFlags.DontSave;
        int checks = 0;
        try
        {
            RunFlow.RunActive = false;
            settingsField.SetValue(null, temporary);
            loadedField.SetValue(null, true);
            foreach (int goal in new[] { 200, 2000, 4000, 5000 })
            {
            typeof(JourneySettings).GetField("journeyGoalScore", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(temporary, goal);
            Check(WorldZones.GoalScore == goal, "Journey goal follows its configuration.", ref checks);
            Check(PlayerProgress.AstronautScore == goal,
                "Astronaut unlock follows arrival in space when the goal changes.", ref checks);
            double[] ratios = { 0, 0.125, 0.275, 0.45, 0.75, 1.0 };
            int[] boundaries = new int[ratios.Length];
            for (int i = 0; i < ratios.Length; i++) boundaries[i] = (int)Math.Round(goal * ratios[i], MidpointRounding.AwayFromZero);
            HashSet<string> names = new HashSet<string>();
            for (int i = 0; i < boundaries.Length; i++)
            {
                WorldZones.Zone zone = (WorldZones.Zone)i;
                Check(WorldZones.StartScore(zone) == boundaries[i], "Exact score boundary.", ref checks);
                Check(WorldZones.AtScore(boundaries[i]) == zone, "Zone starts at its score boundary.", ref checks);
                if (i > 0) Check(WorldZones.AtScore(boundaries[i] - 1) == (WorldZones.Zone)(i - 1),
                    "Previous point still belongs to the prior zone.", ref checks);
                Check(names.Add(WorldZones.Name(zone)), "Each zone has its own name.", ref checks);
            }
            foreach (float points in new[] { 1f, 10f })
            foreach (float origin in new[] { -1.5f, 5.25f })
            {
                WorldZones.ConfigureRun(origin, points);
                Check(WorldZones.ScoreAtHeight(origin) == 0, "Spawn height is zero score.", ref checks);
                foreach (int boundary in boundaries)
                {
                    float height = origin + boundary / points;
                    Check(WorldZones.ScoreAtHeight(height + 0.002f) == boundary,
                        "World height uses the actual score multiplier and start height.", ref checks);
                    Check(WorldZones.AtHeight(height + 0.002f) == WorldZones.AtScore(boundary),
                        "Generator zone agrees with the displayed score.", ref checks);
                    WorldZones.SetScore(boundary);
                    Check(WorldZones.Current == WorldZones.AtScore(boundary), "Runtime zone uses score.", ref checks);
                }
            }
            Check(WorldZones.IsCloudZone(WorldZones.Zone.Clouds) && WorldZones.IsCloudZone(WorldZones.Zone.Sunset),
                "Both cloud zones keep cloud challenges.", ref checks);
            Check(WorldZones.IsNightZone(WorldZones.Zone.Night) && WorldZones.IsNightZone(WorldZones.Zone.Moon),
                "Both night zones keep night challenges.", ref checks);
            Check(WorldZones.AtScore(goal) == WorldZones.Zone.Moon && WorldZones.AtScore(int.MaxValue) == WorldZones.Zone.Moon,
                "The last zone continues beyond the travel goal.", ref checks);
            Check(WorldZones.AtScore(goal - 1) == WorldZones.Zone.Night &&
                WorldZones.StartScore(WorldZones.Zone.Moon) == goal,
                "Space begins exactly when the panorama and reward reach the goal.", ref checks);
            Check(WorldZones.ProgressForScore(-10) == 0f && WorldZones.ProgressForScore(0) == 0f &&
                WorldZones.ProgressForScore(goal / 2) == 0.5f && WorldZones.ProgressForScore(goal) == 1f &&
                WorldZones.ProgressForScore(int.MaxValue) == 1f, "Progress clamps cleanly at both ends.", ref checks);
            WorldZones.SetScore(goal + 100);
            Check(WorldZones.CurrentScore == goal + 100, "Travel goal does not cap the run score.", ref checks);
            }
            foreach (float aspect in new[] { 9f / 16f, 16f / 9f, 20f / 9f, 32f / 9f })
            {
                float priorBottom = -1f;
                foreach (float progress in new[] { 0f, 0.125f, 0.275f, 0.45f, 0.625f, 0.8f, 1f })
                {
                    Rect uv = ScrollingBackground.GetPanoramaUv(aspect, 1f / 3f, progress);
                    Check(uv.xMin >= 0f && uv.yMin >= 0f && uv.xMax <= 1.000001f && uv.yMax <= 1.000001f,
                        "Panorama stays inside the single illustration.", ref checks);
                    Check(Mathf.Abs(uv.width / 3f / uv.height - aspect) < 0.0001f,
                        "Panorama texels retain their aspect in portrait/landscape/ultrawide.", ref checks);
                    Check(uv.yMin >= priorBottom, "Background travel is monotonic, with no wrapping seams.", ref checks);
                    priorBottom = uv.yMin;
                }
                Check(ScrollingBackground.GetPanoramaUv(aspect, 1f / 3f, 1f) ==
                    ScrollingBackground.GetPanoramaUv(aspect, 1f / 3f, 2f), "Space does not loop back after the goal.", ref checks);
            }
            Debug.Log($"SIX_ZONE_RULES_PASS checks={checks} configurableGoals=200,2000,4000,5000 panoramaAspects=4");
        }
        finally
        {
            settingsField.SetValue(null, originalSettings);
            loadedField.SetValue(null, originalLoaded);
            UnityEngine.Object.DestroyImmediate(temporary);
            WorldZones.ConfigureRun(start, multiplier);
            WorldZones.SetScore(score);
            RunFlow.RunActive = runActive;
        }
    }

    public static void ValidateRuntimeUI()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Height HUD validation requires Play Mode.");
        Player player = UnityEngine.Object.FindAnyObjectByType<Player>();
        HeightProgressUI hud = UnityEngine.Object.FindAnyObjectByType<HeightProgressUI>();
        if (player == null || hud == null) throw new InvalidOperationException("Player and height HUD must exist.");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo scoreField = typeof(Player).GetField("reachedHeight", flags);
        FieldInfo deadField = typeof(Player).GetField("isDead", flags);
        MethodInfo update = typeof(HeightProgressUI).GetMethod("Update", flags);
        int originalScore = player.CurrentHeight, zoneScore = WorldZones.CurrentScore;
        bool dead = player.IsDead, active = RunFlow.RunActive, transition = RunFlow.IsTransitioning;
        int checks = 0;
        try
        {
            RunFlow.RunActive = false;
            RunFlow.IsTransitioning = false;
            update.Invoke(hud, null);
            Check(!hud.IsVisible, "No progress rail on the start screen.", ref checks);
            RunFlow.RunActive = true;
            deadField.SetValue(player, false);
            foreach (int score in new[] { 0, WorldZones.StartScore(WorldZones.Zone.Forest),
                WorldZones.StartScore(WorldZones.Zone.Sunset), WorldZones.StartScore(WorldZones.Zone.Moon),
                WorldZones.GoalScore - 1, WorldZones.GoalScore, WorldZones.GoalScore + 100 })
            {
                scoreField.SetValue(player, score);
                update.Invoke(hud, null);
                int displayGoal = WorldZones.GoalScore;
                Check(hud.IsVisible && Mathf.Approximately(hud.DisplayedProgress, Mathf.Clamp01(score / (float)displayGoal)),
                    "The marker reaches the map limit exactly at space arrival.", ref checks);
            }
            foreach (UnityEngine.UI.Graphic graphic in hud.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                Check(!graphic.raycastTarget, "The rail does not intercept touch or mouse input.", ref checks);
            deadField.SetValue(player, true);
            update.Invoke(hud, null);
            Check(!hud.IsVisible, "The rail hides behind the defeat screen.", ref checks);
            deadField.SetValue(player, false);
            RunFlow.IsTransitioning = true;
            update.Invoke(hud, null);
            Check(!hud.IsVisible, "The rail stays hidden during scene transitions.", ref checks);
            Debug.Log($"HEIGHT_PROGRESS_UI_PASS checks={checks}");
        }
        finally
        {
            scoreField.SetValue(player, originalScore);
            deadField.SetValue(player, dead);
            RunFlow.RunActive = false;
            WorldZones.SetScore(zoneScore);
            RunFlow.RunActive = active;
            RunFlow.IsTransitioning = transition;
            update.Invoke(hud, null);
        }
    }

    private static void Check(bool value, string message, ref int count)
    {
        if (!value) throw new InvalidOperationException("Six zones validation: " + message);
        count++;
    }
}
