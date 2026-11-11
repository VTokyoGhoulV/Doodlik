// All resource costs are applied within ExpeditionProgress's same durable
// envelope as crafted ownership. There is no cross-save currency transaction.
public static class ExpeditionWorkshop
{
    public const int WardJarCost = 2, WardWoolCost = 1, MaxPreparedWards = 9;
    public const int HunterSparkCost = 3, HunterStarCost = 1;
    public const string WardName = "Плетёный оберег";
    public const string WardDescription = "Поглощает один удар врага в следующей экспедиции. Не защищает от падения и колючек.";
    public const string HunterCharmName = "Талисман охотника";
    public const string HunterCharmDescription = "Постоянно +10 п.п. к шансу редкой добычи с врагов в экспедициях.";
}
