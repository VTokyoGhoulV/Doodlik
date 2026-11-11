using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Isolated synchronous Play Mode checks; leaves the live player and saves intact.</summary>
public static class RunExtensionValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    public static void ValidatePickupsAndPowers()
    {
        Require(Application.isPlaying, "Pickups/powers validation requires Play Mode.");
        bool savedRun = RunFlow.RunActive;
        bool savedTransition = RunFlow.IsTransitioning;
        float savedScale = Time.timeScale;
        string[] keys = { PlayerProgress.SaveKey, PlayerProgress.BackupKey };
        string[] savedValues = new string[keys.Length];
        bool[] hadKeys = new bool[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            hadKeys[i] = PlayerPrefs.HasKey(keys[i]);
            savedValues[i] = PlayerPrefs.GetString(keys[i], "");
        }

        FieldInfo instanceField = typeof(RunPowerups).GetField("instance", PrivateStatic);
        RunPowerups savedInstance = (RunPowerups)instanceField.GetValue(null);
        List<RunPickup> registry = (List<RunPickup>)typeof(RunPickup).GetField("Active", PrivateStatic).GetValue(null);
        List<RunPickup> savedRegistry = new List<RunPickup>(registry);
        List<GameObject> temporary = new List<GameObject>();
        Action reentrant = null;
        try
        {
            registry.Clear();
            RunFlow.RunActive = true;
            RunFlow.IsTransitioning = false;
            Time.timeScale = 1f;
            foreach (string key in keys) PlayerPrefs.DeleteKey(key);
            PlayerProgress.Reload();

            GameObject cat = MakeObject(temporary, "Validation cat", new Vector2(8000f, 7990f));
            Rigidbody2D body = cat.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            BoxCollider2D feet = cat.AddComponent<BoxCollider2D>();
            feet.size = new Vector2(0.70f, 0.40f);
            RunPowerups powers = cat.AddComponent<RunPowerups>();

            // Hit protection consumes exactly one shield and provides a short
            // contact grace period, rather than consuming one every render frame.
            powers.Collect(RunPowerups.Kind.Shield);
            Require(powers.HasShield, "Shield pickup activates a shield.");
            Require(powers.TryProtect(false, body, null, feet) && !powers.HasShield,
                "First thorn contact consumes one shield.");
            Require(powers.TryProtect(false, body, null, feet), "Repeated contact is protected during grace.");
            Tick(powers, 2f);
            Require(!powers.IsProtected && !powers.TryProtect(false, body, null, feet),
                "A consumed shield cannot protect another hit after grace expires.");

            // Timers use scaled elapsed time and freeze with the game/menu.
            powers.Collect(RunPowerups.Kind.Magnet);
            powers.Collect(RunPowerups.Kind.Slow);
            float magnet = ReadFloat(powers, "magnetTime");
            float slow = ReadFloat(powers, "slowTime");
            Require(Mathf.Approximately(RunPowerups.PlatformSpeedMultiplier, 0.55f), "Slow pickup affects platform speed.");
            Tick(powers, 0.25f);
            Near(ReadFloat(powers, "magnetTime"), magnet - 0.25f, "Magnet follows scaled elapsed time.");
            Near(ReadFloat(powers, "slowTime"), slow - 0.25f, "Slow follows scaled elapsed time.");
            Time.timeScale = 0f;
            Tick(powers, 10f);
            Near(ReadFloat(powers, "slowTime"), slow - 0.25f, "Pause does not consume bonus time.");
            Time.timeScale = 1f;
            RunFlow.IsTransitioning = true;
            Tick(powers, 10f);
            Near(ReadFloat(powers, "magnetTime"), magnet - 0.25f, "Transitions do not consume bonus time.");
            RunFlow.IsTransitioning = false;

            // Verify the real moving-platform integrated clock across slow expiry.
            GameObject movingObject = MakeObject(temporary, "Validation moving platform", new Vector2(8000f, 8020f));
            MovingPlatform moving = movingObject.AddComponent<MovingPlatform>();
            moving.Configure(7997f, 8003f, 1f, 1f, 0f);
            Invoke(moving, "FixedUpdate");
            float clockBefore = ReadFloat(moving, "motionTime");
            Near(clockBefore, Time.fixedDeltaTime * 0.55f, "Slow advances the platform clock at 55 percent.");
            Tick(powers, 20f);
            Require(Mathf.Approximately(RunPowerups.PlatformSpeedMultiplier, 1f), "Slow expires completely.");
            Invoke(moving, "FixedUpdate");
            Near(ReadFloat(moving, "motionTime"), clockBefore + Time.fixedDeltaTime,
                "Expiry continues the clock without a phase reset/teleport.");

            // A fall rescue must select an actual safe, wide-enough surface
            // inside the camera; lower off-screen/narrow/thorn surfaces are decoys.
            GameObject cameraObject = MakeObject(temporary, "Validation camera", new Vector2(8000f, 8002f));
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.aspect = 1f;
            MakePlatform(temporary, "Offscreen decoy", new Vector2(8020f, 7998f), 2f);
            MakePlatform(temporary, "Narrow decoy", new Vector2(8000f, 7999f), 0.25f);
            BoxCollider2D thornSurface = MakePlatform(temporary, "Thorn decoy", new Vector2(8000f, 7998.5f), 2f);
            GameObject thorn = MakeObject(temporary, "Validation thorn", thornSurface.transform.position);
            thorn.transform.SetParent(thornSurface.transform, true);
            thorn.AddComponent<DoodlikHazard>();
            BoxCollider2D safe = MakePlatform(temporary, "Safe rescue surface", new Vector2(8000f, 8000f), 2f);
            Physics2D.SyncTransforms();
            powers.Collect(RunPowerups.Kind.Shield);
            Require(!powers.TryProtect(true, body, null, feet) && powers.HasShield,
                "An unavailable rescue camera does not spend the shield or throw.");
            body.linearVelocity = Vector2.down * 12f;
            Require(powers.TryProtect(true, body, camera, feet), "Shield rescues a fall onto a real visible platform.");
            Near(body.position.x, safe.bounds.center.x, "Shield chooses the visible safe surface.");
            Require(feet.bounds.min.y >= safe.bounds.max.y && feet.bounds.min.y < safe.bounds.max.y + 0.12f,
                "Rescue places the feet directly above the texture/collider.");
            Require(body.linearVelocity == Vector2.zero && !powers.HasShield && powers.IsProtected,
                "Rescue stops falling, consumes one shield and grants contact grace.");
            safe.enabled = false;
            powers.Collect(RunPowerups.Kind.Shield);
            Require(!powers.TryProtect(true, body, camera, feet) && powers.HasShield,
                "No valid surface means no unsafe teleport and no shield consumption.");

            // Test the public collection path, including wallet callback re-entry.
            Vector2 point = new Vector2(8000f, 8040f);
            RunPickup coin = MakePickup(temporary, point, 0);
            Time.timeScale = 0f;
            RunPickup.CollectAlong(point, point, Vector2.one * 0.1f, 10f, powers);
            Require(PlayerProgress.Coins == 0 && coin.gameObject.activeSelf, "Paused pickups cannot award currency.");
            Time.timeScale = 1f;
            reentrant = () => RunPickup.CollectAlong(point, point, Vector2.one * 0.1f, 10f, powers);
            PlayerProgress.Changed += reentrant;
            RunPickup.CollectAlong(point, point, Vector2.one * 0.1f, 10f, powers);
            PlayerProgress.Changed -= reentrant;
            reentrant = null;
            RunPickup.CollectAlong(point, point, Vector2.one * 0.1f, 10f, powers);
            Require(PlayerProgress.Coins == 1, "Coin is awarded once, including re-entrant wallet callbacks.");

            MakePickup(temporary, point + Vector2.up * 5f, 0);
            RunPickup.CollectAlong(point + Vector2.up * 7f, point + Vector2.up * 3f,
                Vector2.one * 0.1f, 10f, powers);
            Require(PlayerProgress.Coins == 2, "Fast falling sweep collects between frames.");

            MakePickup(temporary, point + new Vector2(-4.8f, 10f), 0);
            RunPickup.CollectAlong(point + new Vector2(4.7f, 10f), point + new Vector2(-4.6f, 10f),
                Vector2.one * 0.1f, 10f, powers);
            Require(PlayerProgress.Coins == 3, "Crossing the screen seam collects only the wrapped route.");

            RunPickup movingCoin = MakePickup(temporary, point + new Vector2(1f, 15f), 0);
            SetField(movingCoin, "previousPosition", point + new Vector2(-1f, 15f));
            RunPickup.CollectAlong(point + Vector2.up * 15f, point + Vector2.up * 15f,
                Vector2.one * 0.1f, 10f, powers);
            Require(PlayerProgress.Coins == 4, "Moving-platform pickup sweep crosses a stationary player.");

            Tick(powers, 20f);
            SetField(powers, "shield", false);
            MakePickup(temporary, point + Vector2.up * 20f, 1);
            RunPickup.CollectAlong(point + Vector2.up * 20f, point + Vector2.up * 20f,
                Vector2.one * 0.1f, 10f, powers);
            Require(powers.HasShield && PlayerProgress.Coins == 4, "Shield pickup grants a power, not currency.");
            powers.Collect(RunPowerups.Kind.Slow);
            powers.enabled = false;
            Require(Mathf.Approximately(RunPowerups.PlatformSpeedMultiplier, 1f), "Disabled power owner cannot leave global slow active.");
            Debug.Log("Doodlik powers/pickups validation PASS: one-hit shield, safe rescue, pause/timers, integrated slow clock, duplicate protection, low-FPS and wrap collection.");
        }
        finally
        {
            if (reentrant != null) PlayerProgress.Changed -= reentrant;
            for (int i = temporary.Count - 1; i >= 0; i--)
                if (temporary[i] != null) UnityEngine.Object.DestroyImmediate(temporary[i]);
            registry.Clear();
            foreach (RunPickup pickup in savedRegistry)
                if (pickup != null && pickup.isActiveAndEnabled) registry.Add(pickup);
            instanceField.SetValue(null, savedInstance);
            for (int i = 0; i < keys.Length; i++)
            {
                if (hadKeys[i]) PlayerPrefs.SetString(keys[i], savedValues[i]);
                else PlayerPrefs.DeleteKey(keys[i]);
            }
            PlayerPrefs.Save();
            Time.timeScale = savedScale;
            RunFlow.RunActive = savedRun;
            RunFlow.IsTransitioning = savedTransition;
            PlayerProgress.Reload();
        }
    }

    private static GameObject MakeObject(List<GameObject> objects, string name, Vector2 position)
    {
        GameObject result = new GameObject(name);
        result.transform.position = position;
        objects.Add(result);
        return result;
    }

    private static BoxCollider2D MakePlatform(List<GameObject> objects, string name, Vector2 position, float width)
    {
        GameObject result = MakeObject(objects, name, position);
        result.tag = "Platform";
        BoxCollider2D collider = result.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(width, 0.2f);
        return collider;
    }

    private static RunPickup MakePickup(List<GameObject> objects, Vector2 position, int kind)
    {
        RunPickup result = MakeObject(objects, "Validation pickup", position).AddComponent<RunPickup>();
        SetField(result, "kind", kind);
        return result;
    }

    private static void Tick(RunPowerups powers, float elapsed)
    {
        typeof(RunPowerups).GetMethod("TickTimers", PrivateInstance).Invoke(powers, new object[] { elapsed });
    }

    private static float ReadFloat(object value, string field)
    {
        return (float)value.GetType().GetField(field, PrivateInstance).GetValue(value);
    }

    private static void SetField(object value, string field, object contents)
    {
        value.GetType().GetField(field, PrivateInstance).SetValue(value, contents);
    }

    private static void Invoke(object value, string method)
    {
        value.GetType().GetMethod(method, PrivateInstance).Invoke(value, null);
    }

    private static void Near(float actual, float expected, string message)
    {
        Require(Mathf.Abs(actual - expected) < 0.001f, message + " Actual=" + actual + ", expected=" + expected);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
