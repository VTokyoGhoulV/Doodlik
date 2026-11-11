#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Only this menu illustration is affected; the scrolling game's import is unchanged.
public sealed class HomeBackdropImporter : AssetPostprocessor
{
    public const string AssetPath = "Assets/Resources/Menu/HomeBackdrop.png";

    private void OnPreprocessTexture()
    {
        if (assetPath == AssetPath) Configure((TextureImporter)assetImporter);
    }

    [InitializeOnLoadMethod]
    private static void EnsureSettingsAfterCompilation()
    {
        EditorApplication.delayCall += () =>
        {
            TextureImporter importer = AssetImporter.GetAtPath(AssetPath) as TextureImporter;
            if (importer != null && NeedsConfiguration(importer))
            {
                Configure(importer);
                importer.SaveAndReimport();
            }
        };
    }

    private static bool NeedsConfiguration(TextureImporter importer)
    {
        TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
        return importer.textureType != TextureImporterType.Default ||
            importer.maxTextureSize != 2048 || importer.mipmapEnabled || importer.isReadable ||
            importer.alphaSource != TextureImporterAlphaSource.None ||
            !importer.sRGBTexture || importer.wrapMode != TextureWrapMode.Clamp ||
            importer.npotScale != TextureImporterNPOTScale.None ||
            importer.filterMode != FilterMode.Bilinear ||
            importer.textureCompression != TextureImporterCompression.CompressedHQ ||
            !android.overridden || android.maxTextureSize != 2048 ||
            android.format != TextureImporterFormat.ETC2_RGB4;
    }

    private static void Configure(TextureImporter importer)
    {
        importer.textureType = TextureImporterType.Default;
        importer.maxTextureSize = 2048;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.sRGBTexture = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
        android.name = "Android";
        android.overridden = true;
        android.maxTextureSize = 2048;
        android.format = TextureImporterFormat.ETC2_RGB4;
        importer.SetPlatformTextureSettings(android);
    }
}
#endif
