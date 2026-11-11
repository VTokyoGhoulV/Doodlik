using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// Play Mode UI checks with disposable wallet storage. Optional promoCode is
// supplied by the caller; the secret is neither embedded here nor logged.
public static class WardrobeUiValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly HashSet<string> PromoOnlyIds = new HashSet<string>
    {
        "cat_ghoul", "clothing_ghoul", "accessory_ghoul_mask", "accessory_ghoul_hair"
    };
    private static int checks;

    public static int Validate(string promoCode = null)
    {
        checks = 0;
        if (string.IsNullOrWhiteSpace(promoCode)) promoCode = null;
        FrontEndUI ui = UnityEngine.Object.FindAnyObjectByType<FrontEndUI>();
        Check(Application.isPlaying && ui != null && ui.IsVisible && EventSystem.current != null && !RunFlow.RunActive &&
            ((GameObject)Field(ui, "home")).activeSelf,
            "Wardrobe validation starts on the runtime home screen");
        // Rendering restored UI must use an already-loaded outer cache, never
        // trigger a normal save migration/recovery as a side effect of testing.
        object outerData = typeof(PlayerProgress).GetField("data", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Check(outerData != null, "Normal home progress was loaded before entering isolated validation");
        using (PlayerProgress.UseIsolatedStorageForValidation(out _, out _))
        {
            try
            {
                PlayerProgress.AddCoins(100000);
                ui.ShowHome();
                Call(ui, "ShowShop");
                ValidatePages(ui);
                ValidateAccessories(ui);
                ValidateAstronautWardrobe(ui);
                ValidatePromo(ui, promoCode);
            }
            finally { ui.ShowHome(); }
        }
        ui.Refresh();
        if (string.IsNullOrWhiteSpace(promoCode))
            Debug.Log("WARDROBE_PROMO_SUCCESS_SKIPPED: set DOODLIK_TEST_PROMO_CODE locally to test redemption.");
        Debug.Log($"WARDROBE_UI_PASS checks={checks} promoSuccessTested={!string.IsNullOrWhiteSpace(promoCode)}");
        return checks;
    }

    private static void ValidatePages(FrontEndUI ui)
    {
        object[] rows = (object[])Field(ui, "rows");
        Check(rows.Length == 3, "Expanded catalog keeps three reusable rows");
        for (int tab = 0; tab < 4; tab++)
        {
            Call(ui, "SelectTab", tab);
            var expected = new HashSet<string>();
            foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
                if ((int)item.Category == tab && (!PromoOnlyIds.Contains(item.Id) || PlayerProgress.Owns(item.Id)))
                    expected.Add(item.Id);
            var shown = new HashSet<string>();
            int pages = Mathf.CeilToInt(expected.Count / 3f);
            Check(((GameObject)Field(ui, "pageControls")).activeSelf == (pages > 1) &&
                ((TMP_Text)Field(ui, "pageLabel")).text == "1 / " + Mathf.Max(1, pages),
                "Wardrobe page count excludes secret items until redeemed");
            for (int page = 0; page < pages; page++)
            {
                int active = 0;
                foreach (object row in rows)
                {
                    if (!((GameObject)RowField(row, "Root")).activeSelf) continue;
                    active++;
                    string id = (string)RowField(row, "ItemId");
                    Check(expected.Contains(id) && shown.Add(id), "Each item appears on exactly one page: " + id);
                    foreach (string label in new[] { "Title", "Detail", "ActionLabel" })
                    {
                        TMP_Text text = (TMP_Text)RowField(row, label);
                        text.ForceMeshUpdate();
                        Check(!text.isTextOverflowing, "Catalog label fits its portrait-safe row: " + id + "/" + label);
                    }
                    foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
                    {
                        if (item.Id != id || item.Price >= 0 || PlayerProgress.Owns(id)) continue;
                        Check(!((UnityEngine.UI.Button)RowField(row, "Action")).interactable,
                            "Achievement/promo item cannot be purchased for a negative price");
                        Check(((TMP_Text)RowField(row, "Detail")).text == PlayerProgress.UnlockDescription(id),
                            "Locked item displays its actual unlock condition");
                    }
                }
                Check(active > 0 && active <= 3, "Page has one to three items");
                Check(((TMP_Text)Field(ui, "shopHint")).gameObject.activeSelf,
                    "Purchase feedback is visible even on multi-page categories");
                if (page + 1 < pages) Call(ui, "ChangePage", 1);
            }
            Check(shown.SetEquals(expected), "Paging covers the complete category");
            foreach (string id in PromoOnlyIds)
                if (!PlayerProgress.Owns(id))
                    Check(!shown.Contains(id), "Unredeemed promo item stays off every wardrobe page: " + id);
        }
    }

    private static void ValidateAccessories(FrontEndUI ui)
    {
        PlayerProgress.Equip("accessory_none");
        string[] equipped = { "accessory_flower", "accessory_glasses", "accessory_tufts" };
        foreach (string id in equipped)
            Check(PlayerProgress.TryPurchase(id) && PlayerProgress.Equip(id), "Equip compatible accessory " + id);
        ui.ShowHome();
        object preview = Field(ui, "homePreview");
        IList graphics = (IList)preview.GetType().GetField("Accessories").GetValue(preview);
        var rendered = new HashSet<string>();
        foreach (DoodlikCosmeticPreview graphic in graphics)
            if (graphic.enabled) rendered.Add(graphic.ItemId);
        Check(rendered.SetEquals(equipped), "Home preview renders every equipped accessory");
        Check(preview.GetType().GetField("Accessory").GetValue(preview) != null,
            "Legacy first-accessory preview reference remains available");
        Call(ui, "ShowShop");
        Call(ui, "SelectTab", 2);
        object glassesRow = FindRow(ui, "accessory_glasses");
        UnityEngine.UI.Button toggle = (UnityEngine.UI.Button)RowField(glassesRow, "Action");
        Check(toggle.interactable && ((TMP_Text)RowField(glassesRow, "ActionLabel")).text == "Снять",
            "An equipped accessory has an enabled removal action");
        toggle.onClick.Invoke();
        Check(!PlayerProgress.IsEquipped("accessory_glasses") && PlayerProgress.EquippedAccessories.Count == 2,
            "Row action removes only the selected accessory");
        ((UnityEngine.UI.Button)Field(ui, "clearAccessories")).onClick.Invoke();
        Check(PlayerProgress.EquippedAccessories.Count == 0, "Clear-all action removes all accessories");
    }

    private static void ValidatePromo(FrontEndUI ui, string promoCode)
    {
        ((UnityEngine.UI.Button)Field(ui, "promoButton")).onClick.Invoke();
        GameObject modal = (GameObject)Field(ui, "promo");
        TMP_InputField input = (TMP_InputField)Field(ui, "promoInput");
        TMP_Text feedback = (TMP_Text)Field(ui, "promoFeedback");
        CanvasGroup shop = (CanvasGroup)Field(ui, "shopInteraction");
        UnityEngine.UI.Button redeem = (UnityEngine.UI.Button)Field(ui, "promoRedeem");
        Check(modal.activeSelf && !shop.interactable && !shop.blocksRaycasts,
            "Promo dialog blocks the wardrobe controls underneath");
        bool onMobileDevice = Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer;
        Check(input.textComponent != null && input.textViewport != null && (!onMobileDevice || !input.shouldHideSoftKeyboard) &&
            input.lineType == TMP_InputField.LineType.SingleLine, "Promo uses a real keyboard-capable TMP input");
        Check(!redeem.interactable, "Empty promo cannot be submitted");
        int coins = PlayerProgress.Coins;
        input.text = "not-a-real-promo-code-test";
        Check(redeem.interactable, "Entering a code enables the receive button");
        input.onSubmit.Invoke(input.text);
        Check(!string.IsNullOrWhiteSpace(feedback.text) && PlayerProgress.Coins == coins &&
            !PlayerProgress.Owns("cat_ghoul"), "Keyboard submission gives invalid-code feedback without rewards");
        if (!string.IsNullOrEmpty(promoCode))
        {
            input.text = promoCode;
            redeem.onClick.Invoke();
            Check(PlayerProgress.Owns("cat_ghoul") && PlayerProgress.Owns("clothing_ghoul") &&
                PlayerProgress.Owns("accessory_ghoul_mask") && PlayerProgress.Owns("accessory_ghoul_hair"),
                "Button submission redeems the full promo outfit");
            Check(input.text.Length == 0 && !redeem.interactable && !string.IsNullOrWhiteSpace(feedback.text),
                "Successful promo clears input and leaves a visible result");
            input.text = promoCode;
            input.onSubmit.Invoke(input.text);
            Check(!string.IsNullOrWhiteSpace(feedback.text) && PlayerProgress.Coins == coins,
                "Duplicate promo submission remains safe and provides feedback");
        }
        Check(ui.GoBack() && !modal.activeSelf && shop.interactable && shop.blocksRaycasts,
            "Back closes promo first and restores wardrobe interaction");
        Check(((GameObject)Field(ui, "shop")).activeSelf, "Closing promo keeps the wardrobe open");
        if (!string.IsNullOrEmpty(promoCode))
        {
            ValidatePages(ui);
            foreach (string id in PromoOnlyIds)
                Check(PlayerProgress.Owns(id), "Redeemed secret remains owned after wardrobe paging: " + id);
            EquipFromWardrobe(ui, 0, "cat_ghoul");
            EquipFromWardrobe(ui, 1, "clothing_ghoul");
            EquipFromWardrobe(ui, 2, "accessory_ghoul_hair");
            EquipFromWardrobe(ui, 2, "accessory_ghoul_mask");
            Check(PlayerProgress.IsEquipped("accessory_ghoul_hair") &&
                PlayerProgress.IsEquipped("accessory_ghoul_mask"),
                "Both secret accessories can be worn together from the wardrobe");
        }
    }

    private static void ValidateAstronautWardrobe(FrontEndUI ui)
    {
        const string helmet = "accessory_astronaut_helmet";
        bool obsoleteCat = false;
        bool accessoryListed = false;
        foreach (PlayerProgress.Item item in PlayerProgress.Catalog)
        {
            if (item.Id == "cat_astronaut") obsoleteCat = true;
            if (item.Id == helmet) accessoryListed = item.Category == PlayerProgress.Category.Accessory;
        }
        Check(!obsoleteCat && accessoryListed, "Astronaut helmet is an accessory, not a separate cat skin");
        Check(!PlayerProgress.Owns(helmet) && !PlayerProgress.Owns("clothing_astronaut"),
            "Astronaut pieces start locked in isolated progress");
        Check(PlayerProgress.AwardHeightUnlocks(PlayerProgress.AstronautScore, true) &&
            PlayerProgress.Owns(helmet) && PlayerProgress.Owns("clothing_astronaut"),
            "Reaching space unlocks the helmet and suit together");

        Check(PlayerProgress.TryPurchase("cat_siamese") && PlayerProgress.Equip("cat_siamese") &&
            PlayerProgress.Equip("clothing_astronaut"),
            "An existing cat can wear the astronaut suit");
        Call(ui, "SelectTab", 2);
        object row = FindRow(ui, helmet);
        object rowPreview = RowField(row, "Preview");
        var previewCat = (UnityEngine.UI.Image)rowPreview.GetType().GetField("Cat").GetValue(rowPreview);
        IList artwork = (IList)rowPreview.GetType().GetField("Accessories").GetValue(rowPreview);
        Check(previewCat.enabled && previewCat.sprite != null && artwork.Count > 0 &&
            ((DoodlikCosmeticPreview)artwork[0]).ItemId == helmet,
            "Helmet row previews the selected cat wearing the helmet");
        UnityEngine.UI.Button action = (UnityEngine.UI.Button)RowField(row, "Action");
        Check(action.interactable, "Unlocked astronaut helmet can be equipped from its accessory row");
        action.onClick.Invoke();
        Check(PlayerProgress.IsEquipped(helmet) &&
            PlayerProgress.EquippedId(PlayerProgress.Category.Cat) == "cat_siamese" &&
            PlayerProgress.EquippedId(PlayerProgress.Category.Clothing) == "clothing_astronaut",
            "Helmet and suit combine without replacing the selected cat skin");
        ui.ShowHome();
        object homePreview = Field(ui, "homePreview");
        IList homeArtwork = (IList)homePreview.GetType().GetField("Accessories").GetValue(homePreview);
        bool helmetShown = false;
        foreach (DoodlikCosmeticPreview graphic in homeArtwork)
            if (graphic.enabled && graphic.ItemId == helmet) helmetShown = true;
        Check(helmetShown, "Home preview displays the equipped astronaut helmet");
        Call(ui, "ShowShop");
    }

    private static void EquipFromWardrobe(FrontEndUI ui, int tab, string id)
    {
        Call(ui, "SelectTab", tab);
        object row = FindRow(ui, id);
        UnityEngine.UI.Button action = (UnityEngine.UI.Button)RowField(row, "Action");
        Check(action.interactable, "Unlocked secret item can be selected: " + id);
        action.onClick.Invoke();
        Check(PlayerProgress.IsEquipped(id), "Unlocked secret item can be worn: " + id);
    }

    private static object FindRow(FrontEndUI ui, string id)
    {
        for (int page = 0; page < PlayerProgress.Catalog.Count; page++)
        {
            foreach (object row in (object[])Field(ui, "rows"))
                if (((GameObject)RowField(row, "Root")).activeSelf && (string)RowField(row, "ItemId") == id) return row;
            UnityEngine.UI.Button next = (UnityEngine.UI.Button)Field(ui, "nextPage");
            if (!next.interactable) break;
            next.onClick.Invoke();
        }
        throw new InvalidOperationException("Wardrobe row not found: " + id);
    }

    private static object Field(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static object RowField(object row, string field) => row.GetType().GetField(field).GetValue(row);
    private static void Call(FrontEndUI ui, string method, params object[] args) => typeof(FrontEndUI).GetMethod(method, Private).Invoke(ui, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Wardrobe UI validation: " + message);
        checks++;
    }
}
