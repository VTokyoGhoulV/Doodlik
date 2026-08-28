#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BackgroundImportSetup
{
    private const string BackgroundPath = "Assets/Resources/DoodlikBackground.png";

    static BackgroundImportSetup()
    {
        EditorApplication.delayCall += ConfigureBackground;
    }

    private static void ConfigureBackground()
    {
        TextureImporter importer = AssetImporter.GetAtPath(BackgroundPath) as TextureImporter;
        if (importer == null)
        {
            return;
        }

        bool needsImport = importer.textureType != TextureImporterType.Sprite ||
            importer.spriteImportMode != SpriteImportMode.Single ||
            !Mathf.Approximately(importer.spritePixelsPerUnit, 100f) ||
            importer.wrapMode != TextureWrapMode.Repeat;

        if (!needsImport)
        {
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }
}
#endif
