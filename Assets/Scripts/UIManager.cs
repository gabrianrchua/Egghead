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
    [SerializeField] private Image validWordBackground;
    [SerializeField] private GameObject validWordSubmitButton;
    [SerializeField] private GameObject gameOverOverlay;
    [SerializeField] private TextMeshProUGUI gameOverText;
    [SerializeField] private Slider soundVolumeSlider;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private GameObject gameOverAnimation;
    [SerializeField] private Toggle lowFpsToggle;

    public bool IsOverlayActive { get; private set; }

    public void SetCurrentWord(string word)
    {
        currentWordText.text = word.ToUpper();
    }

    public void SetCurrentWordScore(int score, LetterTile.TileType highestTileType)
    {
        currentWordScore.text = score.ToString() + " points";
        switch(highestTileType)
        {
            case LetterTile.TileType.Normal:
                validWordBackground.color = new Color32(255, 255, 255, 80);
                break;
            case LetterTile.TileType.Fire:
                validWordBackground.color = new Color32(217, 114, 87, 80);
                break;
            case LetterTile.TileType.Bonus:
                validWordBackground.color = new Color32(106, 190, 48, 80);
                break;
            case LetterTile.TileType.Gold:
                validWordBackground.color = new Color32(250, 242, 54, 80);
                break;
            case LetterTile.TileType.Diamond:
                validWordBackground.color = new Color32(0, 211, 255, 80);
                break;
        }
        validWordBackground.gameObject.SetActive(true);
        validWordSubmitButton.SetActive(true);
    }

    public void ClearCurrentWordScore()
    {
        currentWordScore.text = "";
        validWordBackground.gameObject.SetActive(false);
        validWordSubmitButton.SetActive(false);
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

        gameOverAnimation.SetActive(true);
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

    public void SetLowFpsToggleValue(bool value)
    {
        lowFpsToggle.isOn = value;
    }
}
