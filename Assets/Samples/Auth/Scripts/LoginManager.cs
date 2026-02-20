using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Class that manages login and logout to/from the sample user session.
/// </summary>
public static class LoginManager
{
    public static bool IsLoginInProgress { get; private set; }
    public static bool IsLoggedIn => UserSessionManager.IsSessionInProgress;
    public static event System.Action LoginComplete;
    public static event System.Action LogoutComplete;

    public static void LoginRequested()
    {
        Application.deepLinkActivated += OnDeepLinkActivated;
        Application.OpenURL($"{AuthEndpoints.Settings.SignInEndpoint}?redirectType=unity-app");
        IsLoginInProgress = true;
    }

    public static void LogoutRequested()
    {
        if (!IsLoggedIn)
        {
            Application.OpenURL(
                $"{AuthEndpoints.Settings.SignOutEndpoint}?refreshToken={UserSessionManager.RefreshToken}");
        }

        UserSessionManager.StopUserSession();
        LogoutComplete?.Invoke();
    }
    
    public static void MockDeepLink(string url) => OnDeepLinkActivated(url);
    
    private static void OnDeepLinkActivated(string url)
    {
        Application.deepLinkActivated -= OnDeepLinkActivated;
        IsLoginInProgress = false;
        
        // Extract the access token and refresh token from the URL.
        // Params are formatted as "paramName=paramValue"
        
        var userSessionAccessToken = GetParamValue("accessToken", url);
        var userSessionRefreshToken = GetParamValue("refreshToken", url);
        
        UserSessionManager.SetUserSession(userSessionRefreshToken, userSessionAccessToken);
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
