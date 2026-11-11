#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Synchronous Play Mode fixture: no live Player, real record or wallet writes.
public static class AdminGameplayValidation
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static int checks;

    public static void Validate()
    {
        Require(Application.isPlaying, "Play Mode is required.");
        checks = 0;
        bool active = RunFlow.RunActive, transition = RunFlow.IsTransitioning;
        float timeScale = Time.timeScale;
        float zoneStart = WorldZones.RunStartHeight, zoneMultiplier = WorldZones.PointsPerUnit;
        int zoneScore = WorldZones.CurrentScore;
        FieldInfo cheat = typeof(AdminTools).GetField("<CheatMode>k__BackingField", Static);
        bool originalCheat = (bool)cheat.GetValue(null);
        FieldInfo owner = typeof(RunPowerups).GetField("instance", Static);
        object originalOwner = owner.GetValue(null);
        var hazards = (List<DoodlikHazard>)typeof(DoodlikHazard).GetField("Active", Static).GetValue(null);
        var originalHazards = new List<DoodlikHazard>(hazards);
        string recordKey = "DoodlikAdminGameplayValidation_" + Guid.NewGuid().ToString("N");
        GameObject root = new GameObject("Admin gameplay validation sandbox");
        root.hideFlags = HideFlags.DontSave;
        Sprite sprite = null;
        try
        {
            using (PlayerProgress.UseIsolatedStorageForValidation(out _, out _))
            {
                RunFlow.RunActive = true;
                RunFlow.IsTransitioning = false;
                Time.timeScale = 1f;
                hazards.Clear();

                Camera camera = Child(root, "Camera").AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                camera.aspect = 1f;
                camera.transform.position = new Vector3(8000f, 8000f, -10f);

                GameObject cat = Child(root, "Cat");
                Rigidbody2D body = cat.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic;
                BoxCollider2D feet = cat.AddComponent<BoxCollider2D>();
                feet.size = new Vector2(0.5f, 0.1f);
                feet.offset = new Vector2(0f, -0.45f);
                SpriteRenderer renderer = cat.AddComponent<SpriteRenderer>();
                Texture2D texture = Texture2D.whiteTexture;
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    Vector2.one * 0.5f, texture.width);
                renderer.sprite = sprite;
                Player player = cat.AddComponent<Player>();
                player.enabled = false;
                RunPowerups powers = cat.AddComponent<RunPowerups>();
                powers.enabled = false;
                Set(player, "rb", body);
                Set(player, "playerCollider", feet);
                Set(player, "mainCamera", camera);
                Set(player, "spriteRenderer", renderer);
                Set(player, "powers", powers);
                Set(player, "hazardBodyHalfSize", Vector2.one * 0.325f);
                player.pointsPerUnit = 1f;
                WorldZones.ConfigureRun(8000f, 1f);

                PlayerProgress.BeginAdminSandbox();
                cheat.SetValue(null, true);
                GameObject platform = Child(root, "Thorn platform");
                platform.transform.position = new Vector2(8000f, 8000f);
                platform.tag = "Platform";
                BoxCollider2D surface = platform.AddComponent<BoxCollider2D>();
                surface.size = new Vector2(2f, 0.2f);
                surface.enabled = false;
                DoodlikHazard thorn = Child(platform, "Thorns").AddComponent<DoodlikHazard>();
                thorn.transform.localPosition = Vector3.up * 0.05f;
                thorn.Initialize(DoodlikHazard.Kind.Thorns, camera, 1f, 0f, 1f, 0f, default);

                float top = 8000.1f;
                foreach (bool ownsShield in new[] { false, true })
                {
                    Set(powers, "shield", ownsShield);
                    Set(powers, "protectionTime", 0f);
                    Place(player, body, renderer, feet, new Vector2(8000f, top + 0.5f));
                    Set(player, "previousFeetPosition", new Vector2(8000f, top + 0.25f));
                    Place(player, body, renderer, feet, new Vector2(8000f, top - 0.15f), false);
                    body.linearVelocity = Vector2.down * 10f;
                    Call(player, "TryLandOnProtectedThorns", 10f);
                    Require(Mathf.Abs(body.linearVelocity.y - player.NormalJumpForce) < 0.001f,
                        "Cheat bounces from the real thorn surface without a shield.");
                    Require(Mathf.Abs(feet.bounds.min.y - (top + 0.01f)) < 0.003f,
                        "Cheat thorn bounce places paws on the surface.");
                    Require(powers.HasShield == ownsShield && !powers.IsProtected,
                        "Cheat thorn bounce does not spend a shield or grant temporary protection.");
                }
                Place(player, body, renderer, feet, new Vector2(8000f, top - 0.4f));
                Set(player, "previousFeetPosition", new Vector2(8000f, top - 0.2f));
                body.linearVelocity = Vector2.down * 4f;
                Call(player, "TryLandOnProtectedThorns", 10f);
                Require(body.linearVelocity.y < 0f, "Cheat cannot bounce off the underside/air.");
                Place(player, body, renderer, feet, new Vector2(8000f, top + 0.1f));
                body.linearVelocity = Vector2.up * 4f;
                Call(player, "TryLandOnProtectedThorns", 10f);
                Require(Mathf.Abs(body.linearVelocity.y - 4f) < 0.001f, "Ascending cats do not receive a thorn jump.");

                // A real manual hazard hit must not consume a held shield.
                Place(player, body, renderer, feet, new Vector2(8000f, 7999.71f));
                body.linearVelocity = Vector2.zero;
                Set(powers, "shield", true);
                Call(player, "LateUpdate");
                Require(!player.IsDead && powers.HasShield && !powers.IsProtected,
                    "Hazard contact is non-lethal and does not consume the shield in cheat mode.");
                Call(player, "Die", "test damage");
                Require(!player.IsDead, "All direct death paths respect cheat immunity.");

                Place(player, body, renderer, feet, new Vector2(8000f, 7992f));
                body.linearVelocity = new Vector2(3f, -18f);
                Call(player, "CheckDeath");
                Require(!player.IsDead && feet.bounds.min.y > 7995f && body.linearVelocity.y == player.NormalJumpForce,
                    "Without a safe platform, a fall returns to the bottom of the camera and jumps.");
                Require(powers.HasShield && !powers.IsProtected, "Admin fall rescue does not spend the shield.");
                Require(Vector2.Distance(Get<Vector2>(player, "previousFeetPosition"),
                    new Vector2(feet.bounds.center.x, feet.bounds.min.y)) < 0.001f,
                    "Teleport resets swept feet history.");

                GameObject safe = Child(root, "Safe rescue platform");
                safe.tag = "Platform";
                safe.transform.position = new Vector2(8001f, 7997f);
                BoxCollider2D support = safe.AddComponent<BoxCollider2D>();
                support.size = new Vector2(2f, 0.2f);
                Physics2D.SyncTransforms();
                Place(player, body, renderer, feet, new Vector2(8000f, 7992f));
                Call(player, "CheckDeath");
                Require(Mathf.Abs(feet.bounds.min.y - support.bounds.max.y - 0.03f) < 0.003f &&
                    Mathf.Abs(body.position.x - support.bounds.center.x) < 0.001f,
                    "A visible safe platform is preferred over the fallback.");

                cheat.SetValue(null, false);
                Set(powers, "shield", false);
                Set(powers, "protectionTime", 0f);
                Place(player, body, renderer, feet, new Vector2(8000f, 7999.71f));
                body.linearVelocity = Vector2.zero;
                Call(player, "LateUpdate");
                Require(player.IsDead, "Disabling cheat restores lethal thorns even while sandbox remains active.");
                Set(player, "isDead", false);
                Time.timeScale = 1f;
                Place(player, body, renderer, feet, new Vector2(8000f, 7992f));
                Call(player, "CheckDeath");
                Require(player.IsDead, "Ordinary falling remains lethal after disabling cheat.");
                Set(player, "isDead", false);
                Time.timeScale = 1f;
                PlayerProgress.EndAdminSandbox();

                cheat.SetValue(null, true);
                Call(player, "Die", "sandbox already ended");
                Require(player.IsDead, "A stale cheat flag cannot stay invulnerable outside the sandbox.");
                cheat.SetValue(null, false);
                Set(player, "isDead", false);
                Time.timeScale = 1f;

                RecordChecks(player, body, renderer, feet, recordKey, true);
                RecordChecks(player, body, renderer, feet, recordKey, false);

                PrepareRecord(player, recordKey, true);
                Place(player, body, renderer, feet, new Vector2(8000f, 8020f));
                Call(player, "UpdateHeightScore");
                int completedRecord = PlayerPrefs.GetInt(recordKey);
                Set(player, "isDead", true);
                PlayerProgress.BeginAdminSandbox();
                player.RefreshAdminState();
                Require(!player.IsTestAttempt && PlayerPrefs.GetInt(recordKey) == completedRecord,
                    "Opening admin after a completed ordinary attempt preserves its earned record.");
                PlayerProgress.EndAdminSandbox();
                Set(player, "isDead", false);

                PrepareRecord(player, recordKey, true);
                RunFlow.RunActive = false;
                PlayerProgress.BeginAdminSandbox();
                player.RefreshAdminState();
                Require(!player.IsTestAttempt, "Opening the sandbox at home does not taint a future attempt.");
                PlayerProgress.EndAdminSandbox();
                player.RefreshAdminState();
                RunFlow.RunActive = true;
                Place(player, body, renderer, feet, new Vector2(8000f, 8015f));
                Call(player, "UpdateHeightScore");
                Require(!player.IsTestAttempt && PlayerPrefs.GetInt(recordKey) == player.CurrentScore,
                    "Home enable-disable-start remains an ordinary record-eligible attempt.");

                int ordinaryCoins = PlayerProgress.Coins;
                PlayerProgress.BeginAdminSandbox();
                player.RefreshAdminState();
                int sandboxCoins = PlayerProgress.Coins;
                CollectFixtureCoin(player, powers, camera);
                Require(PlayerProgress.Coins == sandboxCoins + 1,
                    "An actual collected coin credits the active sandbox wallet.");
                PlayerProgress.EndAdminSandbox();
                player.RefreshAdminState();
                CollectFixtureCoin(player, powers, camera);
                Require(player.IsTestAttempt && PlayerProgress.Coins == ordinaryCoins,
                    "After sandbox exit a sticky test attempt claims coins without crediting the ordinary wallet.");

                GameObject freshCat = Child(root, "Fresh ordinary attempt");
                freshCat.transform.position = new Vector2(8000f, 8000f);
                freshCat.AddComponent<SpriteRenderer>().sprite = sprite;
                Player freshPlayer = freshCat.AddComponent<Player>();
                freshPlayer.enabled = false;
                RunPowerups freshPowers = freshCat.AddComponent<RunPowerups>();
                freshPowers.enabled = false;
                CollectFixtureCoin(freshPlayer, freshPowers, camera);
                Require(!freshPlayer.IsTestAttempt && PlayerProgress.Coins == ordinaryCoins + 1,
                    "A fresh ordinary attempt credits its collected coin normally.");
            }
            Debug.Log("ADMIN_GAMEPLAY_PASS checks=" + checks +
                " shield-free thorn support, no air bounce, immunity, fall recovery, sticky records, home lifecycle, coin credits.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite);
            hazards.Clear();
            hazards.AddRange(originalHazards);
            owner.SetValue(null, originalOwner);
            cheat.SetValue(null, originalCheat);
            PlayerPrefs.DeleteKey(recordKey);
            PlayerPrefs.Save();
            RunFlow.RunActive = false;
            WorldZones.ConfigureRun(zoneStart, zoneMultiplier);
            WorldZones.SetScore(zoneScore);
            RunFlow.RunActive = active;
            RunFlow.IsTransitioning = transition;
            Time.timeScale = timeScale;
        }
    }

    private static void CollectFixtureCoin(Player player, RunPowerups powers, Camera camera)
    {
        Vector2 center = player.GetComponent<SpriteRenderer>().bounds.center;
        RunPickup coin = RunPickup.SpawnAirCoin(center, camera, 0f);
        Require(coin != null, "The actual coin factory created the fixture collectible.");
        try
        {
            RunPickup.CollectAlong(center, center, Vector2.one * 0.325f, 10f, powers);
            Require(Get<bool>(coin, "collected") && Get<bool>(coin, "attracting"),
                "A coin is claimed and animates even when its reward is intentionally withheld.");
        }
        finally
        {
            if (coin != null) UnityEngine.Object.DestroyImmediate(coin.gameObject);
        }
    }

    private static void RecordChecks(Player player, Rigidbody2D body, SpriteRenderer renderer,
        BoxCollider2D feet, string key, bool existed)
    {
        PrepareRecord(player, key, existed);
        Place(player, body, renderer, feet, new Vector2(8000f, 8015f));
        Call(player, "UpdateHeightScore");
        PlayerPrefs.Save(); // Opening a pause menu may already have flushed it.
        Require(!player.IsTestAttempt && PlayerPrefs.GetInt(key) == player.CurrentScore,
            "Ordinary records still save before admin activation.");
        PlayerProgress.BeginAdminSandbox();
        player.RefreshAdminState();
        Require(player.IsTestAttempt && PlayerPrefs.HasKey(key) == existed &&
            (!existed || PlayerPrefs.GetInt(key) == 10), "Sandbox activation restores this attempt's prior record state.");
        Place(player, body, renderer, feet, new Vector2(8000f, 8025f));
        Call(player, "UpdateHeightScore");
        Require(PlayerPrefs.HasKey(key) == existed && (!existed || PlayerPrefs.GetInt(key) == 10),
            "Sandbox score does not write a record.");
        PlayerProgress.EndAdminSandbox();
        player.RefreshAdminState();
        Place(player, body, renderer, feet, new Vector2(8000f, 8035f));
        Call(player, "UpdateHeightScore");
        Require(player.IsTestAttempt && PlayerPrefs.HasKey(key) == existed &&
            (!existed || PlayerPrefs.GetInt(key) == 10), "Turning sandbox off cannot legitimise the same attempt.");
    }

    private static void PrepareRecord(Player player, string key, bool existed)
    {
        if (existed) PlayerPrefs.SetInt(key, 10); else PlayerPrefs.DeleteKey(key);
        Set(player, "currentHighScoreKey", key);
        Set(player, "startHeight", 8000f);
        Set(player, "maximumHeight", 8000f);
        Set(player, "score", 0);
        Set(player, "reachedHeight", 0);
        Set(player, "highScore", existed ? 10 : 0);
        Set(player, "highScoreAtRunStart", existed ? 10 : 0);
        Set(player, "scoreKeyExistedAtRunStart", existed);
        Set(player, "recordWrittenThisAttempt", false);
        Set(player, "testAttempt", false);
    }

    private static void Place(Player player, Rigidbody2D body, SpriteRenderer renderer,
        BoxCollider2D feet, Vector2 position, bool resetSweep = true)
    {
        body.position = position + Vector2.up * 0.5f;
        body.transform.position = body.position;
        Physics2D.SyncTransforms();
        if (!resetSweep) return;
        Set(player, "previousHazardPosition", (Vector2)renderer.bounds.center);
        Set(player, "previousFeetPosition", new Vector2(feet.bounds.center.x, feet.bounds.min.y));
    }

    private static GameObject Child(GameObject root, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(root.transform, false);
        return child;
    }
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Instance).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
    private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Instance).Invoke(target, args);
    private static void Require(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("Admin gameplay: " + message);
    }
}
#endif
