using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public sealed class RunPowerups : MonoBehaviour
{
    public enum Kind { Shield, Magnet, Slow, Rocket }
    public static float PlatformSpeedMultiplier => instance != null && instance.slowTime > 0f ? 0.55f : 1f;
    private static RunPowerups instance;
    private bool shield;
    private float magnetTime, slowTime, protectionTime;
    private string mode;
    private Player owner;
    private PaperRocketFlight rocket;
    public bool IsRocketActive => rocket != null && rocket.IsActive;
    public bool IsRocketFlying => rocket != null && rocket.IsFlying;
    public float RocketTimeRemaining => rocket != null ? rocket.TimeRemaining : 0f;
    public float RocketDuration => PaperRocketFlight.FlightDuration;
    private TMP_Text hud;
    private int displayedCoins = -1, displayedZone = -1;
    public float PickupRadius => 0.18f + PlayerProgress.PickupRadiusBonus(mode) + (magnetTime > 0f ? 1.3f : 0f);
    public bool HasShield => shield;
    public bool IsProtected => protectionTime > 0f;
    public float MagnetTimeRemaining => magnetTime;
    public float SlowTimeRemaining => slowTime;
    public float ProtectionTimeRemaining => protectionTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => instance = null;

    private void Awake() { mode = RunFlow.ModeKey; owner = GetComponent<Player>(); }
    private void Start() { PlayerBuffVisuals.Attach(this); RocketVisuals.Attach(this); }
    private void OnEnable() { instance = this; }
    private void OnDisable() { if (instance == this) instance = null; }

    public void CreateHud(TMP_Text scoreText)
    {
        if (scoreText == null || hud != null) return;
        GameObject root = new GameObject("Journey HUD", typeof(RectTransform));
        root.transform.SetParent(scoreText.transform.parent, false);
        hud = root.AddComponent<TextMeshProUGUI>();
        displayedCoins = -1;
        hud.font = scoreText.font;
        hud.fontSize = 25f;
        hud.color = new Color(0.24f, 0.14f, 0.08f);
        hud.raycastTarget = false;
        hud.textWrappingMode = TextWrappingModes.NoWrap;
        RectTransform rect = hud.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(15f, -100f);
        rect.sizeDelta = new Vector2(900f, 100f);
        GameHudFrame.Attach(scoreText, hud);
    }

    private void Update()
    {
        TickTimers(Time.deltaTime);
        if (hud == null) return;
        hud.enabled = RunFlow.RunActive;
        hud.color = new Color(0.24f, 0.14f, 0.08f);
        int coins = PlayerProgress.Coins;
        int zone = (int)WorldZones.Current;
        if (displayedCoins == coins && displayedZone == zone) return;
        displayedCoins = coins;
        displayedZone = zone;
        hud.text = $"{coins}   •   {WorldZones.Name(WorldZones.Current)}";
    }

    private void TickTimers(float scaledDeltaTime)
    {
        if (!RunFlow.IsPlaying || Time.timeScale <= 0f) return;
        float elapsed = Mathf.Max(0f, scaledDeltaTime);
        magnetTime = Mathf.Max(0f, magnetTime - elapsed);
        slowTime = Mathf.Max(0f, slowTime - elapsed);
        protectionTime = Mathf.Max(0f, protectionTime - elapsed);
    }

    public void Collect(Kind kind)
    {
        if (kind == Kind.Rocket)
        {
            // Claiming a power is still a pickup if a second rocket cannot
            // extend the effect, or an invalid world prevents safe launch.
            owner?.Challenges?.RecordPowerup(kind);
            if (rocket == null) rocket = PaperRocketFlight.Attach(this);
            if (!rocket.TryLaunch()) return;
            GameAudio.Play(GameAudio.Cue.Boost);
            return;
        }
        owner?.Challenges?.RecordPowerup(kind);
        float bonus = PlayerProgress.PowerDurationBonus(mode);
        switch (kind)
        {
            case Kind.Shield: shield = true; GameAudio.Play(GameAudio.Cue.Shield); break;
            case Kind.Magnet: magnetTime = 8f + bonus; GameAudio.Play(GameAudio.Cue.Boost); break;
            case Kind.Slow: slowTime = 6f + bonus; GameAudio.Play(GameAudio.Cue.Slow); break;
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || DOODLIK_CHEATS
    internal bool GrantAdminPower(Kind kind, float duration)
    {
        if (!PlayerProgress.AdminSandboxActive || owner == null || owner.IsDead || !RunFlow.IsPlaying)
            return false;
        owner.RefreshAdminState();
        if (kind == Kind.Rocket)
        {
            if (rocket == null) rocket = PaperRocketFlight.Attach(this);
            float pausedTime = Time.timeScale;
            try
            {
                // TryLaunch retains all real support/death/active-flight checks.
                // No frame is simulated while the admin card is paused.
                if (pausedTime <= 0f) Time.timeScale = AdminRuntime.GameSpeed;
                if (!rocket.TryLaunch()) return false;
            }
            finally { Time.timeScale = pausedTime; }
            GameAudio.Play(GameAudio.Cue.Boost);
            return true;
        }
        switch (kind)
        {
            case Kind.Shield: shield = true; break;
            case Kind.Magnet: magnetTime = Mathf.Clamp(duration, 1f, 120f); break;
            case Kind.Slow: slowTime = Mathf.Clamp(duration, 1f, 120f); break;
            default: return false;
        }
        return true;
    }

    internal void ClearAdminPowers()
    {
        if (!PlayerProgress.AdminSandboxActive) return;
        rocket?.Cancel();
        shield = false;
        magnetTime = slowTime = protectionTime = 0f;
    }
#endif

    public bool TryProtect(bool falling, Rigidbody2D body, Camera camera, Collider2D feet)
    {
        if (IsRocketActive) return true;
        if (!falling && IsProtected) return true;
        if (!shield) return false;
        Collider2D rescue = null;
        if (falling)
        {
            if (body == null || camera == null || feet == null || !feet.enabled) return false;
            // Only rescue onto a real, visible safe platform, never into empty air.
            float bottom = camera.transform.position.y - camera.orthographicSize + 0.5f;
            float top = camera.transform.position.y + camera.orthographicSize - 1f;
            float halfWidth = camera.orthographicSize * camera.aspect;
            float left = camera.transform.position.x - halfWidth;
            float right = camera.transform.position.x + halfWidth;
            float safeHalfWidth = feet.bounds.extents.x + 0.04f;
            foreach (Collider2D platform in FindObjectsByType<Collider2D>())
            {
                if (!platform.enabled || platform.isTrigger || !platform.CompareTag("Platform") ||
                    platform.GetComponentInChildren<DoodlikHazard>() != null ||
                    platform.GetComponent<CrumblingPlatform>() != null) continue;
                if (platform.bounds.max.y < bottom || platform.bounds.max.y > top) continue;
                if (platform.bounds.size.x < safeHalfWidth * 2f ||
                    platform.bounds.center.x - safeHalfWidth < left ||
                    platform.bounds.center.x + safeHalfWidth > right) continue;
                if (rescue == null || platform.bounds.max.y < rescue.bounds.max.y) rescue = platform;
            }
            if (rescue == null) return false;
        }
        shield = false;
        owner?.Challenges?.RecordShieldUse(falling);
        protectionTime = 1.5f;
        GameAudio.Play(GameAudio.Cue.ShieldBreak);
        if (rescue != null)
        {
            float feetOffset = body.position.y - feet.bounds.min.y;
            body.position = new Vector2(rescue.bounds.center.x, rescue.bounds.max.y + feetOffset + 0.08f);
            body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
        }
        return true;
    }

    public bool ApplyRocketMotion(float deltaTime) => rocket != null && rocket.Step(deltaTime);
    public bool TryCompleteRocketLanding(Collider2D surface) => rocket != null && rocket.TryLand(surface);
    internal void GrantRocketExitProtection(float duration) => protectionTime = Mathf.Max(protectionTime, duration);

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (hud != null) Destroy(hud.gameObject);
    }
}
