using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Synchronous presentation sandbox, with no rewards, saves, real player changes
// or frame yields. RunPowerups.Start/Player.Start are never run on these objects.
public static class BuffVisualValidation
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

    [MenuItem("Doodlik/Validate Player Buff Visuals (Play Mode)")]
    public static void Validate()
    {
        Require(Application.isPlaying, "Run this check in Play Mode.");
        bool savedRun = RunFlow.RunActive, savedTransition = RunFlow.IsTransitioning;
        float savedTime = Time.timeScale;
        FieldInfo instance = typeof(RunPowerups).GetField("instance", Static);
        object savedInstance = instance.GetValue(null);
        var cleanup = new List<Object>();
        int cases = 0;
        try
        {
            RunFlow.RunActive = true;
            RunFlow.IsTransitioning = false;
            Time.timeScale = 1f;
            foreach (float scale in new[] { 1f, 0.64f })
            {
                var root = new GameObject("Buff presentation validation") { hideFlags = HideFlags.DontSave };
                cleanup.Add(root);
                root.transform.position = new Vector3(8000f, 8000f);
                root.transform.localScale = Vector3.one * scale;
                Player player = root.AddComponent<Player>();
                player.enabled = false;
                var visualObject = new GameObject("Unflipped cat visual") { hideFlags = HideFlags.DontSave };
                visualObject.transform.SetParent(root.transform, false);
                SpriteRenderer cat = visualObject.AddComponent<SpriteRenderer>();
                Texture2D texture = Texture2D.whiteTexture;
                Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                    Vector2.one * 0.5f, texture.width);
                cleanup.Add(sprite);
                cat.sprite = sprite;
                cat.sortingOrder = 2;
                RunPowerups powers = root.AddComponent<RunPowerups>();
                PlayerBuffVisuals effects = PlayerBuffVisuals.Attach(powers);
                Require(effects != null && PlayerBuffVisuals.Attach(powers) == effects, "Repeated attachment must not duplicate effects.");
                Refresh(effects, 0f);
                Transform group = root.transform.Find("Active bonus effects");
                Require(group != null && group.parent == root.transform, "Effects must be siblings of the flipped/squashed sprite.");
                Transform shield = group.Find("Shield bubble"), magnet = group.Find("Magnet"), clock = group.Find("Slow clock");
                Require(!shield.gameObject.activeSelf && !magnet.gameObject.activeSelf && !clock.gameObject.activeSelf,
                    "No active bonus means no visual clutter.");

                Set(powers, "shield", true); Set(powers, "magnetTime", 8f); Set(powers, "slowTime", 6f);
                Refresh(effects, 0.2f);
                Require(shield.gameObject.activeSelf && magnet.gameObject.activeSelf && clock.gameObject.activeSelf,
                    "All three powers have visible effects at once.");
                Require(powers.HasShield && powers.MagnetTimeRemaining == 8f && powers.SlowTimeRemaining == 6f,
                    "Presentation must not consume buffs or change gameplay timers.");
                Require(shield.GetComponent<MeshRenderer>().sortingOrder < cat.sortingOrder,
                    "Shield outline must stay behind the cat.");
                Require(magnet.GetComponent<MeshRenderer>().bounds.max.x < cat.bounds.min.x &&
                    clock.GetComponent<MeshRenderer>().bounds.min.x > cat.bounds.max.x,
                    "Bonus icons must sit beside the cat rather than covering its body.");
                MeshRenderer[] drawings = group.GetComponentsInChildren<MeshRenderer>(true);
                Require(drawings.Length == 4 && group.GetComponentsInChildren<ParticleSystem>(true).Length == 0,
                    "Effects use a fixed four small drawings, without particles.");
                Material shared = drawings[0].sharedMaterial;
                foreach (MeshRenderer drawing in drawings)
                {
                    Require(drawing.sharedMaterial == shared, "Drawings share one material, not material instances per update.");
                    Mesh mesh = drawing.GetComponent<MeshFilter>().sharedMesh;
                    Require(mesh != null && mesh.vertexCount > 0 && mesh.vertexCount < 600, "Each effect remains a small nonempty mesh.");
                }
                Vector3 magnetBeforeFlip = magnet.position;
                cat.flipX = true;
                Refresh(effects, 0f);
                Require(magnet.position == magnetBeforeFlip && magnet.lossyScale.x > 0f,
                    "Turning the cat must not mirror or exchange bonus icons.");

                Set(powers, "magnetTime", 1.7f); Set(powers, "slowTime", 1.7f);
                float smallest = float.MaxValue, largest = float.MinValue;
                for (int i = 0; i < 120; i++)
                {
                    Refresh(effects, 1f / 60f);
                    smallest = Mathf.Min(smallest, magnet.lossyScale.x);
                    largest = Mathf.Max(largest, magnet.lossyScale.x);
                    Require(magnet.gameObject.activeSelf && clock.gameObject.activeSelf, "Expiration warning must never strobe/blink off.");
                }
                Require(largest > smallest * 1.03f && largest < smallest * 1.15f, "Last two seconds use only a restrained smooth size pulse.");
                Vector3 pausedSize = magnet.localScale;
                Quaternion pausedClock = clock.localRotation;
                float pausedPhase = Get<float>(effects, "animationTime");
                Time.timeScale = 0f;
                Refresh(effects, 10f);
                Require(magnet.localScale == pausedSize && clock.localRotation == pausedClock &&
                    Mathf.Approximately(Get<float>(effects, "animationTime"), pausedPhase), "Pause freezes all animation phases.");
                Time.timeScale = 1f;

                Set(powers, "shield", false); Set(powers, "protectionTime", 1.5f);
                Refresh(effects, 0f);
                Require(shield.gameObject.activeSelf, "Shield consumption produces a brief visible release.");
                Refresh(effects, 0.4f);
                Require(!shield.gameObject.activeSelf && powers.IsProtected, "The release ends independently of the unchanged immunity timer.");
                Set(powers, "magnetTime", 0f); Set(powers, "slowTime", 0f);
                Refresh(effects, 0f);
                Require(!magnet.gameObject.activeSelf && !clock.gameObject.activeSelf, "Expired powers immediately hide their indicators.");

                Set(powers, "shield", true); Set(powers, "magnetTime", 4f);
                Set(player, "isDead", true);
                Refresh(effects, 0f);
                Require(!shield.gameObject.activeSelf && !magnet.gameObject.activeSelf, "Defeat hides effects even if a bonus remained.");
                Set(player, "isDead", false);
                RunFlow.RunActive = false;
                Refresh(effects, 0f);
                Require(!shield.gameObject.activeSelf && !magnet.gameObject.activeSelf, "Home screen hides active-effect presentation.");
                RunFlow.RunActive = true;
                RunFlow.IsTransitioning = true;
                Refresh(effects, 0f);
                Require(!shield.gameObject.activeSelf, "Scene transition cannot leak an effect behind the menu.");
                RunFlow.IsTransitioning = false;
                powers.enabled = false;
                Refresh(effects, 0f);
                Require(!shield.gameObject.activeSelf, "A disabled power owner must hide effects.");
                cases++;
            }
            Debug.Log($"BUFF_VISUALS_PASS: {cases} scales; all bonus icons, no body overlap/mirroring, shared small meshes, 1 Hz expiry pulse, pause, shield release, expiry, defeat/home/transition/disable. No saves changed.");
        }
        finally
        {
            for (int i = cleanup.Count - 1; i >= 0; i--) if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            instance.SetValue(null, savedInstance);
            RunFlow.RunActive = savedRun;
            RunFlow.IsTransitioning = savedTransition;
            Time.timeScale = savedTime;
        }
    }

    private static void Refresh(PlayerBuffVisuals effects, float dt) =>
        typeof(PlayerBuffVisuals).GetMethod("RefreshPresentation", Instance).Invoke(effects, new object[] { dt });
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Instance).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Buff presentation validation: " + message);
    }
}
