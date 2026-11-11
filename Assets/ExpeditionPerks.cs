using System;
using System.Collections.Generic;

public enum ExpeditionPerkKind { Vitality, Claws, Fortune, Carapace, Recovery, Resolve }

// Run-only talents. They never change platform reach, velocity or ordinary saves.
public static class ExpeditionPerks
{
    public static string Name(ExpeditionPerkKind kind)
    {
        switch (kind)
        {
            case ExpeditionPerkKind.Vitality: return "Крепкий котик";
            case ExpeditionPerkKind.Claws: return "Острые коготки";
            case ExpeditionPerkKind.Fortune: return "Чутьё охотника";
            case ExpeditionPerkKind.Carapace: return "Облачный оберег";
            case ExpeditionPerkKind.Recovery: return "Тёплый привал";
            case ExpeditionPerkKind.Resolve: return "Передышка в бою";
            default: return "Неизвестный талант";
        }
    }
    public static string Description(ExpeditionPerkKind kind)
    {
        switch (kind)
        {
            case ExpeditionPerkKind.Vitality: return "+1 к здоровью от врагов и лечение на 1. До двух раз за забег.";
            case ExpeditionPerkKind.Claws: return "+1 к урону прыжка сверху до конца экспедиции. Предел — 2.";
            case ExpeditionPerkKind.Fortune: return "+10 п.п. к шансу искровой пыли. Общий предел — 55%.";
            case ExpeditionPerkKind.Carapace: return "Один удар врага поглотит оберег. От колючек и падения не спасает.";
            case ExpeditionPerkKind.Recovery: return "Восстановить 2 здоровья сейчас, не выше максимума.";
            case ExpeditionPerkKind.Resolve: return "+0,25 с передышки после удара врага. До двух раз за забег.";
            default: return string.Empty;
        }
    }
    public static bool IsValid(ExpeditionPerkKind kind) => (int)kind >= 0 && (int)kind <= (int)ExpeditionPerkKind.Resolve;

    public static ExpeditionPerkKind[] Offer(IReadOnlyList<ExpeditionPerkKind> available, uint seed)
    {
        if (available == null || available.Count == 0) return Array.Empty<ExpeditionPerkKind>();
        var pool = new List<ExpeditionPerkKind>(6);
        for (int i = 0; i < available.Count; i++)
            if (IsValid(available[i]) && !pool.Contains(available[i])) pool.Add(available[i]);
        if (seed == 0) seed = 0x9e3779b9u;
        for (int i = pool.Count - 1; i > 0; i--)
        {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            int next = (int)(seed % (uint)(i + 1));
            ExpeditionPerkKind value = pool[i]; pool[i] = pool[next]; pool[next] = value;
        }
        return pool.GetRange(0, Math.Min(3, pool.Count)).ToArray();
    }
    public static uint Seed(string runId, int restHeight)
    {
        unchecked
        {
            uint value = 2166136261;
            if (runId != null) foreach (char letter in runId) value = (value ^ letter) * 16777619;
            return (value ^ (uint)restHeight) * 16777619;
        }
    }
}
