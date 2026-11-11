using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// Read-only view on the existing safe-area Canvas. Career data and the bounded
// run journal remain owned by PlayerProgress; records keep their shipped keys.
public sealed class RunStatisticsUI : MonoBehaviour
{
    private static readonly Color Ink = new Color32(94, 49, 24, 255);
    private static readonly Color Amber = new Color32(255, 182, 48, 255);
    private static readonly Color MutedPaper = new Color32(245, 208, 138, 255);
    private const int RowsPerPage = 3;
    private readonly TMP_Text[] values = new TMP_Text[8];
    private readonly JournalRow[] rows = new JournalRow[RowsPerPage];
    private TMP_FontAsset font;
    private GameObject general, recent, paging;
    private TMP_Text subtitle, recordSummary, bestSinceUpdate, empty, pageText;
    private UnityEngine.UI.Button generalTab, recentTab, previous, next, back;
    private CareerStatisticsSnapshot snapshot;
    private int page;

    private sealed class JournalRow
    {
        public GameObject Root;
        public TMP_Text Mode, Result, Counters, Detail;
    }

    public bool IsVisible => gameObject.activeSelf;
    public bool ShowingRecent => recent != null && recent.activeSelf;
    public int CurrentPage => page;
    public int PageCount => Mathf.Max(1, ((snapshot?.RecentRuns.Count ?? 0) + RowsPerPage - 1) / RowsPerPage);

    public static RunStatisticsUI Create(Transform parent, TMP_FontAsset interfaceFont, Action goBack)
    {
        GameObject root = RectObject("Run statistics", parent, new Vector2(780f, 900f), Vector2.zero);
        RunStatisticsUI view = root.AddComponent<RunStatisticsUI>();
        view.font = interfaceFont;
        view.Build(goBack);
        root.SetActive(false);
        return view;
    }

    private void Build(Action goBack)
    {
        Paper(gameObject);
        Label(transform, "Статистика классики", new Vector2(700f, 66f), new Vector2(0f, 391f), 46f, true);
        subtitle = Label(transform, "", new Vector2(700f, 44f), new Vector2(0f, 337f), 23f);
        generalTab = Button(transform, "Общее", new Vector2(324f, 54f), new Vector2(-170f, 279f), () => ChangeTab(false));
        recentTab = Button(transform, "Последние", new Vector2(324f, 54f), new Vector2(170f, 279f), () => ChangeTab(true));

        general = RectObject("Career summary", transform, new Vector2(700f, 620f), new Vector2(0f, -62f));
        GameObject records = RectObject("Saved records", general.transform, new Vector2(700f, 126f), new Vector2(0f, 231f));
        Panel(records);
        Label(records.transform, "Сохранённые рекорды · все режимы", new Vector2(660f, 30f), new Vector2(0f, 37f), 23f, true);
        recordSummary = Label(records.transform, "", new Vector2(660f, 42f), Vector2.zero, 30f, true);
        Label(records.transform, "Высота и счёт могут быть из разных попыток.", new Vector2(660f, 28f), new Vector2(0f, -40f), 20f);

        string[] names = { "Завершено попыток", "Игровых монет", "Кустов использовано", "Активное время",
            "Движущихся опор", "Маленьких опор", "Длинных прыжков", "Монет за лучший забег" };
        for (int i = 0; i < values.Length; i++)
        {
            GameObject tile = RectObject("Metric " + i, general.transform, new Vector2(338f, 74f),
                new Vector2(i % 2 == 0 ? -181f : 181f, 122f - i / 2 * 86f));
            Panel(tile);
            Label(tile.transform, names[i], new Vector2(314f, 27f), new Vector2(0f, 20f), 21f);
            values[i] = Label(tile.transform, "", new Vector2(314f, 37f), new Vector2(0f, -13f), 29f, true);
            values[i].textWrappingMode = TextWrappingModes.NoWrap;
        }
        bestSinceUpdate = Label(general.transform, "", new Vector2(700f, 54f), new Vector2(0f, -222f), 21f);
        Label(general.transform, "Счётчики и журнал ведутся с этого обновления.\nСохранённые рекорды учитывают и прошлые версии.",
            new Vector2(700f, 56f), new Vector2(0f, -285f), 20f);

        recent = RectObject("Recent attempts", transform, new Vector2(700f, 560f), new Vector2(0f, -49f));
        for (int i = 0; i < rows.Length; i++)
        {
            JournalRow row = new JournalRow();
            row.Root = RectObject("Journal row " + i, recent.transform, new Vector2(700f, 170f), new Vector2(0f, 191f - i * 183f));
            Panel(row.Root);
            row.Mode = Label(row.Root.transform, "", new Vector2(658f, 34f), new Vector2(0f, 52f), 25f, true);
            row.Result = Label(row.Root.transform, "", new Vector2(658f, 37f), new Vector2(0f, 12f), 24f, true);
            row.Result.fontSizeMin = 18f;
            row.Counters = Label(row.Root.transform, "", new Vector2(658f, 46f), new Vector2(0f, -33f), 20f);
            row.Detail = Label(row.Root.transform, "", new Vector2(658f, 24f), new Vector2(0f, -70f), 21f);
            rows[i] = row;
        }
        empty = Label(recent.transform, "Здесь появятся завершённые попытки.\nСтарые забеги восстановить нельзя.",
            new Vector2(670f, 130f), new Vector2(0f, 10f), 28f);
        paging = RectObject("Journal pages", transform, new Vector2(500f, 42f), new Vector2(0f, -351f));
        previous = Button(paging.transform, "<", new Vector2(76f, 42f), new Vector2(-158f, 0f), () => ChangePage(-1));
        pageText = Label(paging.transform, "", new Vector2(180f, 42f), Vector2.zero, 24f);
        next = Button(paging.transform, ">", new Vector2(76f, 42f), new Vector2(158f, 0f), () => ChangePage(1));
        back = Button(transform, "Назад", new Vector2(320f, 56f), new Vector2(0f, -409f), goBack);
    }

    public void Show()
    {
        gameObject.SetActive(true);
        ChangeTab(false);
    }

    public void Hide() => gameObject.SetActive(false);
    public void ApplyScale(float scale) => transform.localScale = Vector3.one * Mathf.Max(0.1f, scale);

    public void Refresh()
    {
        if (!IsVisible) return;
        Render(PlayerProgress.Statistics);
    }

    private void Render(CareerStatisticsSnapshot current)
    {
        snapshot = current;
        bool history = ShowingRecent;
        subtitle.text = history ? "Последние пять попыток. Новая — сверху." : "Прогресс на этом устройстве";
        generalTab.GetComponent<UnityEngine.UI.Image>().color = history ? MutedPaper : Amber;
        recentTab.GetComponent<UnityEngine.UI.Image>().color = history ? Amber : MutedPaper;
        recordSummary.text = "Высота: " + Number(RunRecords.BestHeight) + " · Счёт: " + Number(BestSavedScore());
        values[0].text = Number(snapshot.Runs);
        values[1].text = Number(snapshot.TotalCoins);
        values[2].text = Number(snapshot.TotalBushes);
        values[3].text = Duration(snapshot.ActiveSeconds);
        values[4].text = Number(snapshot.TotalMovingLandings);
        values[5].text = Number(snapshot.TotalNarrowLandings);
        values[6].text = Number(snapshot.TotalWideJumps);
        values[7].text = Number(snapshot.BestRunCoins);
        bestSinceUpdate.text = "Лучшее с обновления: высота " + Number(snapshot.BestRunHeight) +
            "\nСчёт " + Number(snapshot.BestRunScore) + " · каждый показатель отдельно";
        page = Mathf.Clamp(page, 0, PageCount - 1);
        for (int i = 0; i < rows.Length; i++)
        {
            int index = page * RowsPerPage + i;
            JournalRow row = rows[i];
            bool exists = index < snapshot.RecentRuns.Count;
            row.Root.SetActive(exists);
            if (!exists) continue;
            RunStatisticsEntry run = snapshot.RecentRuns[index];
            row.Mode.text = "#" + (index + 1) + " · " + (run.Portrait ? "Книжный" : "Альбомный") + " · " +
                (run.Inertia ? "с инерцией" : "точный контроль");
            row.Result.text = "Высота " + Number(run.Height) + " · Счёт " + Number(run.Score) + " · Монеты " + Number(run.Coins);
            row.Counters.text = "Кусты " + Number(run.Bushes) + " · Движущиеся " + Number(run.MovingLandings) +
                " · Маленькие " + Number(run.NarrowLandings) + " · Длинные " + Number(run.WideJumps);
            row.Detail.text = DifficultyRules.Name(run.Difficulty) + " · " + Duration(run.ActiveSeconds) + " · " +
                (string.IsNullOrEmpty(run.EndReason) ? "Забег завершён" : run.EndReason);
        }
        empty.gameObject.SetActive(snapshot.RecentRuns.Count == 0);
        paging.SetActive(history && PageCount > 1);
        pageText.text = (page + 1) + " / " + PageCount;
        previous.interactable = page > 0;
        next.interactable = page + 1 < PageCount;
    }

    private void ChangeTab(bool history)
    {
        general.SetActive(!history);
        recent.SetActive(history);
        page = 0;
        Refresh();
        Focus(history ? recentTab : generalTab);
    }

    private void ChangePage(int delta)
    {
        page = Mathf.Clamp(page + delta, 0, PageCount - 1);
        Refresh();
        Focus(delta > 0 && next.interactable ? next : previous.interactable ? previous : back);
    }

    private static int BestSavedScore()
    {
        int best = Mathf.Max(0, PlayerPrefs.GetInt(Player.HighScoreKey, 0));
        for (int mode = 0; mode < 4; mode++)
        for (int difficulty = 0; difficulty < 3; difficulty++)
            best = Mathf.Max(best, PlayerPrefs.GetInt(RunRecords.ScoreKey(mode >= 2, mode % 2 == 1, (GameDifficulty)difficulty), 0));
        return best;
    }

    private static string Number(long value)
    {
        value = Math.Max(0L, value);
        if (value < 1000000000L) return value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');
        if (value < 1000000000000L)
            return (value / 1000000000d).ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',') + " млрд";
        int exponent = (int)Math.Floor(Math.Log10(value));
        return (value / Math.Pow(10d, exponent)).ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',') + " × 10^" + exponent;
    }

    private static string Duration(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds)) seconds = 0d;
        double total = Math.Floor(Math.Max(0d, seconds));
        if (total >= 1000000d * 3600d) return "> 999 999 ч";
        if (total >= 3600d) return Math.Floor(total / 3600d).ToString("0", CultureInfo.InvariantCulture) + " ч " +
            ((int)(total % 3600d) / 60).ToString("00") + " мин";
        return ((int)total / 60) + " мин " + ((int)total % 60).ToString("00") + " с";
    }

    private TMP_Text Label(Transform parent, string text, Vector2 size, Vector2 position, float fontSize, bool bold = false)
    {
        TMP_Text label = RectObject("Label", parent, size, position).AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = text;
        label.color = Ink;
        label.fontSize = label.fontSizeMax = fontSize;
        label.fontSizeMin = Mathf.Max(18f, fontSize - 3f);
        label.enableAutoSizing = true;
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    private UnityEngine.UI.Button Button(Transform parent, string text, Vector2 size, Vector2 position, Action action)
    {
        GameObject root = RectObject(text, parent, size, position);
        UnityEngine.UI.Image image = root.AddComponent<UnityEngine.UI.Image>();
        image.color = Amber;
        UnityEngine.UI.Outline outline = root.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color32(164, 86, 32, 255); outline.effectDistance = new Vector2(3f, -3f);
        UnityEngine.UI.Button button = root.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        UnityEngine.UI.ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.96f, 0.78f); colors.pressedColor = new Color(0.88f, 0.75f, 0.55f);
        colors.disabledColor = new Color(0.78f, 0.76f, 0.70f); colors.fadeDuration = 0.08f; button.colors = colors;
        button.onClick.AddListener(() => { GameAudio.Play(GameAudio.Cue.Click); action(); });
        Label(root.transform, text, size - new Vector2(16f, 12f), Vector2.zero, 28f, true);
        return button;
    }

    private static void Paper(GameObject root)
    {
        UnityEngine.UI.Image image = root.AddComponent<UnityEngine.UI.Image>(); image.color = new Color32(255, 232, 179, 255);
        UnityEngine.UI.Outline border = root.AddComponent<UnityEngine.UI.Outline>(); border.effectColor = Ink; border.effectDistance = new Vector2(6f, -6f);
        UnityEngine.UI.Shadow shadow = root.AddComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = new Color(0.12f, 0.08f, 0.04f, 0.32f); shadow.effectDistance = new Vector2(10f, -11f);
    }

    private static void Panel(GameObject root)
    {
        UnityEngine.UI.Image image = root.AddComponent<UnityEngine.UI.Image>();
        image.color = new Color32(255, 242, 206, 255); image.raycastTarget = false;
        UnityEngine.UI.Outline border = root.AddComponent<UnityEngine.UI.Outline>();
        border.effectColor = new Color(0.55f, 0.34f, 0.16f, 0.4f); border.effectDistance = new Vector2(2f, -2f);
    }

    private static GameObject RectObject(string name, Transform parent, Vector2 size, Vector2 position)
    {
        GameObject root = new GameObject(name, typeof(RectTransform));
        RectTransform rect = root.GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f); rect.sizeDelta = size; rect.anchoredPosition = position;
        return root;
    }

    private static void Focus(UnityEngine.UI.Button button)
    {
        if (GameplayPreferences.IsDesktopBuild && EventSystem.current != null && button != null)
            EventSystem.current.SetSelectedGameObject(button.gameObject);
    }
}
