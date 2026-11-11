using UnityEngine;

// Ordinary transparent textures are shared by home previews and world sprites.
// Runtime rectangles avoid coupling cosmetics to Sprite Editor metadata.
public static class WardrobeArt
{
    public const string ResourcePath = "Menu/WardrobeArt";
    private static readonly Texture2D[] textures = new Texture2D[25];
    private static readonly bool[] loadAttempted = new bool[25];
    private static readonly Sprite[] sprites = new Sprite[25];

    public static Sprite GetCatSprite(string id, Sprite fallback)
    {
        int index = Index(id);
        return index >= 6 && index <= 10 ? GetSprite(id) : fallback;
    }

    public static Sprite GetClothingSprite(string id)
    {
        if (id == "clothing_explorer") return GetSprite(id);
        int index = Index(id);
        return index >= 12 && index <= 17 ? GetSprite(id) : null;
    }

    public static Sprite GetSkinOverlay(string id) => id == "cat_astronaut" ? GetSprite(id) : null;

    // These fits share the original cat's body coordinates. Only visual children
    // use them; the Player's source SpriteRenderer/collider bounds never change.
    public static Rect ClothingRect(string id, Vector2 catSize)
    {
        if (id == "clothing_astronaut")
        {
            Vector2 suitSize = Vector2.Scale(new Vector2(0.678f, 0.371f), catSize);
            Vector2 suitCenter = Vector2.Scale(new Vector2(-0.055f, -0.312f), catSize);
            return new Rect(suitCenter - suitSize * 0.5f, suitSize);
        }
        float height = id == "clothing_overalls" ? 0.36f : id == "clothing_explorer" ? 0.33f : 0.30f;
        Vector2 size = Vector2.Scale(new Vector2(0.70f, height), catSize);
        Vector2 center = Vector2.Scale(new Vector2(-0.057f, -0.355f + height * 0.5f), catSize);
        return new Rect(center - size * 0.5f, size);
    }

    public static Rect SkinOverlayRect(string id, Vector2 catSize)
    {
        // The helmet follows Doodlik's head rather than extending past his body.
        Vector2 size = Vector2.Scale(new Vector2(0.98f, 0.90f), catSize);
        Vector2 center = Vector2.Scale(new Vector2(-0.07f, 0.17f), catSize);
        return new Rect(center - size * 0.5f, size);
    }

    public static Sprite GetSprite(string itemId)
    {
        Sprite reward = ChallengeRewardArt.GetSprite(itemId);
        if (reward != null) return reward;
        int index = Index(itemId);
        if (index < 0) return null;
        if (!loadAttempted[index])
        {
            loadAttempted[index] = true;
            textures[index] = Resources.Load<Texture2D>(TexturePath(index));
            if (textures[index] == null) Debug.LogError("Doodlik: wardrobe illustration is missing at Resources/" + TexturePath(index));
        }
        Texture2D texture = textures[index];
        if (texture == null) return null;
        if (sprites[index] == null)
        {
            // Normalized source bounds also work after platform downscaling.
            Rect rect = SourceRect(index);
            rect = new Rect(rect.x * texture.width, rect.y * texture.height,
                rect.width * texture.width, rect.height * texture.height);
            sprites[index] = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
            sprites[index].name = "Wardrobe " + itemId;
            sprites[index].hideFlags = HideFlags.DontSave;
        }
        return sprites[index];
    }

    public static int InstanceCount(string id) => id == "accessory_tufts" ? 2 :
        !string.IsNullOrEmpty(id) && id.StartsWith("accessory_") && id != "accessory_none" ? 1 : 0;

    public static Sprite GetAccessorySprite(string id, int instance = 0) => GetSprite(id);

    // One ordering contract for fitted menu graphics and the in-world cat.
    public static int SortLayer(string id, int instance = 1)
    {
        switch (id)
        {
            case "accessory_astronaut_helmet": return 50;
            case "accessory_ghoul_hair": return 45;
            case "accessory_glasses":
            case "accessory_aviator":
            case "accessory_ghoul_mask": return 20;
            case "accessory_tufts":
            case "accessory_headphones": return 30;
            default: return 40;
        }
    }

    // Measured on the trimmed tuft artwork and original cat, respectively.
    // Anchor the narrow root, not the centre of an asymmetrical feather shape.
    public static readonly Vector2 TuftRootUV = new Vector2(0.55f, 0.045f);
    public static Vector2 TuftEarAnchor(Vector2 catSize, int instance) => Vector2.Scale(
        instance == 0 ? new Vector2(-0.326f, 0.484f) : new Vector2(0.164f, 0.473f), catSize);
    public static bool AccessoryFlipX(string id, int instance = 0) => id == "accessory_tufts" && instance == 1;

    public static Rect AccessoryRect(string id, Vector2 catSize, int instance = 0)
    {
        if (id == "accessory_astronaut_helmet")
        {
            Vector2 helmetSize = Vector2.Scale(new Vector2(0.845f, 0.558f), catSize);
            Vector2 helmetCenter = Vector2.Scale(new Vector2(-0.072f, 0.105f), catSize);
            return new Rect(helmetCenter - helmetSize * 0.5f, helmetSize);
        }
        Sprite sprite = GetSprite(id);
        if (sprite == null) return new Rect();
        float width;
        Vector2 center;
        switch (id)
        {
            case "accessory_glasses": width = 0.415f; center = new Vector2(-0.145f, 0.104f); break;
            case "accessory_tufts": width = 0.10f; center = Vector2.zero; break;
            case "accessory_flower": width = 0.30f; center = new Vector2(0.165f, 0.29f); break;
            case "accessory_ghoul_mask": width = 0.48f; center = new Vector2(-0.145f, -0.015f); break;
            case "accessory_ghoul_hair": width = 0.60f; center = new Vector2(-0.095f, 0.25f); break;
            case "accessory_headphones": width = 0.80f; center = new Vector2(-0.14f, 0.31f); break;
            case "accessory_bow": width = 0.28f; center = new Vector2(0.115f, 0.415f); break;
            case "accessory_aviator": width = 0.49f; center = new Vector2(-0.145f, 0.104f); break;
            case "accessory_coin_crown": width = 0.38f; center = new Vector2(-0.065f, 0.465f); break;
            case "accessory_target": width = 0.22f; center = new Vector2(-0.055f, 0.425f); break;
            case "accessory_patch": width = 0.165f; center = new Vector2(-0.29f, 0.355f); break;
            default: width = 0.17f; center = new Vector2(-0.285f, 0.335f); break;
        }
        Vector2 size = new Vector2(catSize.x * width, catSize.x * width * sprite.rect.height / sprite.rect.width);
        if (id == "accessory_headphones") size.y *= 0.82f;
        center = Vector2.Scale(center, catSize);
        if (id == "accessory_tufts")
        {
            Vector2 rootOffset = Vector2.Scale(TuftRootUV - Vector2.one * 0.5f, size);
            if (AccessoryFlipX(id, instance)) rootOffset.x = -rootOffset.x;
            center = TuftEarAnchor(catSize, instance) -
                (Vector2)(Quaternion.Euler(0f, 0f, AccessoryAngle(id, instance)) * rootOffset);
        }
        return new Rect(center - size * 0.5f, size);
    }

    public static float AccessoryAngle(string id, int instance = 0) => id == "accessory_tufts" ? (instance == 0 ? 7f : -7f) : id == "accessory_flower" ? -18f : id == "accessory_star" ? 12f : id == "accessory_patch" ? -24f : 0f;

    private static Rect SourceRect(int index)
    {
        Rect topOrigin;
        Vector2 sourceSize;
        switch (index)
        {
            case 0: topOrigin = new Rect(72f, 703f, 511f, 465f); sourceSize = new Vector2(1254f, 1254f); break;
            case 1: topOrigin = new Rect(693f, 647f, 531f, 550f); sourceSize = new Vector2(1254f, 1254f); break;
            case 2: topOrigin = new Rect(155f, 322f, 992f, 611f); sourceSize = new Vector2(1300f, 1210f); break;
            case 3: topOrigin = new Rect(17f, 139f, 1741f, 589f); sourceSize = new Vector2(1774f, 887f); break;
            case 4: topOrigin = new Rect(367f, 350f, 526f, 561f); sourceSize = new Vector2(1261f, 1247f); break;
            case 5: topOrigin = new Rect(349f, 273f, 637f, 614f); sourceSize = new Vector2(1337f, 1176f); break;
            case 11: topOrigin = new Rect(0f, 0f, 1323f, 1189f); sourceSize = new Vector2(1323f, 1189f); break;
            case 24: topOrigin = new Rect(66f, 258f, 1189f, 709f); sourceSize = new Vector2(1323f, 1189f); break;
            case 12: topOrigin = new Rect(28f, 30f, 1739f, 824f); sourceSize = new Vector2(1795f, 876f); break;
            case 13: topOrigin = new Rect(91f, 46f, 1891f, 681f); sourceSize = new Vector2(2073f, 758f); break;
            case 14: topOrigin = new Rect(106f, 73f, 1596f, 725f); sourceSize = new Vector2(1808f, 870f); break;
            case 15: topOrigin = new Rect(58f, 64f, 1611f, 780f); sourceSize = new Vector2(1730f, 909f); break;
            case 16: topOrigin = new Rect(76f, 518f, 1171f, 577f); sourceSize = new Vector2(1323f, 1189f); break;
            case 17: topOrigin = new Rect(72f, 104f, 1677f, 682f); sourceSize = new Vector2(1819f, 865f); break;
            case 18: topOrigin = new Rect(11f, 107f, 1956f, 623f); sourceSize = new Vector2(1983f, 793f); break;
            case 19: topOrigin = new Rect(45f, 46f, 1684f, 803f); sourceSize = new Vector2(1774f, 887f); break;
            case 20: topOrigin = new Rect(13f, 37f, 1507f, 902f); sourceSize = new Vector2(1536f, 1024f); break;
            case 21: topOrigin = new Rect(93f, 8f, 1522f, 919f); sourceSize = new Vector2(1683f, 935f); break;
            case 22: topOrigin = new Rect(7f, 29f, 2156f, 656f); sourceSize = new Vector2(2172f, 724f); break;
            case 23: topOrigin = new Rect(77f, 8f, 1614f, 854f); sourceSize = new Vector2(1774f, 887f); break;
            default: return new Rect(0f, 0f, 1f, 1f);
        }
        return new Rect(topOrigin.x / sourceSize.x,
            (sourceSize.y - topOrigin.yMax) / sourceSize.y,
            topOrigin.width / sourceSize.x, topOrigin.height / sourceSize.y);
    }

    private static string TexturePath(int index)
    {
        switch (index)
        {
            case 2: return "Menu/Accessories/Flower";
            case 3: return "Menu/Accessories/Glasses";
            case 4: return "Menu/Accessories/Tufts";
            case 5: return "Menu/Accessories/Star";
            case 6: return "Menu/Cosmetics/Calico";
            case 7: return "Menu/Cosmetics/Siamese";
            case 8: return "Menu/Cosmetics/Snow";
            case 9: return "Menu/Cosmetics/Tuxedo";
            case 10: return "Menu/Cosmetics/Ghoul";
            case 11: return "Menu/Cosmetics/AstronautHelmet";
            case 12: return "Menu/Cosmetics/Overalls";
            case 13: return "Menu/Cosmetics/Starry";
            case 14: return "Menu/Cosmetics/Raincoat";
            case 15: return "Menu/Cosmetics/Hoodie";
            case 16: return "Menu/Cosmetics/AstronautSuitCompact";
            case 17: return "Menu/Cosmetics/GhoulOutfit";
            case 18: return "Menu/Cosmetics/GhoulMask";
            case 19: return "Menu/Cosmetics/GhoulHair";
            case 20: return "Menu/Cosmetics/Headphones";
            case 21: return "Menu/Cosmetics/Bow";
            case 22: return "Menu/Cosmetics/Aviator";
            case 23: return "Menu/Cosmetics/CoinCrown";
            case 24: return "Menu/Cosmetics/AstronautHelmetOpen";
            default: return ResourcePath;
        }
    }

    private static int Index(string id)
    {
        switch (id)
        {
            case "upgrade_magnet": return 0;
            case "upgrade_duration": return 1;
            case "accessory_flower": return 2;
            case "accessory_glasses": return 3;
            case "accessory_tufts": return 4;
            case "accessory_star": return 5;
            case "cat_calico": return 6;
            case "cat_siamese": return 7;
            case "cat_snow": return 8;
            case "cat_tuxedo": return 9;
            case "cat_ghoul": return 10;
            case "cat_astronaut": return 11;
            case "accessory_astronaut_helmet": return 24;
            case "clothing_overalls": return 12;
            case "clothing_starry": return 13;
            case "clothing_raincoat": return 14;
            case "clothing_hoodie": return 15;
            case "clothing_astronaut": return 16;
            case "clothing_ghoul": return 17;
            case "accessory_ghoul_mask": return 18;
            case "accessory_ghoul_hair": return 19;
            case "accessory_headphones": return 20;
            case "accessory_bow": return 21;
            case "accessory_aviator": return 22;
            case "accessory_coin_crown": return 23;
            default: return -1;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        foreach (Sprite sprite in sprites)
        {
            if (sprite == null) continue;
            if (Application.isPlaying) Object.Destroy(sprite);
            else Object.DestroyImmediate(sprite);
        }
        for (int i = 0; i < sprites.Length; i++)
        {
            sprites[i] = null;
            textures[i] = null;
            loadAttempted[i] = false;
        }
    }
}
