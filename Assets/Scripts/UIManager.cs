using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class UIManager : Singleton<UIManager>
{
    [SerializeField] private TextMeshProUGUI currentWordText;
    [SerializeField] private TextMeshProUGUI currentWordScore;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private Slider levelScoreSlider;
    [SerializeField] private GameObject validWordBackground;
    [SerializeField] private GameObject gameOverOverlay;
    [SerializeField] private TextMeshProUGUI gameOverText;
    [SerializeField] private Slider soundVolumeSlider;
    [SerializeField] private Slider musicVolumeSlider;

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

    public void ShowGameOverOverlay(int level, int score)
    {
        gameOverText.text = $"Score: {score}\nLevel {level}";
        gameOverOverlay.SetActive(true);
    }

    public void ChangeScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }

    public void SetSoundVolumeSliderValue(float volume)
    {
        soundVolumeSlider.value = volume;
    }

    public void SetMusicVolumeSliderValue(float volume)
    {
        musicVolumeSlider.value = volume;
    }
}
