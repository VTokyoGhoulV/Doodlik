using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Call from a Play Mode QA runner while the home screen is open. This exercises
// focus changes synchronously: no gameplay frames, purchases, resets or exits.
public static class EditorKeyboardNavigationValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static int Validate()
    {
        GameMenuUI menu = UnityEngine.Object.FindAnyObjectByType<GameMenuUI>();
        EventSystem events = EventSystem.current;
        if (!Application.isPlaying || menu == null || events == null)
            throw new InvalidOperationException("Keyboard validation requires the running game and EventSystem.");

        FrontEndUI front = Field<FrontEndUI>(menu, "frontEnd");
        GameObject home = Field<GameObject>(front, "home");
        if (RunFlow.RunActive || RunFlow.IsTransitioning || Field<bool>(menu, "transitionInProgress") ||
            Field<bool>(menu, "isOpen") || !front.IsVisible || !home.activeInHierarchy)
            throw new InvalidOperationException("Open the idle home screen before keyboard validation.");

        GameObject pause = Field<GameObject>(menu, "menuPanel");
        GameObject settings = Field<GameObject>(menu, "settingsPanel");
        GameObject choice = Field<GameObject>(menu, "resetChoicePanel");
        GameObject confirmation = Field<GameObject>(menu, "confirmationPanel");
        GameObject menuButton = Field<GameObject>(menu, "menuButtonObject");
        GameObject originalSelection = events.currentSelectedGameObject;
        float originalTimeScale = Time.timeScale;
        float originalPreviousTimeScale = Field<float>(menu, "previousTimeScale");
        bool originalSettingsFromHome = Field<bool>(menu, "settingsFromHome");
        bool originalResetAll = Field<bool>(menu, "resetAllRecords");
        bool originalMenuButtonActive = menuButton.activeSelf;
        string originalMenuLabel = Field<TMP_Text>(menu, "menuButtonLabel").text;
        int checks = 0;

        try
        {
            menu.StartRunFromHome();
            Check(events.currentSelectedGameObject == null, "Starting clears menu selection", ref checks);
            Invoke(menu, "ToggleMenu");
            SelectedIn(events, pause, "Pause opens with keyboard focus", ref checks);
            Check(events.currentSelectedGameObject == Field<GameObject>(menu, "menuSettingsButton"),
                "Pause defaults to Settings, not Exit", ref checks);
            Check(Time.timeScale == 0f, "Pause keeps gameplay frozen", ref checks);

            AxisEventData move = new AxisEventData(events) { moveDir = MoveDirection.Down, moveVector = Vector2.down };
            GameObject beforeMove = events.currentSelectedGameObject;
            ExecuteEvents.Execute(beforeMove, move, ExecuteEvents.moveHandler);
            Check(events.currentSelectedGameObject != beforeMove, "Arrow navigation moves between pause buttons", ref checks);
            SelectedIn(events, pause, "Arrow navigation stays in the pause panel", ref checks);

            Invoke(menu, "OpenSettings");
            SelectedIn(events, settings, "Settings receive focus", ref checks);
            GameObject fpsButton = Field<TMP_Text>(menu, "frameRateStatus").GetComponentInParent<Button>().gameObject;
            events.SetSelectedGameObject(fpsButton);
            Invoke(menu, "OpenFrameRateOptions");
            SelectedIn(events, Field<GameObject>(menu, "settingsPopup"), "FPS popup receives focus", ref checks);
            Invoke(menu, "CloseSettingsPopup");
            Check(events.currentSelectedGameObject == fpsButton, "Closing popup restores its source button", ref checks);
            ExecuteEvents.Execute(fpsButton, move, ExecuteEvents.moveHandler);
            Check(events.currentSelectedGameObject == Field<Slider>(menu, "musicSlider").gameObject,
                "Down from FPS selects music slider", ref checks);
            ExecuteEvents.Execute(events.currentSelectedGameObject, move, ExecuteEvents.moveHandler);
            Check(events.currentSelectedGameObject == Field<Slider>(menu, "effectsSlider").gameObject,
                "Down from music selects effects slider without changing volume", ref checks);
            Slider hudTransparency = Field<Slider>(menu, "hudTransparencySlider");
            float originalTransparency = hudTransparency.value;
            ExecuteEvents.Execute(events.currentSelectedGameObject, move, ExecuteEvents.moveHandler);
            Check(events.currentSelectedGameObject == hudTransparency.gameObject,
                "Down from effects selects HUD transparency slider", ref checks);
            ExecuteEvents.Execute(events.currentSelectedGameObject, move, ExecuteEvents.moveHandler);
            Check(hudTransparency.navigation.selectOnDown is Button &&
                events.currentSelectedGameObject == hudTransparency.navigation.selectOnDown.gameObject,
                "Down from HUD transparency selects Back", ref checks);
            AxisEventData moveUp = new AxisEventData(events) { moveDir = MoveDirection.Up, moveVector = Vector2.up };
            ExecuteEvents.Execute(events.currentSelectedGameObject, moveUp, ExecuteEvents.moveHandler);
            Check(events.currentSelectedGameObject == hudTransparency.gameObject,
                "Up from Back selects HUD transparency slider", ref checks);
            ExecuteEvents.Execute(events.currentSelectedGameObject, moveUp, ExecuteEvents.moveHandler);
            Check(events.currentSelectedGameObject == Field<Slider>(menu, "effectsSlider").gameObject &&
                hudTransparency.value == originalTransparency,
                "Up from HUD transparency selects effects without changing transparency", ref checks);
            Invoke(menu, "CloseSettings");
            Check(events.currentSelectedGameObject == Field<GameObject>(menu, "menuSettingsButton"),
                "Closing settings returns to pause Settings button", ref checks);

            Invoke(menu, "OpenResetChoice");
            SelectedIn(events, choice, "Record scope receives focus", ref checks);
            foreach (bool allRecords in new[] { false, true })
            {
                Invoke(menu, "OpenResetConfirmation", allRecords);
                SelectedIn(events, confirmation, "Confirmation receives focus", ref checks);
                GameObject cancel = Field<GameObject>(menu, "resetCancelButton");
                Check(events.currentSelectedGameObject == cancel, "Deletion defaults to Cancel", ref checks);
                // Submit only the positively identified Cancel action. Never invoke
                // ConfirmResetHighScore, a destructive button, or the Exit action.
                ExecuteEvents.Execute(cancel, new BaseEventData(events), ExecuteEvents.submitHandler);
                Check(!confirmation.activeSelf && choice.activeSelf, "Enter on default confirmation cancels", ref checks);
                SelectedIn(events, choice, "Cancelling restores scope focus", ref checks);
            }
            Invoke(menu, "CloseResetChoice");
            Check(events.currentSelectedGameObject == Field<GameObject>(menu, "menuResetButton"),
                "Closing record scope returns to its source button", ref checks);
            Invoke(menu, "ToggleMenu");
            Check(!Field<bool>(menu, "isOpen") && events.currentSelectedGameObject == null,
                "Resuming clears UI selection", ref checks);

            RunFlow.RunActive = false;
            Time.timeScale = 0f;
            front.ShowHome();
            menuButton.SetActive(false);
            menu.OpenSettingsFromHome();
            SelectedIn(events, settings, "Home settings receive focus", ref checks);
            Invoke(menu, "OpenFrameRateOptions");
            SelectedIn(events, Field<GameObject>(menu, "settingsPopup"), "FPS popup receives focus", ref checks);
            Invoke(menu, "CloseSettingsPopup");
            SelectedIn(events, settings, "FPS popup close restores settings focus", ref checks);
            Invoke(menu, "CloseSettings");
            Check(front.IsVisible && home.activeInHierarchy && !Field<bool>(menu, "isOpen"),
                "Closing home settings restores home", ref checks);
            if (GameplayPreferences.IsDesktopBuild)
                SelectedIn(events, home, "Home keyboard focus restored", ref checks);
        }
        finally
        {
            Invoke(menu, "CloseSettingsPopup");
            pause.SetActive(false);
            settings.SetActive(false);
            choice.SetActive(false);
            confirmation.SetActive(false);
            SetField(menu, "isOpen", false);
            SetField(menu, "settingsFromHome", originalSettingsFromHome);
            SetField(menu, "previousTimeScale", originalPreviousTimeScale);
            SetField(menu, "resetAllRecords", originalResetAll);
            RunFlow.RunActive = false;
            Time.timeScale = originalTimeScale;
            front.ShowHome();
            menuButton.SetActive(originalMenuButtonActive);
            Field<TMP_Text>(menu, "menuButtonLabel").text = originalMenuLabel;
            if (originalSelection != null && originalSelection.activeInHierarchy)
                events.SetSelectedGameObject(originalSelection);
        }

        Debug.Log($"[KeyboardNavigationValidation] PASS: {checks} checks. No saves, purchases, resets or exits performed.");
        return checks;
    }

    private static void SelectedIn(EventSystem events, GameObject panel, string message, ref int checks)
    {
        GameObject selected = events.currentSelectedGameObject;
        Button button = selected != null ? selected.GetComponent<Button>() : null;
        Check(panel != null && selected != null && selected.activeInHierarchy &&
            selected.transform.IsChildOf(panel.transform) && button != null && button.IsInteractable(), message, ref checks);
    }

    private static void Check(bool condition, string message, ref int checks)
    {
        if (!condition) throw new InvalidOperationException("Keyboard validation failed: " + message);
        checks++;
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, PrivateInstance).GetValue(target);

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, PrivateInstance).SetValue(target, value);

    private static void Invoke(object target, string method, params object[] arguments) =>
        target.GetType().GetMethod(method, PrivateInstance).Invoke(target, arguments);
}
