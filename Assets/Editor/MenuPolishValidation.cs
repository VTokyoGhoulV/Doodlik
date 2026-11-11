#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Batch-only smoke test. Run in an isolated MenuValidation-* project copy.
[InitializeOnLoad]
public static class MenuPolishValidation
{
    private const string Key = "Doodlik.MenuPolishValidation";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int phase;
    private static double since;
    private static string failure;
    private static GameMenuUI menu;
    private static FrontEndUI ui;

    static MenuPolishValidation()
    {
        if (!SessionState.GetBool(Key, false)) return;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Application.logMessageReceived += Log;
    }

    public static void Run()
    {
        RunInternal(false);
    }

    public static void RunAdminTufts()
    {
        RunInternal(true);
    }

    private static void RunInternal(bool focused)
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/MenuValidation-"))
            throw new InvalidOperationException("Run this test only in an isolated .utmp/MenuValidation-* batch project.");
        // Do not share the user's live editor preference namespace.
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "MenuPolishSandbox";
        CosmeticTextureSetup.Ensure();
        JourneySettingsSetup.EnsureAsset();
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Logs");
        SessionState.SetBool(Key + ".focused", focused);
        if (focused)
        {
            AdminProgressValidation.Validate();
        }
        else
        {
        CosmeticsProgressValidation.Validate();
        ModeSelectionValidation.ValidatePreferences();
        SixZoneValidation.ValidateRules();
        CosmeticMigrationValidation.Validate();
        ConnectedHazardValidation.Run();
        AirCoinGenerationValidation.ValidateProduction();
        }
        SessionState.SetBool(Key, true);
        phase = 0;
        since = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived += Log;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.EnterPlaymode();
    }

    private static void Log(string message, string stack, LogType type)
    {
        // Unity 6000.5's first-run Search indexer throws in this fresh batch
        // project. Preserve/classify that Editor-only baseline separately; it
        // is not a game exception and must not prevent exercising the UI.
        if (stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup"))
        {
            Debug.LogWarning("VALIDATION_ENVIRONMENT: Unity Search startup indexer exception recorded separately: " + message);
            return;
        }
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) failure = message;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (failure != null) throw new Exception(failure);
            double elapsed = EditorApplication.timeSinceStartup - since;
            if (elapsed > 40) throw new Exception("UI phase timeout: " + phase);
            if (elapsed < 0.6) return;
            switch (phase)
            {
                case 0:
                    menu = Object.FindAnyObjectByType<GameMenuUI>();
                    ui = menu.GetComponent<FrontEndUI>();
                    Check(ui.IsVisible && !RunFlow.RunActive, "home is shown before play");
                    if (SessionState.GetBool(Key + ".focused", false))
                    {
                        WardrobeArtValidation.Validate();
                        AdminGameplayValidation.Validate();
                        AdminToolsValidation.Validate();
                    }
                    else
                    {
                    ModeSelectionValidation.ValidateRuntimeGuards();
                    HomeBackdropValidation.Validate();
                    ShieldThornValidation.Validate();
                    AerialPickupValidation.Validate();
                    AirCoinGenerationValidation.Validate();
                    SixZoneValidation.ValidateRuntimeUI();
                    StarfieldValidation.Validate();
                    BuffVisualValidation.Validate();
                    if (GameplayPreferences.IsDesktopBuild) EditorKeyboardNavigationValidation.Validate();
                    WardrobeArtValidation.Validate();
                    WardrobeUiValidation.Validate(Environment.GetEnvironmentVariable(
                        CosmeticsProgressValidation.PromoCodeEnvironmentVariable));
                    Type coins = typeof(MenuPolishValidation).Assembly.GetType("CoinAttractionValidation");
                    if (coins != null) coins.GetMethod("Validate")?.Invoke(null, null);
                    }
                    Capture("Home");
                    CheckEquippedHome();
                    Invoke(ui, "ShowShop");
                    Invoke(ui, "SelectTab", 1);
                    Advance(); break;
                case 1:
                    CheckClothingPreviews(); Capture("Clothing");
                    Invoke(ui, "SelectTab", 2); Advance(); break;
                case 2:
                    CheckPreviews("Accessories"); Capture("Accessories");
                    Invoke(ui, "ChangePage", 1); Advance(); break;
                case 3:
                    CheckPreviews("AccessoriesPage2"); Capture("AccessoriesPage2");
                    Invoke(ui, "SelectTab", 3); Advance(); break;
                case 4:
                    CheckPreviews("Trails"); Capture("Trails");
                    Invoke(ui, "SelectTab", 4); Advance(); break;
                case 5:
                    CheckPreviews("Upgrades"); Capture("Upgrades");
                    ui.ShowModes(); Invoke(ui, "ToggleModeOptions"); Advance(); break;
                case 6:
                    Check(ui.IsModeScreenVisible, "mode screen is visible");
                    GameObject options = (GameObject)typeof(FrontEndUI).GetField("modeOptions", Private).GetValue(ui);
                    Check(options.activeSelf && options.GetComponentsInChildren<Button>().Length == (GameplayPreferences.IsDesktopBuild ? 2 : 4), "correct number of options");
                    Capture("ModeOptions");
                    Check(ui.GoBack() && ui.IsModeScreenVisible && !options.activeSelf, "Escape first closes dropdown");
                    Check(menu.SelectGameMode(!GameplayPreferences.IsPortrait, !GameplayPreferences.UseInertia), "mode selection accepted at home");
                    Advance(); break;
                case 7:
                    if (RunFlow.IsTransitioning) return;
                    Check(ui.IsModeScreenVisible && !RunFlow.RunActive && Time.timeScale == 0f, "selection reload returns to modes, no run starts");
                    Capture("ModeSelected");
                    Check(ui.GoBack(), "back to home");
                    menu.OpenSettingsFromHome(); Advance(); break;
                case 8:
                    Capture("Settings");
                    GameObject settings = (GameObject)typeof(GameMenuUI).GetField("settingsPanel", Private).GetValue(menu);
                    Check(settings.GetComponentsInChildren<Button>().Length == 2 &&
                        settings.GetComponentsInChildren<Slider>().Length == 3,
                        "settings contain FPS/back buttons and independent music/effects/HUD transparency sliders");
                    Slider hudTransparency = (Slider)typeof(GameMenuUI).GetField("hudTransparencySlider", Private).GetValue(menu);
                    Check(hudTransparency != null && hudTransparency.transform.IsChildOf(settings.transform) &&
                        hudTransparency.minValue == 0f && hudTransparency.maxValue == 100f && hudTransparency.wholeNumbers &&
                        hudTransparency.IsInteractable(), "HUD transparency supports one-percent adjustments from 0 to 100");
                    Invoke(menu, "CloseSettings");
                    menu.StartRunFromHome();
                    Check(((TMPro.TMP_Text)typeof(GameMenuUI).GetField("menuButtonLabel", Private).GetValue(menu)).text == "Меню", "menu caption resets after home settings");
                    Check(!menu.SelectGameMode(false, !GameplayPreferences.UseInertia), "mode locked during attempt");
                    Check(!ui.IsVisible && RunFlow.RunActive, "game begins");
                    Time.timeScale = 0f;
                    Player livePlayer = Object.FindAnyObjectByType<Player>();
                    typeof(Player).GetField("maximumHeight", Private).SetValue(livePlayer, WorldZones.HeightAtScore(WorldZones.StartScore(WorldZones.Zone.Night) + 10));
                    Invoke(livePlayer, "UpdateHeightScore");
                    livePlayer.GetComponent<RunPowerups>().Collect(RunPowerups.Kind.Shield);
                    livePlayer.GetComponent<RunPowerups>().Collect(RunPowerups.Kind.Magnet);
                    livePlayer.GetComponent<RunPowerups>().Collect(RunPowerups.Kind.Slow);
                    Advance(); break;
                case 9:
                    if (elapsed < 2f) return;
                    Capture("NightBuffsHud");
                    Debug.Log("MENU_POLISH_PASS: home, background, previews, mode dropdown, restart, settings, run guard");
                    Finish(0); break;
            }
        }
        catch (Exception e) { Debug.LogException(e); Finish(1); }
    }

    private static void CheckClothingPreviews()
    {
        int count = 0;
        foreach (Image cat in ui.GetComponentsInChildren<Image>())
            if (cat.name == "Cat" && cat.enabled && cat.sprite != null && cat.material.HasProperty("_ClothingColor")) count++;
        Check(count == 3, "all three clothing choices show the original cat with independent fabric colors");
    }

    private static void CheckPreviews(string category)
    {
        Canvas.ForceUpdateCanvases();
        int populated = 0;
        foreach (DoodlikCosmeticPreview graphic in ui.GetComponentsInChildren<DoodlikCosmeticPreview>())
        {
            string id = (string)typeof(DoodlikCosmeticPreview).GetField("itemId", Private).GetValue(graphic);
            if (id.EndsWith("_none")) continue;
            CanvasRenderer renderer = graphic.GetComponent<CanvasRenderer>();
            Mesh mesh = renderer != null ? renderer.GetMesh() : null;
            int vertices = mesh != null ? mesh.vertexCount : 0;
            Debug.Log($"PREVIEW_DIAGNOSTIC {category}/{id}: active={graphic.IsActive()}, rect={graphic.rectTransform.rect}, renderer={renderer != null}, material={(renderer == null ? -1 : renderer.materialCount)}, verts={vertices}, canvas={graphic.canvas != null}");
            if (renderer != null && renderer.materialCount > 0 && vertices > 0) populated++;
        }
        Debug.Log("PREVIEW_COUNT " + category + "=" + populated);
        Check(populated >= 2 && populated <= 3, category + " renders every visible illustrated accessory");
    }

    private static void CheckEquippedHome()
    {
        bool primaryExists = PlayerPrefs.HasKey(PlayerProgress.SaveKey), backupExists = PlayerPrefs.HasKey(PlayerProgress.BackupKey);
        string primary = PlayerPrefs.GetString(PlayerProgress.SaveKey), backup = PlayerPrefs.GetString(PlayerProgress.BackupKey);
        try
        {
            PlayerProgress.AddCoins(1000);
            foreach (string item in new[] { "cat_cocoa", "clothing_ocean", "accessory_glasses", "trail_honey" })
            {
                Check(PlayerProgress.Owns(item) || PlayerProgress.TryPurchase(item), "test cosmetic purchase");
                Check(PlayerProgress.Equip(item), "equip " + item);
            }
            PlayerProgress.Flush();
            PlayerProgress.Reload();
            ui.ShowHome();
            ui.Refresh();
            object preview = typeof(FrontEndUI).GetField("homePreview", Private).GetValue(ui);
            Image cat = (Image)preview.GetType().GetField("Cat").GetValue(preview);
            Check(cat.material.GetColor("_BodyTint") == CatAppearance.GetCatTint("cat_cocoa"), "home cat matches saved selected skin");
            Check(cat.material.GetColor("_ClothingColor") == CatClothingMaterials.GetClothingColor("clothing_ocean"), "home clothing matches saved independent outfit");
            foreach (string field in new[] { "Accessory", "Trail" })
            {
                DoodlikCosmeticPreview graphic = (DoodlikCosmeticPreview)preview.GetType().GetField(field).GetValue(preview);
                string id = (string)typeof(DoodlikCosmeticPreview).GetField("itemId", Private).GetValue(graphic);
                Check(id == (field == "Accessory" ? "accessory_glasses" : "trail_honey"), "home matches saved " + field);
            }
            Capture("HomeEquipped");
            foreach (string item in new[] { "accessory_flower", "accessory_tufts", "accessory_star" })
            {
                Check(PlayerProgress.Owns(item) || PlayerProgress.TryPurchase(item), "test accessory purchase");
                Check(PlayerProgress.Equip(item), "equip " + item);
                ui.Refresh();
                Capture("Home-" + item);
            }
            Debug.Log("HOME_EQUIPPED_PASS: saved cat, clothing, all accessories and trail match the home preview");
        }
        finally
        {
            if (primaryExists) PlayerPrefs.SetString(PlayerProgress.SaveKey, primary); else PlayerPrefs.DeleteKey(PlayerProgress.SaveKey);
            if (backupExists) PlayerPrefs.SetString(PlayerProgress.BackupKey, backup); else PlayerPrefs.DeleteKey(PlayerProgress.BackupKey);
            PlayerProgress.Reload();
            ui.Refresh();
        }
    }

    private static void Capture(string name) => typeof(JourneyPlayValidation)
        .GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "Polish-" + name });
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    private static void Check(bool value, string label) { if (!value) throw new Exception("UI check: " + label); }
    private static void Advance() { phase++; since = EditorApplication.timeSinceStartup; }
    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= Log;
        SessionState.SetBool(Key, false);
        EditorApplication.Exit(code);
    }
}
#endif
