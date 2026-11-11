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

// Real scene/menu/presenters, with controlled height/landing fixtures for UI
// repetition. It is not a human 3000-height playthrough or a route proof.
[InitializeOnLoad]
public static class ExpeditionUiValidation
{
    private const string Key = "Doodlik.ExpeditionUiValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector2Int[] Views = { new Vector2Int(1280, 720), new Vector2Int(720, 1280), new Vector2Int(1600, 720) };
    private static int phase, view, checks, captures;
    private static double since;
    private static string failure, ordinaryPrimary, ordinaryBackup, primary, backup;
    private static IDisposable normalStorage, expeditionStorage;
    private static readonly Dictionary<string, int?> classicRecords = new Dictionary<string, int?>();
    private static GameMenuUI menu;
    private static FrontEndUI front;
    private static ExpeditionUI ui;
    private static ExpeditionRun run;
    private static Player player;
    private static Collider2D safePlatform;
    private static int seededFinds;
    private static string settledRunId;

    static ExpeditionUiValidation() { if (SessionState.GetBool(Key, false)) Register(); }

    public static void Run()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/"))
            throw new InvalidOperationException("Expedition UI validation requires an isolated batch QA copy.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikExpeditionUI_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        RunFlow.RunActive = RunFlow.IsTransitioning = false;
        GameplayPreferences.SetRunKind(GameRunKind.Classic);
        GameplayPreferences.SetMode(false, false, false);
        GameplayPreferences.SetDifficulty(GameDifficulty.Normal);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ExpeditionProgressValidation.Validate();
        SessionState.SetBool(Key, true); phase = view = checks = captures = 0;
        SetView(); Register();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BeforeScene()
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (normalStorage == null) normalStorage = PlayerProgress.UseIsolatedStorageForValidation(out primary, out backup);
        if (expeditionStorage == null) expeditionStorage = ExpeditionProgress.UseIsolatedStorageForValidation(out _, out _);
        AudioListener.volume = 0f; AudioListener.pause = true;
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
            if (EditorApplication.timeSinceStartup - since > 45) throw new TimeoutException("Expedition UI phase " + phase);
            if (Time.frameCount < 30 || RunFlow.IsTransitioning || EditorApplication.timeSinceStartup - since < .5) return;
            Require(EditorUtility.audioMasterMute && AudioListener.volume == 0f, "All UI frames are silent.");
            Bind();
            switch (phase)
            {
                case 0:
                    Require(!RunFlow.RunActive && Time.timeScale == 0f && player.RunKind == GameRunKind.Classic,
                        "Default natural launch is classic and home-paused.");
                    Require(Object.FindObjectsByType<EventSystem>().Length == 1, "Exactly one EventSystem.");
                    SnapshotClassicRecords();
                    PlayerProgress.AddCoins(900); PlayerProgress.Flush();
                    ordinaryPrimary = PlayerPrefs.GetString(primary); ordinaryBackup = PlayerPrefs.GetString(backup);
                    front.ShowModes(); Click(front.transform, "Run kind Expedition"); Next(1); break;
                case 1:
                    Require(GameplayPreferences.RunKind == GameRunKind.Expedition && player.RunKind == GameRunKind.Expedition,
                        "The real mode button reloads the scene with expedition captured by Player.");
                    Require(!RunFlow.RunActive && Time.timeScale == 0f && run != null && !run.HasStarted,
                        "Mode switching never begins a hidden expedition.");
                    // The gameplay owner supplies stronger runtime invariant
                    // fixtures; execute before this presenter's natural attempt.
                    Type runtimeTests = typeof(ExpeditionUiValidation).Assembly.GetType("ExpeditionRuntimeValidation");
                    runtimeTests?.GetMethod("Validate", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                    Type enemyTests = typeof(ExpeditionUiValidation).Assembly.GetType("ExpeditionEnemyValidation");
                    MethodInfo enemyRuntime = enemyTests?.GetMethod("ValidateRuntime", BindingFlags.Public | BindingFlags.Static);
                    Require(enemyRuntime != null, "The actual enemy-contact runtime suite must be present.");
                    enemyRuntime.Invoke(null, null);
                    CheckOrdinaryProgress();
                    var seed = new ExpeditionSession("ui_stash_" + Guid.NewGuid().ToString("N"));
                    seed.TryCollect(ExpeditionFindKind.FireflyJar); seed.TryCollect(ExpeditionFindKind.CloudWool);
                    Require(ExpeditionProgress.TryRecord(seed.Settle(ExpeditionOutcome.Extracted, 750, 750)), "Only isolated expedition stash is seeded.");
                    seededFinds = ExpeditionProgress.TotalCount;
                    view = 0; SetView(); Next(2); break;
                case 2:
                    RequireActualView(); front.ShowModes();
                    UnityEngine.UI.Button controlSelector = (UnityEngine.UI.Button)typeof(FrontEndUI).GetField("modeButton", Private).GetValue(front);
                    Require(controlSelector.navigation.selectOnUp != null &&
                        controlSelector.navigation.selectOnUp.gameObject.name == "Run kind Expedition",
                        "Keyboard navigation can reach the selected journey kind from control settings.");
                    Inspect(Get(front, "modesCard")); Capture("Modes-" + ViewName(), Get(front, "modesCard"));
                    front.ShowExpeditionStash(); Next(3); break;
                case 3:
                    Require(front.IsExpeditionStashVisible && Time.timeScale == 0f, "Stash is a home screen, not active gameplay.");
                    Inspect(Get(front, "stashCard")); Capture("Stash-" + ViewName(), Get(front, "stashCard"));
                    Require(Get(front, "stashCard").Find("Stored find 3") != null,
                        "The material stash includes the new enemy-only rare drop.");
                    RogueUiValidation.ValidateHome(front, menu, Inspect, Capture, ViewName());
                    if (++view < Views.Length) { SetView(); Next(2); break; }
                    view = 0; SetView(); front.ShowHome(); Next(4); break;
                case 4:
                    CheckOrdinaryProgress();
                    // Presenter-only fixtures must not let the real Player
                    // collect a coin or advance shared quests during the
                    // half-second Editor polling interval after menu start.
                    // Keep Spawner enabled: the real menu still certifies its
                    // opening draft before it starts the genuine session.
                    player.GetComponent<Rigidbody2D>().simulated = false;
                    player.enabled = false;
                    menu.StartRunFromHome(); Next(5); break;
                case 5:
                    if (run == null || !run.HasStarted || !RunFlow.IsPlaying) return;
                    Require(run.Owner == player && !run.IsFinished && run.Session.IsEligible,
                        "A natural expedition start owns a live eligible session.");
                    Require(!player.enabled && !player.GetComponent<Rigidbody2D>().simulated,
                        "The real menu starts its session without introducing unrelated natural coin/quest writes into presenter fixtures.");
                    FreezeSceneForPresenterFixtures();
                    CheckOrdinaryProgress();
                    TMP_Text startHint = GetLabel(ui, "hudStatus");
                    startHint.ForceMeshUpdate();
                    Require(startHint.text == run.StatusText && startHint.text.Contains("сверху"),
                        "The genuine run-start HUD explains the top-stomp before pickup feedback replaces it.");
                    Require(!startHint.isTextOverflowing && !startHint.isTextTruncated && Contains(Screen.safeArea, ScreenRect(startHint.rectTransform)),
                        "The actual initial combat hint fits its HUD rectangle and the safe area.");
                    Require(run.TryCollect(ExpeditionFindKind.FireflyJar) && run.TryCollect(ExpeditionFindKind.CloudWool) &&
                        run.TryCollect(ExpeditionFindKind.StarShard) && run.Session.Count == 3, "Three genuine runtime collection commands fill the bag.");
                    Require(!run.TryCollect(ExpeditionFindKind.FireflyJar) && run.Session.Count == 3, "A full bag never silently replaces finds.");
                    Next(6); break;
                case 6:
                    RequireActualView();
                    if (run.Session.Slots[1] == ExpeditionFindKind.None)
                        Require(run.TryCollect(ExpeditionFindKind.CloudWool),
                            "Each viewport refills the previously discarded test slot before testing Discard again.");
                    ui.OpenBag();
                    Require(ui.IsBagVisible && ExpeditionUI.BlocksGameplayInput && Time.timeScale == 0f, "Bag pauses and blocks gameplay.");
                    ExpeditionFindKind chosen = run.Session.Slots[2];
                    Click(Get(ui, "card").Find("Bag slot 2"), "Защитить");
                    Require(run.Session.Slots[0] == chosen, "The visible Protect button swaps the selected real find into slot zero.");
                    Click(Get(ui, "card").Find("Bag slot 1"), "Выбросить");
                    Require(run.Session.Slots[1] == ExpeditionFindKind.None && run.Session.Count == 2, "The visible Discard button frees only its chosen slot.");
                    foreach (UnityEngine.UI.Button action in Get(ui, "card").Find("Bag slot 1").GetComponentsInChildren<UnityEngine.UI.Button>())
                    {
                        Require(!action.IsInteractable() && action.colors.fadeDuration == 0f,
                            "An empty slot disables both actions instantly without a misleading amber fade.");
                        Color expected = action.colors.disabledColor * action.colors.colorMultiplier;
                        Color actual = action.targetGraphic.canvasRenderer.GetColor();
                        Require(Mathf.Abs(expected.r - actual.r) < .01f && Mathf.Abs(expected.a - actual.a) < .01f,
                            "Disabled slot actions already render their disabled state in the same frame.");
                    }
                    Inspect(Get(ui, "card")); Capture("Bag-" + ViewName(), Get(ui, "card"));
                    CheckOrdinaryProgress(); ui.Close();
                    Require(!ExpeditionUI.IsOpen && Time.timeScale > 0f, "Bag close resumes the previous live game.");
                    Next(7); break;
                case 7:
                    OpenControlledRest((view + 1) * ExpeditionRun.RestInterval);
                    Require(ui.IsRestVisible && run.IsResting && Time.timeScale == 0f, "The audited landing callback opens a paused rest choice.");
                    Next(8); break;
                case 8:
                    Inspect(Get(ui, "card")); Capture("Rest-" + ViewName(), Get(ui, "card"));
                    Require(run.NeedsPerkChoice && !((UnityEngine.UI.Button)typeof(ExpeditionUI).GetField("proceed", Private).GetValue(ui)).interactable,
                        "Rest continuation is locked until one real offered talent is chosen.");
                    Click(ui.transform, "Open expedition talents");
                    Require(ui.IsPerkChoiceVisible && Time.timeScale == 0f, "The talent subpage stays paused.");
                    foreach (UnityEngine.UI.Button talent in Get(ui, "card").GetComponentsInChildren<UnityEngine.UI.Button>())
                        if (talent.gameObject.name.StartsWith("Choose expedition perk ", StringComparison.Ordinal))
                            Require(talent.interactable && talent.colors.fadeDuration == 0f && talent.targetGraphic.canvasRenderer.GetColor().r > .85f,
                                "Offered talents look active immediately rather than briefly retaining their disabled appearance.");
                    Inspect(Get(ui, "card")); Capture("Talents-" + ViewName(), Get(ui, "card"));
                    Click(ui.transform, "Choose expedition perk 0");
                    Require(ui.IsRestVisible && !run.NeedsPerkChoice && Time.timeScale == 0f,
                        "A real choice applies once and returns to the still-paused rest.");
                    Click(ui.transform, "Продолжить подъём");
                    Require(!run.IsResting && !ExpeditionUI.IsOpen && Time.timeScale > 0f, "Continue consumes the rest and resumes the same attempt.");
                    Require(ExpeditionProgress.TotalCount == seededFinds, "Continuing does not bank the backpack for free.");
                    ui.ShowResult(PresentationReceipt(ExpeditionOutcome.Lost), () => Time.timeScale = 1f, true);
                    Next(9); break;
                case 9:
                    Inspect(Get(ui, "card")); Capture("Loss-Presenter-" + ViewName(), Get(ui, "card"));
                    Click(ui.transform, "Домой");
                    Require(!ExpeditionUI.IsOpen && !run.IsFinished, "Presentation-only result does not secretly settle the live session.");
                    ui.ShowResult(PresentationReceipt(ExpeditionOutcome.Extracted), () => Time.timeScale = 1f, false);
                    Next(10); break;
                case 10:
                    Inspect(Get(ui, "card"));
                    Require(GetLabel(ui, "title").text != "Добыча дома!" && GetLabel(ui, "subtitle").text.Contains("не записаны"),
                        "An uncommitted receipt never claims that finds reached the stash.");
                    foreach (TMP_Text label in Get(ui, "card").GetComponentsInChildren<TMP_Text>())
                        Require(!label.text.Contains("Сохранено в кладовой"), "Uncommitted item rows never promise a saved item.");
                    Capture("Unavailable-Presenter-" + ViewName(), Get(ui, "card")); Click(ui.transform, "Домой");
                    if (++view < Views.Length) { SetView(); Next(6); break; }
                    // Closing a presenter deliberately consumes this frame;
                    // a fresh landing cannot open another modal until later.
                    Next(13); break;
                case 13:
                    OpenControlledRest(4 * ExpeditionRun.RestInterval);
                    Require(ui.IsRestVisible && run.IsResting, "A later audited landing opens the final extraction rest.");
                    Next(11); break;
                case 11:
                    settledRunId = run.Session.RunId;
                    Require(run.NeedsPerkChoice, "The final extraction rest still offers an optional talent.");
                    Click(ui.transform, "Забрать добычу и домой");
                    Require(run.IsFinished && run.Result.Outcome == ExpeditionOutcome.Extracted && run.RewardsSaved && ui.IsResultVisible,
                        "The real extraction button settles and commits the actual live session once.");
                    Require(ExpeditionProgress.TotalCount == seededFinds + 2, "Extraction adds exactly the two carried finds.");
                    Inspect(Get(ui, "card")); Capture("Actual-Extraction-Result", Get(ui, "card"));
                    CheckOrdinaryProgress(); Click(ui.transform, "Домой"); Next(12); break;
                case 12:
                    Require(!RunFlow.RunActive && Time.timeScale == 0f && front.IsVisible && !ExpeditionUI.IsOpen,
                        "The real result continuation returns to a paused, fresh home scene.");
                    ExpeditionProgress.Reload();
                    Require(ExpeditionProgress.LastResult != null && ExpeditionProgress.LastResult.RunId == settledRunId &&
                        ExpeditionProgress.TotalCount == seededFinds + 2, "Reload keeps the exact receipt and stash amount.");
                    CheckOrdinaryProgress(); CheckClassicRecords(); front.ShowExpeditionStash();
                    Inspect(Get(front, "stashCard")); Capture("Actual-Persisted-Stash", Get(front, "stashCard"));
                    Debug.Log("EXPEDITION_UI_PASS checks=" + checks + " screenshots=" + captures +
                        " actualViewports=3 kindSelection=true bagButtons=true restChoices=true extraction=true durableReceipt=true classicUntouched=true silent=true controlledLandingFixtures=true");
                    Finish(0); break;
            }
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    private static void Bind()
    {
        menu = Object.FindAnyObjectByType<GameMenuUI>(); front = menu != null ? menu.GetComponent<FrontEndUI>() : null;
        ui = Object.FindAnyObjectByType<ExpeditionUI>(); player = Object.FindAnyObjectByType<Player>(); run = ExpeditionRun.Active;
        Require(menu != null && front != null && ui != null && player != null, "Natural scene/menu/expedition presenters exist.");
    }

    private static void FreezeSceneForPresenterFixtures()
    {
        player.GetComponent<Rigidbody2D>().simulated = false; player.enabled = false;
        Spawner spawner = Object.FindAnyObjectByType<Spawner>(); if (spawner != null) spawner.enabled = false;
        CameraFollow follow = Object.FindAnyObjectByType<CameraFollow>(); if (follow != null) follow.enabled = false;
        foreach (GameObject platform in GameObject.FindGameObjectsWithTag("Platform"))
        {
            Collider2D collider = platform.GetComponent<Collider2D>();
            if (ExpeditionRun.SafeRestSurface(collider)) { safePlatform = collider; break; }
        }
        Require(safePlatform != null, "The real world contains a safe static landing surface.");
    }

    private static void OpenControlledRest(int height)
    {
        Set(player, "reachedHeight", height); Set(player, "score", height);
        run.RecordLanding(safePlatform);
        Set(player, "<LastHazardAuditFrame>k__BackingField", Time.frameCount);
        run.AfterHazardAudit();
    }

    private static ExpeditionResult PresentationReceipt(ExpeditionOutcome outcome)
    {
        var session = new ExpeditionSession("ui_presenter_" + Guid.NewGuid().ToString("N"));
        session.TryCollect(ExpeditionFindKind.FireflyJar); session.TryCollect(ExpeditionFindKind.CloudWool); session.TryCollect(ExpeditionFindKind.StarShard);
        return session.Settle(outcome, 1800, 1800);
    }

    private static void SnapshotClassicRecords()
    {
        classicRecords.Clear();
        foreach (string key in RunRecords.AllKeys) classicRecords[key] = PlayerPrefs.HasKey(key) ? (int?)PlayerPrefs.GetInt(key) : null;
    }
    private static void CheckClassicRecords()
    {
        foreach (var saved in classicRecords)
            Require(saved.Value.HasValue ? PlayerPrefs.HasKey(saved.Key) && PlayerPrefs.GetInt(saved.Key) == saved.Value.Value :
                !PlayerPrefs.HasKey(saved.Key), "Classic score/height key unchanged: " + saved.Key);
    }
    private static void CheckOrdinaryProgress()
    {
        bool unchanged = PlayerPrefs.GetString(primary) == ordinaryPrimary &&
            PlayerPrefs.GetString(backup) == ordinaryBackup && PlayerProgress.Coins == 900;
        if (!unchanged)
            Debug.Log("EXPEDITION_UI_ORDINARY_PROBE phase=" + phase + " coins=" + PlayerProgress.Coins +
                " baseline=" + ordinaryPrimary + " current=" + PlayerPrefs.GetString(primary) +
                " backup=" + PlayerPrefs.GetString(backup));
        Require(unchanged, "Ordinary primary/backup wallet/wardrobe stay byte-identical throughout expedition UI.");
    }
    private static RectTransform Get(object owner, string name) => (RectTransform)owner.GetType().GetField(name, Private).GetValue(owner);
    private static TMP_Text GetLabel(object owner, string name) => (TMP_Text)owner.GetType().GetField(name, Private).GetValue(owner);
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
    private static string ViewName() => Views[view].x + "x" + Views[view].y;
    private static void RequireActualView() => Require(Screen.width == Views[view].x && Screen.height == Views[view].y, "Actual GameView resolution " + ViewName());
    private static void SetView()
    {
        GameplayPreferences.SetPortrait(Views[view].y > Views[view].x, false);
        PlayModeWindow.SetCustomRenderingResolution((uint)Views[view].x, (uint)Views[view].y, "Doodlik expedition UI QA");
        if (Camera.main != null) Camera.main.ResetAspect();
    }
    private static void Click(Transform root, string name)
    {
        foreach (UnityEngine.UI.Button button in root.GetComponentsInChildren<UnityEngine.UI.Button>())
            if (button.gameObject.name == name) { Require(button.interactable, "Interactable button " + name); button.onClick.Invoke(); return; }
        throw new InvalidOperationException("Missing visible button " + name);
    }
    private static void Inspect(RectTransform card)
    {
        Canvas.ForceUpdateCanvases(); Rect bounds = ScreenRect(card);
        Require(Contains(Screen.safeArea, bounds), "Card stays in safe area.");
        Require(Mathf.Abs(bounds.width / bounds.height - card.rect.width / card.rect.height) < .02f, "Card is projected uniformly.");
        foreach (TMP_Text text in card.GetComponentsInChildren<TMP_Text>())
        {
            text.ForceMeshUpdate(); Require(!text.isTextOverflowing && !text.isTextTruncated, "No text overflow: " + text.text);
            Require(Contains(bounds, ScreenRect(text.rectTransform)), "Text rectangle stays inside the card.");
        }
        foreach (UnityEngine.UI.Selectable control in card.GetComponentsInChildren<UnityEngine.UI.Selectable>())
            Require(Contains(bounds, ScreenRect((RectTransform)control.transform)) && control.targetGraphic != null && control.targetGraphic.raycastTarget,
                "Input stays in card and participates in raycast.");
    }
    private static Rect ScreenRect(RectTransform rect, Camera camera = null)
    {
        Vector3[] corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        return new Rect(min, max - min);
    }
    private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 2f && inner.xMax <= outer.xMax + 2f &&
        inner.yMin >= outer.yMin - 2f && inner.yMax <= outer.yMax + 2f;
    private static void Capture(string name, RectTransform card)
    {
        Camera camera = Camera.main; float priorAspect = camera.aspect;
        RenderTexture priorTarget = camera.targetTexture, priorActive = RenderTexture.active;
        RenderTexture target = new RenderTexture(Screen.width, Screen.height, 24);
        Texture2D pixels = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        var canvases = new List<Canvas>(); var cameras = new List<Camera>();
        try
        {
            camera.aspect = (float)Screen.width / Screen.height; camera.targetTexture = target;
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>())
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                canvases.Add(canvas); cameras.Add(canvas.worldCamera);
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
            }
            Canvas.ForceUpdateCanvases(); Rect projected = ScreenRect(card, camera);
            Require(Mathf.Abs(projected.width / projected.height - card.rect.width / card.rect.height) < .02f, "Captured projection preserves the real layout.");
            camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); pixels.Apply();
            Directory.CreateDirectory("Logs/ExpeditionUI"); File.WriteAllBytes("Logs/ExpeditionUI/" + name + ".png", pixels.EncodeToPNG()); captures++;
        }
        finally
        {
            camera.targetTexture = priorTarget; camera.aspect = priorAspect; RenderTexture.active = priorActive;
            for (int i = 0; i < canvases.Count; i++) { canvases[i].renderMode = RenderMode.ScreenSpaceOverlay; canvases[i].worldCamera = cameras[i]; }
            Object.DestroyImmediate(pixels); Object.DestroyImmediate(target);
        }
    }
    private static void Next(int next) { phase = next; since = EditorApplication.timeSinceStartup; }
    private static void Require(bool value, string message) { checks++; if (!value) throw new InvalidOperationException("Expedition UI: " + message); }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = message;
    }
    private static void Finish(int code)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
        // Dispose only our test-key scopes; never copy data to production keys.
        expeditionStorage?.Dispose(); expeditionStorage = null;
        normalStorage?.Dispose(); normalStorage = null;
        EditorApplication.Exit(code);
    }
}
#endif
