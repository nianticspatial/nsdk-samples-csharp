using NianticSpatial.NSDK.AR.Loader;
using NianticSpatial.NSDK.AR.Utilities.Auth;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Configures auth endpoints, starts the sample user session, and manages
/// the auth icon button and login modal dialog.
///
/// The icon button in the top-right corner shows login state (gray = logged out,
/// green = logged in) and taps to open the modal. The modal shows current state
/// with a single Login or Logout action button.
/// </summary>
public class AuthManager : MonoBehaviour
{
    [Header("Endpoints & Session")]
    [SerializeField]
    private AuthEndpoints authEndpoints;

    [SerializeField]
    [Tooltip("Optional: URL used to mock a deep-link login when running in the editor")]
    private string mockDeepLinkUrl;

    [Header("Auth Icon Button")]
    [SerializeField]
    [Tooltip("The small icon button anchored to the top-right corner")]
    private Button _authIconButton;

    [SerializeField]
    [Tooltip("Image component on the icon button — tinted gray (logged out) or green (logged in)")]
    private Image _authIconImage;

    [Header("Login Modal")]
    [SerializeField]
    [Tooltip("Root GameObject of the login modal. Shown when icon is tapped.")]
    private GameObject _loginModal;

    [SerializeField]
    [Tooltip("Text label in the modal showing current auth state")]
    private Text _statusLabel;

    [SerializeField]
    [Tooltip("The single action button inside the modal (Login or Logout)")]
    private Button _actionButton;

    [SerializeField]
    private Text _actionButtonText;

    [SerializeField]
    private Image _actionButtonImage;

    [SerializeField]
    [Tooltip("Button or blocker that closes the modal without taking action")]
    private Button _dismissButton;

    private const int MinUnexpiredTimeLeft = 60; // seconds

    private static readonly Color LoggedOutColor = new Color(0.55f, 0.55f, 0.55f);
    private static readonly Color LoggedInColor = new Color(0.18f, 0.72f, 0.42f);
    private static readonly Color LoginButtonColor = new Color(0.25f, 0.5f, 0.9f);
    private static readonly Color LogoutButtonColor = new Color(0.85f, 0.25f, 0.25f);

    private void Awake()
    {
        authEndpoints?.SetAsSettings();
        
        // Test code for mocking a deep link when running in the editor
        // This will bypass login and invoke LoginComplete
#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(mockDeepLinkUrl))
        {
            LoginManager.MockDeepLink(mockDeepLinkUrl);
            return;
        }
#endif
        NSSampleSessionManager.Start();
        NSSampleSessionManager.SetupSessionAccess();
    }

    private void Start()
    {
        HideLoginModal();

        _actionButton.onClick.AddListener(OnActionButtonTapped);
        _dismissButton.onClick.AddListener(HideLoginModal);
        _authIconButton.onClick.AddListener(OnAuthIconTapped);

        LoginManager.LoginComplete += OnLoginComplete;
        LoginManager.LogoutComplete += OnLogoutComplete;

        RefreshAll();

#if UNITY_EDITOR
        Debug.LogWarning("Login flow disabled when running in the editor");
#else
        if (ShouldPromptLogin())
        {
            ShowLoginModal();
        }
#endif
    }

    private void OnDestroy()
    {
        LoginManager.LoginComplete -= OnLoginComplete;
        LoginManager.LogoutComplete -= OnLogoutComplete;
    }

    private bool ShouldPromptLogin()
    {
        if (HasAccessTokenOverride())
        {
            return false;
        }
        if (UsingDeveloperAuth() && !IsDeveloperRefreshTokenExpiring())
        {
            return false;
        }
        return !LoginManager.IsLoggedIn && !LoginManager.IsLoginInProgress;
    }

    private void OnLoginComplete()
    {
        RefreshAll();
        HideLoginModal();
    }

    private void OnLogoutComplete()
    {
        RefreshAll();
    }

    private void OnAuthIconTapped()
    {
        if (_loginModal.activeSelf)
        {
            HideLoginModal();
        }
        else
        {
            ShowLoginModal();
        }
    }

    private void OnActionButtonTapped()
    {
        if (LoginManager.IsLoggedIn)
        {
            LoginManager.LogoutRequested();
            HideLoginModal();
        }
        else if (LoginManager.IsLoginInProgress)
        {
            LoginManager.CancelLoginRequested();
        }
        else
        {
            LoginManager.LoginRequested();
            HideLoginModal();
        }
        RefreshAll();
    }

    private void ShowLoginModal()
    {
        RefreshLoginModal();
        _loginModal.SetActive(true);
    }

    private void HideLoginModal()
    {
        _loginModal.SetActive(false);
    }

    private void RefreshAll()
    {
        RefreshIcon();
        if (_loginModal.activeSelf)
        {
            RefreshLoginModal();
        }
    }

    private bool HasAccessTokenOverride()
    {
        return !string.IsNullOrEmpty(NsdkSettings.Instance.AccessTokenOverride);
    }

    private bool UsingDeveloperAuth()
    {
        return NsdkSettings.Instance.UseDeveloperAuthentication;
    }

    private bool IsDeveloperRefreshTokenExpiring()
    {
        return AuthPublicUtils.IsEmptyOrExpiring(NsdkSettings.Instance.RefreshToken, MinUnexpiredTimeLeft);
    }

    private void RefreshIcon()
    {
        var isLoggedIn = LoginManager.IsLoggedIn
            || HasAccessTokenOverride()
            || (UsingDeveloperAuth() && !IsDeveloperRefreshTokenExpiring());
        _authIconImage.color = isLoggedIn ? LoggedInColor : LoggedOutColor;
    }

    private void RefreshLoginModal()
    {
        if (HasAccessTokenOverride())
        {
            _statusLabel.text = "Logged In (Access Token)";
            _actionButtonText.text = "";
            _actionButton.gameObject.SetActive(false);
        }
        else if (UsingDeveloperAuth() && !IsDeveloperRefreshTokenExpiring())
        {
            _statusLabel.text = "Logged In (Developer Auth)";
            _actionButtonText.text = "";
            _actionButton.gameObject.SetActive(false);
        }
        else if (LoginManager.IsLoggedIn)
        {
            _actionButton.gameObject.SetActive(true);
            _statusLabel.text = "Logged In";
            _actionButtonText.text = "Logout";
            _actionButtonImage.color = LogoutButtonColor;
        }
        else if (LoginManager.IsLoginInProgress)
        {
            _actionButton.gameObject.SetActive(true);
            _statusLabel.text = "Login in progress...";
            _actionButtonText.text = "Cancel";
            _actionButtonImage.color = LoggedOutColor;
        }
        else
        {
            _actionButton.gameObject.SetActive(true);
            _statusLabel.text = "Not Logged In";
            _actionButtonText.text = "Login";
            _actionButtonImage.color = LoginButtonColor;
        }
    }
}
