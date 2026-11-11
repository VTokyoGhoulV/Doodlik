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
using Object = UnityEngine.Object;

// Disposable real-viewport regression runner; never drives the user's Editor.
[InitializeOnLoad]
public static class AdminExpandedUiValidation
{
    private const string Key = "Doodlik.AdminExpandedUiValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector2Int[] Views =
    {
        new Vector2Int(1280, 720), new Vector2Int(720, 1280), new Vector2Int(1600, 720)
    };
    private static int phase, view, tab, checks, captures;
    private static double since;
    private static string failure, primary, backup, originalPrimary, originalBackup;
    private static IDisposable storage;
    private static AdminTools tools;
    private static GameMenuUI menu;
    private static Player player;

    static AdminExpandedUiValidation()
    {
        if (SessionState.GetBool(Key, false)) Register();
    }

    public static void Run()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-"))
            throw new InvalidOperationException("Expanded admin UI requires a disposable MenuValidation batch project.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikAdminExpandedSandbox";
        Directory.CreateDirectory("Logs");
        GameplayPreferences.SetMode(false, false, false);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AdminProgressValidation.Validate();
        AdminRuntimeValidation.Validate();
        AdminRuntime.ResetTuning();
        SessionState.SetBool(Key, true);
        phase = view = tab = checks = captures = 0;
        Register();
        SetView();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BeforeScene()
    {
        if (SessionState.GetBool(Key, false) && storage == null)
            storage = PlayerProgress.UseIsolatedStorageForValidation(out primary, out backup);
    }

    private static void Register()
    {
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        AudioListener.volume = 0f; AudioListener.pause = true;
        try
        {
            if (failure != null) throw new InvalidOperationException(failure);
            double elapsed = EditorApplication.timeSinceStartup - since;
            if (elapsed > 35) throw new TimeoutException("Admin UI phase=" + phase);
            if (RunFlow.IsTransitioning || Time.frameCount < 20 || elapsed < .4) return;
            Require(EditorUtility.audioMasterMute && AudioListener.volume == 0f, "Every test frame is silent.");
            switch (phase)
            {
                case 0:
                    Bind();
                    Require(storage != null && !RunFlow.RunActive && Time.timeScale == 0f, "Isolated natural home state.");
                    Require(Object.FindObjectsByType<EventSystem>().Length == 1, "Exactly one input EventSystem.");
                    AdminToolsValidation.Validate();
                    PlayerProgress.AddCoins(1000); PlayerProgress.Flush();
                    originalPrimary = PlayerPrefs.GetString(primary); originalBackup = PlayerPrefs.GetString(backup);
                    tools.Open(); SelectTab(0); Next(1); break;
                case 1:
                    Require(Screen.width == Views[view].x && Screen.height == Views[view].y, "Actual requested viewport, not just a scaled capture.");
                    ExerciseTab();
                    Canvas.ForceUpdateCanvases();
                    Next(2); break;
                case 2:
                    InspectVisiblePage();
                    Capture("Tab" + tab + "-" + Views[view].x + "x" + Views[view].y);
                    if (++tab < 4) { SelectTab(tab); Next(1); break; }
                    tab = 0;
                    if (++view < Views.Length) { SetView(); SelectTab(0); Next(1); break; }
                    tools.EndSandbox();
                    Require(!AdminTools.IsOpen && !PlayerProgress.AdminSandboxActive && Time.timeScale == 0f,
                        "Leaving admin from home does not start gameplay.");
                    CheckOrdinarySave();
                    menu.StartRunFromHome(); Next(3); break;
                case 3:
                    if (!RunFlow.IsPlaying) return;
                    Bind(); tools.Open();
                    Require(player.IsTestAttempt && Time.timeScale == 0f, "Opening in a live run irrevocably marks it as a test.");
                    SelectTab(1); SetSlider("GameSpeed", 1.5f);
                    tools.Close();
                    Require(Time.timeScale == 1.5f, "Closing a live admin panel resumes selected speed.");
                    tools.Open();
                    Require(Time.timeScale == 0f, "Reopening pauses selected speed.");
                    SelectTab(2); SetSlider("BuffDuration", 37f);
                    Click("Выдать щит"); Click("Выдать магнит"); Click("Замедлить платформы");
                    RunPowerups powers = player.GetComponent<RunPowerups>();
                    Require(powers.HasShield && powers.MagnetTimeRemaining == 37f && powers.SlowTimeRemaining == 37f,
                        "Actual UI buttons grant real powers with chosen duration while paused.");
                    Click("Убрать все бафы");
                    Require(!powers.HasShield && powers.MagnetTimeRemaining == 0f && powers.SlowTimeRemaining == 0f,
                        "Clear removes the actual effects.");
                    Click("Прыгнуть сейчас");
                    Require(player.GetComponent<Rigidbody2D>().linearVelocity.y > 0f, "Immediate jump button assigns a real launch velocity.");
                    Click("Запустить ракету");
                    Require(powers.IsRocketFlying && Time.timeScale == 0f, "UI starts a real finite rocket without leaking pause.");
                    Capture("Live-Powers-Paused");
                    tools.Close(); Next(4); break;
                case 4:
                    Bind(); tools.Open(); SelectTab(2);
                    Require(player.GetComponent<RunPowerups>().IsRocketActive, "Rocket executes in actual gameplay between panel openings.");
                    Click("Убрать все бафы");
                    Require(!player.GetComponent<RunPowerups>().IsRocketActive && Time.timeScale == 0f, "Clear cancels rocket and preserves pause.");
                    tools.EndSandbox();
                    Require(!PlayerProgress.AdminSandboxActive && !AdminTools.CheatMode && player.IsTestAttempt && Time.timeScale == 1f,
                        "EndSandbox resets speed and cheats but cannot legitimise the same attempt.");
                    CheckOrdinarySave();
                    menu.ReturnToHome(); Next(5); break;
                case 5:
                    Bind();
                    Require(!RunFlow.RunActive && Time.timeScale == 0f && !AdminTools.IsOpen, "Return home restores paused normal UI.");
                    CheckOrdinarySave();
                    storage.Dispose(); storage = null;
                    AdminRuntime.ResetTuning();
                    AdminGameplayValidation.Validate();
                    Debug.Log("ADMIN_EXPANDED_UI_PASS checks=" + checks + " tabs=4 actualViewports=3 screenshots=" + captures +
                        " realButtons=true realSliders=true liveRocket=true currency=true upgrades=true pause=true saveCopiesUnchanged=true silent=true");
                    Finish(0); break;
            }
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    private static void Bind()
    {
        tools = Object.FindAnyObjectByType<AdminTools>(); menu = Object.FindAnyObjectByType<GameMenuUI>();
        player = Object.FindAnyObjectByType<Player>();
        Require(tools != null && menu != null && player != null && AdminTools.IsAvailable, "Real scene contains menu, player and expanded admin.");
    }

    private static void ExerciseTab()
    {
        Require(AdminTools.IsOpen && PlayerProgress.AdminSandboxActive && Time.timeScale == 0f, "All tab interaction stays in a paused sandbox.");
        if (tab == 0)
        {
            TMP_InputField field = (TMP_InputField)typeof(AdminTools).GetField("amount", Private).GetValue(tools);
            int before = PlayerProgress.Coins;
            field.text = "123"; Click("Выдать");
            Require(PlayerProgress.Coins == before + 123, "Visible grant button uses typed amount.");
            Click("Забрать"); Require(PlayerProgress.Coins == before, "Visible subtract button reverses grant.");
            Click("Обнулить"); Require(PlayerProgress.Coins == 0, "Visible zero button clears only sandbox.");
            Click("+1 000"); Click("+10 000"); Require(PlayerProgress.Coins == 11000, "Both shortcut coin buttons work.");
        }
        else if (tab == 1)
        {
            SetSlider("MovementMultiplier", 1.75f); SetSlider("JumpMultiplier", 1.25f);
            SetSlider("GravityMultiplier", .75f); SetSlider("BushMultiplier", 1.5f); SetSlider("GameSpeed", .5f);
            Require(AdminRuntime.MovementMultiplier == 1.75f && AdminRuntime.JumpMultiplier == 1.25f &&
                AdminRuntime.GravityMultiplier == .75f && AdminRuntime.BushMultiplier == 1.5f && AdminRuntime.GameSpeed == .5f,
                "All five slider listeners reach authoritative tuning.");
            Require(Time.timeScale == 0f, "Game speed slider never unpauses panel.");
            Click("Сбросить физику и темп");
            Require(AdminRuntime.MovementMultiplier == 1f && AdminRuntime.JumpMultiplier == 1f && AdminRuntime.GravityMultiplier == 1f &&
                AdminRuntime.BushMultiplier == 1f && AdminRuntime.GameSpeed == 1f && Time.timeScale == 0f, "Reset neutralises all tuning without unpausing.");
        }
        else if (tab == 2)
        {
            SetSlider("BuffDuration", 37f); Require(AdminRuntime.BuffDuration == 37f, "Buff duration integer slider listener works.");
            Click("Выдать магнит");
            Require(!player.GetComponent<RunPowerups>().HasShield && Time.timeScale == 0f, "Power commands do not start a run from home.");
        }
        else
        {
            SetSlider("UpgradeMagnet", 1f); SetSlider("UpgradeDuration", 2f);
            Require(PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Magnet, RunFlow.ModeKey) == 1 &&
                PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Duration, RunFlow.ModeKey) == 2, "Upgrade sliders control current mode independently.");
            Click("Уровень 0");
            Require(PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Magnet, RunFlow.ModeKey) == 0 &&
                PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Duration, RunFlow.ModeKey) == 0, "Level-zero shortcut applies both upgrades.");
            Click("Максимум");
            Require(PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Magnet, RunFlow.ModeKey) == 3 &&
                PlayerProgress.GetUpgradeLevel(PlayerProgress.Upgrade.Duration, RunFlow.ModeKey) == 3, "Max shortcut applies both upgrades.");
        }
        CheckOrdinarySave();
    }

    private static void InspectVisiblePage()
    {
        Canvas.ForceUpdateCanvases();
        RectTransform card = (RectTransform)typeof(AdminTools).GetField("card", Private).GetValue(tools);
        Rect screenCard = ScreenRect(card);
        Require(Contains(Screen.safeArea, screenCard), "Card fits real safe area.");
        Require(Mathf.Abs(screenCard.width / screenCard.height - 780f / 820f) < .02f,
            "Actual card projection is uniform in portrait and landscape.");
        int visiblePages = 0;
        for (int i = 0; i < 4; i++)
        {
            GameObject page = card.Find("AdminPage" + i).gameObject;
            if (page.activeSelf) { visiblePages++; Require(i == tab, "The selected tab alone is visible."); }
        }
        Require(visiblePages == 1, "Exactly one content page visible.");
        foreach (TMP_Text label in card.GetComponentsInChildren<TMP_Text>())
        {
            label.ForceMeshUpdate();
            Require(!label.isTextOverflowing && !label.isTextTruncated, "No visible label overflow: " + label.text);
            Require(Contains(screenCard, ScreenRect(label.rectTransform)), "Visible text stays inside card.");
        }
        foreach (UnityEngine.UI.Selectable input in card.GetComponentsInChildren<UnityEngine.UI.Selectable>())
        {
            Require(Contains(screenCard, ScreenRect(input.GetComponent<RectTransform>())), "Every visible input is inside card.");
            Require(input.targetGraphic != null && input.targetGraphic.raycastTarget, "Visible input has a raycast graphic.");
            if (input is UnityEngine.UI.Slider slider)
            {
                Require(ScreenRect(slider.handleRect).height <= ScreenRect(slider.GetComponent<RectTransform>()).height + 2f,
                    "Slider handle remains within its touch row, not double-stretched into a label.");
                Rect handle = ScreenRect(slider.handleRect);
                foreach (TMP_Text label in card.GetComponentsInChildren<TMP_Text>())
                    Require(!handle.Overlaps(ScreenRect(label.rectTransform)), "Slider handle never covers text: " + label.text);
            }
        }
    }

    private static bool Contains(Rect outside, Rect inside) => inside.xMin >= outside.xMin - 2f && inside.xMax <= outside.xMax + 2f &&
        inside.yMin >= outside.yMin - 2f && inside.yMax <= outside.yMax + 2f;
    private static Rect ScreenRect(RectTransform rect, Camera camera = null)
    {
        Vector3[] corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        return new Rect(min, max - min);
    }
    private static void SetSlider(string name, float value)
    {
        foreach (UnityEngine.UI.Slider slider in tools.GetComponentsInChildren<UnityEngine.UI.Slider>())
            if (slider.gameObject.name == name) { slider.value = value; return; }
        throw new InvalidOperationException("No visible admin slider: " + name);
    }
    private static void SelectTab(int index) { tab = index; Click("AdminTab" + index); }
    private static void Click(string name)
    {
        foreach (UnityEngine.UI.Button button in tools.GetComponentsInChildren<UnityEngine.UI.Button>())
            if (button.gameObject.name == name) { Require(button.interactable, "Button interactable: " + name); button.onClick.Invoke(); return; }
        throw new InvalidOperationException("No visible admin button: " + name);
    }
    private static void CheckOrdinarySave() => Require(PlayerPrefs.GetString(primary) == originalPrimary && PlayerPrefs.GetString(backup) == originalBackup,
        "Both ordinary durable save copies stay byte-identical.");
    private static void SetView()
    {
        GameplayPreferences.SetPortrait(Views[view].y > Views[view].x, false);
        PlayModeWindow.SetCustomRenderingResolution((uint)Views[view].x, (uint)Views[view].y, "Doodlik expanded admin QA");
        // Legacy gallery capture restores Camera.aspect as a number, which
        // pins it to the old landscape ratio. Restore automatic tracking.
        if (Camera.main != null) Camera.main.ResetAspect();
    }
    private static void Capture(string name)
    {
        Camera camera = Camera.main;
        float priorAspect = camera.aspect;
        RenderTexture priorTarget = camera.targetTexture, priorActive = RenderTexture.active;
        RenderTexture target = new RenderTexture(Screen.width, Screen.height, 24);
        Texture2D capture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        var overlays = new List<Canvas>(); var cameras = new List<Camera>();
        try
        {
            // An explicit RT does not reset a previously pinned camera aspect.
            // Overlay UI is correct in GameView but would otherwise be squeezed
            // when temporarily projected through the old landscape camera.
            camera.aspect = (float)Screen.width / Screen.height;
            camera.targetTexture = target;
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>())
            {
                // Nested sorting Canvases inherit their root projection. Making
                // both root and child ScreenSpaceCamera distorts portrait UI.
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                overlays.Add(canvas); cameras.Add(canvas.worldCamera);
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
            }
            Canvas.ForceUpdateCanvases();
            Rect projected = ScreenRect((RectTransform)typeof(AdminTools).GetField("card", Private).GetValue(tools), camera);
            Require(Mathf.Abs(projected.width / projected.height - 780f / 820f) < .02f,
                "Capture camera projection preserves the real overlay card aspect.");
            camera.Render(); RenderTexture.active = target;
            capture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); capture.Apply();
            Directory.CreateDirectory("Logs/AdminExpandedUI");
            File.WriteAllBytes("Logs/AdminExpandedUI/" + name + ".png", capture.EncodeToPNG()); captures++;
        }
        finally
        {
            camera.targetTexture = priorTarget; camera.aspect = priorAspect; RenderTexture.active = priorActive;
            for (int i = 0; i < overlays.Count; i++) { overlays[i].renderMode = RenderMode.ScreenSpaceOverlay; overlays[i].worldCamera = cameras[i]; }
            Object.DestroyImmediate(capture); Object.DestroyImmediate(target);
        }
    }
    private static void Next(int next) { phase = next; since = EditorApplication.timeSinceStartup; }
    private static void Require(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException("Expanded admin UI: " + message); }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = message;
    }
    private static void Finish(int code)
    {
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
        if (tools != null) tools.EndSandbox();
        storage?.Dispose(); storage = null;
        EditorApplication.Exit(code);
    }
}
#endif
