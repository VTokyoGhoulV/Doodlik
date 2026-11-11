using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Deterministic lifecycle regression checks, with disposable objects and isolated progress.
public static class CosmeticLifecycleValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Serializable]
    private sealed class SaveEnvelope
    {
        public int version;
        public long revision;
        public string payload;
        public string checksum;
    }

    public static int Validate()
    {
        List<string> failures = new List<string>();
        foreach (Action check in new Action[] { ValidateWorldReenable, ValidatePreviewReenable, ValidateUnknownAccessoryPreservation })
        {
            try { check(); }
            catch (Exception exception) { failures.Add(exception.Message); }
        }
        if (failures.Count > 0)
            throw new InvalidOperationException("Cosmetic lifecycle validation failed: " + string.Join("; ", failures));
        Debug.Log("[CosmeticLifecycleValidation] PASS: 3 isolated lifecycle checks.");
        return 3;
    }

    private static void ValidateWorldReenable()
    {
        using (PlayerProgress.UseIsolatedStorageForValidation(out _, out _))
        {
            GameObject visual = new GameObject("Cosmetic lifecycle validation", typeof(SpriteRenderer));
            CatAppearance appearance = null;
            try
            {
                SpriteRenderer renderer = visual.GetComponent<SpriteRenderer>();
                renderer.sprite = WardrobeArt.GetCatSprite("cat_calico", null);
                Require(renderer.sprite != null, "World fixture has a source sprite.");
                appearance = CatAppearance.Attach(visual.transform, renderer);
                Require(appearance != null, "World fixture initialized.");
                // Explicit callbacks keep this check synchronous in EditMode and PlayMode.
                Invoke(appearance, "OnDisable");
                PlayerProgress.AddCoins(1000);
                Require(PlayerProgress.TryPurchase("cat_snow") && PlayerProgress.Equip("cat_snow"),
                    "An inactive cat can receive a saved equipment change.");
                Invoke(appearance, "OnEnable");
                SpriteRenderer skin = visual.transform.Find("Cat skin artwork").GetComponent<SpriteRenderer>();
                Require(skin.enabled && skin.sprite == WardrobeArt.GetCatSprite("cat_snow", null),
                    "Reenabling world appearance refreshes equipment changed while unsubscribed.");
            }
            finally
            {
                if (appearance != null) Invoke(appearance, "OnDisable");
                Object.DestroyImmediate(visual);
            }
        }
    }

    private static void ValidatePreviewReenable()
    {
        GameObject preview = new GameObject("Inactive cosmetic preview validation", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        try
        {
            Image source = preview.GetComponent<Image>();
            source.sprite = WardrobeArt.GetCatSprite("cat_calico", null);
            source.rectTransform.sizeDelta = new Vector2(100f, 100f);
            preview.SetActive(false);
            CatAppearance.ConfigurePreview(source, "cat_calico", "clothing_overalls");
            Image clothing = preview.transform.Find("Cat clothing preview").GetComponent<Image>();
            Require(!clothing.enabled, "Inactive preview initially hides its outfit.");
            preview.SetActive(true);
            Invoke(preview.GetComponent<CatWardrobePreview>(), "LateUpdate");
            Require(clothing.enabled && clothing.sprite == WardrobeArt.GetClothingSprite("clothing_overalls"),
                "Showing a preview configured while inactive also shows its clothing.");
        }
        finally { Object.DestroyImmediate(preview); }
    }

    private static void ValidateUnknownAccessoryPreservation()
    {
        using (PlayerProgress.UseIsolatedStorageForValidation(out string primary, out _))
        {
            const string payload = "{\"coins\":0,\"owned\":[\"accessory_future_one\",\"accessory_future_two\",\"accessory_tufts\",\"accessory_glasses\",\"accessory_bow\"]," +
                "\"accessories\":[\"accessory_future_one\",\"accessory_future_two\",\"accessory_tufts\",\"accessory_glasses\",\"accessory_bow\"]}";
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            string checksum = (string)typeof(PlayerProgress).GetMethod("Checksum", flags).Invoke(null, new object[] { payload, 1L });
            PlayerPrefs.SetString(primary, JsonUtility.ToJson(new SaveEnvelope
            {
                version = (int)typeof(PlayerProgress).GetField("CurrentVersion", flags).GetRawConstantValue(),
                revision = 1,
                payload = payload,
                checksum = checksum
            }));
            PlayerProgress.Reload();
            Require(PlayerProgress.EquippedAccessories.Count == 5,
                "Loading preserves unknown owned accessory IDs for compatible future versions.");
            GameObject visual = new GameObject("Unknown accessory validation", typeof(SpriteRenderer));
            CatAppearance appearance = null;
            try
            {
                SpriteRenderer renderer = visual.GetComponent<SpriteRenderer>();
                renderer.sprite = WardrobeArt.GetCatSprite("cat_calico", null);
                appearance = CatAppearance.Attach(visual.transform, renderer);
                int visible = 0;
                for (int i = 0; i < 4; i++)
                {
                    SpriteRenderer accessory = visual.transform.Find("Cat accessory " + i).GetComponent<SpriteRenderer>();
                    if (accessory.enabled && accessory.sprite != null) visible++;
                }
                Require(visible == 4,
                    "Preserved unknown accessories do not hide the two tufts, glasses, or bow by consuming renderer slots.");
            }
            finally
            {
                if (appearance != null) Invoke(appearance, "OnDisable");
                Object.DestroyImmediate(visual);
            }
        }
    }

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, PrivateInstance).Invoke(target, null);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
