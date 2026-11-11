#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Isolated, actual-viewport UI regression. No source scene is saved or built.
[InitializeOnLoad]
public static class ChallengeUiValidation
{
    private const string Key = "Doodlik.ChallengeUiValidation";
    private static int phase, checks, viewIndex, catalogCards, capturedPages, hudView, catalogHudViews;
    private static double since;
    private static string failure;
    private static FrontEndUI frontEnd;
    private static GameMenuUI menu;
    private static ChallengeBoardUI board;
    private static Player player;
    private static IDisposable storage;
    private static int walletBeforeReward;
    private static readonly Vector2Int[] Viewports =
    {
        new Vector2Int(1280, 720), new Vector2Int(720, 1280), new Vector2Int(1600, 720)
    };
    private static readonly HashSet<string> Seen = new HashSet<string>();
    private static readonly Vector3[] Corners = new Vector3[4];

    static ChallengeUiValidation()
    {
        if (!SessionState.GetBool(Key, false)) return;
        Register();
    }

    public static void Run()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-"))
            throw new InvalidOperationException("Challenge UI validation needs an isolated MenuValidation batch copy.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikChallengeUiSandbox";
        GameplayPreferences.SetMode(false, false, false);
        GameplayPreferences.SetDifficulty(GameDifficulty.Normal);
        SessionState.SetBool(Key, true);
        phase = checks = viewIndex = catalogCards = capturedPages = hudView = catalogHudViews = 0;
        Register();
        PlayModeWindow.SetCustomRenderingResolution(1280, 720, "Doodlik challenge UI validation");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void IsolateBeforeFirstScene()
    {
        if (!SessionState.GetBool(Key, false) || storage != null) return;
        // Establish unique save keys BEFORE any front-end subscribes to Changed.
        // Keep the same scope across scene reloads to test genuine persistence.
        storage = PlayerProgress.UseIsolatedStorageForValidation(out _, out _);
    }

    private static void Register()
    {
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        AudioListener.volume = 0f;
        AudioListener.pause = true;
        try
        {
            if (failure != null) throw new InvalidOperationException(failure);
            if (EditorApplication.timeSinceStartup - since > 25) throw new TimeoutException("Challenge UI phase " + phase);
            if (RunFlow.IsTransitioning || Time.frameCount < 20 || EditorApplication.timeSinceStartup - since < 0.35) return;
            Require(EditorUtility.audioMasterMute && AudioListener.volume == 0f, "Editor audio stays fully muted.");
            switch (phase)
            {
                case 0:
                    Require(Screen.width == 1280 && Screen.height == 720, "Actual landscape GameView.");
                    Bind();
                    Require(frontEnd.IsVisible && !RunFlow.RunActive && Time.timeScale == 0f, "Natural paused home screen.");
                    Require(Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>().Length == 1, "One EventSystem only.");
                    Require(Count(false) > 0 && Count(true) > 0 && Count(false) + Count(true) == ChallengeCatalog.All.Count,
                        "Both dynamically enumerated sections cover the entire current catalog.");
                    var ids = new HashSet<string>();
                    foreach (ChallengeDefinition definition in ChallengeCatalog.All)
                        Require(ids.Add(definition.Id), "Catalog IDs remain unique before checking every visible card.");
                    Require(storage != null, "Unique disposable progression storage starts before the UI scene.");
                    Capture("Home-Landscape");
                    Click(frontEnd.transform, "Испытания");
                    Require(frontEnd.IsChallengeScreenVisible, "Home challenge button opens board.");
                    Click(board.transform, "Без испытания");
                    Seen.Clear();
                    Advance(); break;
                case 1:
                    RequireViewport(viewIndex);
                    int expectedPages = (Count(board.ShowingAchievements) + 2) / 3;
                    Require(board.PageCount == expectedPages, "Pagination derives from the complete section, not a fixed old count.");
                    InspectRows();
                    if (!board.ShowingAchievements) ExerciseSelections();
                    Capture((board.ShowingAchievements ? "Achievements" : "Trials") + "-Page" +
                        (board.CurrentPage + 1) + "-" + Viewports[viewIndex].x + "x" + Viewports[viewIndex].y);
                    capturedPages++;
                    if (board.CurrentPage + 1 < board.PageCount)
                    { Click(board.transform, ">"); WaitAgain(); break; }
                    Require(Seen.Count == Count(board.ShowingAchievements), "Every section entry appears exactly once in each actual viewport.");
                    if (!board.ShowingAchievements)
                    { Click(board.transform, "Достижения"); Seen.Clear(); WaitAgain(); break; }
                    Require(frontEnd.GoBack() && !frontEnd.IsChallengeScreenVisible && frontEnd.IsVisible,
                        "Escape/Android Back returns from board to home.");
                    Require(!frontEnd.GoBack(), "Home Back does not reopen or duplicate a board.");
                    if (++viewIndex < Viewports.Length)
                    {
                        SetViewport(viewIndex); frontEnd.ShowChallenges(); Seen.Clear(); WaitAgain(); break;
                    }
                    Require(catalogCards == ChallengeCatalog.All.Count * Viewports.Length,
                        "Every current catalog description was inspected in all three real viewports.");
                    frontEnd.ShowChallenges(); ClickRow(0);
                    Require(PlayerProgress.SelectedChallengeId == "run_coin10", "Select a fresh coin task before the attempt.");
                    PlayerProgress.Flush(); PlayerProgress.Reload();
                    Require(PlayerProgress.SelectedChallengeId == "run_coin10", "Task selection survives save-cache reload.");
                    frontEnd.GoBack(); menu.StartRunFromHome(); Advance(); break;
                case 2:
                    if (!RunFlow.IsPlaying) return;
                    Bind();
                    player.GetComponent<Rigidbody2D>().simulated = false;
                    Object.FindAnyObjectByType<Spawner>().enabled = false;
                    Object.FindAnyObjectByType<CameraFollow>().enabled = false;
                    Advance(); break;
                case 3:
                    Require(RunChallenges.Active != null && RunChallenges.Active.IsEligible, "An eligible run owns a challenge session.");
                    walletBeforeReward = PlayerProgress.Coins;
                    RunChallenges.Active.RecordCoin(false); RunChallenges.Active.RecordCoin(false); RunChallenges.Active.RecordCoin(false);
                    hudView = 0; SetViewport(hudView);
                    Advance(); break;
                case 4:
                    RequireViewport(hudView);
                    ChallengeHud hud = Object.FindAnyObjectByType<ChallengeHud>();
                    Require(hud != null && hud.GetComponent<CanvasGroup>().alpha > 0f, "Active challenge HUD is visible.");
                    TMP_Text hudText = hud.GetComponent<TMP_Text>();
                    Require(hudText != null && hudText.text.Contains("3/10") && !hudText.raycastTarget,
                        "HUD displays actual progress without blocking touch.");
                    RectTransform hudRect = hud.GetComponent<RectTransform>();
                    GameHudFrame frame = Object.FindAnyObjectByType<GameHudFrame>();
                    Require(ScreenRect(hudRect).yMax < ScreenRect(frame.GetComponent<RectTransform>()).yMin,
                        "Challenge note remains below the existing score card.");
                    hudText.ForceMeshUpdate();
                    Require(!hudText.isTextOverflowing && !hudText.isTextTruncated, "Progress note fits at the actual viewport.");
                    Capture("Active-HUD-" + Viewports[hudView].x + "x" + Viewports[hudView].y);
                    InspectAllHudTexts(hud, hudText);
                    if (++hudView < Viewports.Length) { SetViewport(hudView); WaitAgain(); break; }
                    for (int i = 0; i < 7; i++) RunChallenges.Active.RecordCoin(false);
                    Advance(); break;
                case 5:
                    Require(PlayerProgress.IsChallengeCompleted("run_coin10"), "Task completes automatically without a claim button.");
                    Require(PlayerProgress.SelectedChallengeId == string.Empty, "Reward atomically clears the completed selection.");
                    Require(PlayerProgress.Coins == walletBeforeReward + ChallengeCatalog.Find("run_coin10").RewardCoins,
                        "Presentation does not award event-fixture coins or duplicate the thirty-coin task reward.");
                    ChallengeHud completedHud = Object.FindAnyObjectByType<ChallengeHud>();
                    TMP_Text completedText = completedHud.GetComponent<TMP_Text>();
                    Require(completedText.text.Contains("Выполнено") && completedText.text.Contains(ChallengeCatalog.Find("run_coin10").RewardCoins + " монет"),
                        "Completion toast identifies the task and automatic coin reward.");
                    completedText.ForceMeshUpdate();
                    Require(!completedText.isTextOverflowing && !completedText.isTextTruncated, "Completion toast has no ellipsis.");
                    Capture("Reward-Toast-Landscape20x9");
                    RunChallenges.Active.RecordCoin(false);
                    Require(PlayerProgress.Coins == walletBeforeReward + ChallengeCatalog.Find("run_coin10").RewardCoins, "Repeated progress cannot repeat the reward.");
                    Require(!PlayerProgress.Owns("trail_ninelives"), "Exclusive mastery reward starts locked in the disposable profile.");
                    RunChallenges.Active.RecordShieldUse(true); RunChallenges.Active.RecordShieldUse(true);
                    RunChallenges.Active.RecordShieldUse(true);
                    Advance(); break;
                case 6:
                    Require(PlayerProgress.IsChallengeCompleted("mastery_rescue3") && PlayerProgress.Owns("trail_ninelives"),
                        "A background cosmetic reward is automatically unlocked.");
                    TMP_Text cosmeticToast = Object.FindAnyObjectByType<ChallengeHud>().GetComponent<TMP_Text>();
                    Require(cosmeticToast.text.Contains("Девять жизней") && cosmeticToast.text.Contains("Эксклюзивный предмет открыт"),
                        "The background reward toast uses the cosmetic variant, not a zero-coin prize.");
                    cosmeticToast.ForceMeshUpdate();
                    Require(!cosmeticToast.isTextOverflowing && !cosmeticToast.isTextTruncated, "Cosmetic reward toast fits without ellipsis.");
                    Capture("Cosmetic-Reward-Toast-Landscape20x9");
                    menu.ReturnToHome(); Advance(); break;
                case 7:
                    Bind();
                    Require(!RunFlow.RunActive && frontEnd.IsVisible && Time.timeScale == 0f, "Returning home ends and reloads the attempt.");
                    Require(Object.FindObjectsByType<ChallengeBoardUI>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1,
                        "No duplicate challenge board survives scene reload.");
                    Require(Object.FindObjectsByType<ChallengeHud>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1,
                        "One fresh scene challenge HUD after restart.");
                    Require(PlayerProgress.SelectedChallengeId == string.Empty && PlayerProgress.IsChallengeCompleted("run_coin10"),
                        "Completion and empty selection survive returning home.");
                    frontEnd.ShowChallenges(); InspectRows(false);
                    Require(!board.transform.Find("Challenge row 0").GetComponentInChildren<UnityEngine.UI.Button>().interactable,
                        "Completed task cannot be reselected or manually claimed.");
                    ClickRow(1);
                    Require(PlayerProgress.SelectedChallengeId == "run_coin25", "Another unfinished task remains selectable.");
                    Capture("Board-Completed-AfterReload");
                    frontEnd.GoBack(); menu.StartRunFromHome(); Advance(); break;
                case 8:
                    if (!RunFlow.IsPlaying) return;
                    Bind(); player.GetComponent<Rigidbody2D>().simulated = false;
                    Object.FindAnyObjectByType<Spawner>().enabled = false;
                    Object.FindAnyObjectByType<CameraFollow>().enabled = false;
                    Require(RunChallenges.Active != null && RunChallenges.Active.SelectedDefinition.Id == "run_coin25",
                        "The next run captures only the newly selected task.");
                    menu.ReturnToHome(); Advance(); break;
                case 9:
                    Bind();
                    Require(!RunFlow.RunActive && PlayerProgress.SelectedChallengeId == "run_coin25", "An incomplete task survives a second run and scene reload.");
                    frontEnd.ShowChallenges(); Capture("Board-Incomplete-Persists");
                    Click(board.transform, "Без испытания"); PlayerProgress.Flush(); PlayerProgress.Reload();
                    Require(PlayerProgress.SelectedChallengeId == string.Empty, "No-task choice is also saved persistently.");
                    frontEnd.GoBack();
                    Require(Object.FindAnyObjectByType<ChallengeHud>().GetComponent<CanvasGroup>().alpha == 0f,
                        "Runtime progress and toast are hidden on home.");
                    Require(catalogHudViews == Count(false) * Viewports.Length, "Every run-task name/counter fits every HUD viewport.");
                    Debug.Log("CHALLENGE_UI_PASS checks=" + checks + " tasks=" + Count(false) + " achievements=" + Count(true) +
                        " actualViewports=3 catalogCardViews=" + catalogCards + " catalogPages=" + capturedPages +
                        " catalogHudViews=" + catalogHudViews + " reload=true selectionAll=true reward=true toast=true hudAllViewports=true silent=true");
                    Finish(0); break;
            }
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    private static void Bind()
    {
        frontEnd = Object.FindAnyObjectByType<FrontEndUI>();
        menu = Object.FindAnyObjectByType<GameMenuUI>();
        board = Object.FindAnyObjectByType<ChallengeBoardUI>(FindObjectsInactive.Include);
        player = Object.FindAnyObjectByType<Player>();
        Require(frontEnd != null && menu != null && board != null && player != null, "Existing menu, board and scene references.");
    }

    private static void InspectRows(bool unique = true)
    {
        Canvas.ForceUpdateCanvases();
        Require(frontEnd.IsChallengeScreenVisible, "Board is visible on the front-end.");
        Rect boardRect = ScreenRect(board.GetComponent<RectTransform>());
        Rect safe = Screen.safeArea;
        Require(boardRect.xMin >= safe.xMin - 2f && boardRect.xMax <= safe.xMax + 2f &&
            boardRect.yMin >= safe.yMin - 2f && boardRect.yMax <= safe.yMax + 2f, "Board fits the actual safe area.");
        int found = 0;
        foreach (Transform child in board.transform)
        {
            if (!child.gameObject.activeSelf || !child.name.StartsWith("Challenge row ")) continue;
            TMP_Text[] labels = child.GetComponentsInChildren<TMP_Text>();
            Require(labels.Length == 4, "Each row has a title, description, reward and action.");
            ChallengeDefinition definition = null;
            foreach (ChallengeDefinition candidate in ChallengeCatalog.All)
                if (candidate.Name == labels[0].text) definition = candidate;
            Require(definition != null && definition.IsAchievement == board.ShowingAchievements, "Correct catalog entry is displayed.");
            if (unique)
            {
                Require(Seen.Add(definition.Id), "No duplicate catalog entry across pages.");
                catalogCards++;
            }
            Require(labels[1].text == definition.Description, "The full unabridged description is displayed.");
            foreach (TMP_Text label in labels)
            {
                label.ForceMeshUpdate();
                Require(!label.isTextOverflowing && !label.isTextTruncated, "No clipped challenge text: " + definition.Id + "/" + label.text);
            }
            Require(!labels[2].text.Contains("эксклюзивный предмет"), "Reward uses its real wardrobe name.");
            if (string.IsNullOrEmpty(definition.RewardItemId))
                Require(labels[2].text.Contains(definition.RewardCoins + " монет"), "Coin reward is accurate for trials and career achievements.");
            Require(!definition.IsAchievement || !child.GetComponentInChildren<UnityEngine.UI.Button>().interactable,
                "Background achievements cannot be selected or claimed manually.");
            Rect rowRect = ScreenRect((RectTransform)child);
            Require(rowRect.xMin >= boardRect.xMin && rowRect.xMax <= boardRect.xMax &&
                rowRect.yMin >= boardRect.yMin && rowRect.yMax <= boardRect.yMax, "Row stays inside the card.");
            found++;
        }
        Require(found > 0 && found <= 3, "A page has one to three readable cards.");
        foreach (TMP_Text label in board.GetComponentsInChildren<TMP_Text>())
        {
            label.ForceMeshUpdate();
            Require(!label.isTextOverflowing && !label.isTextTruncated, "All board labels fit without ellipsis.");
        }
    }

    private static int Count(bool achievements)
    {
        int count = 0;
        foreach (ChallengeDefinition definition in ChallengeCatalog.All)
            if (definition.IsAchievement == achievements) count++;
        return count;
    }

    private static void ExerciseSelections()
    {
        for (int i = 0; i < 3; i++)
        {
            Transform row = board.transform.Find("Challenge row " + i);
            if (!row.gameObject.activeSelf) continue;
            TMP_Text title = row.GetComponentsInChildren<TMP_Text>()[0];
            ChallengeDefinition definition = null;
            foreach (ChallengeDefinition candidate in ChallengeCatalog.All)
                if (candidate.Name == title.text) definition = candidate;
            ClickRow(i);
            Require(PlayerProgress.SelectedChallengeId == definition.Id, "Every dynamically enumerated task button selects its own entry.");
            Require(!row.GetComponentInChildren<UnityEngine.UI.Button>().interactable, "Selected action cannot be activated twice.");
            foreach (TMP_Text label in board.GetComponentsInChildren<TMP_Text>())
            {
                label.ForceMeshUpdate();
                Require(!label.isTextOverflowing && !label.isTextTruncated,
                    "Selecting a long-named task does not truncate the board header: " + definition.Id);
            }
            Click(board.transform, "Без испытания");
            Require(PlayerProgress.SelectedChallengeId == string.Empty, "Clearing selection works from every task page.");
        }
    }

    private static void InspectAllHudTexts(ChallengeHud hud, TMP_Text label)
    {
        // Presentation fixtures only: the real progress/reward path above still
        // uses its original session. No fixture publishes a gameplay event or
        // mutates the saved selected task, counters or wallet.
        RunChallenges bridge = RunChallenges.Active;
        ChallengeSession original = bridge.Session;
        PropertyInfo session = typeof(RunChallenges).GetProperty("Session", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo refresh = typeof(ChallengeHud).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        int wallet = PlayerProgress.Coins;
        string selection = PlayerProgress.SelectedChallengeId;
        try
        {
            foreach (ChallengeDefinition definition in ChallengeCatalog.All)
            {
                if (definition.IsAchievement) continue;
                session.SetValue(bridge, new ChallengeSession(definition.Id, GameplayPreferences.UseInertia, player.RunDifficulty));
                refresh.Invoke(hud, null); Canvas.ForceUpdateCanvases(); label.ForceMeshUpdate();
                Require(label.text.Contains(definition.Name) && label.text.Contains("0/" + definition.Target),
                    "Full task name and actual target appear in its HUD: " + definition.Id);
                Require(!label.isTextOverflowing && !label.isTextTruncated && !label.raycastTarget,
                    "Every catalog HUD name/counter is readable and non-blocking: " + definition.Id);
                catalogHudViews++;
                if (definition.Id.StartsWith("run_rocket_") || definition.Id.StartsWith("run_gold") ||
                    definition.Id.StartsWith("run_bee_") || definition.Id == "run_sky_combo3")
                    Capture("Adventure-HUD-" + definition.Id + "-" + Viewports[hudView].x + "x" + Viewports[hudView].y);
            }
        }
        finally { session.SetValue(bridge, original); refresh.Invoke(hud, null); }
        Require(PlayerProgress.Coins == wallet && PlayerProgress.SelectedChallengeId == selection,
            "HUD-only fixtures do not change the wallet or saved selection.");
    }

    private static void SetViewport(int index)
    {
        Vector2Int viewport = Viewports[index];
        GameplayPreferences.SetPortrait(viewport.y > viewport.x, false);
        PlayModeWindow.SetCustomRenderingResolution((uint)viewport.x, (uint)viewport.y, "Doodlik challenge UI validation");
    }

    private static void RequireViewport(int index)
    {
        Vector2Int viewport = Viewports[index];
        Require(Screen.width == viewport.x && Screen.height == viewport.y,
            "Real GameView matches " + viewport.x + "x" + viewport.y + ", not just a capture target.");
    }

    private static void ClickRow(int index)
    {
        Transform row = board.transform.Find("Challenge row " + index);
        UnityEngine.UI.Button button = row.GetComponentInChildren<UnityEngine.UI.Button>();
        Require(button != null && button.interactable, "The intended task action is interactable.");
        button.onClick.Invoke();
    }

    private static void Click(Transform root, string name)
    {
        foreach (UnityEngine.UI.Button button in root.GetComponentsInChildren<UnityEngine.UI.Button>())
        {
            if (button.gameObject.name != name) continue;
            Require(button.interactable, "Button is interactable: " + name);
            button.onClick.Invoke();
            return;
        }
        throw new InvalidOperationException("Missing visible button: " + name);
    }

    private static Rect ScreenRect(RectTransform rect)
    {
        rect.GetWorldCorners(Corners);
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 minimum = RectTransformUtility.WorldToScreenPoint(uiCamera, Corners[0]);
        Vector2 maximum = RectTransformUtility.WorldToScreenPoint(uiCamera, Corners[2]);
        return new Rect(minimum, maximum - minimum);
    }

    private static void Capture(string name)
    {
        Camera camera = Camera.main;
        RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
        RenderTexture target = new RenderTexture(Screen.width, Screen.height, 24);
        Texture2D capture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        List<Canvas> overlays = new List<Canvas>();
        List<Camera> oldCameras = new List<Camera>();
        try
        {
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>())
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                overlays.Add(canvas); oldCameras.Add(canvas.worldCamera);
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
            }
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            capture.ReadPixels(new Rect(0f, 0f, Screen.width, Screen.height), 0, 0); capture.Apply();
            string output = "Logs/ChallengeUI-" + ChallengeCatalog.All.Count;
            Directory.CreateDirectory(output);
            File.WriteAllBytes(output + "/" + name + ".png", capture.EncodeToPNG());
            if (frontEnd != null && frontEnd.IsChallengeScreenVisible && board != null)
            {
                Rect card = ScreenRect(board.GetComponent<RectTransform>());
                int left = Mathf.Max(0, Mathf.FloorToInt(card.xMin) - 12);
                int bottom = Mathf.Max(0, Mathf.FloorToInt(card.yMin) - 12);
                int width = Mathf.Min(Screen.width - left, Mathf.CeilToInt(card.width) + 30);
                int height = Mathf.Min(Screen.height - bottom, Mathf.CeilToInt(card.height) + 30);
                Texture2D cardCapture = new Texture2D(width, height, TextureFormat.RGB24, false);
                try
                {
                    cardCapture.ReadPixels(new Rect(left, bottom, width, height), 0, 0); cardCapture.Apply();
                    File.WriteAllBytes(output + "/Card-" + name + ".png", cardCapture.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(cardCapture); }
            }
        }
        finally
        {
            camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
            for (int i = 0; i < overlays.Count; i++)
            { overlays[i].renderMode = RenderMode.ScreenSpaceOverlay; overlays[i].worldCamera = oldCameras[i]; }
            Object.DestroyImmediate(capture); Object.DestroyImmediate(target);
        }
    }

    private static void Advance() { phase++; since = EditorApplication.timeSinceStartup; }
    private static void WaitAgain() => since = EditorApplication.timeSinceStartup;
    private static void Require(bool value, string message) { checks++; if (!value) throw new InvalidOperationException("Challenge UI: " + message); }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = message;
    }
    private static void Finish(int code)
    {
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
        storage?.Dispose(); storage = null;
        EditorApplication.Exit(code);
    }
}
#endif
