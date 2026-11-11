using System.Collections.Generic;
using UnityEngine;

// Shared materials tint the fur and original clothing independently; no copied
// cat textures, ReadPixels, or runtime image allocations are needed.
public static class CatClothingMaterials
{
    private static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
    private static readonly int BodyTint = Shader.PropertyToID("_BodyTint");
    private static readonly int ClothingColor = Shader.PropertyToID("_ClothingColor");
    private static readonly int ClothingStrength = Shader.PropertyToID("_ClothingStrength");
    private static readonly int AstronautHeadMask = Shader.PropertyToID("_AstronautHeadMask");
    private static readonly int AstronautSuitSeamMask = Shader.PropertyToID("_AstronautSuitSeamMask");
    private static readonly int SpriteUv = Shader.PropertyToID("_SpriteUVRect");

    public static Material For(Sprite sprite, string catId, string clothingId, bool forUI)
    {
        if (sprite == null) return null;
        catId = string.IsNullOrEmpty(catId) ? "cat_ginger" : catId;
        clothingId = string.IsNullOrEmpty(clothingId) ? "clothing_original" : clothingId;
        string key = (forUI ? "ui|" : "world|") + sprite.GetHashCode() + "|" + catId + "|" + clothingId;
        if (cache.TryGetValue(key, out Material existing) && existing != null) return existing;
        Shader shader = Resources.Load<Shader>(forUI ? "CatClothingUI" : "CatClothingWorld");
        if (shader == null)
        {
            Debug.LogError("Doodlik: independent cat clothing shader is missing.");
            return null;
        }
        Material material = new Material(shader) { name = "Doodlik " + key, hideFlags = HideFlags.DontSave };
        material.SetColor(BodyTint, CatAppearance.GetCatTint(catId));
        material.SetColor(ClothingColor, GetClothingColor(clothingId));
        material.SetFloat(ClothingStrength, clothingId == "clothing_original" ? 0f : 1f);
        material.SetFloat(AstronautHeadMask, catId == "cat_astronaut" ? 1f : 0f);
        material.SetFloat(AstronautSuitSeamMask, catId == "cat_astronaut" && clothingId == "clothing_astronaut" ? 1f : 0f);
        Vector4 uv = UnityEngine.Sprites.DataUtility.GetOuterUV(sprite);
        material.SetVector(SpriteUv, new Vector4(uv.x, uv.y, uv.z - uv.x, uv.w - uv.y));
        cache[key] = material;
        return material;
    }

    public static Color GetClothingColor(string clothingId)
    {
        if (clothingId == "clothing_ocean") return new Color32(62, 172, 179, 255);
        if (clothingId == "clothing_berry") return new Color32(210, 80, 115, 255);
        if (clothingId == "clothing_overalls") return new Color32(66, 123, 181, 255);
        if (clothingId == "clothing_starry") return new Color32(33, 52, 102, 255);
        if (clothingId == "clothing_raincoat") return new Color32(255, 196, 43, 255);
        if (clothingId == "clothing_hoodie") return new Color32(161, 73, 143, 255);
        if (clothingId == "clothing_astronaut") return new Color32(239, 236, 216, 255);
        if (clothingId == "clothing_ghoul") return new Color32(45, 44, 49, 255);
        if (clothingId == "clothing_explorer") return new Color32(194, 177, 121, 255);
        return new Color32(246, 143, 32, 255);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        foreach (Material material in cache.Values)
        {
            if (material == null) continue;
            if (Application.isPlaying) Object.Destroy(material);
            else Object.DestroyImmediate(material);
        }
        cache.Clear();
    }
}
