using System;
using System.Collections.Generic;

public sealed class ChallengeDefinition
{
    public readonly string Id, Name, Description, RewardItemId;
    public readonly int Target, RewardCoins;
    public readonly bool IsAchievement;

    internal ChallengeDefinition(string id, string name, string description, int target,
        int rewardCoins = 0, string rewardItemId = "", bool achievement = false)
    {
        Id = id; Name = name; Description = description; Target = target;
        RewardCoins = rewardCoins; RewardItemId = rewardItemId; IsAchievement = achievement;
    }
}

// One-time tasks use the existing route unchanged. The physics bridge certifies
// observed landings, bush flights and pickups; no task adds a mandatory detour.
public static class ChallengeCatalog
{
    private static readonly ChallengeDefinition[] Definitions =
    {
        new ChallengeDefinition("run_coin10", "Карманные расходы", "Собери 10 монет за один забег.", 10, 30),
        new ChallengeDefinition("run_coin25", "Пушистый капиталист", "Собери 25 монет за один забег.", 25, 60),
        new ChallengeDefinition("run_coin_nomagnet15", "Честно заработано", "Собери 15 монет за один забег до первого подобранного магнита.", 15, 45),
        new ChallengeDefinition("run_height500_nopowers", "Без волшебства", "Достигни высоты 500, не подбирая усилители до этой высоты. Кусты разрешены.", 500, 45),
        new ChallengeDefinition("run_height500_noshield", "Усы целы", "Достигни высоты 500 без срабатывания щита до этой высоты.", 500, 40),
        new ChallengeDefinition("run_height1000", "Выше облаков", "Достигни высоты 1000 за один забег на любой сложности.", 1000, 60),
        new ChallengeDefinition("run_moving5", "Подвижная мишень", "Приземлись на 5 разных движущихся платформ при подъёме за один забег.", 5, 50),
        new ChallengeDefinition("run_narrow8", "Маленькая, но моя", "Приземлись на 8 разных маленьких платформ при подъёме за один забег.", 8, 55),
        new ChallengeDefinition("run_center10", "В яблочко", "10 раз приземлись в центральную треть разных платформ при подъёме за один забег.", 10, 45),
        new ChallengeDefinition("run_wide5", "Дальнобойщик", "Выполни при подъёме 5 длинных боковых прыжков за один забег. Порог учитывает ширину экрана и дальность обычного прыжка.", 5, 60),
        new ChallengeDefinition("run_coin50", "Пушистая копилка", "Собери 50 монет за один забег.", 50, 80),
        new ChallengeDefinition("run_coin_fall_save", "Последнюю — и домой", "Подбери монету во время падения и самостоятельно приземлись на новую более высокую платформу.", 1, 30),
        new ChallengeDefinition("run_coin_apex3", "На самой вершине", "Подбери 3 монеты вблизи верхней точки прыжка за один забег.", 3, 45),
        new ChallengeDefinition("run_magnet2", "Усатый пылесос", "Собери 2 монеты за одно действие подобранного магнита.", 2, 40),
        new ChallengeDefinition("run_space_coins3", "Сокровища космоса", "Собери 3 монеты в космосе за один забег.", 3, 80),
        new ChallengeDefinition("run_edge5", "На кончиках лап", "5 раз приземлись возле края новой платформы при подъёме за один забег.", 5, 45),
        new ChallengeDefinition("run_center_streak5", "Пять в яблочко", "Приземлись в центральную треть 5 новых платформ подряд. Промах по центру или возврат на нижнюю опору обрывает серию.", 5, 55),
        new ChallengeDefinition("run_moving10", "Ловец движения", "Приземлись на 10 разных движущихся платформ при подъёме за один забег.", 10, 80),
        new ChallengeDefinition("run_narrow12", "Ювелирные лапки", "Приземлись на 12 разных маленьких платформ при подъёме за один забег.", 12, 80),
        new ChallengeDefinition("run_wide10", "Через всё небо", "Выполни при подъёме 10 длинных боковых прыжков за один забег. Порог учитывает ширину экрана и дальность обычного прыжка.", 10, 90),
        new ChallengeDefinition("run_turn4", "Туда-сюда", "4 раза смени направление боковых прыжков между новыми платформами за один забег.", 4, 55),
        new ChallengeDefinition("run_wrap5", "Кругосветный котик", "5 раз перелети через боковую границу экрана за один забег.", 5, 45),
        new ChallengeDefinition("run_bush3", "Пчелиный экспресс", "Воспользуйся 3 кустами за один забег.", 3, 45),
        new ChallengeDefinition("run_bush_chain2", "Двойная пересадка", "Используй 2 куста подряд, не приземляясь между ними на обычную платформу.", 2, 60),
        new ChallengeDefinition("run_bush_skip2", "Выше ожиданий", "После прыжка от куста перелети 2 ряда платформ и безопасно приземлись выше.", 2, 60),
        new ChallengeDefinition("run_all_powers", "Все возможности", "Подбери щит, магнит и замедление за один забег.", 3, 50),
        new ChallengeDefinition("run_shield_recovery100", "Вторая жизнь", "После срабатывания щита увеличь максимальную высоту ещё на 100 за тот же забег.", 100, 50),
        new ChallengeDefinition("run_slow_moving1", "Спокойная посадка", "При действующем замедлении приземлись на новую движущуюся платформу.", 1, 30),
        new ChallengeDefinition("run_clean1000", "Хладнокровный", "Достигни высоты 1000 без срабатывания щита до этой высоты.", 1000, 75),
        new ChallengeDefinition("run_no_powers1000", "Только своими силами", "Достигни высоты 1000, не подбирая усилители до этой высоты. Кусты разрешены.", 1000, 80),
        new ChallengeDefinition("run_rocket_land1", "Бумажный пилот", "Заверши полёт на бумажной ракете мягкой посадкой на безопасную платформу.", 1, 50),
        new ChallengeDefinition("run_rocket_wrap1", "Вокруг бумажного мира", "Во время полёта на ракете перелети через боковую границу экрана, а затем безопасно приземлись.", 1, 70),
        new ChallengeDefinition("run_rocket_land2", "Рейс с пересадкой", "Безопасно заверши 2 разных полёта на ракете за один забег.", 2, 85),
        new ChallengeDefinition("run_rocket_climb100", "Дальше — своими лапами", "После безопасной посадки с ракеты увеличь максимальную высоту ещё на 100 без новой ракеты и срабатывания щита. Кусты разрешены.", 100, 65),
        new ChallengeDefinition("run_gold1", "Золотой улов", "Подбери одну золотую рыбку. Считается сама рыбка, не её стоимость в монетах.", 1, 35),
        new ChallengeDefinition("run_gold3", "Три желания", "Подбери 3 золотые рыбки за один забег. Стоимость рыбок в монетах не увеличивает счётчик.", 3, 80),
        new ChallengeDefinition("run_gold_manual1", "Без притяжения", "Подбери золотую рыбку без действующего магнита.", 1, 45),
        new ChallengeDefinition("run_gold_magnet1", "Золотое притяжение", "Подбери золотую рыбку при действующем магните.", 1, 45),
        new ChallengeDefinition("run_gold_safe1", "Улов с возвращением", "Подбери золотую рыбку вне полёта на ракете и затем без помощи щита приземлись на новую более высокую платформу.", 1, 60),
        new ChallengeDefinition("run_bee_dodge1", "Не сегодня, шмель", "Уклонись от настоящего броска сердитого шмеля, не используя щит или защиту ракеты.", 1, 50),
        new ChallengeDefinition("run_bee_dodge3", "Танец со шмелями", "За один забег уклонись от 3 разных бросков сердитых шмелей без щита и защиты ракеты.", 3, 90),
        new ChallengeDefinition("run_sky_combo3", "Небесный трюк", "За один забег безопасно заверши полёт на ракете, подбери золотую рыбку и уклонись от броска сердитого шмеля. Порядок любой.", 3, 120),
        new ChallengeDefinition("mastery_center50", "Меткие лапки", "50 раз приземлись в центральную треть разных платформ при подъёме. Считается за все забеги.", 50,
            rewardItemId: "accessory_target", achievement: true),
        new ChallengeDefinition("mastery_rescue3", "Девять жизней", "Трижды спаси котика от падения благодаря щиту. Считается за все забеги.", 3,
            rewardItemId: "trail_ninelives", achievement: true),
        new ChallengeDefinition("mastery_hard_clean1000", "Колючий характер", "Достигни высоты 1000 на сложной сложности без срабатывания щита.", 1000,
            rewardItemId: "accessory_patch", achievement: true),
        new ChallengeDefinition("mastery_hard_space", "Без скидок", "Доберись до космоса на сложной сложности. Учитывается высота, не счёт.", WorldZones.GoalScore,
            rewardItemId: "trail_meteor", achievement: true),
        new ChallengeDefinition("mastery_controls1000", "Котик на все случаи", "Достигни высоты 1000 с точным управлением и с инерцией. Формат и сложность любые.", 2,
            rewardItemId: "clothing_explorer", achievement: true),
        new ChallengeDefinition("mastery_coins250", "Кошачий капитал", "Собери 250 игровых монет за все забеги. Монеты из наград не учитываются.", 250, 150, achievement: true),
        new ChallengeDefinition("mastery_bush25", "Друг пчёл", "Воспользуйся 25 кустами за все забеги.", 25, 120, achievement: true),
        new ChallengeDefinition("mastery_moving50", "Неуловимый", "Приземлись на 50 новых движущихся платформ при подъёме за все забеги.", 50, 150, achievement: true),
        new ChallengeDefinition("mastery_narrow75", "Мастер маленьких опор", "Приземлись на 75 новых маленьких платформ при подъёме за все забеги.", 75, 180, achievement: true),
        new ChallengeDefinition("mastery_tasks10", "Котик-мастер", "Выполни 10 разных выбранных испытаний на забег.", 10, 200, achievement: true),
        new ChallengeDefinition("mastery_rocket10", "Капитан бумажного неба", "Безопасно заверши 10 полётов на ракете за все забеги.", 10, 150, achievement: true),
        new ChallengeDefinition("mastery_gold15", "Золотая коллекция", "Подбери 15 золотых рыбок за все забеги. Считаются рыбки, не их стоимость в монетах.", 15, 150, achievement: true),
        new ChallengeDefinition("mastery_bee20", "Укротитель шмелей", "Уклонись от 20 настоящих бросков сердитых шмелей без щита и защиты ракеты за все забеги.", 20, 200, achievement: true)
    };

    public static IReadOnlyList<ChallengeDefinition> All { get; } = Array.AsReadOnly(Definitions);

    public static ChallengeDefinition Find(string id)
    {
        for (int i = 0; i < Definitions.Length; i++)
            if (Definitions[i].Id == id) return Definitions[i];
        return null;
    }
}
