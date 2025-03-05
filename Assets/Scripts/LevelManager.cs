using UnityEngine;

public class LevelManager : MonoBehaviour
{
    // levelScore multiplier = ax^2 + bx + c
    [SerializeField] private float levelScoreA = 500f;
    [SerializeField] private float levelScoreB;
    [SerializeField] private float levelScoreC = 3000f;

    // heat increases each level from 0.1 - 0.8 for probability of fire tile per turn, reduced by high value words
    // heat probability per level = modified sigmoid; (a / (1 + e^(bx+c))) + d
    [SerializeField] private float heatProbabilityA = 0.7f;
    [SerializeField] private float heatProbabilityB = -0.3f;
    [SerializeField] private float heatProbabilityC = 4f;
    [SerializeField] private float heatProbabilityD = 0.1f;

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

    public static LevelManager instance;

    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogWarning("More than one LevelManager in the scene! This one will be disabled.");
            enabled = false;
            return;
        }
        instance = this;

        // TODO: load saved game from disk
        TotalScore = 0;
        Level = 1;
        currentLevelScore = 0;
    }

    /// <summary>
    /// Add score to current score, incrementing current level score and progress
    /// </summary>
    /// <param name="amount">Amount to increase score</param>
    /// <returns><c>true</c> if levelled up as a result, else <c>false</c></returns>
    public bool AddScore(int amount)
    {
        TotalScore += amount;
        currentLevelScore += amount;
        if (currentLevelScore >= LevelScoreRequirement)
        {
            currentLevelScore -= LevelScoreRequirement;
            Level++;
            return true;
        }
        return false;
    }
}
