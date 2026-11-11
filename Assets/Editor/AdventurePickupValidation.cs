#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Narrow checks for the new pickups. Scheduling is plain deterministic C#;
// reward / attraction checks use disposable profile keys and scene objects.
public static class AdventurePickupValidation
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static int checks;

    public static int ValidateSchedule()
    {
        checks = 0;
        UnityEngine.Random.State random = UnityEngine.Random.state;
        int rockets = 0;
        for (uint seed = 0; seed < 64; seed++)
        {
            var current = new PickupSpawnSchedule(seed);
            var duplicate = new PickupSpawnSchedule(seed);
            var legacy = new LegacySchedule(seed);
            var ordinaryRows = new List<int>();
            var rocketRows = new List<int>();
            int lastRocket = -1;
            for (int index = 0; index < 2000; index++)
            {
                bool oldBonus = legacy.TryBonus(index, out int oldKind);
                bool normal = current.TryGetKind(index, out int kind);
                Check(normal == oldBonus && kind == oldKind,
                    "New content preserves the exact legacy bonus row and kind sequence.");
                Check(duplicate.TryGetKind(index, out int duplicateKind) == normal && duplicateKind == kind,
                    "Independent attempts reproduce ordinary bonus scheduling.");
                if (normal) ordinaryRows.Add(index);
                bool rocket = current.TryRocketForPlatform(index, 1000, out int rocketKind);
                Check(duplicate.TryRocketForPlatform(index, 1000, out int sameRocket) == rocket && sameRocket == rocketKind,
                    "Independent rocket clocks reproduce the same rare rows.");
                Check(!current.TryRocketForPlatform(index, 1000, out _), "The same row cannot emit a second rocket.");
                if (!rocket) continue;
                Check(rocketKind == 4 && !normal, "Rocket has its own kind and never replaces an old bonus.");
                Check(lastRocket < 0 ? index >= 55 && index <= 82 : index - lastRocket >= 100 && index - lastRocket <= 157,
                    "Rare rocket intervals include only the small ordinary-bonus collision deferral.");
                rocketRows.Add(index);
                lastRocket = index;
                rockets++;
            }
            foreach (int rocket in rocketRows)
            foreach (int normal in ordinaryRows)
                Check(Math.Abs(rocket - normal) >= 4, "Rocket rows cannot cluster with shield, magnet or slow rows.");
            for (int i = 0; i < 100; i++)
            {
                bool first = current.TryTakeCoinBand(5000, out PickupSpawnSchedule.CoinBand band);
                bool old = legacy.TryBand(5000, out PickupSpawnSchedule.CoinBand previous);
                Check(first == old && band.Minimum == previous.Minimum && band.Maximum == previous.Maximum && band.Seed == previous.Seed,
                    "Rocket scheduling never consumes or changes the old air-coin random stream.");
            }
            Check(rocketRows.Count > 10 && rocketRows.Count < 20, "A long route gets rare rockets, not a dense extra power chain.");
        }
        var delayed = new PickupSpawnSchedule(98765);
        int afterGate = -1;
        for (int index = 0; index <= 110; index++)
        {
            delayed.TryGetKind(index, out _);
            bool rocket = delayed.TryRocketForPlatform(index, index < 100 ? 249 : 250, out _);
            if (index < 100) Check(!rocket, "No rocket can bypass the raw-height 250 introduction gate.");
            else if (rocket) afterGate = index;
        }
        Check(afterGate >= 100 && afterGate <= 107, "An overdue first rocket waits for a free row after its height gate, not a backlog.");
        Check(UnityEngine.Random.state.Equals(random), "All added scheduling leaves UnityEngine.Random untouched.");
        Debug.Log($"ADVENTURE_PICKUP_SCHEDULE_PASS checks={checks} seeds=64 platforms=128000 rockets={rockets} legacyStreamsExact=True");
        return checks;
    }

    public static int ValidatePickups()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Pickup integration requires an isolated Play Mode QA run.");
        checks = 0;
        EditorUtility.audioMasterMute = true;
        AudioListener.volume = 0;
        AudioListener.pause = true;
        bool active = RunFlow.RunActive, transition = RunFlow.IsTransitioning;
        float timeScale = Time.timeScale;
        float startHeight = WorldZones.RunStartHeight, pointsPerUnit = WorldZones.PointsPerUnit;
        int height = WorldZones.CurrentHeight;
        var registry = (List<RunPickup>)typeof(RunPickup).GetField("Active", Static).GetValue(null);
        var original = new List<RunPickup>(registry);
        var pickups = new List<RunPickup>();
        var priorPower = typeof(RunPowerups).GetField("instance", Static).GetValue(null);
        FieldInfo activeBridge = typeof(RunChallenges).GetField("<Active>k__BackingField", Static);
        object priorBridge = activeBridge.GetValue(null);
        GameObject root = new GameObject("Adventure pickup validation") { hideFlags = HideFlags.DontSave };
        try
        {
            using (PlayerProgress.UseIsolatedStorageForValidation(out _, out _))
            {
                registry.Clear();
                RunFlow.RunActive = RunFlow.IsTransitioning = false;
                Check(PlayerProgress.SelectChallenge(string.Empty), "Fixture starts without a selected task reward.");
                RunFlow.RunActive = true;
                Time.timeScale = 1;
                WorldZones.ConfigureRun(0, 1);
                GameObject cat = new GameObject("Disabled fixture cat", typeof(SpriteRenderer), typeof(BoxCollider2D), typeof(Rigidbody2D));
                cat.transform.SetParent(root.transform, false);
                cat.transform.position = new Vector3(8000, 8000, 0);
                Rigidbody2D body = cat.GetComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic;
                Player player = cat.AddComponent<Player>();
                player.enabled = false; // Never starts a real game, record or journal.
                RunPowerups powers = cat.AddComponent<RunPowerups>();
                powers.enabled = false;
                Set(player, "powers", powers);
                Set(player, "rb", body);
                RunChallenges bridge = RunChallenges.Attach(player);
                bridge.enabled = false;
                Set(player, "<Challenges>k__BackingField", bridge);
                GameObject cameraObject = new GameObject("Fixture camera", typeof(Camera));
                cameraObject.transform.SetParent(root.transform, false);
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 5;
                camera.transform.position = cat.transform.position + Vector3.back * 10;

                RunPickup coin = RunPickup.SpawnAirCoin(cat.transform.position, camera, 0);
                pickups.Add(coin);
                Check(coin != null && coin.KindId == 0 && coin.Value == 1 && coin.IsCoinReward && coin.transform.parent == null,
                    "Old coin API and kind/value remain unchanged.");
                Collect(cat.transform.position, powers);
                Check(PlayerProgress.Coins == 1 && bridge.Session.Stats.Coins == 1, "Old coin still grants exactly one unit.");
                Check(bridge.Session.Stats.GoldenFish == 0, "Ordinary coin units cannot impersonate a golden fish.");
                for (int i = 0; i < 2; i++)
                {
                    RunPickup fish = RunPickup.SpawnGoldenFish(cat.transform.position, camera, i);
                    pickups.Add(fish);
                    Check(fish != null && fish.KindId == 5 && fish.IsCoinReward && fish.Value == 5 && fish.transform.parent == null,
                        "Golden fish is a separate five-coin air reward, not a power or platform child.");
                    Check(fish.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount > 0, "Golden fish uses real generated artwork.");
                    Collect(cat.transform.position, powers);
                    int expected = 1 + (i + 1) * 5;
                    Check(PlayerProgress.Coins == expected && bridge.Session.Stats.Coins == expected &&
                        (int)typeof(Player).GetField("airCoinsThisAttempt", Instance).GetValue(player) == expected,
                        "Wallet, session metrics and physical-player coin count all receive five units once.");
                    Check(bridge.Session.Stats.GoldenFish == i + 1 &&
                        bridge.Session.Stats.GoldenFishWithoutMagnet == i + 1,
                        "New quests count one physical golden fish, not its five-unit coin value.");
                    Check(Get<bool>(fish, "collected") && Get<bool>(fish, "attracting") && !registry.Contains(fish),
                        "Fish claims before callbacks and uses the existing short coin attraction.");
                    Collect(cat.transform.position, powers);
                    Check(PlayerProgress.Coins == expected && bridge.Session.Stats.Coins == expected,
                        "Repeated contact / sweep during attraction cannot duplicate a golden reward.");
                    Check(bridge.Session.Stats.GoldenFish == i + 1,
                        "Repeated collection callbacks cannot farm golden-fish quest progress.");
                }
                Check(PlayerProgress.Owns("accessory_coin_crown"), "A five-unit reward crossing 10 still grants the old coin achievement.");
                powers.Collect(RunPowerups.Kind.Magnet);
                RunPickup magneticFish = RunPickup.SpawnGoldenFish(cat.transform.position, camera, 2.5f);
                pickups.Add(magneticFish);
                Collect(cat.transform.position, powers);
                Check(bridge.Session.Stats.GoldenFish == 3 &&
                    bridge.Session.Stats.GoldenFishWithMagnet == 1 &&
                    bridge.Session.Stats.GoldenFishWithoutMagnet == 2,
                    "The real pickup bridge distinguishes active-magnet collection from earlier manual fish.");
                int normalWallet = PlayerProgress.Coins;
                PlayerProgress.BeginAdminSandbox();
                try
                {
                    RunPickup fish = RunPickup.SpawnGoldenFish(cat.transform.position, camera, 3);
                    pickups.Add(fish);
                    Collect(cat.transform.position, powers);
                    Check(PlayerProgress.Coins == normalWallet + 5 && bridge.Session.Stats.Coins == normalWallet,
                        "Admin fish rewards remain disposable and never advance legitimate coin metrics.");
                    Check(bridge.Session.Stats.GoldenFish == 3,
                        "Admin collection never advances the new physical-fish counter.");
                }
                finally { PlayerProgress.EndAdminSandbox(); }
                Check(PlayerProgress.Coins == normalWallet, "Leaving admin cannot transfer a golden reward into the real wallet.");
                Set(player, "testAttempt", true);
                RunPickup invalid = RunPickup.SpawnGoldenFish(cat.transform.position, camera, 4);
                pickups.Add(invalid);
                Collect(cat.transform.position, powers);
                Check(PlayerProgress.Coins == normalWallet && Get<bool>(invalid, "collected"),
                    "A cheat-assisted attempt consumes the visual without minting normal gold.");

                RunPickup unclaimed = RunPickup.SpawnGoldenFish(cat.transform.position + Vector3.right * 3, camera, 5);
                pickups.Add(unclaimed);
                RunPickup.RemoveUnclaimedAerialBonuses();
                Check(unclaimed.isActiveAndEnabled, "A fish is not accidentally removed as an aerial power.");
                RunPickup.RemoveUnclaimedCoins();
                Check(!unclaimed.isActiveAndEnabled, "Layout invalidation removes unclaimed golden and ordinary coin rewards together.");

                GameObject platform = new GameObject("Rocket fixture platform", typeof(BoxCollider2D));
                platform.transform.SetParent(root.transform, false);
                platform.transform.position = new Vector3(8000, 400, 0);
                BoxCollider2D platformCollider = platform.GetComponent<BoxCollider2D>();
                float beforeSync = platformCollider.bounds.max.y;
                // Unlike Instantiate(prefab, position), this synchronous fixture
                // creates its collider at the origin and then moves it. Publish
                // that transform before the production height gate reads bounds.
                Physics2D.SyncTransforms();
                float afterSync = platformCollider.bounds.max.y;
                Debug.Log($"ADVENTURE_PICKUP_FIXTURE_PHYSICS beforeY={beforeSync:F2} afterY={afterSync:F2} rawHeight={WorldZones.ScoreAtHeight(afterSync)}");
                Check(Mathf.Abs(afterSync - 400.5f) < 0.01f && WorldZones.ScoreAtHeight(afterSync) >= 250,
                    "The moved test collider reaches its intended height before testing the production rocket gate.");
                var schedule = new PickupSpawnSchedule(7331);
                bool foundRocket = false;
                for (int index = 0; index < 100; index++)
                {
                    RunPickup bonus = RunPickup.SpawnBonusForPlatform(platform, index, schedule);
                    if (bonus == null) continue;
                    pickups.Add(bonus);
                    Check(!bonus.IsCoinReward && bonus.Value == 0 && bonus.KindId >= 1 && bonus.KindId <= 4,
                        "Platform factory emits old powers or a rocket, never a golden coin.");
                    if (bonus.KindId == 4)
                    {
                        Check(bonus.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount > 0 &&
                            bonus.transform.parent == platform.transform, "Rare rocket has real artwork and the normal optional platform fallback.");
                        foundRocket = true;
                        break;
                    }
                    Object.DestroyImmediate(bonus.gameObject);
                }
                Check(foundRocket, "The production bonus factory is connected to the independent rocket clock.");
                ValidateGoldenLandingBridge(root, pickups, camera);
            }
        }
        finally
        {
            foreach (RunPickup pickup in pickups) if (pickup != null) Object.DestroyImmediate(pickup.gameObject);
            Object.DestroyImmediate(root);
            registry.Clear();
            foreach (RunPickup pickup in original) if (pickup != null && pickup.isActiveAndEnabled) registry.Add(pickup);
            typeof(RunPowerups).GetField("instance", Static).SetValue(null, priorPower);
            activeBridge.SetValue(null, priorBridge);
            RunFlow.RunActive = false;
            WorldZones.ConfigureRun(startHeight, pointsPerUnit);
            WorldZones.SetScore(height);
            RunFlow.RunActive = active;
            RunFlow.IsTransitioning = transition;
            Time.timeScale = timeScale;
        }
        Debug.Log($"ADVENTURE_PICKUP_RUNTIME_PASS checks={checks} goldenValue=5 exactOnce=True oldCoinCompatible=True");
        return checks;
    }

    private static void Collect(Vector2 position, RunPowerups powers) =>
        RunPickup.CollectAlong(position, position, Vector2.one * 0.1f, 10, powers, false);

    private static void ValidateGoldenLandingBridge(GameObject root, List<RunPickup> pickups, Camera camera)
    {
        GameObject cat = new GameObject("Golden landing bridge probe", typeof(SpriteRenderer), typeof(BoxCollider2D), typeof(Rigidbody2D));
        cat.transform.SetParent(root.transform, false);
        // A collider added beside an empty SpriteRenderer has zero auto-fit
        // dimensions. This fixture supplies an explicit physical foot box.
        cat.GetComponent<BoxCollider2D>().size = Vector2.one;
        Rigidbody2D body = cat.GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.interpolation = RigidbodyInterpolation2D.None;
        Player player = cat.AddComponent<Player>(); player.enabled = false;
        RunPowerups powers = cat.AddComponent<RunPowerups>(); powers.enabled = false;
        Set(player, "powers", powers); Set(player, "rb", body);
        RunChallenges bridge = RunChallenges.Attach(player); bridge.enabled = false;
        Set(player, "<Challenges>k__BackingField", bridge);

        void Land(int row)
        {
            GameObject pad = new GameObject("Golden support " + row, typeof(BoxCollider2D));
            pad.transform.SetParent(root.transform, false); pad.tag = "Platform";
            pad.transform.position = new Vector2(8100f, 8100f + row * 2f);
            BoxCollider2D box = pad.GetComponent<BoxCollider2D>(); box.size = new Vector2(4f, 0.2f);
            Physics2D.SyncTransforms();
            Vector2 position = new Vector2(8100f, box.bounds.max.y + 0.5f);
            body.position = position; cat.transform.position = position;
            Physics2D.SyncTransforms();
            bridge.RecordLanding(box);
            Debug.Log($"SKY_GOLD_LANDING_FIXTURE row={row} foot={cat.GetComponent<Collider2D>().bounds.min.y:F4} top={box.bounds.max.y:F4} launch={Get<bool>(bridge, "hasLaunch")} highest={Get<float>(bridge, "highestLandingY"):F4} pending={Get<bool>(bridge.Session, "pendingGoldenFish")} safe={bridge.Session.Stats.GoldenFishSafeLandings} scale={GameplayPreferences.ModelScale:F4}");
        }
        void Fish()
        {
            RunPickup fish = RunPickup.SpawnGoldenFish(cat.transform.position, camera, 0f);
            pickups.Add(fish); Collect(cat.transform.position, powers);
        }

        Land(0);
        Fish();
        Check(bridge.Session.Stats.GoldenFish == 1 && bridge.Session.Stats.GoldenFishSafeLandings == 0,
            "A genuine golden pickup does not claim a safe return before any landing.");
        typeof(RunChallenges).GetMethod("LateUpdate", Instance).Invoke(bridge, null);
        Check(Get<bool>(bridge.Session, "pendingGoldenFish"),
            "A queued previous bounce cannot erase a fish picked up later in that same launch frame.");
        Land(1);
        Check(bridge.Session.Stats.GoldenFishSafeLandings == 1,
            "The same pickup earns a safe-return event on the next new higher physical support.");
        bridge.RecordAssistedFlight();
        Fish(); Land(2); Land(3);
        Check(bridge.Session.Stats.GoldenFish == 2 && bridge.Session.Stats.GoldenFishSafeLandings == 1,
            "A rocket-assisted fish and its automatic handoff cannot impersonate a self-made safe return.");
        Fish(); bridge.RecordShieldUse(false); Land(4);
        Check(bridge.Session.Stats.GoldenFishSafeLandings == 1,
            "Shield contact cancels a pending golden safe return in the real bridge.");
        int before = bridge.Session.Stats.GoldenFish;
        Time.timeScale = 0f; Fish(); Time.timeScale = 1f;
        Check(bridge.Session.Stats.GoldenFish == before,
            "Paused physical pickup collection cannot advance a golden quest.");
        bridge.RecordBush();
        // Model the next physics step, where the support collision is delivered
        // before the bush trigger. Contact collection must retain the deferred
        // landing until that trigger can identify it as another bush bounce.
        Set(bridge, "lastBushStep", Time.fixedTime - 1f);
        Land(5);
        bridge.BeginContactCollection(true);
        try { Fish(); }
        finally { bridge.EndContactCollection(); }
        bridge.RecordBush();
        Check(bridge.Session.Stats.BushChain == 2,
            "A golden pickup during collision-first bush contact cannot break the existing two-bush chain.");
        bridge.EndRun(); bridge.RecordBeeDodge(); bridge.RecordRocketLaunch(); bridge.RecordRocketEnd(true);
        Check(bridge.Session.Stats.BeeDodges == 0 && bridge.Session.Stats.RocketLandings == 0,
            "An ended attempt ignores late sky-content callbacks.");
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Instance).SetValue(target, value);
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Instance).GetValue(target);
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new InvalidOperationException("Adventure pickups: " + message);
    }

    // Frozen v0.0.3 ordinary clocks: a baseline, not the new implementation
    // under a second name. New schedules must match these exact sequences.
    private sealed class LegacySchedule
    {
        private uint state, coinState;
        private int nextBonus, nextKind;
        private float nextCoinHeight;
        public LegacySchedule(uint seed)
        {
            state = seed == 0 ? 0x9e3779b9u : seed;
            coinState = state ^ 0x85ebca6bu;
            if (coinState == 0) coinState = 0x27d4eb2du;
            nextCoinHeight = CoinRange(15, 25);
            nextBonus = Range(18, 28);
            nextKind = Range(1, 3);
        }
        public bool TryBonus(int index, out int kind)
        {
            kind = -1;
            if (index < nextBonus) return false;
            kind = nextKind;
            nextKind = nextKind % 3 + 1;
            nextBonus = index + Range(18, 28);
            return true;
        }
        public bool TryBand(float knownHeight, out PickupSpawnSchedule.CoinBand band)
        {
            band = default;
            if (knownHeight < nextCoinHeight + 2) return false;
            band = new PickupSpawnSchedule.CoinBand(nextCoinHeight, CoinRange(0, 1000000));
            nextCoinHeight += CoinRange(15, 25);
            return true;
        }
        private int Range(int min, int max) => Next(ref state, min, max);
        private int CoinRange(int min, int max) => Next(ref coinState, min, max);
        private static int Next(ref uint value, int min, int max)
        {
            value ^= value << 13; value ^= value >> 17; value ^= value << 5;
            return min + (int)(value % (uint)(max - min + 1));
        }
    }
}
#endif
