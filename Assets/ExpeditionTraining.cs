using System;
using System.Collections.Generic;

public enum ExpeditionTrainingKind { Vitality = 0, Claws = 1, Fortune = 2 }

// Training changes combat preparation, never the certified jump controller or
// classic mode. Wallet and level are persisted together by PlayerProgress.
public static class ExpeditionTraining
{
    private static readonly ExpeditionTrainingKind[] kinds =
        { ExpeditionTrainingKind.Vitality, ExpeditionTrainingKind.Claws, ExpeditionTrainingKind.Fortune };
    public static IReadOnlyList<ExpeditionTrainingKind> All { get; } = Array.AsReadOnly(kinds);
    public static bool IsValid(ExpeditionTrainingKind kind) => (int)kind >= 0 && (int)kind < kinds.Length;
    public static string Id(ExpeditionTrainingKind kind) => kind == ExpeditionTrainingKind.Vitality ? "vitality" :
        kind == ExpeditionTrainingKind.Claws ? "claws" : kind == ExpeditionTrainingKind.Fortune ? "fortune" : string.Empty;
    public static string Name(ExpeditionTrainingKind kind) => kind == ExpeditionTrainingKind.Vitality ? "Живучесть" :
        kind == ExpeditionTrainingKind.Claws ? "Острые коготки" : kind == ExpeditionTrainingKind.Fortune ? "Удача охотника" : "Неизвестное улучшение";
    public static string Description(ExpeditionTrainingKind kind) => kind == ExpeditionTrainingKind.Vitality ?
        "+1 здоровье против врагов за уровень. Колючки и падение остаются опасными." :
        kind == ExpeditionTrainingKind.Claws ? "+1 урон прыжка-атаки. На обычные прыжки не влияет." :
        kind == ExpeditionTrainingKind.Fortune ? "+5 п.п. к шансу редкой добычи с врагов за уровень." : string.Empty;
    public static int MaxLevel(ExpeditionTrainingKind kind) => !IsValid(kind) ? 0 : kind == ExpeditionTrainingKind.Claws ? 1 : 2;
    public static int Price(ExpeditionTrainingKind kind, int currentLevel)
    {
        if (!IsValid(kind) || currentLevel < 0 || currentLevel >= MaxLevel(kind)) return 0;
        return kind == ExpeditionTrainingKind.Vitality ? currentLevel == 0 ? 120 : 300 :
            kind == ExpeditionTrainingKind.Claws ? 220 : currentLevel == 0 ? 100 : 260;
    }
}
