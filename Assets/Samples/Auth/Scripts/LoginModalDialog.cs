using NianticSpatial.NSDK.AR.Loader;
using NianticSpatial.NSDK.AR.Utilities.Auth;
using UnityEngine;
using UnityEngine.UI;

public class LoginModalDialog : MonoBehaviour
{
    [SerializeField]
    private Button _loginButton;

    [SerializeField]
    private Button _dismissButton;

    private bool _wasDismissed;

    void Start()
    {
        if (!ShowDialog())
        {
            Hide();
            return;
        }

#if UNITY_EDITOR
        Debug.LogWarning("Login disabled when running in the editor");
        Hide();
        return;
#endif

        _loginButton.onClick.AddListener(OnLogin);
        _dismissButton.onClick.AddListener(OnDismiss);
    }

    private bool ShowDialog()
    {
        if (!string.IsNullOrEmpty(NsdkSettingsHelper.ActiveSettings.ApiKey))
        {
            return false;
        }

        if (NsdkSettings.Instance.UseDeveloperAuthentication &&
            !AuthPublicUtils.IsEmptyOrExpiring(NsdkSettingsHelper.ActiveSettings.RefreshToken, 0))
        {
            return false;
        }

        return !_wasDismissed && !LoginManager.IsLoggedIn && !LoginManager.IsLoginInProgress;
    }

    private void OnLogin()
    {
        LoginManager.LoginRequested();
        Hide();
    }

    private void OnDismiss()
    {
        _wasDismissed = true;
        Hide();
    }

    private void Hide()
    {
        gameObject.SetActive(false);
    }
}
