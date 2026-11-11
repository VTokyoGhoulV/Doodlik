// Independent per-attempt clocks: bonuses count route platforms; coins count
// world height bands, not platforms. Neither changes UnityEngine.Random.
public sealed class PickupSpawnSchedule
{
    private uint state, coinState, rocketState;
    private int nextBonus, nextBonusKind;
    private int lastPlatform = -1;
    private float nextCoinHeight;
    private int nextRocket, lastRocketPlatform = -1, lastOrdinaryBonus = -1000;

    public readonly struct CoinBand
    {
        public readonly float Minimum, Maximum;
        public readonly int Seed;
        public CoinBand(float center, int seed) { Minimum = center - 2f; Maximum = center + 2f; Seed = seed; }
    }

    public PickupSpawnSchedule(uint seed)
    {
        state = seed == 0 ? 0x9e3779b9u : seed;
        coinState = state ^ 0x85ebca6bu;
        if (coinState == 0) coinState = 0x27d4eb2du;
        nextCoinHeight = CoinRange(15, 25);
        nextBonus = Range(18, 28);
        nextBonusKind = Range(1, 3);
        // Do not consume either existing stream: adding rockets cannot move
        // coins or change the old shield / magnet / slow rotation.
        rocketState = state ^ 0xc2b2ae35u;
        if (rocketState == 0) rocketState = 0x165667b1u;
        nextRocket = RocketRange(55, 75);
    }

    public bool TryGetKind(int platformIndex, out int kind)
    {
        kind = -1;
        if (platformIndex <= lastPlatform) return false;
        lastPlatform = platformIndex;
        if (platformIndex >= nextBonus)
        {
            kind = nextBonusKind;
            lastOrdinaryBonus = platformIndex;
            nextBonusKind = nextBonusKind % 3 + 1;
            nextBonus = platformIndex + Range(18, 28);
        }
        else return false;
        return true;
    }

    public bool BonusScheduledFor(int platformIndex) =>
        platformIndex > lastPlatform && platformIndex >= nextBonus;

    // Call alongside the ordinary platform clock. A due rocket waits for a
    // clear row; it never replaces a normal power or accumulates a backlog.
    public bool TryRocketForPlatform(int platformIndex, int rawHeight, out int kind)
    {
        kind = -1;
        if (platformIndex <= lastRocketPlatform) return false;
        lastRocketPlatform = platformIndex;
        if (rawHeight < 250 || platformIndex < nextRocket || platformIndex - lastOrdinaryBonus < 4 ||
            nextBonus >= platformIndex && nextBonus - platformIndex < 4) return false;
        kind = 4;
        nextRocket = platformIndex + RocketRange(100, 150);
        return true;
    }

    public bool TryTakeCoinBand(float knownHeight, out CoinBand band)
    {
        band = default;
        if (knownHeight < nextCoinHeight + 2f) return false;
        band = new CoinBand(nextCoinHeight, CoinRange(0, 1000000));
        // One opportunity every 15–25 world units (~6–10 typical gaps), even
        // when no safe detour exists. Failed bands are not queued into clusters.
        nextCoinHeight += CoinRange(15, 25);
        return true;
    }

    private int CoinRange(int minimum, int maximum)
    {
        coinState ^= coinState << 13;
        coinState ^= coinState >> 17;
        coinState ^= coinState << 5;
        return minimum + (int)(coinState % (uint)(maximum - minimum + 1));
    }

    private int Range(int minimum, int maximum)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return minimum + (int)(state % (uint)(maximum - minimum + 1));
    }

    private int RocketRange(int minimum, int maximum)
    {
        rocketState ^= rocketState << 13;
        rocketState ^= rocketState >> 17;
        rocketState ^= rocketState << 5;
        return minimum + (int)(rocketState % (uint)(maximum - minimum + 1));
    }
}
