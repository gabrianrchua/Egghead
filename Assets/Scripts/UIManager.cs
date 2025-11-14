using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : Singleton<UIManager>
{
    [SerializeField] private TextMeshProUGUI currentWordText;
    [SerializeField] private TextMeshProUGUI currentWordScore;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private Slider levelScoreSlider;
    [SerializeField] private GameObject validWordBackground;

    public bool IsOverlayActive { get; private set; }

    public void SetCurrentWord(string word)
    {
        currentWordText.text = word.ToUpper();
    }

    public void SetCurrentWordScore(int score)
    {
        currentWordScore.text = score.ToString() + " points";
        validWordBackground.SetActive(true);
    }

    public void ClearCurrentWordScore()
    {
        currentWordScore.text = "";
        validWordBackground.SetActive(false);
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

    public void BlockTaps()
    {
        IsOverlayActive = true;
    }

    public void UnblockTaps()
    {
        IsOverlayActive = false;
    }
}
