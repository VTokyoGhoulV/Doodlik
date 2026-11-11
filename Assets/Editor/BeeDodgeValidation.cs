#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class BeeDodgeValidation
{
    private const string Key = "Doodlik.BeeDodgeValidation";
    private static IDisposable storage;
    private static BeeDodgeFixtureDriver driver;
    private static double started;
    private static int phase, checks;
    private static string failure;
    static BeeDodgeValidation() { if (SessionState.GetBool(Key, false)) Register(); }
    public static void Run() => Begin(false);
    public static void RunPortrait() => Begin(true);
    private static void Begin(bool portrait)
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/"))
            throw new InvalidOperationException("Bee dodge checks need an isolated batch Editor.");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "DoodlikBeeDodge_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".portrait", portrait);
        GameplayPreferences.SetMode(portrait, false, false);
        GameplayPreferences.SetDifficulty(GameDifficulty.Normal);
        phase = checks = 0; Register(); RequestViewport();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BeforeScene()
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (storage == null) storage = PlayerProgress.UseIsolatedStorageForValidation(out _, out _);
        RequestViewport();
    }
    private static void RequestViewport()
    {
        bool portrait = SessionState.GetBool(Key + ".portrait", false);
        PlayModeWindow.SetCustomRenderingResolution(portrait ? 720u : 1280u, portrait ? 1280u : 720u, "Doodlik bee dodge proof");
    }
    private static void Register()
    {
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        AudioListener.volume = 0f; AudioListener.pause = true;
        try
        {
            if (failure != null) throw new Exception(failure);
            if (EditorApplication.timeSinceStartup - started > 90d) throw new TimeoutException("Bee dodge phase " + phase);
            if (Time.frameCount < 25 || RunFlow.IsTransitioning) return;
            Check(EditorUtility.audioMasterMute && AudioListener.volume == 0f, "Silent test frame.");
            if (phase == 0)
            {
                bool portrait = SessionState.GetBool(Key + ".portrait", false);
                Check(Screen.width == (portrait ? 720 : 1280) && Screen.height == (portrait ? 1280 : 720),
                    "Actual Game View matches the requested phone/landscape viewport.");
                Check(PlayerProgress.SelectChallenge("run_bee_dodge3"), "A real dodge task can be selected before a run.");
                Object.FindAnyObjectByType<GameMenuUI>().StartRunFromHome(); phase = 1; return;
            }
            if (phase == 1)
            {
                Player player = Object.FindAnyObjectByType<Player>();
                if (!RunFlow.IsPlaying || player?.Challenges?.Session == null) return;
                player.GetComponent<Rigidbody2D>().simulated = false;
                player.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
                Object.FindAnyObjectByType<Spawner>().enabled = false;
                Object.FindAnyObjectByType<CameraFollow>().enabled = false;
                foreach (DoodlikHazard hazard in Object.FindObjectsByType<DoodlikHazard>()) Object.DestroyImmediate(hazard.gameObject);
                foreach (RunPickup pickup in Object.FindObjectsByType<RunPickup>()) Object.DestroyImmediate(pickup.gameObject);
                GameObject root = new GameObject("Bee dodge fixture driver");
                driver = root.AddComponent<BeeDodgeFixtureDriver>(); driver.Initialize(player);
                phase = 2; return;
            }
            if (driver == null || !driver.Done) return;
            Check(driver.TotalDodges == 2, "Only two genuinely avoided attacks issued bridge events.");
            Debug.Log("BEE_DODGE_PASS checks=" + checks + " scenarios=12 certified=2 genuine=True farAndOffscreenRejected=True" +
                " protectedContact=True multiHazardContact=True rocketRejected=True lateAuditFence=True pause=True death=True disabled=True duplicate=True portrait=" +
                SessionState.GetBool(Key + ".portrait", false));
            Finish(0);
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }
    public static void Check(bool value, string message)
    {
        checks++; if (!value) throw new InvalidOperationException("Bee dodge: " + message);
    }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search") && stack.Contains("IndexationOnStartup")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = message;
    }
    private static void Finish(int code)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
        if (driver != null) { driver.Cleanup(); Object.DestroyImmediate(driver.gameObject); }
        Object.FindAnyObjectByType<Player>()?.Journal?.EndRun("Проверка уклонений");
        storage?.Dispose(); storage = null; RunFlow.RunActive = false; Time.timeScale = 1f;
        EditorApplication.Exit(code);
    }
}

// Test-only ordering: complete a controlled dash AFTER the real hazard Update,
// then let the real Player.LateUpdate audit it. Production execution order is
// unchanged. LateUpdate here can pause immediately AFTER that genuine audit.
[DefaultExecutionOrder(10000)]
public sealed class BeeDodgeFixtureDriver : MonoBehaviour
{
    private enum Scenario { Genuine, FarVisible, Offscreen, ShieldContact, GraceContact, MultiGraceContact,
        Rocket, Despawn, Disabled, PausedUnreviewed, PausedReviewed, Death }
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private Player player;
    private RunPowerups powers;
    private ChargingBumblebee bee;
    private DoodlikHazard thorn;
    private GameObject support;
    private Vector2 home;
    private int scenario, stage, waitFrame, before, completedFrame, shieldUsesBefore;
    private bool returnedToAim, paused, pauseAfterAudit;
    public bool Done { get; private set; }
    public int TotalDodges => player.Challenges.Session.Stats.BeeDodges;
    private Scenario Current => (Scenario)scenario;

    public void Initialize(Player owner)
    {
        player = owner; powers = owner.GetComponent<RunPowerups>();
        home = (Vector2)Camera.main.transform.position + Vector2.up;
        support = new GameObject("Bee dodge rocket support"); support.tag = "Platform";
        support.transform.position = new Vector3(home.x, home.y - 1.5f, 0f);
        support.AddComponent<BoxCollider2D>().size = new Vector2(Camera.main.orthographicSize * Camera.main.aspect * 1.8f, 0.2f);
    }
    private void Update()
    {
        if (Done || player == null) return;
        if (stage == 0) { Setup(); return; }
        if (Time.frameCount < waitFrame) return;
        if (stage == 1)
        {
            if (Current == Scenario.Despawn)
            {
                Object.DestroyImmediate(bee.gameObject); bee = null; stage = 2; waitFrame = Time.frameCount + 5; return;
            }
            bool contact = Current == Scenario.ShieldContact || Current == Scenario.GraceContact || Current == Scenario.MultiGraceContact;
            if (contact && !returnedToAim)
            {
                MovePlayer(bee.AimPoint); returnedToAim = true; waitFrame = Time.frameCount + 3; return;
            }
            if (Current == Scenario.Disabled) { bee.enabled = false; bee.enabled = true; }
            typeof(ChargingBumblebee).GetField("phaseTime", Private).SetValue(bee, bee.WindupDuration);
            bee.Step(0.01f);
            if (Current == Scenario.Rocket)
            {
                powers.Collect(RunPowerups.Kind.Rocket);
                BeeDodgeValidation.Check(powers.IsRocketActive, "The rocket cancellation uses the real rocket state.");
                bee.Step(0.01f); player.GetComponent<PaperRocketFlight>().Cancel();
                BeeDodgeValidation.Check(!bee.IsDamaging, "Rocket interruption cancels the damaging attack.");
            }
            else
            {
                bee.Step(20f); completedFrame = Time.frameCount;
                BeeDodgeValidation.Check(bee.IsDamaging && TotalDodges == before, "Completing a dash cannot award before Player.LateUpdate.");
                if (Current == Scenario.MultiGraceContact)
                {
                    DoodlikHazard first = DoodlikHazard.FindHit(player.HazardBodyCenter, player.HazardBodyCenter,
                        player.HazardBodyHalfSize, Camera.main.orthographicSize * Camera.main.aspect * 2f, false);
                    BeeDodgeValidation.Check(first == thorn, "The primary hit really is an earlier thorn, not the bee.");
                }
                if (Current == Scenario.PausedUnreviewed) { Time.timeScale = 0f; paused = true; }
                if (Current == Scenario.PausedReviewed) pauseAfterAudit = true;
                if (Current == Scenario.Death)
                    typeof(Player).GetMethod("Die", Private).Invoke(player, new object[] { "Проверка гибели после рывка" });
            }
            stage = 2; waitFrame = Time.frameCount + 5; return;
        }
        if (paused && Time.timeScale == 0f)
        {
            BeeDodgeValidation.Check(TotalDodges == before && (bee == null || !bee.DodgeAwarded), "A paused frame cannot grant a dodge.");
            Time.timeScale = 1f; paused = false; waitFrame = Time.frameCount + 5; return;
        }
        bool success = Current == Scenario.Genuine || Current == Scenario.PausedReviewed;
        if (bee != null)
        {
            string state = "";
            foreach (string field in new[] { "dodgeThreat", "dodgeMovedAway", "dodgeCancelled", "dodgeEvaluated", "dodgeAudited", "dodgeCompletedFrame", "dodgeRecoveryFrame" })
                state += " " + field + "=" + typeof(ChargingBumblebee).GetField(field, Private).GetValue(bee);
            Debug.Log("BEE_DODGE_STATE " + Current + " phase=" + bee.CurrentPhase + " frame=" + Time.frameCount + " lastAudit=" + player.LastHazardAuditFrame + state);
        }
        BeeDodgeValidation.Check(TotalDodges == before + (success ? 1 : 0), "Correct certified count for " + Current);
        if (bee != null)
        {
            BeeDodgeValidation.Check(bee.DodgeAwarded == success, "Actor outcome matches the real bridge event: " + Current);
            for (int i = 0; i < 8; i++) typeof(ChargingBumblebee).GetMethod("TryAwardDodge", Private).Invoke(bee, null);
            BeeDodgeValidation.Check(TotalDodges == before + (success ? 1 : 0), "Repeated evaluation cannot duplicate " + Current);
        }
        if (Current == Scenario.ShieldContact)
            BeeDodgeValidation.Check(!player.IsDead && !powers.HasShield && powers.IsProtected, "An actual bee hit consumed the carried shield, not a dodge.");
        if (Current == Scenario.GraceContact || Current == Scenario.MultiGraceContact)
            BeeDodgeValidation.Check(!player.IsDead && powers.IsProtected && player.Challenges.Session.Stats.ShieldUses == shieldUsesBefore,
                "Actual grace-protected contact has no new ShieldUses event and still must cancel the dodge.");
        Debug.Log("BEE_DODGE_SCENARIO " + Current + " before=" + before + " after=" + TotalDodges + " awarded=" + success);
        DestroyActors();
        if (++scenario >= Enum.GetValues(typeof(Scenario)).Length) { Done = true; return; }
        stage = 0;
    }
    private void LateUpdate()
    {
        if (!pauseAfterAudit || bee == null || Time.frameCount != completedFrame) return;
        BeeDodgeValidation.Check(player.LastHazardAuditFrame == completedFrame, "The completed damage frame was truly audited before pausing.");
        pauseAfterAudit = false; paused = true; Time.timeScale = 0f;
    }
    private void Setup()
    {
        Time.timeScale = 1f;
        player.GetComponent<PaperRocketFlight>()?.Cancel();
        typeof(RunPowerups).GetMethod("TickTimers", Private).Invoke(powers, new object[] { 50f });
        MovePlayer(home); returnedToAim = paused = pauseAfterAudit = false;
        before = TotalDodges;
        if (Current == Scenario.ShieldContact) powers.Collect(RunPowerups.Kind.Shield);
        if (Current == Scenario.GraceContact || Current == Scenario.MultiGraceContact)
        {
            powers.Collect(RunPowerups.Kind.Shield);
            BeeDodgeValidation.Check(powers.TryProtect(false, player.GetComponent<Rigidbody2D>(), Camera.main, player.GetComponent<Collider2D>()),
                "The fixture starts with actual pre-existing grace protection.");
        }
        shieldUsesBefore = player.Challenges.Session.Stats.ShieldUses;
        if (Current == Scenario.MultiGraceContact)
        {
            GameObject root = new GameObject("Earlier thorn contact"); root.transform.position = home;
            thorn = root.AddComponent<DoodlikHazard>();
            thorn.Initialize(DoodlikHazard.Kind.Thorns, Camera.main, 1f, 0f, 0f, 0f, new Rect(home - Vector2.one, Vector2.one * 2f));
        }
        Vector2 center = player.HazardBodyCenter;
        float halfScreen = Camera.main.orthographicSize * Camera.main.aspect;
        Vector2 beePosition = center + Vector2.right;
        Rect reserved = new Rect(center - new Vector2(2f, 1f), new Vector2(4f, 2f));
        if (Current == Scenario.FarVisible || Current == Scenario.Offscreen)
        {
            beePosition = center + Vector2.right * (Current == Scenario.Offscreen ? halfScreen + 2f : Mathf.Min(halfScreen * 0.65f, 3f));
            reserved = new Rect(beePosition - Vector2.one * 0.5f, Vector2.one);
        }
        GameObject actor = new GameObject("Dodge proof bee"); actor.transform.position = beePosition;
        actor.AddComponent<DoodlikHazard>().InitializeBumblebee(Camera.main, player, 1f, reserved, GameDifficulty.Normal);
        bee = actor.GetComponent<ChargingBumblebee>(); bee.Step(0.01f);
        bool expectedThreat = Current != Scenario.FarVisible && Current != Scenario.Offscreen;
        BeeDodgeValidation.Check(bee.DodgeThreatened == expectedThreat, "Exact visible body/shot geometry qualifies " + Current);
        MovePlayer(home + Vector2.left * Mathf.Min(2f, halfScreen * 0.65f));
        stage = 1; waitFrame = Time.frameCount + 3;
    }
    private void MovePlayer(Vector2 position)
    {
        player.GetComponent<Rigidbody2D>().position = position;
        player.transform.position = new Vector3(position.x, position.y, player.transform.position.z);
        Physics2D.SyncTransforms();
    }
    private void DestroyActors()
    {
        if (bee != null) Object.DestroyImmediate(bee.gameObject); bee = null;
        if (thorn != null) Object.DestroyImmediate(thorn.gameObject); thorn = null;
    }
    public void Cleanup() { DestroyActors(); if (support != null) Object.DestroyImmediate(support); }
}
#endif
