using NianticSpatial.NSDK.AR.Loader;
using NianticSpatial.NSDK.AR.Utilities.Auth;
using UnityEngine;
using UnityEngine.UI;

public class EnterpriseAuthManager : MonoBehaviour
{
    [Header("UI Setup")]
    [SerializeField]
    [Tooltip("Button to login or logout from the enterprise account")]
    private Button _requestButton;

    [SerializeField]
    private Text _requestButtonText;

    [SerializeField]
    private Text _statusText;

    [SerializeField]
    private Toggle _sampleBackendToggle;

    private void Awake()
    {
        Refresh();
        _requestButton.onClick.AddListener(OnRequest);
        LoginManager.LoginComplete += OnLoginComplete;
        _sampleBackendToggle.onValueChanged.AddListener(OnSampleBackendToggleChanged);
    }

    private void OnDestroy()
    {
        LoginManager.LoginComplete -= OnLoginComplete;
    }

    private void OnLoginComplete()
    {
        Refresh();
    }

    private void Refresh()
    {
        if (!string.IsNullOrEmpty(NsdkSettingsHelper.ActiveSettings.ApiKey))
        {
            Show("API Key Set", "Login", false);
            _sampleBackendToggle.interactable = false;
        }
        else if (NsdkSettings.Instance.UseDeveloperAuthentication &&
            !AuthPublicUtils.IsEmptyOrExpiring(NsdkSettingsHelper.ActiveSettings.RefreshToken, 0))
        {
            Show("Developer Authentication Active", "Login", false);
            _sampleBackendToggle.interactable = false;
        }
        else if (LoginManager.IsLoginInProgress)
        {
            Show("Login in progress", "Cancel", true);
        }
        else if (LoginManager.IsLoggedIn)
        {
            Show("Logged In", "Logout", true);
        }
        else
        {
            Show("Not Logged In", "Login", true);
        }

        _sampleBackendToggle.isOn = NianticSpatialAccessManager.UseSampleBackend;
    }

    private void Show(string infoText, string buttonText, bool interactable)
    {
        _requestButton.interactable = interactable;
        _requestButtonText.text = buttonText;
        _statusText.text = infoText;
    }

    private void OnRequest()
    {
        if (LoginManager.IsLoggedIn)
        {
            LoginManager.LogoutRequested();
        }
        else
        {
            LoginManager.LoginRequested();
        }

        Refresh();
    }

    private void OnSampleBackendToggleChanged(bool isOn)
    {
        NianticSpatialAccessManager.UseSampleBackend = isOn;
    }
}
