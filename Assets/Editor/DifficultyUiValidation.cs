using System;
using System.Reflection;
using TMPro;
using UnityEngine;

// Run synchronously from the isolated Play Mode QA runner at the idle home
// screen. No preferences, records, purchases, or scene reloads are performed.
public static class DifficultyUiValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Validate()
    {
        GameMenuUI menu = UnityEngine.Object.FindAnyObjectByType<GameMenuUI>();
        Player player = UnityEngine.Object.FindAnyObjectByType<Player>();
        Require(Application.isPlaying && menu != null && player != null,
            "Running game and initialized Player are required.");
        FrontEndUI front = menu.GetComponent<FrontEndUI>();
        Require(!RunFlow.RunActive && !RunFlow.IsTransitioning && front.IsVisible,
            "Start at the idle home screen.");
        GameDifficulty originalDifficulty = GameplayPreferences.Difficulty;
        bool originalTransition = Field<bool>(menu, "transitionInProgress");
        try
        {
            front.ShowHome();
            Require(Field<TMP_Text>(front, "homeDifficulty").text.Contains(DifficultyRules.Name(originalDifficulty)) &&
                    Field<TMP_Text>(front, "homeDifficulty").text.Contains(DifficultyRules.MultiplierLabel(originalDifficulty)),
                "Home names the difficulty and its multiplier.");
            Require(Field<TMP_Text>(front, "homeRecord").text.Contains("высота:") &&
                    Field<TMP_Text>(front, "homeRecord").text.Contains("счёт:"),
                "Home presents both separate record metrics.");
            front.ShowModes();
            Canvas.ForceUpdateCanvases();
            var buttons = Field<UnityEngine.UI.Button[]>(front, "difficultyButtons");
            Require(buttons.Length == 3, "All three difficulties are available before play.");
            for (int i = 0; i < buttons.Length; i++)
            {
                GameDifficulty difficulty = (GameDifficulty)i;
                TMP_Text label = buttons[i].GetComponentInChildren<TMP_Text>();
                Require(label.text.Contains(DifficultyRules.Name(difficulty)) &&
                        label.text.Contains(DifficultyRules.MultiplierLabel(difficulty)),
                    "Each difficulty labels its own score multiplier.");
                Require(buttons[i].IsInteractable() && buttons[i].navigation.mode == UnityEngine.UI.Navigation.Mode.Explicit,
                    "Difficulty buttons support explicit keyboard navigation.");
                label.ForceMeshUpdate();
                Require(!label.isTextOverflowing, "Difficulty names and multipliers fit their buttons.");
            }
            Require(Field<TMP_Text>(front, "modeRecord").text.Contains("Рекорд высоты:") &&
                    Field<TMP_Text>(front, "modeRecord").text.Contains("Рекорд счёта:"),
                "The selected combination displays both records.");
            Invoke(front, "ToggleModeOptions");
            Require(!Field<GameObject>(front, "difficultySection").activeSelf,
                "The control dropdown hides the difficulty controls below it.");
            Require(front.GoBack() && Field<GameObject>(front, "difficultySection").activeSelf,
                "Closing the dropdown restores difficulty choices.");

            GameDifficulty other = originalDifficulty == GameDifficulty.Hard ? GameDifficulty.Easy : GameDifficulty.Hard;
            RunFlow.RunActive = true;
            Require(!menu.SelectDifficulty(other), "Active and paused attempts reject difficulty changes.");
            RunFlow.RunActive = false;
            RunFlow.IsTransitioning = true;
            Require(!menu.SelectDifficulty(other), "Scene transitions reject difficulty changes.");
            RunFlow.IsTransitioning = false;
            Set(menu, "transitionInProgress", true);
            Require(!menu.SelectDifficulty(other), "Pending local transitions reject difficulty changes.");
            Set(menu, "transitionInProgress", false);
            Require(!menu.SelectDifficulty((GameDifficulty)(-1)) && !menu.SelectDifficulty((GameDifficulty)3),
                "Invalid difficulty values cannot enter the selection flow.");
            Require(menu.SelectDifficulty(originalDifficulty) && !RunFlow.IsTransitioning,
                "Selecting the current difficulty does not reload the scene.");
            Require(GameplayPreferences.Difficulty == originalDifficulty,
                "Rejected and unchanged requests preserve the selected difficulty.");
            ValidateHeightRail(player);
            Debug.Log("DIFFICULTY_UI_PASS: three labeled choices, keyboard links, dual records, dropdown visibility, run/transition guards, height-only rail, compact HUD.");
        }
        finally
        {
            RunFlow.RunActive = false;
            RunFlow.IsTransitioning = false;
            Set(menu, "transitionInProgress", originalTransition);
            front.ShowHome();
        }
    }

    private static void ValidateHeightRail(Player player)
    {
        HeightProgressUI rail = UnityEngine.Object.FindAnyObjectByType<HeightProgressUI>();
        GameHudFrame frame = UnityEngine.Object.FindAnyObjectByType<GameHudFrame>();
        Require(rail != null && frame != null, "Height rail and score frame exist.");
        int originalHeight = player.CurrentHeight, originalScore = player.CurrentScore;
        bool originalDead = player.IsDead, originalActive = RunFlow.RunActive;
        try
        {
            RunFlow.RunActive = true;
            Set(player, "isDead", false);
            foreach (int height in new[] { WorldZones.GoalScore / 2, WorldZones.GoalScore, WorldZones.GoalScore + 100 })
            foreach (GameDifficulty difficulty in new[] { GameDifficulty.Easy, GameDifficulty.Normal, GameDifficulty.Hard })
            {
                Set(player, "reachedHeight", height);
                Set(player, "score", DifficultyRules.ScoreForHeight(height, difficulty));
                Invoke(rail, "Update");
                Require(Mathf.Approximately(rail.DisplayedProgress, Mathf.Clamp01(height / (float)WorldZones.GoalScore)),
                    "Physical height, not difficulty-multiplied score, determines travel progress.");
            }
            TMP_Text goal = Field<TMP_Text>(rail, "topLabel");
            Require(goal.text == "До космоса\n" + WorldZones.GoalScore,
                "The height goal is clearly labeled as the journey to space.");
            Invoke(player, "UpdateScoreText");
            Invoke(frame, "LateUpdate");
            TMP_Text summary = Field<TMP_Text>(frame, "score");
            Require(summary.text.StartsWith("Высота:") && summary.text.Contains("Счёт:") &&
                    summary.text.Contains(DifficultyRules.MultiplierLabel(player.RunDifficulty)),
                "HUD makes height primary and labels score with the run multiplier.");
            Require(summary.rectTransform.rect.height == 94f && frame.GetComponent<RectTransform>().rect.height == 156f,
                "The three-line HUD uses the compact expanded frame.");
        }
        finally
        {
            Set(player, "reachedHeight", originalHeight);
            Set(player, "score", originalScore);
            Set(player, "isDead", originalDead);
            RunFlow.RunActive = originalActive;
            Invoke(player, "UpdateScoreText");
            Invoke(rail, "Update");
            Invoke(frame, "LateUpdate");
        }
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Private).GetValue(target);
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Invoke(object target, string name) =>
        target.GetType().GetMethod(name, Private).Invoke(target, null);
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Difficulty UI validation: " + message);
    }
}
