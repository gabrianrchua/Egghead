using UnityEngine;
using Egghead.SaveSystem;

public class LevelManager : Singleton<LevelManager>
{
    // level score requirement = base + linear * (level - 1) + amplitude / (1 + e^(-rate * (level - midpoint)))
    // sigmoid accelerates early progression, while the linear term keeps late levels increasing
    // default values ~~ 1000, 1300, 1600, 2000, 2500 ... 5000, 5100, 5200
    [Header("Level score requirement function (linear + sigmoid)")]
    [SerializeField] private float levelScoreBase = 824f;
    [SerializeField] private float levelScoreLinearGrowth = 100f;
    [SerializeField] private float levelScoreSigmoidAmplitude = 2276f;
    [SerializeField] private float levelScoreSigmoidRate = 0.648f;
    [SerializeField] private float levelScoreSigmoidMidpoint = 4.66f;

    // heat increases each level for probability of fire tile per turn, reduced by high value words
    // heat probability per level = modified sigmoid; (a / (1 + e^(bx+c))) + d
    // default values: 0.01 to 0.5, plateauing around level 25
    [Header("Heat probability function ((a / (1 + e^(bx+c))) + d)")]
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
            float sigmoid = levelScoreSigmoidAmplitude /
                (1f + Mathf.Exp(-levelScoreSigmoidRate * (Level - levelScoreSigmoidMidpoint)));

            return Mathf.RoundToInt(
                levelScoreBase +
                (levelScoreLinearGrowth * (Level - 1)) +
                sigmoid);
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
        SaveData data = await SaveManager.Instance.GetCurrentSaveData();

        TotalScore = 0;
        Level = 1;
        currentLevelScore = 0;
        if (data.LetterTileData != null)
        {
            // adding the saved score value will also increment level + other values appropriately
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
    /// <returns><c>true</c> if at least one level-up occurred, otherwise <c>false</c></returns>
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
