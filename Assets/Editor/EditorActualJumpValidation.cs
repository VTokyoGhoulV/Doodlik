#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Unlike generator checks, these measurements come from Box2D and the actual
// Player movement methods. No Player.Start, UI, scoring or collision callbacks.
public static class EditorActualJumpValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo Horizontal = typeof(Player).GetMethod("ApplyHorizontalMovement", Private);
    private static readonly MethodInfo Gravity = typeof(Player).GetMethod("ApplyGravityCurve", Private);
    private static int checks, cases;

    private struct Measurement
    {
        public bool CanLand;
        public float Apex, Distance, LandingTime;
    }

    public static void Run()
    {
        if (!EditorApplication.isPlaying)
            throw new InvalidOperationException("Run actual-jump validation in Play Mode after the gameplay scene has loaded.");
        bool hadFormat = PlayerPrefs.HasKey(GameplayPreferences.OrientationKey);
        bool hadControl = PlayerPrefs.HasKey(GameplayPreferences.InertiaKey);
        int originalFormat = PlayerPrefs.GetInt(GameplayPreferences.OrientationKey);
        int originalControl = PlayerPrefs.GetInt(GameplayPreferences.InertiaKey);
        float originalStep = Time.fixedDeltaTime;
        float originalTimeScale = Time.timeScale;
        SimulationMode2D originalMode = Physics2D.simulationMode;
        bool originalSync = Physics2D.autoSyncTransforms;
        Scene simulationScene = default;
        GameObject testObject = null;
        checks = cases = 0;
        try
        {
            Player source = Object.FindAnyObjectByType<Player>();
            Check(source != null, "source player exists");
            Check(Horizontal != null && Gravity != null, "real controller methods found");

            simulationScene = SceneManager.CreateScene("Doodlik actual jump " + Guid.NewGuid().ToString("N"),
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            PhysicsScene2D physics = simulationScene.GetPhysicsScene2D();
            Check(physics.IsValid() && physics != Physics2D.defaultPhysicsScene, "isolated physics scene");
            testObject = new GameObject("Actual controller jump probe");
            SceneManager.MoveGameObjectToScene(testObject, simulationScene);
            Rigidbody2D body = testObject.AddComponent<Rigidbody2D>();
            Rigidbody2D sourceBody = source.GetComponent<Rigidbody2D>();
            Check(sourceBody != null, "source rigidbody exists");
            body.bodyType = RigidbodyType2D.Dynamic;
            body.mass = sourceBody.mass;
            body.linearDamping = sourceBody.linearDamping;
            body.angularDamping = sourceBody.angularDamping;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.interpolation = RigidbodyInterpolation2D.None;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            body.simulated = true;
            // A fixture ensures normal Box2D mass/body integration even though
            // this isolated scene deliberately has no contact surfaces.
            testObject.AddComponent<CircleCollider2D>().radius = 0.1f;
            Player player = testObject.AddComponent<Player>();
            EditorUtility.CopySerialized(source, player);
            player.enabled = false;
            typeof(Player).GetField("rb", Private).SetValue(player, body);
            typeof(Player).GetField("horizontalInput", Private).SetValue(player, 1f);

            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.autoSyncTransforms = false;
            Time.timeScale = 1f;
            int formats = GameplayPreferences.IsDesktopBuild ? 1 : 2;
            float[] targets = { 2f, 3f, 4f };
            // The configured tick plus two common fixed ticks catch time-step
            // assumptions independently of the display frame-rate setting.
            float[] steps = { originalStep, 1f / 60f, 1f / 120f };
            for (int format = 0; format < formats; format++)
            for (int control = 0; control < 2; control++)
            {
                PlayerPrefs.SetInt(GameplayPreferences.OrientationKey, format);
                PlayerPrefs.SetInt(GameplayPreferences.InertiaKey, control);
                float scale = GameplayPreferences.HorizontalMovementScale;
                foreach (float step in steps)
                {
                    Time.fixedDeltaTime = step;
                    foreach (float target in targets)
                    for (int inheritance = 0; inheritance < 2; inheritance++)
                    {
                        float inherited = inheritance == 1 ? player.maximumInheritedSpeed * scale : 0f;
                        JumpReachability.Result predicted = JumpReachability.Simulate(player,
                            target, control == 1, scale, inherited);
                        Measurement measured = Measure(player, body, physics, target, inherited);
                        string id = $"format={format} inertia={control} target={target} inherit={inherited:F3} step={step:F5}";
                        Check(predicted.CanLand == measured.CanLand, "reachability agrees: " + id);
                        Check(Mathf.Abs(predicted.Apex - measured.Apex) < 0.015f,
                            $"apex agrees: {id} prediction={predicted.Apex} actual={measured.Apex}");
                        if (measured.CanLand)
                        {
                            Check(predicted.HorizontalDistance <= measured.Distance + 0.015f,
                                $"no optimistic reach: {id} prediction={predicted.HorizontalDistance} actual={measured.Distance}");
                            Check(Mathf.Abs(predicted.HorizontalDistance - measured.Distance) < 0.025f,
                                $"distance agrees: {id} prediction={predicted.HorizontalDistance} actual={measured.Distance}");
                            Check(Mathf.Abs(predicted.LandingTime - measured.LandingTime) < 0.005f,
                                $"landing time agrees: {id} prediction={predicted.LandingTime} actual={measured.LandingTime}");
                        }
                        cases++;
                    }
                }
            }
            Debug.Log($"ACTUAL_JUMP_PHYSICS_PASS cases={cases} checks={checks} formats={formats} source=Box2D+PlayerController");
        }
        finally
        {
            if (testObject != null) Object.DestroyImmediate(testObject);
            if (simulationScene.IsValid()) SceneManager.UnloadSceneAsync(simulationScene);
            Physics2D.simulationMode = originalMode;
            Physics2D.autoSyncTransforms = originalSync;
            Time.fixedDeltaTime = originalStep;
            Time.timeScale = originalTimeScale;
            Restore(GameplayPreferences.OrientationKey, hadFormat, originalFormat);
            Restore(GameplayPreferences.InertiaKey, hadControl, originalControl);
            PlayerPrefs.Save();
        }
    }

    private static Measurement Measure(Player player, Rigidbody2D body,
        PhysicsScene2D physics, float targetHeight, float inherited)
    {
        body.position = Vector2.zero;
        // Precise control also starts moving the wrong way: the real controller
        // must overwrite that velocity on the first tick, as it does in-game.
        body.linearVelocity = new Vector2(-player.moveSpeed * GameplayPreferences.HorizontalMovementScale - inherited,
            player.NormalJumpForce);
        body.angularVelocity = 0f;
        body.WakeUp();
        Physics2D.SyncTransforms();
        Measurement result = new Measurement();
        bool roseAbove = false;
        float step = Time.fixedDeltaTime;
        for (int tick = 0; tick * step < 6f; tick++)
        {
            Vector2 previous = body.position;
            Horizontal.Invoke(player, null);
            Gravity.Invoke(player, null);
            if (!physics.Simulate(step))
                throw new InvalidOperationException("Local Box2D simulation failed to advance.");
            Vector2 position = body.position;
            result.Apex = Mathf.Max(result.Apex, position.y);
            roseAbove |= position.y >= targetHeight;
            if (body.linearVelocity.y < 0f && position.y <= targetHeight && roseAbove)
            {
                float fraction = Mathf.InverseLerp(previous.y, position.y, targetHeight);
                result.CanLand = true;
                result.Distance = Mathf.Max(0f, Mathf.Lerp(previous.x, position.x, fraction));
                result.LandingTime = (tick + fraction) * step;
                return result;
            }
            if (body.linearVelocity.y < 0f && position.y < 0f)
                return result;
        }
        throw new InvalidOperationException("Actual jump did not finish in the six-second limit.");
    }

    private static void Restore(string key, bool existed, int value)
    { if (existed) PlayerPrefs.SetInt(key, value); else PlayerPrefs.DeleteKey(key); }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("Actual jump validation: " + message);
    }
}
#endif
