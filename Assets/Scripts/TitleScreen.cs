using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class TitleScreen : MonoBehaviour
{
    private const int MinUsernameLength = 3;
    private const int MaxUsernameLength = 20;
    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 30;

    [Header("Register section")]
    [SerializeField] private TMP_InputField registerUsername;
    [SerializeField] private TMP_InputField registerPassword;
    [SerializeField] private TMP_InputField registerConfirm;
    [SerializeField] private TMP_Text registerPasswordError;

    [Header("Sign in section")]
    [SerializeField] private TMP_InputField signInUsername;
    [SerializeField] private TMP_InputField signInPassword;

    private void Start()
    {
        signInPassword.asteriskChar = '•';
        registerPassword.asteriskChar = '•';
        registerConfirm.asteriskChar = '•';
    }

    public void ChangeScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
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
}
