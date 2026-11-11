using System;
using System.Collections.Generic;
using UnityEngine;

public enum ExpeditionFindKind { None = 0, FireflyJar = 1, CloudWool = 2, StarShard = 3, SparkDust = 4 }

// World finds and rare enemy drops share the same protected cargo rules.
public static class ExpeditionCatalog
{
    public sealed class Definition
    {
        public readonly ExpeditionFindKind Kind;
        public readonly string Id, Name, Description;
        public readonly int MinimumHeight, Value;
        public readonly bool EnemyOnly;
        public readonly Color32 Color;

        internal Definition(ExpeditionFindKind kind, string id, string name,
            string description, int minimumHeight, int value, Color32 color, bool enemyOnly = false)
        {
            Kind = kind; Id = id; Name = name; Description = description;
            MinimumHeight = minimumHeight; Value = value; Color = color; EnemyOnly = enemyOnly;
        }
    }

    private static readonly Definition[] definitions =
    {
        new Definition(ExpeditionFindKind.FireflyJar, "firefly_jar", "Банка светлячков",
            "Свет для защитных оберегов в мастерской.", 0, 1, new Color32(229, 190, 68, 255)),
        new Definition(ExpeditionFindKind.CloudWool, "cloud_wool", "Облачная шерсть",
            "Мягкая основа защитного оберега.", 900, 3, new Color32(181, 218, 229, 255)),
        new Definition(ExpeditionFindKind.StarShard, "star_shard", "Осколок звезды",
            "Редкий материал для талисмана охотника.", 2200, 8, new Color32(212, 168, 231, 255)),
        new Definition(ExpeditionFindKind.SparkDust, "spark_dust", "Искровая пыль",
            "Редкая добыча грозового жука. Нужна для талисмана охотника.", 0, 5,
            new Color32(126, 205, 213, 255), true)
    };

    public static IReadOnlyList<Definition> All { get; } = Array.AsReadOnly(definitions);
    public static bool IsValid(ExpeditionFindKind kind) => (int)kind >= (int)ExpeditionFindKind.FireflyJar &&
        (int)kind <= (int)ExpeditionFindKind.SparkDust;
    public static Definition Find(ExpeditionFindKind kind) => IsValid(kind) ? definitions[(int)kind - 1] : null;
    public static Definition Find(string id)
    {
        for (int i = 0; i < definitions.Length; i++) if (definitions[i].Id == id) return definitions[i];
        return null;
    }
    public static int TotalValue(IReadOnlyList<ExpeditionFindKind> finds)
    {
        if (finds == null) return 0;
        long sum = 0;
        for (int i = 0; i < finds.Count; i++) sum += Find(finds[i])?.Value ?? 0;
        return (int)Math.Min(int.MaxValue, sum);
    }
}
