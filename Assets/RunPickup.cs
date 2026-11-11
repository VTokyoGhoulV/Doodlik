using System.Collections.Generic;
using UnityEngine;

// Bonuses can follow platforms. Coins are always independent world objects:
// their height-band planner chooses free-air detours before creating them.
public sealed class RunPickup : MonoBehaviour
{
    private static readonly List<RunPickup> Active = new List<RunPickup>();
    private static readonly Mesh[] Icons = new Mesh[6];
    public const int GoldenFishValue = 5;
    private static Material ink;
    private int kind; // 0 coin, 1 shield, 2 magnet, 3 slow, 4 rocket, 5 golden fish
    public int KindId => kind;
    public bool IsCoinReward => kind == 0 || kind == 5;
    public int Value => IsCoinReward ? (kind == 5 ? GoldenFishValue : 1) : 0;
    private Transform art;
    private Camera sceneCamera;
    private float phase;
    private Vector2 previousPosition, sampledPosition;
    private bool collected;
    private Transform attractionTarget;
    private SpriteRenderer targetSprite;
    private Player targetPlayer;
    private Vector3 attractionStart, unwrappedTarget, originalArtScale;
    private float attractionElapsed, attractionDuration, attractionWidth, attractionArc;
    private float attractionWrapCenter, originalArtAngle;
    private bool attracting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        Active.Clear();
        // AdventureArt recreates its meshes on a new Play session, including
        // domain-reload-disabled sessions. Do not retain its previous icons.
        Icons[4] = Icons[5] = null;
    }
    private void OnEnable()
    {
        previousPosition = sampledPosition = transform.position;
        if (!collected && !Active.Contains(this)) Active.Add(this);
    }
    private void OnDisable() => Active.Remove(this);

    public static RunPickup SpawnBonusForPlatform(GameObject platform, int index, PickupSpawnSchedule schedule)
    {
        if (schedule == null || platform == null) return null;
        Collider2D collider = platform.GetComponent<Collider2D>();
        if (collider == null) return null;
        bool ordinaryBonus = schedule.TryGetKind(index, out int kind);
        bool rocket = schedule.TryRocketForPlatform(index, WorldZones.ScoreAtHeight(collider.bounds.max.y), out int rocketKind);
        if (!ordinaryBonus && !rocket) return null;
        if (!ordinaryBonus) kind = rocketKind;
        float scale = GameplayPreferences.ModelScale;
        return Create(kind, new Vector2(collider.bounds.center.x,
            collider.bounds.max.y + 0.7f * scale), platform.transform, Camera.main, index * 1.7f);
    }

    public static RunPickup SpawnAirCoin(Vector2 position, Camera camera, float phase)
        => Create(0, position, null, camera, phase);

    // Production callers use only an already certified air-coin detour. This
    // creation API also lets isolated gameplay tests exercise the real reward.
    public static RunPickup SpawnGoldenFish(Vector2 position, Camera camera, float phase)
        => Create(5, position, null, camera, phase);

    public static void RemoveUnclaimedCoins()
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            RunPickup pickup = Active[i];
            if (pickup == null || !pickup.IsCoinReward || pickup.collected) continue;
            pickup.gameObject.SetActive(false);
            Destroy(pickup.gameObject);
        }
    }

    public static void RemoveUnclaimedAerialBonuses()
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            RunPickup pickup = Active[i];
            if (pickup == null || pickup.IsCoinReward || pickup.collected ||
                pickup.transform.parent != null) continue;
            pickup.gameObject.SetActive(false);
            Destroy(pickup.gameObject);
        }
    }

    private static RunPickup Create(int kind, Vector2 position, Transform parent, Camera camera, float phase)
    {
        float scale = GameplayPreferences.ModelScale;
        GameObject root = new GameObject("Collectible");
        root.transform.position = position;
        // Only bonuses receive a platform parent; coin creation never does.
        root.transform.SetParent(parent, true);
        RunPickup pickup = root.AddComponent<RunPickup>();
        pickup.kind = kind;
        pickup.phase = phase;
        pickup.sceneCamera = camera;
        GameObject drawing = new GameObject(pickup.kind == 5 ? "Golden fish" : pickup.kind == 0 ? "Coin" : "Bonus");
        drawing.transform.SetParent(root.transform, false);
        drawing.transform.localScale = new Vector3(scale / root.transform.lossyScale.x,
            scale / root.transform.lossyScale.y, 1f);
        pickup.art = drawing.transform;
        if (ink == null)
        {
            Shader shader = Resources.Load<Shader>("HazardInk");
            if (shader == null) shader = Shader.Find("Doodlik/HazardInk");
            if (shader == null) { root.SetActive(false); Destroy(root); return null; }
            ink = new Material(shader);
        }
        if (Icons[pickup.kind] == null) Icons[pickup.kind] = BuildIcon(pickup.kind);
        drawing.AddComponent<MeshFilter>().sharedMesh = Icons[pickup.kind];
        MeshRenderer renderer = drawing.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = ink;
        renderer.sortingOrder = 4;
        return pickup;
    }

    // Optional aerial placement uses the same scheduled pickup, never an extra
    // reward. A route validator must choose the position before calling this.
    public void PlaceInAir(Vector2 position)
    {
        if (collected) return;
        transform.SetParent(null, true);
        transform.position = new Vector3(position.x, position.y, transform.position.z);
        previousPosition = sampledPosition = position;
    }

    private void Update()
    {
        if (collected) return;
        previousPosition = sampledPosition;
        sampledPosition = transform.position;
        if (art != null) art.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 2f + phase) * 7f);
        if (sceneCamera != null && transform.position.y < sceneCamera.transform.position.y - sceneCamera.orthographicSize - 2f)
            Destroy(gameObject);
    }

    private void LateUpdate()
    {
        if (attracting) TickAttraction(Time.deltaTime);
    }

    public static void CollectAlong(Vector2 from, Vector2 to, Vector2 bodyHalfSize, float width, RunPowerups powers,
        bool sweepMotion = true)
    {
        if (powers == null || !RunFlow.IsPlaying || Time.timeScale <= 0f) return;
        to.x = from.x + TrapGeometry.WrappedDelta(from.x, to.x, width);
        float radius = powers.PickupRadius;
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            if (i >= Active.Count) continue;
            RunPickup pickup = Active[i];
            if (pickup == null || pickup.collected || !pickup.isActiveAndEnabled) continue;
            Vector2 center = pickup.transform.position;
            Vector2 relativeFrom = from - (sweepMotion ? pickup.previousPosition : center);
            Vector2 relativeTo = to - center;
            bool hit = false;
            for (int copy = -1; copy <= 1 && !hit; copy++)
            {
                Vector2 point = Vector2.right * (copy * width);
                hit = TrapGeometry.SweptBox(relativeFrom, relativeTo, bodyHalfSize,
                    new Rect(point - Vector2.one * radius, Vector2.one * radius * 2f));
            }
            if (!hit) continue;
            // Claim/remove before callbacks. The coin's flight is presentation only:
            // dying or restarting during it cannot lose or duplicate the reward.
            if (!pickup.TryClaim(powers.transform, width)) continue;
            if (pickup.IsCoinReward)
            {
                Player collector = powers.GetComponent<Player>();
                // Turning admin mode off halfway through a cheat-assisted run
                // must not transfer its later rewards to the normal wallet.
                if (PlayerProgress.AdminSandboxActive || collector == null || !collector.IsTestAttempt)
                    PlayerProgress.AddCoins(pickup.Value);
                if (collector != null)
                {
                    collector.RegisterAirCoinCollected(pickup.Value);
                    if (pickup.kind == 5) collector.Challenges?.RecordGoldenFish();
                }
                GameAudio.Play(GameAudio.Cue.Coin);
            }
            else powers.Collect((RunPowerups.Kind)(pickup.kind - 1));
        }
    }

    private bool TryClaim(Transform collector, float width)
    {
        if (collected) return false;
        collected = true;
        Active.Remove(this);
        if (IsCoinReward && collector != null && art != null)
            BeginAttraction(collector, width);
        else FinishCollection();
        return true;
    }

    private void BeginAttraction(Transform collector, float width)
    {
        // The platform may move or crumble while the already claimed coin flies.
        transform.SetParent(null, true);
        attractionTarget = collector;
        targetSprite = collector.GetComponentInChildren<SpriteRenderer>();
        targetPlayer = collector.GetComponent<Player>();
        attractionStart = transform.position;
        attractionWidth = Mathf.Max(0f, width);
        attractionWrapCenter = sceneCamera != null ? sceneCamera.transform.position.x : collector.position.x;
        unwrappedTarget = TargetPosition();
        unwrappedTarget.x = attractionStart.x + TrapGeometry.WrappedDelta(
            attractionStart.x, unwrappedTarget.x, attractionWidth);
        originalArtScale = art.localScale;
        originalArtAngle = art.localEulerAngles.z;
        float visualScale = Mathf.Max(0.01f, Mathf.Abs(art.lossyScale.y));
        float distance = Vector2.Distance(attractionStart, unwrappedTarget) / visualScale;
        attractionDuration = Mathf.Lerp(0.26f, 0.42f, Mathf.Clamp01(distance / 2f));
        attractionArc = visualScale * Mathf.Lerp(0.16f, 0.34f, Mathf.Clamp01(distance));
        attractionElapsed = 0f;
        attracting = true;
    }

    private Vector3 TargetPosition() => targetSprite != null
        ? targetSprite.bounds.center : attractionTarget.position;

    private void TickAttraction(float scaledDeltaTime)
    {
        if (art == null || attractionTarget == null || !attractionTarget.gameObject.activeInHierarchy ||
            (targetPlayer != null && targetPlayer.IsDead) || !RunFlow.RunActive || RunFlow.IsTransitioning)
        {
            FinishCollection();
            return;
        }
        if (Time.timeScale <= 0f || scaledDeltaTime <= 0f) return;

        attractionElapsed = Mathf.Min(attractionElapsed + scaledDeltaTime, attractionDuration);
        float t = attractionElapsed / attractionDuration;
        Vector3 target = TargetPosition();
        // Keep a continuous short route even if the cat crosses the screen seam
        // during the animation. Only the rendered position is wrapped to the view.
        unwrappedTarget.x += TrapGeometry.WrappedDelta(unwrappedTarget.x, target.x, attractionWidth);
        unwrappedTarget.y = target.y;
        Vector3 position = Vector3.Lerp(attractionStart, unwrappedTarget, t * t);
        position.y += Mathf.Sin(t * Mathf.PI) * attractionArc;
        position.z = attractionStart.z;
        if (attractionWidth > 0f)
        {
            float center = sceneCamera != null ? sceneCamera.transform.position.x : attractionWrapCenter;
            position.x = center + TrapGeometry.WrappedDelta(center, position.x, attractionWidth);
        }
        transform.position = position;

        // A little pop and turn makes contact readable; the last part contracts
        // into the cat. No particles, material instances or per-frame allocations.
        float shrink = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 1f, t));
        float size = (1f + 0.14f * Mathf.Sin(t * Mathf.PI)) * (1f - shrink);
        art.localScale = originalArtScale * size;
        art.localRotation = Quaternion.Euler(0f, 0f, originalArtAngle + 210f * t * t);
        if (t >= 1f) FinishCollection();
    }

    private void FinishCollection()
    {
        attracting = false;
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    private static Mesh BuildIcon(int kind)
    {
        if (kind == 4 || kind == 5) return AdventureArt.PickupIcon(kind);
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Color> colors = new List<Color>();
        Color dark = new Color(0.22f, 0.13f, 0.08f);
        Color fill = kind == 0 ? new Color(1f, 0.71f, 0.13f) :
            kind == 1 ? new Color(0.43f, 0.81f, 0.88f) :
            kind == 2 ? new Color(0.97f, 0.58f, 0.48f) :
            new Color(0.66f, 0.72f, 0.97f);
        void Polygon(Color color, params Vector2[] points)
        {
            int start = vertices.Count;
            foreach (Vector2 point in points) { vertices.Add(point); colors.Add(color); }
            for (int i = 1; i < points.Length - 1; i++) { triangles.Add(start); triangles.Add(start+i); triangles.Add(start+i+1); }
        }
        void Disc(float size, Color color)
        {
            Vector2[] p = new Vector2[16];
            for (int i = 0; i < p.Length; i++) p[i] = new Vector2(Mathf.Cos(i*Mathf.PI/8f), Mathf.Sin(i*Mathf.PI/8f))*size;
            Polygon(color,p);
        }
        void Arc(float radius, float thickness, Color color)
        {
            // Two straight sides give the magnet the pointed V silhouette.
            const int segments = 2;
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.PI + Mathf.PI * i / segments;
                float b = Mathf.PI + Mathf.PI * (i + 1) / segments;
                float inner = radius - thickness * 0.5f;
                float outer = radius + thickness * 0.5f;
                Polygon(color,
                    new Vector2(Mathf.Cos(a) * inner, Mathf.Sin(a) * inner - 0.005f),
                    new Vector2(Mathf.Cos(a) * outer, Mathf.Sin(a) * outer - 0.005f),
                    new Vector2(Mathf.Cos(b) * outer, Mathf.Sin(b) * outer - 0.005f),
                    new Vector2(Mathf.Cos(b) * inner, Mathf.Sin(b) * inner - 0.005f));
            }
        }
        Disc(0.23f,dark); Disc(0.20f,fill);
        if (kind == 0) // Tiny fish embossed on the coin.
        {
            Polygon(dark,new Vector2(-.12f,0),new Vector2(0,.08f),new Vector2(.1f,0),new Vector2(0,-.08f));
            Polygon(dark,new Vector2(.08f,0),new Vector2(.15f,.07f),new Vector2(.15f,-.07f));
        }
        else if (kind == 1)
            Polygon(dark,new Vector2(-.10f,.12f),new Vector2(.10f,.12f),new Vector2(.08f,-.03f),new Vector2(0,-.13f),new Vector2(-.08f,-.03f));
        else if (kind == 2)
        {
            // Angular horseshoe with pale pole tips, inside the same pink
            // medallion and ink outline as the other pickups.
            Arc(.105f, .06f, dark);
            foreach (float x in new[] { -.105f, .105f })
            {
                Polygon(dark, new Vector2(x-.03f,-.02f), new Vector2(x+.03f,-.02f),
                    new Vector2(x+.03f,.12f), new Vector2(x-.03f,.12f));
                Polygon(new Color(1f, .9f, .67f),
                    new Vector2(x-.023f,.073f), new Vector2(x+.023f,.073f),
                    new Vector2(x+.023f,.112f), new Vector2(x-.023f,.112f));
            }
        }
        else
        {
            Polygon(dark,new Vector2(-.1f,.12f),new Vector2(.1f,.12f),new Vector2(0,-.01f));
            Polygon(dark,new Vector2(-.1f,-.12f),new Vector2(.1f,-.12f),new Vector2(0,.01f));
        }
        Mesh mesh = new Mesh { name = "Pickup " + kind };
        mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds();
        return mesh;
    }
}
