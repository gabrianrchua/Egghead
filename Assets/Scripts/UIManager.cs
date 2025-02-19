using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI currentWordText;
    [SerializeField] private TextMeshProUGUI currentWordScore;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private Slider levelScoreSlider;

    public static UIManager instance;

    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogWarning("More than one UIManager in the scene. Disabling this one");
            enabled = false;
            return;
        }
        instance = this;
    }

    public void SetCurrentWord(string word)
    {
        currentWordText.text = word.ToUpper();
    }

    public void SetCurrentWordScore(int score)
    {
        currentWordScore.text = score.ToString() + " points";
    }

    public void ClearCurrentWordScore()
    {
        currentWordScore.text = "";
    }

    public void SetCurrentScore(int score, float scorePercentage)
    {
        scoreText.text = score.ToString("N0");
        levelScoreSlider.value = scorePercentage / 100f;
    }

    public void SetLevel(int level)
    {
        levelText.text = "LVL\n" + level.ToString();
    }
}
