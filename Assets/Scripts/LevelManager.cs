using UnityEngine;

public class LevelManager : Singleton<LevelManager>
{
    // levelScore multiplier = ax^2 + bx + c
    [SerializeField] private float levelScoreA = 500f;
    [SerializeField] private float levelScoreB;
    [SerializeField] private float levelScoreC = 3000f;

    // heat increases each level for probability of fire tile per turn, reduced by high value words
    // heat probability per level = modified sigmoid; (a / (1 + e^(bx+c))) + d
    // default values: 0.01 to 0.5, plateauing around level 25
    [SerializeField] private float heatProbabilityA = 0.5f;
    [SerializeField] private float heatProbabilityB = -0.3f;
    [SerializeField] private float heatProbabilityC = 4f;
    [SerializeField] private float heatProbabilityD = 0f;

    public float Heat
    {
        get
        {
            return heatProbabilityA / (1 + Mathf.Exp(heatProbabilityB * Level + heatProbabilityC)) + heatProbabilityD;
        }
    }
    public int TotalScore { get; private set; }
    public int Level { get; private set; }
    public int LevelScoreRequirement
    {
        get
        {
            int adjustedLevel = Level - 1;
            return Mathf.RoundToInt((levelScoreA * adjustedLevel * adjustedLevel) + (levelScoreB * adjustedLevel) + levelScoreC);
        }
    }
    public float LevelPercentage
    {
        get
        {
            return (float)currentLevelScore / LevelScoreRequirement * 100f;
        }
    }

    private int currentLevelScore;

    private async void Start()
    {
        SaveManager.SaveData data = await SaveManager.Instance.GetCurrentSaveData();

        if (data.LetterTileData == null)
        {
            // if new game, use 0 score
            TotalScore = 0;
            Level = 1;
            currentLevelScore = 0;
        }
        else
        {
            TotalScore = 0;
            AddScore(data.Score);
        }

        // initialize UI
        UIManager ui = UIManager.Instance;
        ui.SetLevel(Level);
        ui.SetCurrentScore(TotalScore, LevelPercentage);
    }

    /// <summary>
    /// Add score to current score, incrementing current level score and progress.
    /// Can handle multiple level ups.
    /// </summary>
    /// <param name="amount">Amount to increase score</param>
    /// <returns><c>true</c> if at least one level?up occurred, otherwise <c>false</c></returns>
    public bool AddScore(int amount)
    {
        bool leveledUp = false;

        TotalScore += amount;
        currentLevelScore += amount;

        while (currentLevelScore >= LevelScoreRequirement)
        {
            currentLevelScore -= LevelScoreRequirement;
            Level++;
            leveledUp = true;
        }

        return leveledUp;
    }
}
