using UnityEngine;

[CreateAssetMenu(fileName = "JourneySettings", menuName = "Doodlik/Настройки путешествия")]
public sealed class JourneySettings : ScriptableObject
{
    public const int DefaultGoalScore = 5000;
    public const string ResourcePath = "Settings/JourneySettings";

    [Header("Подъём от луга до космоса")]
    [SerializeField, Min(200), InspectorName("Высота полного путешествия")]
    [Tooltip("Счёт, на котором заканчивается подъём фона к космосу. Все шесть зон распределяются пропорционально. Можно увеличить позже: рекорды и физика прыжков не меняются, игра продолжается выше этой отметки.")]
    private int journeyGoalScore = DefaultGoalScore;

    public int GoalScore => Mathf.Max(200, journeyGoalScore);

    private void OnValidate() => journeyGoalScore = Mathf.Max(200, journeyGoalScore);
}
