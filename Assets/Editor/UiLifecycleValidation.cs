using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

// Synchronous checks for the isolated Play Mode runner. No setting values,
// records, purchases, or files are changed by these checks.
public static class UiLifecycleValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Validate()
    {
        GameMenuUI menu = UnityEngine.Object.FindAnyObjectByType<GameMenuUI>();
        EventSystem events = EventSystem.current;
        Require(Application.isPlaying && menu != null && events != null,
            "Running game and EventSystem are required.");
        FrontEndUI front = menu.GetComponent<FrontEndUI>();
        Require(!RunFlow.RunActive && !RunFlow.IsTransitioning && front.IsVisible,
            "Start from the idle home screen.");
        GameObject originalSelection = events.currentSelectedGameObject;
        try
        {
            menu.OpenSettingsFromHome();
            Invoke(menu, "OpenFrameRateOptions");
            GameObject popup = Field<GameObject>(menu, "settingsPopup");
            Transform window = popup.transform.Find("Popup Window");
            Canvas.ForceUpdateCanvases();
            UnityEngine.UI.Button[] options = window.GetComponentsInChildren<UnityEngine.UI.Button>();
            Require(options.Length == (GameplayPreferences.IsDesktopBuild ? 5 : 3),
                "Popup exposes the platform's supported frame-rate modes.");
            for (int optionIndex = 0; optionIndex < options.Length; optionIndex++)
            {
                UnityEngine.UI.Button option = options[optionIndex];
                foreach (MoveDirection direction in new[]
                         { MoveDirection.Up, MoveDirection.Down, MoveDirection.Left, MoveDirection.Right })
                {
                    events.SetSelectedGameObject(option.gameObject);
                    var move = new AxisEventData(events)
                    {
                        moveDir = direction,
                        moveVector = direction == MoveDirection.Up ? Vector2.up :
                            direction == MoveDirection.Down ? Vector2.down :
                            direction == MoveDirection.Left ? Vector2.left : Vector2.right
                    };
                    ExecuteEvents.Execute(option.gameObject, move, ExecuteEvents.moveHandler);
                    GameObject selected = events.currentSelectedGameObject;
                    Require(selected != null && selected.transform.IsChildOf(window),
                        $"FPS popup navigation escaped from {option.name} to " +
                        $"{(selected != null ? selected.name : "null")} when moving {direction}.");
                    int expectedIndex = direction == MoveDirection.Up ? Mathf.Max(0, optionIndex - 1) :
                        direction == MoveDirection.Down ? Mathf.Min(options.Length - 1, optionIndex + 1) : optionIndex;
                    Require(selected == options[expectedIndex].gameObject,
                        $"FPS popup {direction} selects the adjacent option or stays at the list boundary.");
                }
            }
            Invoke(menu, "CloseSettingsPopup");
            Require(events.currentSelectedGameObject ==
                    Field<TMPro.TMP_Text>(menu, "frameRateStatus").transform.parent.gameObject,
                "Closing the popup restores the source button.");
            Debug.Log("UI_POPUP_NAVIGATION_PASS: all FPS options contain four-direction keyboard navigation.");
        }
        finally
        {
            Invoke(menu, "CloseSettingsPopup");
            Invoke(menu, "CloseSettings");
            if (originalSelection != null && originalSelection.activeInHierarchy)
                events.SetSelectedGameObject(originalSelection);
        }
    }

    // Call both before and after a real SceneManager scene reload / mode swap.
    public static void ValidateSceneDisplay()
    {
        Require(Application.isPlaying, "Scene display validation requires Play Mode.");
        GameObject scoreCanvas = GameObject.Find("Score UI");
        Require(scoreCanvas != null, "Score UI exists.");
        Require(scoreCanvas.GetComponent<ResponsiveDisplay>() != null,
            "The loaded scene must configure its own ResponsiveDisplay, including after restart.");
        Transform safeRoot = scoreCanvas.transform.Find("Safe Area");
        Require(safeRoot != null, "The loaded Score UI has a safe-area root.");
        GameHudFrame frame = scoreCanvas.GetComponentInChildren<GameHudFrame>(true);
        Require(frame != null && frame.transform.parent == safeRoot,
            "The score frame inherits the loaded scene's safe area.");
        var scaler = scoreCanvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        Vector2 expected = Screen.width < Screen.height ? new Vector2(1080f, 1920f) : new Vector2(1920f, 1080f);
        Require(scaler != null && scaler.referenceResolution == expected,
            "Loaded scene CanvasScaler follows the actual viewport orientation.");
        Debug.Log("UI_SCENE_DISPLAY_PASS: safe-area HUD parent and viewport scaler survive scene lifecycle.");
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, Private).GetValue(target);

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, Private).Invoke(target, null);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("UI lifecycle validation: " + message);
    }
}
