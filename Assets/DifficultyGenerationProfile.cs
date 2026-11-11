// Captured once by a run's generators. All Normal values are identities so
// its existing height curves, control/format compensation and random draws stay intact.
public readonly struct DifficultyGenerationProfile
{
    public GameDifficulty Difficulty { get; }

    public DifficultyGenerationProfile(GameDifficulty difficulty)
    {
        Difficulty = difficulty == GameDifficulty.Easy || difficulty == GameDifficulty.Hard
            ? difficulty : GameDifficulty.Normal;
    }

    private float Value(float easy, float normal, float hard) =>
        Difficulty == GameDifficulty.Easy ? easy : Difficulty == GameDifficulty.Hard ? hard : normal;

    public float PlatformWidth => Value(1.12f, 1f, 0.92f);
    public float ReachMargin => Value(0.90f, 1f, 1f);
    public float VerticalGap => Value(0.94f, 1f, 1f);
    public float MovingChance => Value(0.85f, 1f, 1.12f);
    public float MovingSpeed => Value(0.82f, 1f, 1.12f);
    public float NarrowChance => Value(0.75f, 1f, 1.10f);
    public float HazardChance => Value(0.75f, 1f, 1.10f);
    public float HazardSpacing => Value(1.22f, 1f, 0.92f);
    public float HazardDensity => Value(0.80f, 1f, 1.10f);
    public float HazardSpeed => Value(0.80f, 1f, 1.20f);
    // Wider Easy supports admit more safe patches after route vetos. Keep the
    // actual density below Normal, not just the initial placement probability.
    public float ThornPatchChance => Value(0.58f, 1f, 1.12f);

    public int PrecisionMinimumInterval(bool portrait) =>
        (portrait ? 2 : 3) + (Difficulty == GameDifficulty.Easy ? 1 : 0);
    public int PrecisionMaximumInterval(bool portrait) =>
        (portrait ? 4 : 6) + (Difficulty == GameDifficulty.Easy ? 2 :
            Difficulty == GameDifficulty.Hard ? -1 : 0);
    public float PrecisionChance(bool portrait) =>
        (portrait ? 0.60f : 0.50f) * Value(0.70f, 1f, 1.15f);
    public int WideMinimumInterval => Difficulty == GameDifficulty.Easy ? 3 : 2;
    public int WideMaximumInterval => Difficulty == GameDifficulty.Easy ? 6 :
        Difficulty == GameDifficulty.Hard ? 3 : 4;
    public float WideChance => Value(0.50f, 0.75f, 0.90f);
    public int ChallengeInterval => Difficulty == GameDifficulty.Easy ? 6 : 4;
    public int ThornPatchInterval(bool portrait) => (portrait ? 18 : 35) +
        (Difficulty == GameDifficulty.Easy ? 8 : Difficulty == GameDifficulty.Hard ? -2 : 0);
}
