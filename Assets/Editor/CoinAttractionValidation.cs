using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Synchronous sandbox: never touches PlayerPrefs, wallet callbacks, live player,
// scenes on disk, or Editor play state. Requires an already running Play Mode.
public static class CoinAttractionValidation
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

    [MenuItem("Doodlik/Validate Coin Attraction (Play Mode)")]
    public static void Validate()
    {
        Require(Application.isPlaying, "Start Play Mode before this check.");
        bool savedActive = RunFlow.RunActive, savedTransition = RunFlow.IsTransitioning;
        float savedTimeScale = Time.timeScale;
        var active = (List<RunPickup>)typeof(RunPickup).GetField("Active", Static).GetValue(null);
        var originalPickups = new List<RunPickup>(active);
        var coins = new List<RunPickup>();
        GameObject sandbox = new GameObject("Coin attraction validation sandbox") { hideFlags = HideFlags.DontSave };
        try
        {
            active.Clear();
            RunFlow.RunActive = true;
            RunFlow.IsTransitioning = false;
            Time.timeScale = 1f;
            GameObject cat = Child(sandbox, "Target");
            Player player = cat.AddComponent<Player>();
            player.enabled = false; // Its Start never runs during this synchronous check.
            Camera camera = Child(sandbox, "Camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new Vector3(8000f, 8000f, -10f);
            int cases = 0;

            foreach (float width in new[] { 4f, 10f, 18f })
            foreach (int fps in new[] { 30, 60, 120 })
            foreach (float scale in new[] { 1f, 0.64f })
            {
                cat.transform.position = new Vector3(8000f + width * 0.5f - 0.15f, 8000f, 0f);
                RunPickup coin = MakeCoin(sandbox, coins, camera,
                    new Vector3(8000f - width * 0.5f + 0.30f, 8000f, 0f), scale);
                Require(Claim(coin, cat.transform, width), "The first claim succeeds.");
                Require(!Claim(coin, cat.transform, width), "A second claim cannot award another coin.");
                Require(!active.Contains(coin) && coin.gameObject.activeSelf, "A flying coin is visible but no longer collectable.");
                Require(coin.transform.parent == null, "Flight detaches from the moving/crumbling platform.");
                float duration = Get<float>(coin, "attractionDuration");
                Vector3 previous = coin.transform.position;
                int frames = Mathf.CeilToInt(duration * fps) + 1;
                for (int frame = 0; frame < frames && coin.gameObject.activeSelf; frame++)
                {
                    Vector3 target = cat.transform.position;
                    target.x = 8000f + TrapGeometry.WrappedDelta(8000f, target.x + 2f / fps, width);
                    target.y += 1.5f / fps;
                    cat.transform.position = target;
                    Tick(coin, 1f / fps);
                    float horizontalStep = Mathf.Abs(TrapGeometry.WrappedDelta(previous.x, coin.transform.position.x, width));
                    Require(horizontalStep < width * 0.3f, "Crossing the seam does not send the coin through the whole screen.");
                    previous = coin.transform.position;
                }
                Require(!coin.gameObject.activeSelf, "Flight finishes within its short bounded duration.");
                Near(TrapGeometry.WrappedDelta(coin.transform.position.x, cat.transform.position.x, width), 0f,
                    "Coin arrives at the current wrapped target, not its old position.");
                Near(coin.transform.position.y, cat.transform.position.y, "Coin follows the moving target vertically.");
                Require(Get<Transform>(coin, "art").localScale.sqrMagnitude < 0.00001f, "Coin shrinks into the cat before disappearing.");
                cases++;
            }

            cat.transform.position = new Vector3(8000f, 8000f, 0f);
            RunPickup paused = MakeCoin(sandbox, coins, camera, cat.transform.position + Vector3.left, 1f);
            Claim(paused, cat.transform, 10f);
            Vector3 atPause = paused.transform.position;
            Time.timeScale = 0f;
            Tick(paused, 10f);
            Require(paused.transform.position == atPause && paused.gameObject.activeSelf,
                "Menu pause freezes rather than consumes the flight.");
            Time.timeScale = 1f;
            Tick(paused, 0.04f);
            Require(paused.transform.position != atPause, "Flight resumes after pause.");

            Set(player, "isDead", true);
            Time.timeScale = 0f;
            Tick(paused, 0f);
            Require(!paused.gameObject.activeSelf, "Death safely retires flight even while time is frozen.");
            Set(player, "isDead", false);
            Time.timeScale = 1f;

            RunPickup transition = MakeCoin(sandbox, coins, camera, cat.transform.position + Vector3.left, 1f);
            Claim(transition, cat.transform, 10f);
            RunFlow.IsTransitioning = true;
            Tick(transition, 0f);
            Require(!transition.gameObject.activeSelf, "Restart/home transition cannot leave detached coins behind.");
            RunFlow.IsTransitioning = false;

            RunPickup missingTarget = MakeCoin(sandbox, coins, camera, cat.transform.position + Vector3.left, 1f);
            Claim(missingTarget, cat.transform, 10f);
            Object.DestroyImmediate(cat);
            Tick(missingTarget, 0.01f);
            Require(!missingTarget.gameObject.activeSelf, "Destroyed collector cancels presentation safely.");
            ValidateSchedule();
            Debug.Log($"COIN_ATTRACTION_PASS: {cases} moving/wrapped scale + FPS cases; duplicate claim, detach, shrink, pause, death, restart, missing target. No saves were changed.");
        }
        finally
        {
            foreach (RunPickup coin in coins)
                if (coin != null) Object.DestroyImmediate(coin.gameObject);
            Object.DestroyImmediate(sandbox);
            active.Clear();
            foreach (RunPickup pickup in originalPickups)
                if (pickup != null && pickup.isActiveAndEnabled) active.Add(pickup);
            RunFlow.RunActive = savedActive;
            RunFlow.IsTransitioning = savedTransition;
            Time.timeScale = savedTimeScale;
        }
    }

    [MenuItem("Doodlik/Validate Rare Pickup Schedule")]
    public static void ValidateSchedule()
    {
        int coins = 0, bonuses = 0;
        for (uint seed = 1; seed <= 128; seed++)
        {
            var schedule = new PickupSpawnSchedule(seed);
            var duplicate = new PickupSpawnSchedule(seed);
            int lastBonus = 0, lastKind = 0;
            for (int index = 0; index < 2000; index++)
            {
                bool spawned = schedule.TryGetKind(index, out int kind);
                Require(spawned == duplicate.TryGetKind(index, out int sameKind) && kind == sameKind,
                    "A fresh run owns an independent reproducible schedule.");
                Require(!schedule.TryGetKind(index, out _), "Repeated platform index cannot spawn duplicates.");
                if (!spawned) continue;
                Require(kind >= 1 && kind <= 3, "Platform scheduling never creates coins.");
                Require(index - lastBonus >= 18 && index - lastBonus <= 28, "Bonus spacing stays at 18–28 platforms.");
                Require(lastKind == 0 || kind == lastKind % 3 + 1, "All three power types take turns.");
                lastKind = kind;
                lastBonus = index;
                bonuses++;
            }
            float lastCenter = 0f;
            for (int height = 0; height < 5000; height++)
            {
                bool spawned = schedule.TryTakeCoinBand(height, out PickupSpawnSchedule.CoinBand band);
                Require(spawned == duplicate.TryTakeCoinBand(height, out PickupSpawnSchedule.CoinBand same) &&
                    band.Minimum == same.Minimum && band.Maximum == same.Maximum && band.Seed == same.Seed,
                    "Height bands are deterministic and independent from platform indexes.");
                if (!spawned) continue;
                float center = (band.Minimum + band.Maximum) * 0.5f;
                Require(center - lastCenter >= 15f && center - lastCenter <= 25f,
                    "Sparse coin opportunities stay 15–25 world units apart.");
                Require(!schedule.TryTakeCoinBand(height, out _) && !duplicate.TryTakeCoinBand(height, out _),
                    "The same revealed height never spawns a second coin.");
                lastCenter = center;
                coins++;
            }
        }
        Debug.Log($"RARE_PICKUP_SCHEDULE_PASS: 256000 platforms / 128 attempts; {coins} independent height bands, {bonuses} bonuses; spacing and reset checks passed.");
    }

    private static RunPickup MakeCoin(GameObject sandbox, List<RunPickup> coins, Camera camera, Vector3 position, float scale)
    {
        GameObject root = Child(sandbox, "Test coin");
        root.transform.position = position;
        RunPickup pickup = root.AddComponent<RunPickup>();
        Transform art = Child(root, "Art").transform;
        art.localScale = Vector3.one * scale;
        Set(pickup, "art", art);
        Set(pickup, "sceneCamera", camera);
        coins.Add(pickup);
        return pickup;
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name) { hideFlags = HideFlags.DontSave };
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    private static bool Claim(RunPickup pickup, Transform target, float width) =>
        (bool)typeof(RunPickup).GetMethod("TryClaim", Instance).Invoke(pickup, new object[] { target, width });
    private static void Tick(RunPickup pickup, float dt) =>
        typeof(RunPickup).GetMethod("TickAttraction", Instance).Invoke(pickup, new object[] { dt });
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Instance).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
    private static void Near(float actual, float expected, string message) => Require(Mathf.Abs(actual - expected) < 0.005f, message);
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Coin attraction validation: " + message);
    }
}
