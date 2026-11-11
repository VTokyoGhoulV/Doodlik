using UnityEngine;

// The menu shares a persistent canvas with the game menu, but not its scenery.
// Keep this outside FrontEndUI's overlay so home settings use the same backdrop.
public sealed class HomeScreenBackdrop : MonoBehaviour
{
    public const string ResourcePath = "Menu/HomeBackdrop";

    private RectTransform backdrop;
    private UnityEngine.UI.RawImage illustration;
    private Vector2 lastSize = new Vector2(-1f, -1f);

    public static HomeScreenBackdrop AttachTo(Transform menuCanvas)
    {
        HomeScreenBackdrop result = menuCanvas.GetComponent<HomeScreenBackdrop>();
        if (result == null) result = menuCanvas.gameObject.AddComponent<HomeScreenBackdrop>();
        result.Initialize();
        return result;
    }

    private void Initialize()
    {
        if (backdrop != null) return;

        GameObject background = new GameObject("Home backdrop", typeof(RectTransform));
        backdrop = background.GetComponent<RectTransform>();
        backdrop.SetParent(transform, false);
        Stretch(backdrop);
        backdrop.SetAsFirstSibling();

        // A fully opaque base also covers the world if the illustration is missing.
        // It intentionally fills the whole canvas, including outside safe areas.
        UnityEngine.UI.Image paper = background.AddComponent<UnityEngine.UI.Image>();
        paper.color = new Color32(239, 226, 191, 255);
        paper.raycastTarget = false;

        GameObject artwork = new GameObject("Menu illustration", typeof(RectTransform));
        RectTransform artworkRect = artwork.GetComponent<RectTransform>();
        artworkRect.SetParent(backdrop, false);
        Stretch(artworkRect);
        illustration = artwork.AddComponent<UnityEngine.UI.RawImage>();
        illustration.texture = Resources.Load<Texture2D>(ResourcePath);
        illustration.raycastTarget = false;
        illustration.enabled = illustration.texture != null;
        Refresh();
    }

    private void LateUpdate() => Refresh();

    private void OnEnable()
    {
        if (backdrop != null) Refresh();
    }

    private void OnDisable()
    {
        if (backdrop != null) backdrop.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (backdrop != null) Destroy(backdrop.gameObject);
    }

    private void Refresh()
    {
        if (backdrop == null) return;
        bool show = !RunFlow.RunActive;
        if (backdrop.gameObject.activeSelf != show) backdrop.gameObject.SetActive(show);
        if (!show || illustration.texture == null) return;

        Vector2 size = backdrop.rect.size;
        if (size == lastSize || size.x <= 0f || size.y <= 0f) return;
        lastSize = size;
        illustration.uvRect = CoverUv(size,
            new Vector2(illustration.texture.width, illustration.texture.height));
    }

    // Crop the artwork, never stretch it or leave letterbox strips showing the world.
    public static Rect CoverUv(Vector2 viewport, Vector2 texture)
    {
        if (viewport.x <= 0f || viewport.y <= 0f || texture.x <= 0f || texture.y <= 0f)
            return new Rect(0f, 0f, 1f, 1f);
        float relativeAspect = viewport.x / viewport.y / (texture.x / texture.y);
        if (relativeAspect < 1f)
            return new Rect((1f - relativeAspect) * 0.5f, 0f, relativeAspect, 1f);
        float height = 1f / relativeAspect;
        return new Rect(0f, (1f - height) * 0.5f, 1f, height);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
