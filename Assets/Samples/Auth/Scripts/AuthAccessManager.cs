using System;
using System.Threading;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR.Loader;
using NianticSpatial.NSDK.AR.Utilities.Auth;
using UnityEngine;

/// <summary>
/// class that maintains auth access to the NS API, refreshing it periodically.
/// Access is requested using the sample user session access token.
/// </summary>
public static class AuthAccessManager
{
    private const int UpdateInterval = 10; // seconds
    private const int MinUnexpiredTimeLeft = 60; // seconds
    private static CancellationTokenSource s_updateCts;

    private static string s_userSessionAccessToken;

    public static void StartAuthAccess(string userSessionAccessToken)
    {
        // Don't interrupt the current auth access loop if we already have a good access token
        if (!AuthPublicUtils.IsEmptyOrExpiring(s_userSessionAccessToken, 0))
        {
            return;
        }
        // Don't start the auth access loop if we don't have an access token
        if (string.IsNullOrEmpty(userSessionAccessToken))
        {
            return;
        }

        s_updateCts?.Cancel();
        s_updateCts = new();
        s_userSessionAccessToken = userSessionAccessToken;

        if (!string.IsNullOrEmpty(userSessionAccessToken))
        {
            var linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(s_updateCts.Token, Application.exitCancellationToken);

            _ = StartAuthAccessAsync(linkedCts.Token);
        }
    }

    public static void StopAuthAccess()
    {
        s_updateCts?.Cancel();
        s_updateCts = null;
    }

    public static string UserSessionAccessToken
    {
        set => s_userSessionAccessToken = value;
    }

    private static bool AccessIsExpiredOrAboutToExpire()
    {
        if (string.IsNullOrEmpty(NsdkSettingsHelper.ActiveSettings.AccessToken))
        {
            return true;
        }

        var expiresAt = NsdkSettingsHelper.ActiveSettings.AccessExpiresAt;
        if (expiresAt <= 0)
        {
            return true;
        }

        var currentTimeSeconds = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timeLeft = expiresAt - currentTimeSeconds;
        return timeLeft <= MinUnexpiredTimeLeft;
    }

    private static async Task StartAuthAccessAsync(CancellationToken cancellationToken)
    {
        // Loop that runs forever during runtime, periodically refreshing the access token
        // (if we have a valid refresh token)
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (AccessIsExpiredOrAboutToExpire())
                {
                    var accessToken = await RequestAuthAccess.ExecuteAsync(s_userSessionAccessToken);
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        Debug.LogError("Failed to request NS access token.");
                        break;
                    }
                    NsdkSettingsHelper.ActiveSettings.AccessToken = accessToken;
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
            Debug.Log("Auth Access Refresh loop stopped.");
        }
    }
}
