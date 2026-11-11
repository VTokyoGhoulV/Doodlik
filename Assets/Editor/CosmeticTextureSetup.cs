#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Only the newly introduced ordinary textures; existing sprite slicing and
// user-authored import settings on the original cat/wardrobe atlas are untouched.
[InitializeOnLoad]
public static class CosmeticTextureSetup
{
    static CosmeticTextureSetup() => EditorApplication.delayCall += Ensure;

    public static void Ensure()
    {
        if (AssetDatabase.IsAssetImportWorkerProcess() || EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach (string name in new[] { "Flower", "Glasses", "Tufts", "Star" })
            Configure("Assets/Resources/Menu/Accessories/" + name + ".png", true, 1024, 512);
        Configure("Assets/Resources/Menu/Cosmetics/AstronautHelmetOpen.png", true, 2048, 1024);
        Configure("Assets/Resources/Menu/Cosmetics/AstronautSuitCompact.png", true, 2048, 1024);
        Configure("Assets/Resources/WorldZones/AscentPanorama.png", false, 4096, 4096);
    }

    private static void Configure(string path, bool alpha, int desktopSize, int mobileSize)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
        TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
        TextureImporterFormat format = alpha ? TextureImporterFormat.ETC2_RGBA8 : TextureImporterFormat.ETC2_RGB4;
        bool changed = importer.textureType != TextureImporterType.Default || importer.mipmapEnabled ||
            importer.isReadable || importer.npotScale != TextureImporterNPOTScale.None ||
            importer.maxTextureSize != desktopSize || importer.wrapMode != TextureWrapMode.Clamp ||
            importer.filterMode != FilterMode.Bilinear || importer.alphaIsTransparency != alpha ||
            !android.overridden || android.maxTextureSize != mobileSize || android.format != format;
        if (!changed) return;
        importer.textureType = TextureImporterType.Default;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = desktopSize;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        importer.alphaIsTransparency = alpha;
        android.overridden = true;
        android.maxTextureSize = mobileSize;
        android.format = format;
        importer.SetPlatformTextureSettings(android);
        importer.SaveAndReimport();
    }
}
#endif
