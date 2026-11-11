#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class HomeBackdropValidation
{
    [MenuItem("Doodlik/Validation/Home background")]
    public static void Validate()
    {
        Require(Application.isPlaying, "Home background validation requires Play Mode.");
        Texture2D artwork = Resources.Load<Texture2D>(HomeScreenBackdrop.ResourcePath);
        Require(artwork != null, "Home illustration must be available as a resource.");
        TextureImporter importer = AssetImporter.GetAtPath(HomeBackdropImporter.AssetPath) as TextureImporter;
        Require(importer != null && !importer.mipmapEnabled && !importer.isReadable &&
            importer.maxTextureSize <= 2048 && importer.alphaSource == TextureImporterAlphaSource.None,
            "The menu image uses an opaque, non-readable, bounded-size import without mipmaps.");
        TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
        Require(android.overridden && android.format == TextureImporterFormat.ETC2_RGB4,
            "Android menu image must be compressed.");

        Vector2[] screens = {
            new Vector2(1920f, 1080f), new Vector2(1080f, 1920f),
            new Vector2(1080f, 2400f), new Vector2(2560f, 1080f),
            new Vector2(1024f, 768f)
        };
        foreach (Vector2 screen in screens)
        {
            Rect uv = HomeScreenBackdrop.CoverUv(screen, new Vector2(artwork.width, artwork.height));
            Require(uv.width > 0f && uv.height > 0f && uv.xMin >= 0f && uv.yMin >= 0f &&
                uv.xMax <= 1.0001f && uv.yMax <= 1.0001f, "Cover crop stays inside the texture.");
            Require(Mathf.Approximately(uv.width, 1f) || Mathf.Approximately(uv.height, 1f),
                "Cover uses the full source on one axis and leaves no letterboxing.");
            float sampledAspect = artwork.width * uv.width / (artwork.height * uv.height);
            Require(Mathf.Abs(sampledAspect - screen.x / screen.y) < 0.0001f,
                "Artwork proportions survive portrait and landscape layouts.");
        }

        bool savedRun = RunFlow.RunActive;
        bool savedTransition = RunFlow.IsTransitioning;
        GameObject sandbox = new GameObject("Home backdrop validation", typeof(RectTransform), typeof(Canvas));
        sandbox.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            sandbox.GetComponent<RectTransform>().sizeDelta = new Vector2(1920f, 1080f);
            RunFlow.RunActive = false;
            HomeScreenBackdrop component = HomeScreenBackdrop.AttachTo(sandbox.transform);
            Require(HomeScreenBackdrop.AttachTo(sandbox.transform) == component && sandbox.transform.childCount == 1,
                "Repeated setup does not duplicate the backdrop.");
            RectTransform background = sandbox.transform.GetChild(0) as RectTransform;
            Require(background != null && background.anchorMin == Vector2.zero && background.anchorMax == Vector2.one &&
                background.offsetMin == Vector2.zero && background.offsetMax == Vector2.zero,
                "Background fills the whole canvas, not just the safe area.");
            UnityEngine.UI.Image paper = background.GetComponent<UnityEngine.UI.Image>();
            Require(paper != null && Mathf.Approximately(paper.color.a, 1f), "Opaque fallback hides all gameplay.");
            foreach (UnityEngine.UI.Graphic graphic in background.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                Require(!graphic.raycastTarget, "Background does not intercept buttons or touches.");
            Require(background.gameObject.activeSelf, "Home and home settings have a backdrop.");
            MethodInfo refresh = typeof(HomeScreenBackdrop).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            RunFlow.RunActive = true;
            refresh.Invoke(component, null);
            Require(!background.gameObject.activeSelf, "Gameplay and in-run settings retain the game background.");
            RunFlow.RunActive = false;
            RunFlow.IsTransitioning = true;
            refresh.Invoke(component, null);
            Require(background.gameObject.activeSelf, "Returning to home keeps the world covered during transitions.");
            RunFlow.IsTransitioning = false;
            component.enabled = false;
            Require(!background.gameObject.activeSelf, "Disabling the owner hides its background.");
            component.enabled = true;
            Require(background.gameObject.activeSelf, "Re-enabling restores the home backdrop.");
        }
        finally
        {
            RunFlow.RunActive = savedRun;
            RunFlow.IsTransitioning = savedTransition;
            UnityEngine.Object.DestroyImmediate(sandbox);
        }
        Debug.Log("HOME_BACKDROP_VALIDATION PASS: resource/import, five aspect ratios, coverage, input passthrough, run/transition lifecycle.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [MenuItem("Doodlik/Validation/Home background", true)]
    private static bool CanValidate() => EditorApplication.isPlaying;
}
#endif
