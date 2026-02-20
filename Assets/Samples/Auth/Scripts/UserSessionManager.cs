using System;
using System.Threading;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR.Utilities.Auth;
using UnityEngine;

/// <summary>
/// Class that maintains a user session with the sample backend.
/// The session requires at least a refresh token, which is stored in PlayerPrefs so that it persists across app launches.
/// The session is refreshed periodically when the access token is about to expire.
/// </summary>
public static class UserSessionManager
{
    private const int UpdateInterval = 10; // seconds
    private const int MinUnexpiredTimeLeft = 60; // seconds
    private static CancellationTokenSource s_updateCts;

    private static string s_userSessionRefreshToken;
    private static string s_userSessionAccessToken;

    public static string RefreshToken => s_userSessionRefreshToken;
    public static string AccessToken => s_userSessionAccessToken;

    public static bool IsSessionInProgress => !AuthPublicUtils.IsEmptyOrExpiring(s_userSessionRefreshToken, 0);

    public static void Start()
    {
        // Check if we've already started the user session
        if (!string.IsNullOrEmpty(s_userSessionRefreshToken))
        {
            return;
        }
        LoadUserSessionData();

        // If we don't have a refresh token or it has expired, we can't start the user session
        if (AuthPublicUtils.IsEmptyOrExpiring(s_userSessionRefreshToken, 0))
        {
            s_userSessionRefreshToken = null;
            s_userSessionAccessToken = null;
            return; // Refresh token expired or could not be parsed (logged out)
        }

        UpdateUserSession();
    }

    public static void SetUserSession(string userSessionRefreshToken, string userSessionAccessToken)
    {
        s_userSessionRefreshToken = userSessionRefreshToken;
        s_userSessionAccessToken = userSessionAccessToken;
        SaveUserSessionData();
        UpdateUserSession();
    }

    public static void StopUserSession()
    {
        s_updateCts?.Cancel();
        s_updateCts = null;
        s_userSessionRefreshToken = null;
        s_userSessionAccessToken = null;
        SaveUserSessionData();
    }

    private static void SaveUserSessionData()
    {
        PlayerPrefs.SetString("UserSessionRefreshToken", s_userSessionRefreshToken);
        PlayerPrefs.SetString("UserSessionAccessToken", s_userSessionAccessToken);
    }

    private static void LoadUserSessionData()
    {
        s_userSessionRefreshToken = PlayerPrefs.GetString("UserSessionRefreshToken");
        s_userSessionAccessToken = PlayerPrefs.GetString("UserSessionAccessToken");
    }

    private static void UpdateUserSession()
    {
        s_updateCts?.Cancel();
        s_updateCts = new();
        var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(s_updateCts.Token, Application.exitCancellationToken);

        _ = StartUserSessionAsync(linkedCts.Token);
    }

    private static async Task StartUserSessionAsync(CancellationToken cancellationToken)
    {
        // Loop that runs forever during runtime, periodically refreshing the access token
        // (if we have a valid refresh token)
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Is the access token about to expire?
                if (AuthPublicUtils.IsEmptyOrExpiring(s_userSessionAccessToken, MinUnexpiredTimeLeft))
                {
                    // Use the refresh token to request a new pair
                    var result = await RequestUserSessionAccess.ExecuteAsync(s_userSessionRefreshToken);
                    s_userSessionRefreshToken = result?.RefreshToken;
                    s_userSessionAccessToken = result?.AccessToken;
                    SaveUserSessionData();

                    AuthAccessManager.UserSessionAccessToken = s_userSessionAccessToken;

                    // If refresh failed, then exit the user session
                    if (string.IsNullOrEmpty(s_userSessionRefreshToken))
                    {
                        break;
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(UpdateInterval), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Exiting the application, so we don't need to do anything here.
        }
        finally
        {
            Debug.Log("User Session Refresh loop stopped.");
        }
    }
}
