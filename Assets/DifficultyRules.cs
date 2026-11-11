public enum GameDifficulty { Easy = 0, Normal = 1, Hard = 2 }

// Height drives the journey; difficulty changes only the points awarded for it.
public static class DifficultyRules
{
    public static GameDifficulty Sanitize(GameDifficulty difficulty) =>
        difficulty == GameDifficulty.Easy || difficulty == GameDifficulty.Hard
            ? difficulty : GameDifficulty.Normal;

    public static string Name(GameDifficulty difficulty)
    {
        switch (Sanitize(difficulty))
        {
            case GameDifficulty.Easy: return "Лёгкая";
            case GameDifficulty.Hard: return "Сложная";
            default: return "Обычная";
        }
    }

    public static string MultiplierLabel(GameDifficulty difficulty)
    {
        switch (Sanitize(difficulty))
        {
            case GameDifficulty.Easy: return "×0,85";
            case GameDifficulty.Hard: return "×1,25";
            default: return "×1";
        }
    }

    public static int ScoreForHeight(int height, GameDifficulty difficulty)
    {
        if (height <= 0) return 0;
        GameDifficulty valid = Sanitize(difficulty);
        int numerator = valid == GameDifficulty.Easy ? 85 : valid == GameDifficulty.Hard ? 125 : 100;
        long score = (long)height * numerator / 100;
        return score >= int.MaxValue ? int.MaxValue : (int)score;
    }
}
