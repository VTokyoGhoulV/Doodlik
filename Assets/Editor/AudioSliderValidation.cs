#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Batch-only integration test: -executeMethod AudioSliderValidation.Run (without -quit).
// It never runs in the user's project or shares the production preference namespace.
[InitializeOnLoad]
public static class AudioSliderValidation
{
    private const string Key = "Doodlik.AudioSliderValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static int phase, checks;
    private static double since;
    private static string failure;
    private static GameMenuUI menu;
    private static Slider music, effects, hudTransparency;
    private static Player oldPlayer;
    [Serializable] private sealed class Preference
    {
        public string key, text;
        public int type, integer;
        public float number;
        public bool exists;
    }
    [Serializable] private sealed class Snapshot { public List<Preference> values = new List<Preference>(); }

    static AudioSliderValidation()
    {
        if (!SessionState.GetBool(Key, false)) return;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Application.logMessageReceived += Log;
    }

    public static void Run()
    {
        string dataPath = Application.dataPath.Replace('\\', '/');
        bool isolatedCopy = dataPath.Contains("/.utmp/MenuValidation-") ||
            dataPath.EndsWith("/.utmp/BuildQA-20261005/Assets", StringComparison.OrdinalIgnoreCase);
        if (!Application.isBatchMode || !isolatedCopy)
            throw new InvalidOperationException("Audio slider validation requires an isolated .utmp/MenuValidation-* or .utmp/BuildQA-20261005 batch project.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "MenuPolishSandbox";
        Snapshot snapshot = new Snapshot();
        foreach (string key in Player.ModeHighScoreKeys) Save(snapshot, key, 0);
        foreach (string key in new[] { Player.HighScoreKey, GameplayPreferences.OrientationKey,
                     GameplayPreferences.InertiaKey, "DoodlikMobileFrameRate" }) Save(snapshot, key, 0);
        Save(snapshot, PlayerProgress.SaveKey, 1);
        Save(snapshot, PlayerProgress.BackupKey, 1);
        Save(snapshot, GameAudio.MusicVolumeKey, 2);
        Save(snapshot, GameAudio.EffectsVolumeKey, 2);
        Save(snapshot, GameplayPreferences.ScorePanelTransparencyKey, 2);
        SessionState.SetString(Key + ".prefs", JsonUtility.ToJson(snapshot));
        // Existing non-integer settings must be displayed rounded, not overwritten just by opening UI.
        PlayerPrefs.SetFloat(GameAudio.MusicVolumeKey, 0.324f);
        PlayerPrefs.SetFloat(GameAudio.EffectsVolumeKey, 0.757f);
        PlayerPrefs.SetFloat(GameplayPreferences.ScorePanelTransparencyKey, 0.324f);
        PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, 0);
        PlayerPrefs.Save();
        Directory.CreateDirectory("Logs");
        SessionState.SetBool(Key, true);
        phase = checks = 0;
        failure = null;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= Log;
        Application.logMessageReceived += Log;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void Log(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup"))
        {
            Debug.LogWarning("VALIDATION_ENVIRONMENT: existing Unity Search startup indexer exception: " + message);
            return;
        }
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) failure = message;
    }

    private static void Tick()
    {
        AudioListener.pause = true;
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (failure != null) throw new Exception(failure);
            double elapsed = EditorApplication.timeSinceStartup - since;
            if (elapsed > 45) throw new Exception("Audio sliders timed out in phase " + phase);
            if (elapsed < 0.7) return;
            switch (phase)
            {
                case 0:
                    if (Time.frameCount < 20) return;
                    Bind();
                    Check(menu.GetComponent<FrontEndUI>().IsVisible && !RunFlow.RunActive && Time.timeScale == 0f,
                        "initial home is paused");
                    TransparencyPreferenceContract();
                    menu.OpenSettingsFromHome();
                    Canvas.ForceUpdateCanvases();
                    Contract();
                    Check(music.value == 32 && effects.value == 76, "saved precise volumes are displayed rounded");
                    Check(hudTransparency.value == 32, "saved precise transparency is displayed rounded");
                    CloseAndOpen();
                    Approximately(GameAudio.MusicVolume, 0.324f, "opening UI preserves non-integer music volume");
                    Approximately(GameAudio.EffectsVolume, 0.757f, "opening UI preserves non-integer effects volume");
                    Approximately(GameplayPreferences.ScorePanelTransparency, 0.324f,
                        "opening UI preserves non-integer HUD transparency");
                    CheckLabels();
                    Interactions();
                    music.value = 37;
                    effects.value = 63;
                    TransparencyInteractions();
                    hudTransparency.value = 65;
                    CheckTransparency(65);
                    Capture("Home-Landscape");
                    Advance();
                    break;
                case 1:
                    Check(!(bool)typeof(GameAudio).GetField("preferencesDirty", Static).GetValue(null),
                        "deferred preference flush also runs while timeScale is zero");
                    Check(!(bool)typeof(GameMenuUI).GetField("hudPreferencesDirty", Private).GetValue(menu),
                        "HUD preference flush also runs while timeScale is zero");
                    CloseAndOpen();
                    CheckVolumes(37, 63);
                    CheckTransparency(65);
                    // Reload the audio cache from PlayerPrefs, independently of the visible controls.
                    typeof(GameAudio).GetField("preferencesLoaded", Static).SetValue(null, false);
                    Approximately(GameAudio.MusicVolume, 0.37f, "music reloads from saved preference");
                    Approximately(GameAudio.EffectsVolume, 0.63f, "effects reload from saved preference");
                    oldPlayer = Object.FindAnyObjectByType<Player>();
                    Invoke(menu, "CloseSettings");
                    menu.ReturnToHome();
                    Advance();
                    break;
                case 2:
                    if (RunFlow.IsTransitioning) return;
                    Bind();
                    Check(Object.FindAnyObjectByType<Player>() != oldPlayer, "scene was reloaded");
                    Check(Object.FindObjectsByType<GameAudio>().Length == 1, "scene reload keeps one audio service");
                    menu.OpenSettingsFromHome();
                    CheckVolumes(37, 63);
                    CheckTransparency(65);
                    Invoke(menu, "CloseSettings");
                    menu.StartRunFromHome();
                    Invoke(menu, "ToggleMenu");
                    Invoke(menu, "OpenSettings");
                    Check(Time.timeScale == 0f && GameMenuUI.BlocksGameplayInput && RunFlow.RunActive,
                        "in-run settings keep gameplay paused");
                    Pointer(music, 0.38f, true);
                    CheckVolumes(38, 63);
                    music.value = 37;
                    TransparencyInteractions();
                    CaptureHudTransparency();
                    hudTransparency.value = 65;
                    CheckTransparency(65);
                    Capture("Pause-Landscape");
                    Invoke(menu, "CloseSettings");
                    menu.ReturnToHome();
                    Advance();
                    break;
                case 3:
                    if (RunFlow.IsTransitioning) return;
                    CheckTransparency(65);
                    if (GameplayPreferences.IsDesktopBuild) { Complete(); break; }
                    menu.GetComponent<FrontEndUI>().ShowModes();
                    Check(menu.SelectGameMode(true, GameplayPreferences.UseInertia), "mobile portrait mode is selected at home");
                    Advance();
                    break;
                case 4:
                    if (RunFlow.IsTransitioning) return;
                    Bind();
                    Check(GameplayPreferences.IsPortrait, "mobile portrait preference survives transition");
                    menu.OpenSettingsFromHome();
                    Canvas.ForceUpdateCanvases();
                    Contract();
                    CheckVolumes(37, 63);
                    CheckTransparency(65);
                    Pointer(effects, 0.42f, true);
                    CheckVolumes(37, 42);
                    Capture("Home-Portrait");
                    CloseAndOpen();
                    CheckVolumes(37, 42);
                    CheckTransparency(65);
                    Complete();
                    break;
            }
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    private static void Bind()
    {
        AudioListener.pause = true;
        menu = Object.FindAnyObjectByType<GameMenuUI>();
        Check(menu != null, "menu exists");
        music = (Slider)typeof(GameMenuUI).GetField("musicSlider", Private).GetValue(menu);
        effects = (Slider)typeof(GameMenuUI).GetField("effectsSlider", Private).GetValue(menu);
        hudTransparency = (Slider)typeof(GameMenuUI).GetField("hudTransparencySlider", Private).GetValue(menu);
        Check(music != null && effects != null && music != effects, "independent slider references exist");
        Check(hudTransparency != null && hudTransparency != music && hudTransparency != effects,
            "HUD transparency has an independent slider reference");
    }

    private static void Contract()
    {
        GameObject settings = (GameObject)typeof(GameMenuUI).GetField("settingsPanel", Private).GetValue(menu);
        Check(settings.GetComponentsInChildren<Button>().Length == 2 &&
            settings.GetComponentsInChildren<Slider>().Length == 3,
            "FPS/back buttons, two audio sliders and one HUD transparency slider");
        foreach (Slider slider in new[] { music, effects, hudTransparency })
        {
            Check(slider.minValue == 0 && slider.maxValue == 100 && slider.wholeNumbers, "one-percent slider range");
            Check(slider.direction == Slider.Direction.LeftToRight && slider.IsInteractable(), "active horizontal slider");
            Check(slider.handleRect != null && slider.fillRect != null && slider.targetGraphic != null, "visible handle, fill and navigation target");
            Approximately(slider.handleRect.rect.width, 44f, "handle width");
            Approximately(slider.handleRect.rect.height, 44f, "handle height");
            RectTransform hitArea = (RectTransform)slider.transform;
            Check(hitArea.rect.height >= 64f, "large touch area");
            Vector3[] corners = new Vector3[4];
            slider.handleRect.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector3 local = hitArea.InverseTransformPoint(corner);
                Check(local.x >= hitArea.rect.xMin - 0.01f && local.x <= hitArea.rect.xMax + 0.01f &&
                    local.y >= hitArea.rect.yMin - 0.01f && local.y <= hitArea.rect.yMax + 0.01f,
                    "handle stays within touch strip");
            }
        }
        Check(music.name == "Music Volume" && effects.name == "Effects Volume", "descriptive control names");
        Check(music.navigation.mode == Navigation.Mode.Explicit && effects.navigation.mode == Navigation.Mode.Explicit &&
            hudTransparency.navigation.mode == Navigation.Mode.Explicit,
            "settings slider navigation is explicit");
        Check(music.navigation.selectOnDown == effects && effects.navigation.selectOnUp == music,
            "vertical navigation joins both sliders");
        Button fps = ((TMP_Text)typeof(GameMenuUI).GetField("frameRateStatus", Private).GetValue(menu)).GetComponentInParent<Button>();
        Check(fps.navigation.selectOnDown == music && music.navigation.selectOnUp == fps,
            "FPS and music form the first navigation pair");
        Check(effects.navigation.selectOnDown == hudTransparency && hudTransparency.navigation.selectOnUp == effects,
            "effects and HUD transparency form the next navigation pair");
        Check(hudTransparency.navigation.selectOnDown is Button &&
            hudTransparency.navigation.selectOnDown.navigation.selectOnUp == hudTransparency,
            "HUD transparency and back form the final navigation pair");
        CheckLabels();
    }

    private static void Interactions()
    {
        Pointer(music, 0.37f, false);
        CheckVolumes(37, 76, 0.757f);
        Pointer(music, 0.64f, true);
        CheckVolumes(64, 76, 0.757f);
        Pointer(effects, 0.21f, false);
        CheckVolumes(64, 21);
        EventSystem.current.SetSelectedGameObject(music.gameObject);
        music.OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Right });
        CheckVolumes(65, 21);
        music.OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Left });
        CheckVolumes(64, 21);
        music.OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Down });
        Check(EventSystem.current.currentSelectedGameObject == effects.gameObject, "down selects effects without changing volume");
        float transparencyBeforeNavigation = hudTransparency.value;
        effects.OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Down });
        Check(EventSystem.current.currentSelectedGameObject == hudTransparency.gameObject,
            "down from effects selects HUD transparency");
        hudTransparency.OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Down });
        Check(EventSystem.current.currentSelectedGameObject == hudTransparency.navigation.selectOnDown.gameObject,
            "down from HUD transparency selects back button");
        Approximately(hudTransparency.value, transparencyBeforeNavigation,
            "vertical navigation does not change HUD transparency");
        CheckVolumes(64, 21);
        music.value = -12;
        effects.value = 150;
        CheckVolumes(0, 100);
        music.value = 100;
        effects.value = 0;
        CheckVolumes(100, 0);
        Check(Time.timeScale == 0f, "all interaction works while paused");
    }

    private static void TransparencyPreferenceContract()
    {
        string key = GameplayPreferences.ScorePanelTransparencyKey;
        bool existed = PlayerPrefs.HasKey(key);
        float original = PlayerPrefs.GetFloat(key, 0f);
        try
        {
            PlayerPrefs.DeleteKey(key);
            Approximately(GameplayPreferences.ScorePanelTransparency, 0f,
                "missing transparency preference preserves the original opaque panel");
            foreach (float value in new[] { -0.5f, 0f, 0.5f, 1f, 1.5f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                float expected = float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
                GameplayPreferences.SetScorePanelTransparency(value);
                Approximately(GameplayPreferences.ScorePanelTransparency, expected, "transparency setter sanitizes invalid values");
                Approximately(PlayerPrefs.GetFloat(key), expected, "only sanitized transparency is stored");
                PlayerPrefs.SetFloat(key, value);
                Approximately(GameplayPreferences.ScorePanelTransparency, expected, "transparency getter tolerates corrupt saved values");
            }
        }
        finally
        {
            if (existed) PlayerPrefs.SetFloat(key, original);
            else PlayerPrefs.DeleteKey(key);
        }
    }

    private static void TransparencyInteractions()
    {
        float previousMusic = GameAudio.MusicVolume;
        float previousEffects = GameAudio.EffectsVolume;
        foreach (int percent in new[] { -12, 0, 50, 100, 150 })
        {
            hudTransparency.value = percent;
            CheckTransparency(Mathf.Clamp(percent, 0, 100));
        }
        Pointer(hudTransparency, 0.68f, false);
        CheckTransparency(68);
        Pointer(hudTransparency, 0.43f, true);
        CheckTransparency(43);
        EventSystem.current.SetSelectedGameObject(hudTransparency.gameObject);
        hudTransparency.OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Right });
        CheckTransparency(44);
        hudTransparency.OnMove(new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Left });
        CheckTransparency(43);
        Approximately(GameAudio.MusicVolume, previousMusic, "HUD transparency does not change music");
        Approximately(GameAudio.EffectsVolume, previousEffects, "HUD transparency does not change effects");
        Check(Time.timeScale == 0f, "HUD transparency works while gameplay is paused");
    }

    private static void CheckTransparency(int percent)
    {
        float transparency = percent / 100f;
        Approximately(hudTransparency.value, percent, "transparency slider value");
        Approximately(GameplayPreferences.ScorePanelTransparency, transparency, "transparency applies immediately");
        Approximately(PlayerPrefs.GetFloat(GameplayPreferences.ScorePanelTransparencyKey), transparency,
            "transparency is retained in preferences");
        GameHudFrame frame = Object.FindAnyObjectByType<GameHudFrame>(FindObjectsInactive.Include);
        Check(frame != null, "score HUD frame exists");
        // Exercise the real refresh synchronously with unchanged score/layout, including while paused.
        Invoke(frame, "LateUpdate");
        Image paper = frame.GetComponent<Image>();
        Image divider = frame.transform.Find("Paper divider").GetComponent<Image>();
        CanvasGroup visibility = frame.GetComponent<CanvasGroup>();
        Approximately(paper.color.a, (243f / 255f) * (1f - transparency), "paper alone fades with transparency");
        Approximately(divider.color.a, 0.2f * (1f - transparency), "divider fades with the paper");
        Approximately(visibility.alpha, RunFlow.RunActive ? 1f : 0f,
            "home/run HUD visibility is independent of transparency");
        Check(!visibility.interactable && !visibility.blocksRaycasts && !paper.raycastTarget && !divider.raycastTarget,
            "transparent HUD does not intercept gameplay input");
        Shadow[] paperEffects = frame.GetComponents<Shadow>();
        Check(paperEffects.Length == 1, "paper retains its shadow without overlapping outline copies");
        foreach (Shadow effect in paperEffects)
            Check(effect.useGraphicAlpha, "shadow inherits the fading paper alpha");
        GameHudPanelBorder border = frame.GetComponentInChildren<GameHudPanelBorder>(true);
        Check(border != null && !border.raycastTarget, "hollow border remains present without blocking input");
        Approximately(border.color.a, (240f / 255f) * (1f - transparency), "hollow border fades with the paper");
        TMP_Text[] texts = frame.GetComponentsInChildren<TMP_Text>(true);
        Check(texts.Length == 2, "score and wallet text remain in the HUD");
        foreach (TMP_Text text in texts)
        {
            Approximately(text.color.a, 1f, "HUD text stays opaque");
            Approximately(text.outlineWidth, 0.16f * transparency, "text contrast outline follows panel transparency");
        }
        GameHudCoinIcon coin = frame.GetComponentInChildren<GameHudCoinIcon>(true);
        Check(coin != null && !coin.raycastTarget, "wallet coin remains present without blocking input");
        Approximately(coin.color.a, 1f, "wallet coin stays opaque");
        using (VertexHelper vertices = new VertexHelper())
        {
            typeof(GameHudCoinIcon).GetMethod("OnPopulateMesh", Private, null,
                new[] { typeof(VertexHelper) }, null).Invoke(coin, new object[] { vertices });
            bool opaque = vertices.currentVertCount > 0;
            UIVertex vertex = default(UIVertex);
            for (int i = 0; i < vertices.currentVertCount; i++)
            {
                vertices.PopulateUIVertex(ref vertex, i);
                opaque &= vertex.color.a == 255;
            }
            Check(opaque, "wallet coin mesh remains fully opaque at every transparency");
        }
        CheckLabels();
    }

    private static void Pointer(Slider slider, float normalized, bool drag)
    {
        Canvas.ForceUpdateCanvases();
        // Batch Mode has no rendered Game view. GraphicRaycaster skips depth=-1
        // until a real canvas render; lay out and render the newly opened UI first.
        if (slider.GetComponent<UnityEngine.UI.Image>().depth < 0)
        {
            Capture("InputFrame-" + phase);
            Canvas.ForceUpdateCanvases();
        }
        RectTransform area = slider.handleRect.parent as RectTransform;
        Vector3 local = new Vector3(Mathf.Lerp(area.rect.xMin, area.rect.xMax, normalized), area.rect.center.y, 0);
        Canvas canvas = slider.GetComponentInParent<Canvas>();
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 position = RectTransformUtility.WorldToScreenPoint(camera, area.TransformPoint(local));
        PointerEventData data = new PointerEventData(EventSystem.current)
        { position = position, button = PointerEventData.InputButton.Left, pointerId = drag ? 1 : -1 };
        List<RaycastResult> hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, hits);
        bool reachable = hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerDownHandler>(hits[0].gameObject) == slider.gameObject;
        if (!reachable)
        {
            PointerDiagnostic(slider, position, camera, hits);
            Capture("PointerFailure-" + slider.name.Replace(' ', '-'));
            Canvas.ForceUpdateCanvases();
            List<RaycastResult> afterRender = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, afterRender);
            Debug.Log("AUDIO_POINTER_AFTER_RENDER: preserving original failure result; inspecting render-dependent depth only");
            PointerDiagnostic(slider, position, camera, afterRender);
        }
        Check(reachable, slider.name + " is topmost pointer target at requested track position");
        if (hits.Count > 0) data.pointerPressRaycast = hits[0];
        if (drag)
        {
            Vector2 endPosition = data.position;
            data.position = RectTransformUtility.WorldToScreenPoint(camera, slider.handleRect.TransformPoint(slider.handleRect.rect.center));
            // Pointer-down belongs to the handle center, not the eventual drag target.
            // Otherwise Slider treats the target's displacement as the grab offset.
            hits.Clear();
            EventSystem.current.RaycastAll(data, hits);
            Check(hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerDownHandler>(hits[0].gameObject) == slider.gameObject,
                slider.name + " handle is reachable at drag start");
            data.pointerPressRaycast = data.pointerCurrentRaycast = hits[0];
            slider.OnPointerDown(data);
            slider.OnInitializePotentialDrag(data);
            data.position = endPosition;
            slider.OnDrag(data);
        }
        else slider.OnPointerDown(data);
        slider.OnPointerUp(data);
    }

    private static void PointerDiagnostic(Slider slider, Vector2 position, Camera camera, List<RaycastResult> hits)
    {
        Canvas canvas = slider.GetComponentInParent<Canvas>();
        Graphic graphic = slider.GetComponent<Graphic>();
        RectTransform root = (RectTransform)slider.transform;
        Debug.Log("AUDIO_POINTER_DIAGNOSTIC control=" + slider.name + " frame=" + Time.frameCount +
            " screen=" + Screen.width + "x" + Screen.height + " position=" + position + " safe=" + Screen.safeArea +
            " canvas=" + canvas.name + " mode=" + canvas.renderMode + " scale=" + canvas.scaleFactor +
            " order=" + canvas.sortingOrder + " camera=" + (camera == null ? "null" : camera.name) +
            " active=" + slider.gameObject.activeInHierarchy + " enabled=" + slider.enabled +
            " hits=" + hits.Count + " rootRect=" + root.rect + " rootAnchored=" + root.anchoredPosition +
            " handleRect=" + slider.handleRect.rect + " handleParentRect=" + ((RectTransform)slider.handleRect.parent).rect);
        Debug.Log("AUDIO_POINTER_GRAPHIC alpha=" + graphic.color.a + " depth=" + graphic.depth +
            " raycast=" + graphic.raycastTarget + " cull=" + graphic.canvasRenderer.cull +
            " cullTransparent=" + graphic.canvasRenderer.cullTransparentMesh +
            " rendererAlpha=" + graphic.canvasRenderer.GetAlpha() +
            " contains=" + RectTransformUtility.RectangleContainsScreenPoint(root, position, camera) +
            " graphicRaycast=" + graphic.Raycast(position, camera));
        Vector3[] corners = new Vector3[4];
        root.GetWorldCorners(corners);
        foreach (Vector3 corner in corners)
            Debug.Log("AUDIO_POINTER_ROOT_CORNER world=" + corner + " screen=" + RectTransformUtility.WorldToScreenPoint(camera, corner));
        for (int i = 0; i < hits.Count; i++)
        {
            RaycastResult hit = hits[i];
            GameObject handler = ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit.gameObject);
            Graphic hitGraphic = hit.gameObject.GetComponent<Graphic>();
            Debug.Log("AUDIO_POINTER_HIT " + i + " path=" + HierarchyPath(hit.gameObject.transform) +
                " depth=" + hit.depth + " order=" + hit.sortingOrder + " layer=" + hit.sortingLayer +
                " module=" + (hit.module == null ? "null" : hit.module.name) +
                " pointerHandler=" + (handler == null ? "null" : HierarchyPath(handler.transform)) +
                " cull=" + (hitGraphic == null ? "n/a" : hitGraphic.canvasRenderer.cull.ToString()));
        }
    }

    private static string HierarchyPath(Transform item)
    {
        string path = item.name;
        while (item.parent != null) { item = item.parent; path = item.name + "/" + path; }
        return path;
    }

    private static void CheckVolumes(int musicPercent, int effectsPercent, float exactEffects = -1f)
    {
        Check(music.value == musicPercent && effects.value == effectsPercent,
            $"slider values reflect independent interaction: expected {musicPercent}/{effectsPercent}, got {music.value}/{effects.value}");
        Approximately(GameAudio.MusicVolume, musicPercent / 100f, "music volume applies immediately");
        Approximately(GameAudio.EffectsVolume, exactEffects < 0 ? effectsPercent / 100f : exactEffects,
            "effects volume applies independently and immediately");
        Approximately(PlayerPrefs.GetFloat(GameAudio.MusicVolumeKey), GameAudio.MusicVolume, "music preference updated");
        Approximately(PlayerPrefs.GetFloat(GameAudio.EffectsVolumeKey), GameAudio.EffectsVolume, "effects preference updated");
        CheckLabels();
    }

    private static void CheckLabels()
    {
        TMP_Text musicText = (TMP_Text)typeof(GameMenuUI).GetField("musicStatus", Private).GetValue(menu);
        TMP_Text effectsText = (TMP_Text)typeof(GameMenuUI).GetField("effectsStatus", Private).GetValue(menu);
        TMP_Text hudText = (TMP_Text)typeof(GameMenuUI).GetField("hudTransparencyStatus", Private).GetValue(menu);
        Check(musicText.text == "Музыка: " + Mathf.RoundToInt(GameAudio.MusicVolume * 100) + "%", "music percentage label");
        Check(effectsText.text == "Звуки: " + Mathf.RoundToInt(GameAudio.EffectsVolume * 100) + "%", "effects percentage label");
        Check(hudText.text == "Прозрачность панели счёта: " + Mathf.RoundToInt(GameplayPreferences.ScorePanelTransparency * 100) + "%",
            "HUD transparency percentage label");
        foreach (TMP_Text label in new[] { musicText, effectsText, hudText })
        {
            label.ForceMeshUpdate();
            Check(!label.isTextOverflowing, "percentage label fits");
        }
    }

    private static void CloseAndOpen()
    {
        Invoke(menu, "CloseSettings");
        menu.OpenSettingsFromHome();
        Canvas.ForceUpdateCanvases();
    }

    private static void Capture(string suffix)
    {
        // Same render path as the established Journey test: these are rendered Unity frames,
        // not synthetic mockups. Root performs the visual review after the batch run.
        typeof(JourneyPlayValidation).GetMethod("Capture", Static).Invoke(null, new object[] { "AudioSliders-" + suffix });
    }

    private static void CaptureHudTransparency()
    {
        GameObject settings = (GameObject)typeof(GameMenuUI).GetField("settingsPanel", Private).GetValue(menu);
        GameObject menuButton = (GameObject)typeof(GameMenuUI).GetField("menuButtonObject", Private).GetValue(menu);
        bool settingsActive = settings.activeSelf;
        bool menuButtonActive = menuButton.activeSelf;
        float previousTransparency = hudTransparency.value;
        try
        {
            settings.SetActive(false);
            menuButton.SetActive(false);
            // StartRun and pause happened in this same QA tick. Refresh the real
            // HUD visibility methods without advancing gameplay or sending input.
            Player player = Object.FindAnyObjectByType<Player>();
            Invoke(player, "LateUpdate");
            Invoke(player.GetComponent<RunPowerups>(), "Update");
            Invoke(menu.GetComponent<HomeScreenBackdrop>(), "LateUpdate");
            Check(!menu.transform.Find("Home backdrop").gameObject.activeSelf,
                "gameplay captures are not covered by the home illustration");
            foreach (int percent in new[] { 0, 65, 100 })
            {
                hudTransparency.value = percent;
                CheckTransparency(percent);
                Capture("Hud-Landscape-" + percent);
            }
        }
        finally
        {
            hudTransparency.value = previousTransparency;
            settings.SetActive(settingsActive);
            menuButton.SetActive(menuButtonActive);
        }
    }

    private static void Complete()
    {
        Debug.Log("AUDIO_SLIDERS_PASS checks=" + checks + " target=" + EditorUserBuildSettings.activeBuildTarget +
            ": independent 1% volumes, labels, pointer click/drag, keyboard, pause, preference reload, scene restart" +
            ", HUD transparency defaults/clamping/endpoints, paper-only fade, opaque text/coin, persistence" +
            (GameplayPreferences.IsDesktopBuild ? ", desktop landscape" : ", mobile landscape/portrait"));
        Finish(0);
    }

    private static void Save(Snapshot snapshot, string key, int type) => snapshot.values.Add(new Preference
    {
        key = key, exists = PlayerPrefs.HasKey(key), type = type,
        integer = type == 0 ? PlayerPrefs.GetInt(key) : 0,
        text = type == 1 ? PlayerPrefs.GetString(key) : "", number = type == 2 ? PlayerPrefs.GetFloat(key) : 0
    });
    private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Approximately(float value, float expected, string message) =>
        Check(Mathf.Abs(value - expected) < 0.00001f, message + $" (expected {expected}, got {value})");
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("Audio slider check: " + message); }
    private static void Advance() { phase++; since = EditorApplication.timeSinceStartup; }
    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= Log;
        SessionState.SetBool(Key, false);
        Snapshot snapshot = JsonUtility.FromJson<Snapshot>(SessionState.GetString(Key + ".prefs", ""));
        if (snapshot != null)
        {
            foreach (Preference preference in snapshot.values)
            {
                if (!preference.exists) PlayerPrefs.DeleteKey(preference.key);
                else if (preference.type == 0) PlayerPrefs.SetInt(preference.key, preference.integer);
                else if (preference.type == 1) PlayerPrefs.SetString(preference.key, preference.text);
                else PlayerPrefs.SetFloat(preference.key, preference.number);
            }
            PlayerPrefs.Save();
        }
        // No pending test-volume write may overwrite restored preferences during quit.
        typeof(GameAudio).GetField("preferencesDirty", Static).SetValue(null, false);
        typeof(GameAudio).GetField("preferencesLoaded", Static).SetValue(null, false);
        if (menu != null) typeof(GameMenuUI).GetField("hudPreferencesDirty", Private).SetValue(menu, false);
        Time.timeScale = 1f;
        EditorApplication.Exit(code);
    }
}
#endif
