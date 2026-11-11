#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Presentation fixtures use unique save keys before the first UI scene. The
// separate RunJournalValidation suite proves physical telemetry and lifecycle.
[InitializeOnLoad]
public static class StatisticsUiValidation
{
    private const string Key = "Doodlik.StatisticsUiValidation";
    private static int phase, checks, viewport;
    private static double since;
    private static string failure;
    private static FrontEndUI frontEnd;
    private static GameMenuUI menu;
    private static RunStatisticsUI statistics;
    private static IDisposable storage;
    private static CareerStatisticsSnapshot extremeSnapshot;
    private static readonly Dictionary<string, int?> OriginalRecords = new Dictionary<string, int?>();
    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly Vector2Int[] Viewports = { new Vector2Int(1280, 720), new Vector2Int(720, 1280), new Vector2Int(1600, 720) };

    static StatisticsUiValidation()
    {
        if (SessionState.GetBool(Key, false)) Register();
    }

    public static void Run()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-"))
            throw new InvalidOperationException("Statistics UI validation requires an isolated batch project.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikStatisticsUiSandbox";
        GameplayPreferences.SetMode(false, false, false);
        GameplayPreferences.SetDifficulty(GameDifficulty.Normal);
        SessionState.SetBool(Key, true);
        phase = checks = viewport = 0;
        Register(); SetViewport(0);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Isolate()
    {
        if (!SessionState.GetBool(Key, false) || storage != null) return;
        storage = PlayerProgress.UseIsolatedStorageForValidation(out _, out _);
        foreach (string key in RunRecords.AllKeys)
        {
            OriginalRecords[key] = PlayerPrefs.HasKey(key) ? (int?)PlayerPrefs.GetInt(key) : null;
            PlayerPrefs.DeleteKey(key);
        }
        PlayerPrefs.SetInt(RunRecords.HeightKey(false, false, GameDifficulty.Normal), 2500);
        PlayerPrefs.SetInt(RunRecords.ScoreKey(false, false, GameDifficulty.Normal), 4321);
        PlayerPrefs.Save();
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
            if (EditorApplication.timeSinceStartup - since > 30d) throw new TimeoutException("Statistics UI phase " + phase);
            if (RunFlow.IsTransitioning || Time.frameCount < 25 || EditorApplication.timeSinceStartup - since < 0.35d) return;
            Require(EditorUtility.audioMasterMute && AudioListener.volume == 0f, "Every UI action remains completely muted.");
            switch (phase)
            {
                case 0:
                    Bind(); Require(storage != null, "Storage isolation begins before UI subscribers.");
                    Require(!RunFlow.RunActive && Time.timeScale == 0f && frontEnd.IsVisible, "Natural paused home.");
                    Require(!frontEnd.IsStatisticsScreenVisible && PlayerProgress.Statistics.Runs == 0, "Home never fabricates runs.");
                    InspectHome(); Capture("Home"); Click(frontEnd.transform, "Статистика"); Move(1); break;
                case 1:
                    RequireViewport(); Inspect();
                    Require(!statistics.ShowingRecent && Contains("Высота: 2 500 · Счёт: 4 321"), "Existing records keep an old profile's statistics meaningful.");
                    Require(Contains("Счётчики и журнал ведутся с этого обновления."), "New counters are not misrepresented as lifetime history.");
                    Require(PlayerProgress.Statistics.Runs == 0, "Empty counters stay honest despite old records.");
                    Capture("Empty-General-" + Suffix()); Click(statistics.transform, "Последние"); Move(2); break;
                case 2:
                    RequireViewport(); Inspect();
                    Require(statistics.ShowingRecent && Contains("Старые забеги восстановить нельзя."), "Empty journal has a clear explanation.");
                    Require(VisibleRows() == 0 && statistics.PageCount == 1, "An empty journal has no fake row or page.");
                    Capture("Empty-Journal-" + Suffix());
                    Require(frontEnd.GoBack() && !frontEnd.IsStatisticsScreenVisible, "Escape/Android Back returns to home.");
                    Require(!frontEnd.GoBack(), "Home Back does not duplicate panels.");
                    if (++viewport < Viewports.Length)
                    { SetViewport(viewport); Click(frontEnd.transform, "Статистика"); Move(1); break; }
                    AddFixtures(); viewport = 0; SetViewport(0); Click(frontEnd.transform, "Статистика"); Move(3); break;
                case 3:
                    RequireViewport(); Inspect();
                    CareerStatisticsSnapshot snapshot = PlayerProgress.Statistics;
                    Require(snapshot.Runs == 6 && snapshot.RecentRuns.Count == 5 && snapshot.TotalCoins == 87 &&
                        snapshot.TotalBushes == 21 && snapshot.TotalMovingLandings == 27 && snapshot.TotalNarrowLandings == 33 &&
                        snapshot.TotalWideJumps == 39 && snapshot.ActiveSeconds == 1035d, "All aggregate fixtures are exact.");
                    Require(Contains("17 мин 15 с") && Contains("Лучшее с обновления: высота 600"), "Time and since-update best values are displayed.");
                    Require(Contains("Высота: 2 500 · Счёт: 4 321"), "Since-update statistics do not overwrite legacy records.");
                    Capture("Populated-General-" + Suffix()); Click(statistics.transform, "Последние"); Move(4); break;
                case 4:
                    RequireViewport(); Inspect();
                    Require(statistics.PageCount == 2 && statistics.CurrentPage == 0 && VisibleRows() == 3, "Five attempts use two pages, not an unbounded list.");
                    Require(RowContains(0, "Высота 600") && RowContains(1, "Высота 500") && RowContains(2, "Высота 400"), "Recent attempts are newest first.");
                    Capture("Journal-Page1-" + Suffix()); Click(statistics.transform, ">"); Move(5); break;
                case 5:
                    RequireViewport(); Inspect();
                    Require(statistics.CurrentPage == 1 && VisibleRows() == 2 && RowContains(0, "Высота 300") && RowContains(1, "Высота 200"),
                        "The last page contains the two remaining attempts, with no sixth stale row.");
                    Capture("Journal-Page2-" + Suffix()); Click(statistics.transform, "<");
                    Require(statistics.CurrentPage == 0, "Previous-page navigation restores page one.");
                    ExerciseRoutes();
                    if (++viewport < Viewports.Length)
                    { SetViewport(viewport); Click(frontEnd.transform, "Статистика"); Move(3); break; }
                    viewport = 0; SetViewport(0); Click(frontEnd.transform, "Статистика"); Move(6); break;
                case 6:
                    RequireViewport(); RenderExtreme(); Inspect();
                    Require(Contains("9,22 × 10^18") && Contains("> 999 999 ч"), "Finite extreme totals and time use readable compact formatting.");
                    Capture("Extreme-General-" + Suffix()); Click(statistics.transform, "Последние");
                    RenderExtreme(); Move(7); break;
                case 7:
                    RequireViewport(); Inspect(); Require(Contains("2,15 млрд"), "Extreme per-run counters fit the journal.");
                    Capture("Extreme-Journal-Page1-" + Suffix()); Click(statistics.transform, ">"); RenderExtreme(); Inspect();
                    Capture("Extreme-Journal-Page2-" + Suffix()); frontEnd.GoBack();
                    if (++viewport < Viewports.Length)
                    { SetViewport(viewport); Click(frontEnd.transform, "Статистика"); Move(6); break; }
                    Require(PlayerProgress.Statistics.Runs == 6 && PlayerProgress.Statistics.TotalCoins == 87,
                        "Extreme presentation fixtures never write into the real statistics snapshot.");
                    PlayerProgress.Flush(); PlayerProgress.Reload();
                    Require(PlayerProgress.Statistics.Runs == 6 && PlayerProgress.Statistics.RecentRuns.Count == 5, "Journal and totals persist through cache reload.");
                    menu.StartRunFromHome(); Move(8); break;
                case 8:
                    if (!RunFlow.IsPlaying) return;
                    Player player = Object.FindAnyObjectByType<Player>();
                    player.GetComponent<Rigidbody2D>().simulated = false;
                    Object.FindAnyObjectByType<Spawner>().enabled = false;
                    Object.FindAnyObjectByType<CameraFollow>().enabled = false;
                    Require(player.Journal != null && player.Journal.Started, "The existing start path begins a journal attempt.");
                    Require(!frontEnd.IsStatisticsScreenVisible, "Starting gameplay hides all statistics panels.");
                    player.Challenges.RecordCoin(false); player.Challenges.RecordCoin(false); player.Challenges.RecordCoin(false);
                    menu.ReturnToHome(); Move(9); break;
                case 9:
                    Bind(); Require(!RunFlow.RunActive && frontEnd.IsVisible, "Returning home completes the real scene reload.");
                    Require(PlayerProgress.Statistics.Runs == 7 && PlayerProgress.Statistics.TotalCoins == 90 &&
                        PlayerProgress.Statistics.RecentRuns[0].Coins == 3 && PlayerProgress.Statistics.RecentRuns[0].EndReason == "В меню",
                        "A new finalized attempt automatically appears above the UI fixtures.");
                    Require(Object.FindObjectsByType<RunStatisticsUI>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1,
                        "Exactly one statistics panel after scene reload.");
                    Require(Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>().Length == 1, "Exactly one EventSystem after reload.");
                    Click(frontEnd.transform, "Статистика"); Inspect(); Capture("General-AfterReload");
                    Click(statistics.transform, "Последние"); Inspect(); Capture("Journal-AfterReload");
                    Require(RowContains(0, "Монеты 3"), "The refreshed first row shows the newly finalized attempt.");
                    Require(PlayerProgress.Coins == 0, "Read-only statistics and fixture entries never create wallet coins.");
                    Debug.Log("STATISTICS_UI_PASS checks=" + checks + " actualViewports=3 legacyRecords=True empty=True rows=5 pages=2" +
                        " allCounters=True extremeValues=True mutualExclusion=True reload=True readOnly=True silent=True");
                    Finish(0); break;
            }
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    private static void AddFixtures()
    {
        string[] reasons = { "Падение", "Колючки", "Перезапуск", "В меню", "Выход", "Пчёлы" };
        for (int i = 0; i < 6; i++)
            Require(PlayerProgress.RecordRun(new RunStatisticsEntry(Guid.NewGuid().ToString("N"), i % 2 == 0, i % 2 != 0,
                (GameDifficulty)(i % 3), (i + 1) * 100, (i + 1) * 150, 7 + i * 3,
                i + 1, i + 2, i + 3, i + 4, 45d + i * 51d, reasons[i])), "Each isolated UI fixture is accepted once.");
    }

    private static void RenderExtreme()
    {
        if (extremeSnapshot == null)
        {
            RunStatisticsEntry[] runs = new RunStatisticsEntry[5];
            for (int i = 0; i < runs.Length; i++)
                runs[i] = new RunStatisticsEntry("extreme" + i, i % 2 == 0, i % 2 != 0, GameDifficulty.Hard,
                    int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue,
                    double.MaxValue, "Падение");
            // The model is intentionally immutable and its constructor internal.
            // Drive the exact production render path without a fake public API,
            // mutating profile fields or weakening save-data validation.
            ConstructorInfo constructor = typeof(CareerStatisticsSnapshot).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)[0];
            extremeSnapshot = (CareerStatisticsSnapshot)constructor.Invoke(new object[] { int.MaxValue,
                long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, double.MaxValue,
                int.MaxValue, int.MaxValue, int.MaxValue, runs });
        }
        typeof(RunStatisticsUI).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(statistics, new object[] { extremeSnapshot });
    }

    private static void ExerciseRoutes()
    {
        Require(frontEnd.GoBack() && !frontEnd.IsStatisticsScreenVisible, "Statistics Back closes its panel.");
        Click(frontEnd.transform, "Испытания"); Require(frontEnd.IsChallengeScreenVisible, "Challenges still open normally.");
        frontEnd.ShowStatistics(); Require(!frontEnd.IsChallengeScreenVisible && frontEnd.IsStatisticsScreenVisible, "Statistics replaces challenges.");
        frontEnd.ShowModes(); Require(frontEnd.IsModeScreenVisible && !frontEnd.IsStatisticsScreenVisible, "Modes replaces statistics.");
        frontEnd.ShowStatistics(); Require(!frontEnd.IsModeScreenVisible && frontEnd.IsStatisticsScreenVisible, "Statistics replaces modes.");
        frontEnd.GoBack(); Click(frontEnd.transform, "Гардероб");
        frontEnd.ShowStatistics(); Require(frontEnd.IsStatisticsScreenVisible, "Statistics replaces the wardrobe.");
        frontEnd.Hide(); Require(!frontEnd.IsStatisticsScreenVisible && !statistics.IsVisible, "Hide also deactivates the statistics object.");
        frontEnd.ShowHome(); Click(frontEnd.transform, "Настройки");
        Require(!frontEnd.IsVisible && !statistics.IsVisible, "Settings cannot overlap statistics.");
        Click(menu.transform, "Назад");
        Require(frontEnd.IsVisible && !frontEnd.IsStatisticsScreenVisible, "Settings Back returns to the home card, not an old panel.");
        InspectHome();
    }

    private static void Bind()
    {
        frontEnd = Object.FindAnyObjectByType<FrontEndUI>(); menu = Object.FindAnyObjectByType<GameMenuUI>();
        statistics = Object.FindAnyObjectByType<RunStatisticsUI>(FindObjectsInactive.Include);
        Require(frontEnd != null && menu != null && statistics != null, "Existing front-end and one statistics view are connected.");
    }

    private static void InspectHome()
    {
        Transform home = frontEnd.transform.Find("Start screen/Safe Area/Home");
        Require(home != null && home.gameObject.activeSelf, "Home card is visible.");
        UnityEngine.UI.Button settings = FindButton(home, "Настройки"), stats = FindButton(home, "Статистика");
        Rect first = ScreenRect(settings.GetComponent<RectTransform>()), second = ScreenRect(stats.GetComponent<RectTransform>());
        Require(first.xMax < second.xMin && Mathf.Abs(first.yMin - second.yMin) < 1f, "Paired bottom-row buttons have a clean gap.");
        InspectText(home);
    }

    private static void Inspect()
    {
        Canvas.ForceUpdateCanvases();
        Require(frontEnd.IsStatisticsScreenVisible, "Statistics is the active front-end view.");
        Rect rect = ScreenRect(statistics.GetComponent<RectTransform>()), safe = Screen.safeArea;
        Require(rect.xMin >= safe.xMin - 2f && rect.xMax <= safe.xMax + 2f && rect.yMin >= safe.yMin - 2f && rect.yMax <= safe.yMax + 2f,
            "The statistics card fits the actual safe area.");
        InspectText(statistics.transform);
        foreach (UnityEngine.UI.Button button in statistics.GetComponentsInChildren<UnityEngine.UI.Button>())
            Require(button.gameObject.name != "Удалить" && button.gameObject.name != "Сбросить", "The statistics view has no destructive reset action.");
    }

    private static void InspectText(Transform parent)
    {
        foreach (TMP_Text label in parent.GetComponentsInChildren<TMP_Text>())
        {
            label.ForceMeshUpdate();
            Require(!label.isTextOverflowing && !label.isTextTruncated, "No clipped statistics text: " + label.text);
            Require(!label.raycastTarget, "Labels do not intercept touch input.");
        }
    }

    private static bool Contains(string text)
    {
        foreach (TMP_Text label in statistics.GetComponentsInChildren<TMP_Text>()) if (label.text.Contains(text)) return true;
        return false;
    }

    private static int VisibleRows()
    {
        int count = 0;
        foreach (Transform child in statistics.transform.Find("Recent attempts"))
            if (child.gameObject.activeSelf && child.name.StartsWith("Journal row ")) count++;
        return count;
    }

    private static bool RowContains(int row, string text)
    {
        Transform target = statistics.transform.Find("Recent attempts/Journal row " + row);
        foreach (TMP_Text label in target.GetComponentsInChildren<TMP_Text>()) if (label.text.Contains(text)) return true;
        return false;
    }

    private static UnityEngine.UI.Button FindButton(Transform root, string name)
    {
        foreach (UnityEngine.UI.Button button in root.GetComponentsInChildren<UnityEngine.UI.Button>())
            if (button.gameObject.name == name) return button;
        throw new InvalidOperationException("Missing visible button: " + name);
    }

    private static void Click(Transform root, string name)
    {
        UnityEngine.UI.Button button = FindButton(root, name);
        Require(button.interactable, "Button is interactable: " + name); button.onClick.Invoke();
    }

    private static void SetViewport(int index)
    {
        Vector2Int size = Viewports[index]; GameplayPreferences.SetPortrait(size.y > size.x, false);
        PlayModeWindow.SetCustomRenderingResolution((uint)size.x, (uint)size.y, "Doodlik statistics UI validation");
    }
    private static void RequireViewport() => Require(Screen.width == Viewports[viewport].x && Screen.height == Viewports[viewport].y,
        "Actual GameView matches " + Suffix());
    private static string Suffix() => Viewports[viewport].x + "x" + Viewports[viewport].y;
    private static Rect ScreenRect(RectTransform rect)
    {
        rect.GetWorldCorners(Corners); Canvas canvas = rect.GetComponentInParent<Canvas>();
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 minimum = RectTransformUtility.WorldToScreenPoint(camera, Corners[0]);
        return new Rect(minimum, RectTransformUtility.WorldToScreenPoint(camera, Corners[2]) - minimum);
    }

    private static void Capture(string name)
    {
        Camera camera = Camera.main; RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
        RenderTexture target = new RenderTexture(Screen.width, Screen.height, 24);
        Texture2D texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        List<Canvas> overlays = new List<Canvas>(); List<Camera> cameras = new List<Camera>();
        try
        {
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>())
            {
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                overlays.Add(canvas); cameras.Add(canvas.worldCamera);
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
            }
            camera.targetTexture = target; Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); texture.Apply();
            Directory.CreateDirectory("Logs/StatisticsUI"); File.WriteAllBytes("Logs/StatisticsUI/" + name + ".png", texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
            for (int i = 0; i < overlays.Count; i++) { overlays[i].renderMode = RenderMode.ScreenSpaceOverlay; overlays[i].worldCamera = cameras[i]; }
            Object.DestroyImmediate(texture); Object.DestroyImmediate(target);
        }
    }

    private static void Move(int value) { phase = value; since = EditorApplication.timeSinceStartup; }
    private static void Require(bool value, string message) { checks++; if (!value) throw new InvalidOperationException("Statistics UI: " + message); }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search") && stack.Contains("IndexationOnStartup")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = message;
    }
    private static void Finish(int code)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
        storage?.Dispose(); storage = null;
        foreach (KeyValuePair<string, int?> original in OriginalRecords)
        {
            if (original.Value.HasValue) PlayerPrefs.SetInt(original.Key, original.Value.Value); else PlayerPrefs.DeleteKey(original.Key);
        }
        PlayerPrefs.Save(); OriginalRecords.Clear(); EditorApplication.Exit(code);
    }
}
#endif
