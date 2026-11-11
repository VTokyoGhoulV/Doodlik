#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Batch-only Play Mode diagnostic. This exercises live generation and checks
// launch reservations; it does not simulate a human-controlled bush bounce.
// Do not pass -quit: this method enters Play Mode and exits the isolated Editor
// after the last runtime frame.
// -batchmode -nographics -buildTarget Android
// -executeMethod HazardRuntimeDistributionValidation.Run
[InitializeOnLoad]
public static class HazardRuntimeDistributionValidation
{
    private const string ActiveKey = "Doodlik.RuntimeHazardCheck.Active";
    private const string SeenPlayKey = "Doodlik.RuntimeHazardCheck.SeenPlay";
    private const string PreferencesKey = "Doodlik.RuntimeHazardCheck.Preferences";
    private const string OldVolumeKey = "Doodlik.RuntimeHazardCheck.Volume";
    private const string SpeedKey = "Doodlik.RuntimeHazardCheck.Speed";
    private static float cameraSpeed = 18f;
    private const int DensityEndScore = 1050;
    private const int ChainEndScore = 1300;
    private const double TimeoutSeconds = 230;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Serializable]
    private sealed class IntPreference
    {
        public string key;
        public bool exists;
        public int value;
    }

    [Serializable]
    private sealed class StringPreference
    {
        public string key;
        public bool exists;
        public string value;
    }

    [Serializable]
    private sealed class SavedPreferences
    {
        public List<IntPreference> ints = new List<IntPreference>();
        public List<StringPreference> strings = new List<StringPreference>();
    }

    private static readonly FieldInfo GeneratorField = typeof(Spawner)
        .GetField("hazardSpawner", Private);
    private static readonly FieldInfo RoutesField = typeof(HazardSpawner)
        .GetField("steps", Private);

    private static double started;
    private static int phase;
    private static int lastFrame = -1;
    private static int observedFrames;
    private static int settledFrames;
    private static float cameraY;
    private static float largestCameraStep;
    private static bool setupChangedAspect;
    private static Spawner spawner;
    private static HazardSpawner generator;
    private static Camera camera;
    private static readonly HashSet<DoodlikHazard> countedHazards = new HashSet<DoodlikHazard>();
    private static readonly HashSet<Bush> countedBushes = new HashSet<Bush>();
    private static readonly HashSet<int> bushPlatforms = new HashSet<int>();
    private static readonly HashSet<long> bushChains = new HashSet<long>();
    private static readonly int[] scoreBins = new int[5];
    private static int horizontal, vertical, flying, patches, meadowFlying;
    private static int firstThousandBushes, allBushes, clearanceChecks;

    static HazardRuntimeDistributionValidation()
    {
        cameraSpeed = SessionState.GetFloat(SpeedKey, 18f);
        if (!SessionState.GetBool(ActiveKey, false)) return;
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (SessionState.GetBool(ActiveKey, false) &&
            state == PlayModeStateChange.EnteredPlayMode)
        {
            SessionState.SetBool(SeenPlayKey, true);
            // When the callback precedes Spawner.Start, this also avoids an
            // artificial aspect-change invalidation of the opening rows.
            Camera playCamera = Camera.main;
            if (playCamera != null) playCamera.aspect = 9f / 20f;
            int difficultySeed = SessionState.GetInt("Doodlik.DifficultyValidation.HazardSeed", -1);
            if (difficultySeed >= 0)
            {
                UnityEngine.Random.InitState(difficultySeed);
                Spawner.ValidationPickupSeed = (uint)difficultySeed;
            }
            AudioListener.volume = 0f;
        }
    }

    public static void Run()
    {
        cameraSpeed = SessionState.GetFloat(SpeedKey, 18f);
        if (!Application.isBatchMode ||
            EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Debug.LogError("Runtime hazard diagnostic requires a dedicated batch Editor with Android active. No project settings were changed.");
            if (Application.isBatchMode) EditorApplication.Exit(2);
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            SessionState.GetBool(ActiveKey, false))
            throw new InvalidOperationException("Runtime hazard diagnostic already active");

        SavedPreferences saved = new SavedPreferences();
        var integerKeys = new HashSet<string>(RunRecords.AllKeys)
        {
            Player.HighScoreKey, GameplayPreferences.OrientationKey,
            GameplayPreferences.InertiaKey, GameplayPreferences.DifficultyKey
        };
        foreach (string key in integerKeys)
            saved.ints.Add(new IntPreference { key = key,
                exists = PlayerPrefs.HasKey(key), value = PlayerPrefs.GetInt(key) });
        foreach (string key in new[] { PlayerProgress.SaveKey, PlayerProgress.BackupKey })
            saved.strings.Add(new StringPreference { key = key,
                exists = PlayerPrefs.HasKey(key), value = PlayerPrefs.GetString(key) });
        SessionState.SetString(PreferencesKey, JsonUtility.ToJson(saved));
        SessionState.SetFloat(OldVolumeKey, AudioListener.volume);
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetBool(SeenPlayKey, false);
        PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, 1);
        PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, 1);
        PlayerPrefs.Save();
        AudioListener.volume = 0f;
        started = EditorApplication.timeSinceStartup;
        phase = 0;
        ResetObservations();
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    public static void RunModerate()
    {
        SessionState.SetFloat(SpeedKey, 9f);
        Run();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(ActiveKey, false)) return;
        try
        {
            if (EditorApplication.timeSinceStartup - started > TimeoutSeconds)
                throw new InvalidOperationException("Play Mode hazard diagnostic timed out in phase " + phase);
            if (!EditorApplication.isPlaying)
            {
                if (SessionState.GetBool(SeenPlayKey, false) &&
                    !EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Play Mode ended before the runtime diagnostic completed");
                return;
            }
            SessionState.SetBool(SeenPlayKey, true);
            if (EditorApplication.isCompiling) return;
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;

            if (phase == 0)
            {
                Prepare();
                return;
            }

            // One advance per actual game frame. No generator method is called
            // by this test: Spawner.Update and its sliced coroutines run normally.
            float frameSeconds = Mathf.Clamp(Time.unscaledDeltaTime, 1f / 120f, 1f / 30f);
            float nextY = cameraY + cameraSpeed * frameSeconds;
            largestCameraStep = Mathf.Max(largestCameraStep, nextY - cameraY);
            cameraY = nextY;
            camera.transform.position = new Vector3(0f, cameraY,
                camera.transform.position.z);
            observedFrames++;
            ObserveRuntimeGeometry();

            int cameraScore = WorldZones.ScoreAtHeight(cameraY);
            if (phase == 1 && cameraScore >= DensityEndScore)
            {
                if (observedFrames < 100)
                    throw new InvalidOperationException("Ascent sampled fewer than 100 real game frames");
                Debug.Log($"HAZARD_RUNTIME_DENSITY_DIAGNOSTIC total={FirstThousandTotal} " +
                    $"horizontal={horizontal} vertical={vertical} flying={flying} " +
                    $"meadowFlying={meadowFlying} patches={patches} " +
                    $"bushes={firstThousandBushes} bins={string.Join(",", scoreBins)} " +
                    $"frames={observedFrames} cameraScore={cameraScore} " +
                    $"cameraAspect={camera.aspect:F3} setupChangedAspect={setupChangedAspect} " +
                    $"speed={cameraSpeed:F1}");
                // A separate stress tail seeks actual consecutive bushes. It
                // does not contribute to the 0-999-point density diagnostic.
                spawner.bushSpawnChance = 1f;
                phase = 2;
            }
            if (phase == 2 && cameraScore >= ChainEndScore)
            {
                phase = 3;
                settledFrames = 0;
            }
            if (phase == 3 && ++settledFrames >= 30)
            {
                if (bushChains.Count == 0)
                    throw new InvalidOperationException(
                        "No consecutive bush pair occurred during the forced-bush tail; chain safety remains unverified");
                Debug.Log($"HAZARD_RUNTIME_CLEARANCE_PASS frames={observedFrames} " +
                    $"first1000Total={FirstThousandTotal} " +
                    $"chains={bushChains.Count} bushes={allBushes} " +
                    $"clearanceChecks={clearanceChecks} " +
                    $"maximumCameraStep={largestCameraStep:F3}");
                Finish(0);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static int FirstThousandTotal => horizontal + vertical + flying + patches;

    private static void Prepare()
    {
        spawner = Object.FindAnyObjectByType<Spawner>();
        camera = Camera.main;
        Player player = Object.FindAnyObjectByType<Player>();
        GameMenuUI menu = Object.FindAnyObjectByType<GameMenuUI>();
        if (spawner == null || camera == null || player == null || menu == null)
            return;
        generator = (HazardSpawner)GeneratorField.GetValue(spawner);
        if (generator == null) return; // Spawner.Start has not completed yet.

        AudioListener.volume = 0f;
        PlayerProgress.BeginAdminSandbox();
        setupChangedAspect = Mathf.Abs(camera.aspect - 9f / 20f) > 0.01f;
        camera.aspect = 9f / 20f;
        CameraFollow follower = Object.FindAnyObjectByType<CameraFollow>();
        if (follower != null) follower.enabled = false;
        player.enabled = false;
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null) body.simulated = false;
        menu.StartRunFromHome();
        if (!RunFlow.IsPlaying || !GameplayPreferences.IsPortrait ||
            !GameplayPreferences.UseInertia || Mathf.Abs(camera.aspect - 0.45f) > 0.01f)
            throw new InvalidOperationException("Portrait/inertia Play Mode setup failed");
        cameraY = camera.transform.position.y;
        phase = 1;
        Debug.Log($"HAZARD_RUNTIME_START portraitInertia=true cameraAspect={camera.aspect:F3} " +
            $"platformBushChance={spawner.bushSpawnChance:F2} cameraSpeed={cameraSpeed:F1}");
    }

    private static void ObserveRuntimeGeometry()
    {
        foreach (Bush bush in Object.FindObjectsByType<Bush>(FindObjectsSortMode.None))
        {
            if (bush == null || bush.transform.parent == null) continue;
            if (!countedBushes.Add(bush)) continue;
            allBushes++;
            if (WorldZones.ScoreAtHeight(bush.transform.position.y) < 1000)
                firstThousandBushes++;
            bushPlatforms.Add(Mathf.RoundToInt(bush.transform.parent.position.y * 1000f));
        }
        IList routes = (IList)RoutesField.GetValue(generator);
        foreach (object step in routes)
        {
            Type type = step.GetType();
            Vector2 from = (Vector2)type.GetField("from").GetValue(step);
            Vector2 to = (Vector2)type.GetField("to").GetValue(step);
            int lower = Mathf.RoundToInt(from.y * 1000f);
            int upper = Mathf.RoundToInt(to.y * 1000f);
            if (bushPlatforms.Contains(lower) && bushPlatforms.Contains(upper))
                bushChains.Add(((long)lower << 32) ^ (uint)upper);
        }

        foreach (DoodlikHazard hazard in
                 Object.FindObjectsByType<DoodlikHazard>(FindObjectsSortMode.None))
        {
            if (hazard == null) continue;
            Rect danger = hazard.Type == DoodlikHazard.Kind.ThornPatch
                ? hazard.WarningBounds : hazard.PatrolBounds;
            clearanceChecks++;
            if (generator.OverlapsBushLaunch(danger))
                throw new InvalidOperationException($"Bush launch crossed a live " +
                    $"{hazard.Type} at {danger} on frame {Time.frameCount}, " +
                    $"cameraScore={WorldZones.ScoreAtHeight(cameraY)}");
            if (!countedHazards.Add(hazard)) continue;
            int score = WorldZones.ScoreAtHeight(hazard.transform.position.y);
            if (score < 0 || score >= 1000) continue;
            scoreBins[Mathf.Clamp(score / 200, 0, scoreBins.Length - 1)]++;
            switch (hazard.Type)
            {
                case DoodlikHazard.Kind.Thorns: horizontal++; break;
                case DoodlikHazard.Kind.VerticalThorns: vertical++; break;
                case DoodlikHazard.Kind.ThornPatch: patches++; break;
                case DoodlikHazard.Kind.FlyingSeed:
                case DoodlikHazard.Kind.Bumblebee:
                    flying++;
                    if (WorldZones.AtScore(score) == WorldZones.Zone.Meadow)
                        meadowFlying++;
                    break;
            }
        }
    }

    private static void ResetObservations()
    {
        lastFrame = -1;
        observedFrames = settledFrames = 0;
        largestCameraStep = 0f;
        setupChangedAspect = false;
        countedHazards.Clear();
        countedBushes.Clear();
        bushPlatforms.Clear();
        bushChains.Clear();
        Array.Clear(scoreBins, 0, scoreBins.Length);
        horizontal = vertical = flying = patches = meadowFlying = 0;
        firstThousandBushes = allBushes = clearanceChecks = 0;
    }

    private static void Finish(int code)
    {
        SessionState.EraseInt("Doodlik.DifficultyValidation.HazardSeed");
        Spawner.ValidationPickupSeed = null;
        SessionState.SetFloat(SpeedKey, 18f);
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        SessionState.SetBool(ActiveKey, false);
        SessionState.SetBool(SeenPlayKey, false);
        try
        {
            if (PlayerProgress.AdminSandboxActive) PlayerProgress.EndAdminSandbox();
            SavedPreferences saved = JsonUtility.FromJson<SavedPreferences>(
                SessionState.GetString(PreferencesKey, ""));
            if (saved != null)
            {
                foreach (IntPreference entry in saved.ints)
                {
                    if (entry.exists) PlayerPrefs.SetInt(entry.key, entry.value);
                    else PlayerPrefs.DeleteKey(entry.key);
                }
                foreach (StringPreference entry in saved.strings)
                {
                    if (entry.exists) PlayerPrefs.SetString(entry.key, entry.value);
                    else PlayerPrefs.DeleteKey(entry.key);
                }
                PlayerPrefs.Save();
            }
            AudioListener.volume = SessionState.GetFloat(OldVolumeKey, 1f);
            Time.timeScale = 1f;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            code = 1;
        }
        EditorApplication.Exit(code);
    }
}
#endif
