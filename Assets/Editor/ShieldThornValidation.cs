using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Synchronous Play Mode regression checks. No scene, save or live-player changes.
public static class ShieldThornValidation
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

    public static void Validate()
    {
        Require(Application.isPlaying, "Run shield/thorn validation in Play Mode.");
        bool active = RunFlow.RunActive, transition = RunFlow.IsTransitioning;
        float timeScale = Time.timeScale;
        FieldInfo ownerField = typeof(RunPowerups).GetField("instance", Static);
        object savedOwner = ownerField.GetValue(null);
        var registry = (List<DoodlikHazard>)typeof(DoodlikHazard).GetField("Active", Static).GetValue(null);
        var savedHazards = new List<DoodlikHazard>(registry);
        GameObject root = new GameObject("Shield thorn validation sandbox");
        root.hideFlags = HideFlags.DontSave;
        Sprite sprite = null;
        try
        {
            RunFlow.RunActive = true;
            RunFlow.IsTransitioning = false;
            Time.timeScale = 1f;
            registry.Clear();

            Camera camera = Child(root, "Camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.aspect = 1f;
            camera.transform.position = new Vector3(8000f, 8000f, -10f);
            GameObject platform = Child(root, "Disabled thorn platform");
            platform.transform.position = new Vector2(8000f, 8000f);
            platform.tag = "Platform";
            BoxCollider2D surface = platform.AddComponent<BoxCollider2D>();
            surface.size = new Vector2(0.98f, 0.2f);
            surface.offset = new Vector2(0f, -0.155f);
            surface.enabled = false;
            GameObject thorns = Child(platform, "Thorns");
            DoodlikHazard hazard = thorns.AddComponent<DoodlikHazard>();

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
            Set(player, "rb", body);
            Set(player, "playerCollider", feet);
            Set(player, "mainCamera", camera);
            Set(player, "spriteRenderer", renderer);
            Set(player, "powers", powers);

            int checks = 0;
            foreach (float scale in new[] { 1f, 0.64f })
            {
                cat.transform.localScale = Vector3.one * scale;
                platform.transform.localScale = Vector3.one * scale;
                thorns.transform.localPosition = Vector3.zero;
                hazard.Initialize(DoodlikHazard.Kind.Thorns, camera, scale, 0f, 0f, 0f, default);
                Set(player, "hazardBodyHalfSize", Vector2.one * (0.325f * scale));
                float top = 8000f - 0.055f * scale;
                float width = scale < 1f ? 5.625f : 17.7778f;
                camera.aspect = width / 10f;

                Reset(player, powers);
                powers.Collect(RunPowerups.Kind.Shield);
                Place(body, player, renderer, feet, 8000f, top + 0.8f * scale, true);
                Place(body, player, renderer, feet, 8000f, top + 0.08f * scale, false);
                Invoke(player, "LateUpdate");
                Require(!player.IsDead && !powers.HasShield && powers.IsProtected,
                    "Early body contact consumes a shield, not the cat."); checks++;
                Require(body.linearVelocity.y < 0f && feet.bounds.min.y > top,
                    "No mid-air bounce before the paws reach the platform."); checks++;

                Place(body, player, renderer, feet, 8000f, top - 0.05f * scale, false);
                Invoke(player, "LateUpdate");
                Require(!player.IsDead && Mathf.Approximately(body.linearVelocity.y, player.NormalJumpForce),
                    "Protected downward landing produces the ordinary jump."); checks++;
                Require(Mathf.Abs(feet.bounds.min.y - top - 0.01f) < 0.002f,
                    "The paws are placed on the actual platform surface."); checks++;
                Require(!surface.enabled, "The trap collider remains disabled for unprotected players."); checks++;

                Reset(player, powers);
                powers.Collect(RunPowerups.Kind.Shield);
                Place(body, player, renderer, feet, 8000f, top + 1.4f * scale, true);
                Place(body, player, renderer, feet, 8000f, top - 1.1f * scale, false);
                Invoke(player, "LateUpdate");
                Require(!player.IsDead && body.linearVelocity.y > 0f && feet.bounds.min.y > top,
                    "A long frame crossing the whole trap still lands safely."); checks++;

                Reset(player, powers);
                Place(body, player, renderer, feet, 8000f, top + 0.8f * scale, true);
                Place(body, player, renderer, feet, 8000f, top - 0.1f * scale, false);
                Invoke(player, "LateUpdate");
                Require(player.IsDead && Time.timeScale == 0f,
                    "The same landing without a shield remains lethal."); checks++;

                Reset(player, powers);
                powers.Collect(RunPowerups.Kind.Shield);
                Place(body, player, renderer, feet, 8000f, top - 0.6f * scale, true);
                Place(body, player, renderer, feet, 8000f, top - 0.3f * scale, false);
                body.linearVelocity = Vector2.up * 5f;
                Invoke(player, "LateUpdate");
                Require(!player.IsDead && Mathf.Approximately(body.linearVelocity.y, 5f),
                    "Protected underside contact does not create a jump."); checks++;

                Require(!DoodlikHazard.FindProtectedLanding(new Vector2(8000f, top - 0.1f),
                    new Vector2(8000.1f, top - 0.3f), 0.25f * scale, width, out _),
                    "Side contact below the surface is not a landing."); checks++;
                Require(!DoodlikHazard.FindProtectedLanding(new Vector2(8003f, top + 0.2f),
                    new Vector2(8003f, top - 0.3f), 0.25f * scale, width, out _),
                    "Crossing surface height away from the platform does not bounce."); checks++;

                platform.transform.position = new Vector2(8000f - width * 0.5f + 0.2f, 8000f);
                Require(DoodlikHazard.FindProtectedLanding(new Vector2(8000f + width * 0.5f - 0.1f, top + 0.2f),
                    new Vector2(8000f - width * 0.5f + 0.3f, top - 0.2f), 0.25f * scale, width, out _),
                    "Landing across the screen seam follows the short wrapped path."); checks++;
                platform.transform.position = new Vector2(8000f, 8000f);
            }

            hazard.Initialize(DoodlikHazard.Kind.FlyingSeed, camera, 1f, 0f, 0f, 0f, default);
            Require(!DoodlikHazard.FindProtectedLanding(new Vector2(8000f, 8001f),
                new Vector2(8000f, 7999f), 0.25f, 10f, out _), "Flying seeds never become platforms.");
            Debug.Log("SHIELD_THORN_PASS: " + (checks + 1) +
                " checks: delayed paw landing, shield consumption, low-FPS sweep, lethal unprotected contact, underside, side, wrap, portrait scale, seeds.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite);
            registry.Clear();
            foreach (DoodlikHazard hazard in savedHazards)
                if (hazard != null && hazard.isActiveAndEnabled) registry.Add(hazard);
            ownerField.SetValue(null, savedOwner);
            RunFlow.RunActive = active;
            RunFlow.IsTransitioning = transition;
            Time.timeScale = timeScale;
        }
    }

    private static GameObject Child(GameObject parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    private static void Reset(Player player, RunPowerups powers)
    {
        Set(player, "isDead", false);
        Set(powers, "shield", false);
        Set(powers, "protectionTime", 0f);
        Time.timeScale = 1f;
    }

    private static void Place(Rigidbody2D body, Player player, SpriteRenderer renderer,
        Collider2D collider, float x, float feetY, bool remember)
    {
        Physics2D.SyncTransforms();
        float offset = body.position.y - collider.bounds.min.y;
        body.position = new Vector2(x, feetY + offset);
        body.transform.position = body.position;
        body.linearVelocity = Vector2.down * 10f;
        Physics2D.SyncTransforms();
        if (!remember) return;
        Set(player, "previousHazardPosition", (Vector2)renderer.bounds.center);
        Set(player, "previousFeetPosition", new Vector2(collider.bounds.center.x, collider.bounds.min.y));
    }

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Instance).SetValue(target, value);
    private static void Invoke(object target, string name) =>
        target.GetType().GetMethod(name, Instance).Invoke(target, null);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Shield/thorn check failed: " + message);
    }
}
