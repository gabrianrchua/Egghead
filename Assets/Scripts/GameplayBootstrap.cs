using System.Collections;
using System.Threading.Tasks;
using Egghead.SaveSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameplayBootstrap : MonoBehaviour
{
    private static System.Func<Task<SaveData>> loadSaveOverride = null;

    [SerializeField] private GameManager gameManager;
    [SerializeField] private LevelManager levelManager;
    [SerializeField] private UIManager uiManager;
    [SerializeField] private TouchscreenHandler touchscreenHandler;

    private bool isInitializing;

    public bool IsInitialized { get; private set; }

    private void Start()
    {
        BeginInitialization();
    }

    private void BeginInitialization()
    {
        if (isInitializing)
        {
            return;
        }

        isInitializing = true;
        IsInitialized = false;
        PrepareBlankState();
        _ = InitializeGuardedAsync();
    }

    private async Task InitializeGuardedAsync()
    {
        try
        {
            SaveData data = await (loadSaveOverride?.Invoke() ?? SaveManager.Instance.GetCurrentSaveData());

            levelManager.Initialize(data);
            uiManager.SetLevel(levelManager.Level);
            uiManager.SetCurrentScore(levelManager.TotalScore, levelManager.LevelPercentage);

            await gameManager.InitializeBoardAsync(data);

            touchscreenHandler.enabled = true;
            uiManager.SetGameplayControlsEnabled(true);
            gameManager.CompleteInitialization();
            IsInitialized = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Gameplay initialization failed: " + ex.Message);
            PrepareBlankState();
            ShowFailureModal();
        }
        finally
        {
            isInitializing = false;
        }
    }

    private void PrepareBlankState()
    {
        touchscreenHandler.enabled = false;
        uiManager.SetGameplayControlsEnabled(false);
        uiManager.ClearGameplayState();
        levelManager.PrepareForInitialization();
        gameManager.PrepareForInitialization();
    }

    private void ShowFailureModal()
    {
        Modal.Instance.OpenModal(
            () => StartCoroutine(WaitForModalThenReturnToTitle()),
            () => StartCoroutine(WaitForModalThenRetry()),
            "We couldn't load your game. Try again or return to the main menu.",
            "Main Menu",
            "Retry");
    }

    private IEnumerator WaitForModalThenRetry()
    {
        yield return WaitForModalToClose();
        BeginInitialization();
    }

    private IEnumerator WaitForModalThenReturnToTitle()
    {
        yield return WaitForModalToClose();
        SceneManager.LoadScene("Title");
    }

    private static IEnumerator WaitForModalToClose()
    {
        while (Modal.Instance != null && Modal.Instance.IsOpen)
        {
            yield return null;
        }
    }
}
