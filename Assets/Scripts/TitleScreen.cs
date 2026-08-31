using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Threading.Tasks;
using Unity.Services.Core;
using UnityEngine.UI;

public class TitleScreen : MonoBehaviour, IAuthStateListener
{
    [Header("Register section")]
    [SerializeField] private GameObject registerPanel;
    [SerializeField] private TMP_InputField registerUsername;
    [SerializeField] private TMP_InputField registerPassword;
    [SerializeField] private TMP_InputField registerConfirm;
    [SerializeField] private TMP_Text registerPasswordError;
    [SerializeField] private Animator registerPasswordErrorAnimator;
    [SerializeField] private Button registerButton;

    [Header("Sign in section")]
    [SerializeField] private GameObject signInPanel;
    [SerializeField] private TMP_InputField signInUsername;
    [SerializeField] private TMP_InputField signInPassword;
    [SerializeField] private Button signInButton;

    [Header("Profile section")]
    [SerializeField] private GameObject profilePanel;
    [SerializeField] private TMP_Text profileUsername;
    [SerializeField] private TMP_Text profileDate;
    [SerializeField] private TMP_Text profileId;
    [SerializeField] private Button profileDeleteSaveButton;

    [Header("Play button")]
    [SerializeField] private TMP_Text playButtonText;

    [Header("Tutorial panel")]
    [SerializeField] private Animator tutorialPanelAnimator;
    [SerializeField] private GameObject[] tutorialPanels;
    [SerializeField] private Button tutorialBackButton;
    [SerializeField] private Button tutorialForwardButton;
    [SerializeField] private TMP_Text tutorialForwardButtonText;

    [Header("About section")]
    [SerializeField] private TMP_Text licensesText;
    [SerializeField] private TextAsset[] licenses;
    [SerializeField] private RectTransform licensesContentRect;

    private int tutorialIndex = 0;

    private static readonly int CloseHash = Animator.StringToHash("Close");
    private static readonly int ShakeHash = Animator.StringToHash("Shake");
    private const int MinUsernameLength = 3;
    private const int MaxUsernameLength = 20;
    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 30;
    private const string NewGameLabel = "New Game";
    private const string ContinueGameLabel = "Continue Game";
    private const string BallingburgerUrl = "https://www.youtube.com/@ballingburger";

    private void Start()
    {
        signInPassword.asteriskChar = '•';
        registerPassword.asteriskChar = '•';
        registerConfirm.asteriskChar = '•';
        registerPasswordError.text = "";
        playButtonText.text = NewGameLabel;

        SaveManager.Instance.RegisterAuthListener(this);
        Debug.Log("Registered auth state listener");

        // Auth may complete before register is complete, so try applying state anyway
        ApplySaveManagerState();
        _ = RefreshPlayButtonLabelAsync();

        // Load third party licenses text
        System.Text.StringBuilder sb = new();
        foreach (TextAsset asset in licenses)
        {
            sb.Append(asset.text);
            sb.Append("\n\n----------\n\n");
        }
        licensesText.text = sb.ToString();
        // force TMP text to recalculate its size
        licensesText.ComputeMarginSize();
        licensesText.ForceMeshUpdate();
        LayoutRebuilder.ForceRebuildLayoutImmediate(licensesContentRect);
    }

    private void OnDestroy()
    {
        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.UnregisterAuthListener(this);
        }
    }

    private void ApplySaveManagerState()
    {
        SaveManager saveManager = SaveManager.Instance;

        profilePanel.SetActive(false);
        signInPanel.SetActive(false);
        registerPanel.SetActive(false);

        if (saveManager.IsCloudActive && saveManager.PlayerInfo.Username != null)
        {
            profilePanel.SetActive(true);
            Unity.Services.Authentication.PlayerInfo info = saveManager.PlayerInfo;
            profileUsername.text = $"Signed in as: {info.Username}";
            profileDate.text = info.CreatedAt == null ? "" : $"Playing Egghead since {info.CreatedAt?.ToShortDateString()}";
            profileId.text = $"Player ID: {info.Id}";
        }
        else if (saveManager.IsLocalOnly)
        {
            signInPanel.SetActive(true);
        }
        else
        {
            registerPanel.SetActive(true);
        }
    }

    #region UI events
    public void OnRegisterFieldChanged()
    {
        string errorMessage = ValidateRegistrationCredentials(registerUsername.text, registerPassword.text, registerConfirm.text);
        if (errorMessage == null)
        {
            registerButton.interactable = true;
            registerPasswordError.text = "";
        }
        else
        {
            registerButton.interactable = false;
            registerPasswordError.text = errorMessage;
        }
    }

    public void OnRegisterButtonClicked()
    {
        if (ValidateRegistrationCredentials(registerUsername.text, registerPassword.text, registerConfirm.text) != null)
        {
            // This should not happen, but if it does, show a nice shake animation
            registerPasswordErrorAnimator.SetTrigger(ShakeHash);
            return;
        }
        // Intentional non-await async call: this wrapper method is called by the UI button click
        _ = OnRegisterClicked();
    }
    private async Task OnRegisterClicked()
    {
        string validationResult = ValidateRegistrationCredentials(registerUsername.text, registerPassword.text, registerConfirm.text);
        if (validationResult == null)
        {
            await SaveManager.Instance.RegisterWithUsernamePasswordAsync(registerUsername.text, registerPassword.text);
            ApplySaveManagerState();
        }
        else
        {
            registerPasswordError.text = validationResult;
        }
    }
    public void OnLogInClicked()
    {
        string username = signInUsername.text.Trim();
        string password = signInPassword.text;
        if (username.Length == 0 || password.Length == 0)
        {
            return;
        }
        _ = SaveManager.Instance.LoginWithUsernamePasswordAsync(username, password);
    }
    public void OnLogInFieldChanged()
    {
        signInButton.interactable = signInUsername.text.Trim().Length != 0 && signInPassword.text.Length != 0;
    }
    public void OnSignOutClicked()
    {
        Modal.Instance.OpenModal(null, () =>
        {
            SaveManager.Instance.SignOutToLocalOnly();
        }, "Are you sure you want to sign out?");
    }
    public void OnDeleteSaveDataClicked()
    {
        Modal.Instance.OpenModal(null, () =>
        {
            _ = DeleteSaveDataAndRefreshAsync();
        }, "Are you sure you want to delete your saved game?");
    }
    public void OnDeleteAccountClicked()
    {
        Modal.Instance.OpenModal(null, () =>
        {
            _ = DeleteAccountAndRefreshAsync();
        }, "DANGER: Delete your account and saved game? This action is irreversible!", "Cancel", "Yes, I'm sure");
    }
    public void OnMusicButtonClicked()
    {
        Application.OpenURL(BallingburgerUrl);
    }
    #endregion

    public void ChangeScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }

    /// <summary>
    /// Update the play button after its asynchronous local/cloud save lookup completes.
    /// </summary>
    private async Task RefreshPlayButtonLabelAsync()
    {
        try
        {
            bool hasSavedGame = await SaveManager.Instance.HasSavedGame();
            if (hasSavedGame)
            {
                playButtonText.text = ContinueGameLabel;
                profileDeleteSaveButton.interactable = true;
            }
            else
            {
                playButtonText.text = NewGameLabel;
                profileDeleteSaveButton.interactable = false;
            }

        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to check for saved game data: " + ex.Message);
        }
    }

    /// <summary>
    /// Delete saved data and restore the play button's new-game label.
    /// </summary>
    private async Task DeleteSaveDataAndRefreshAsync()
    {
        try
        {
            await SaveManager.Instance.DeleteData();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to delete saved game data: " + ex.Message);
        }
        finally
        {
            await RefreshPlayButtonLabelAsync();
        }
    }

    /// <summary>
    /// Delete the current account and refresh the title screen after the operation finishes.
    /// Confirmation, if desired, must happen before this UI entry point is called.
    /// </summary>
    private async Task DeleteAccountAndRefreshAsync()
    {
        try
        {
            await SaveManager.Instance.DeleteAccountAsync();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Failed to delete account: " + ex.Message);
        }
        finally
        {
            ApplySaveManagerState();
            await RefreshPlayButtonLabelAsync();
        }
    }

    /// <summary>
    /// Validate username/password registration fields against Unity Authentication requirements.
    /// </summary>
    /// <param name="username">Username entered by the player.</param>
    /// <param name="password">Password entered by the player.</param>
    /// <param name="confirmPassword">Repeated password confirmation entered by the player.</param>
    /// <returns>An error message when invalid; otherwise <c>null</c>.</returns>
    private string ValidateRegistrationCredentials(string username, string password, string confirmPassword)
    {
        username = username == null ? "" : username.Trim();
        password ??= "";
        confirmPassword ??= "";

        if (username.Length < MinUsernameLength || username.Length > MaxUsernameLength)
        {
            return $"Username must be {MinUsernameLength}-{MaxUsernameLength} characters long.";
        }

        foreach (char c in username)
        {
            bool isAllowedLetter = c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z';
            bool isAllowedDigit = c >= '0' && c <= '9';
            bool isAllowedSymbol = c == '.' || c == '-' || c == '@' || c == '_';

            if (!isAllowedLetter && !isAllowedDigit && !isAllowedSymbol)
            {
                return "Username can only contain letters, numbers, '.', '-', '@', or '_'.";
            }
        }

        if (password.Length < MinPasswordLength || password.Length > MaxPasswordLength)
        {
            return $"Password must be {MinPasswordLength}-{MaxPasswordLength} characters long.";
        }

        bool hasLowercase = false;
        bool hasUppercase = false;
        bool hasNumber = false;
        bool hasSymbol = false;

        foreach (char c in password)
        {
            hasLowercase |= c >= 'a' && c <= 'z';
            hasUppercase |= c >= 'A' && c <= 'Z';
            hasNumber |= c >= '0' && c <= '9';
            hasSymbol |= !char.IsLetterOrDigit(c);
        }

        if (!hasLowercase || !hasUppercase || !hasNumber || !hasSymbol)
        {
            return "Password must contain lowercase, uppercase, number, and symbol characters.";
        }

        if (password != confirmPassword)
        {
            return "Passwords do not match.";
        }

        return null;
    }

    #region IAuthStateListener events
    public void OnSignedIn()
    {
        ApplySaveManagerState();
        _ = RefreshPlayButtonLabelAsync();
    }

    public void OnSignInFailed(RequestFailedException err)
    {
        ApplySaveManagerState();
        _ = RefreshPlayButtonLabelAsync();
    }

    public void OnSignedOut()
    {
        ApplySaveManagerState();
        _ = RefreshPlayButtonLabelAsync();
    }

    public void OnExpired()
    {
        ApplySaveManagerState();
        _ = RefreshPlayButtonLabelAsync();
    }
    #endregion

    #region Tutorial panel methods
    public void ResetTutorial()
    {
        tutorialIndex = 0;

        for (int i = 0; i < tutorialPanels.Length; i++)
        {
            tutorialPanels[i].SetActive(i == tutorialIndex);
        }

        tutorialBackButton.interactable = false;
        tutorialForwardButton.interactable = tutorialPanels.Length > 0;
        tutorialForwardButtonText.text = tutorialPanels.Length == 1 ? "Done" : "Next";
    }
    public void ForwardTutorial()
    {
        if (tutorialIndex >= tutorialPanels.Length - 1)
        {
            // close the tutorial panel
            tutorialPanelAnimator.SetTrigger(CloseHash);
            return;
        }

        tutorialPanels[tutorialIndex].SetActive(false);
        tutorialIndex++;
        tutorialPanels[tutorialIndex].SetActive(true);

        tutorialBackButton.interactable = true;

        if (tutorialIndex == tutorialPanels.Length - 1)
        {
            tutorialForwardButtonText.text = "Done";
        }
    }
    public void BackTutorial()
    {
        if (tutorialIndex <= 0)
        {
            return;
        }

        tutorialPanels[tutorialIndex].SetActive(false);
        tutorialIndex--;
        tutorialPanels[tutorialIndex].SetActive(true);

        tutorialForwardButton.interactable = true;
        tutorialForwardButtonText.text = "Next";

        if (tutorialIndex == 0)
        {
            tutorialBackButton.interactable = false;
        }
    }
    #endregion
}
