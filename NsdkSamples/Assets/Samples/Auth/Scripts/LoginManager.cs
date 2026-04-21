using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Class that manages login and logout to/from the sample user session.
///
/// When login requested, opens a URL in the browser which points to the login web portal.
/// On success, a "deep link" is returned with a session token (query param name remains <c>refreshToken</c> for portal compatibility).
///
/// If reusing this code outside the sample, it is recommended to change the redirectType in the URL to "nsdk-external"
/// </summary>
public static class LoginManager
{
    public static bool IsLoginInProgress { get; private set; }
    public static bool IsLoggedIn => NSSampleSessionManager.IsSessionInProgress;
    public static event System.Action LoginComplete;
    public static event System.Action LogoutComplete;

    public static void LoginRequested()
    {
        Application.deepLinkActivated += OnDeepLinkActivated;
        // On success, the "nsdk-unity-samples" redirectType returns a deep link with "nsdk-unity-samples://..." scheme.
        // NOTE: This can be replaced with "nsdk-external" if reusing this code (so as not to conflict).
        // (AndroidManifest.xml and iOSURLSchemes in ProjectSettings.asset will need to be updated).
        Application.OpenURL($"{AuthEndpoints.Settings.SignInEndpoint}?redirectType=nsdk-unity-samples");
        IsLoginInProgress = true;
    }

    public static void CancelLoginRequested()
    {
        Application.deepLinkActivated -= OnDeepLinkActivated;
        IsLoginInProgress = false;
    }

    public static void LogoutRequested()
    {
        if (!IsLoggedIn)
        {
            Application.OpenURL(
                $"{AuthEndpoints.Settings.SignOutEndpoint}?refreshToken={NSSampleSessionManager.SampleToken}");
        }

        NSSampleSessionManager.StopNSSampleSession();
        LogoutComplete?.Invoke();
    }
    
    public static void MockDeepLink(string url) => OnDeepLinkActivated(url);
    
    private static void OnDeepLinkActivated(string url)
    {
        Application.deepLinkActivated -= OnDeepLinkActivated;
        IsLoginInProgress = false;
        
        // Session token from the portal (query name is still refreshToken for backward compatibility).
        var userSessionToken = GetParamValue("refreshToken", url);

        NSSampleSessionManager.SetNSSampleSession(userSessionToken);
        LoginComplete?.Invoke();
    }
    
    private static string GetParamValue(string parameterName, string deepLink)
    {
        // The pattern looks for the parameter name, an equals sign,
        // and then captures everything until an ampersand or the end of the string.
        var match = Regex.Match(deepLink, $@"{Regex.Escape(parameterName)}\s*=\s*([^&]*)", RegexOptions.IgnoreCase);

        // If the match was successful, return the captured group
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }
}
