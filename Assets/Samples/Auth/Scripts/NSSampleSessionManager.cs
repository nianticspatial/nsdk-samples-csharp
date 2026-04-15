// Copyright 2022-2026 Niantic Spatial.

using System;
using System.Threading;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR.Auth;
using NianticSpatial.NSDK.AR.Settings;
using NianticSpatial.NSDK.AR.Utilities.Auth;
using UnityEngine;

/// <summary>
/// Maintains a user session with the sample backend and forwards NSDK access tokens
/// to the native SDK via <see cref="Metadata.SetAccessToken"/>.
///
/// The session requires a session token, which is stored in PlayerPrefs so that it persists
/// across app launches. The sample session token and NSDK access token are refreshed
/// periodically when close to expiry.
/// </summary>
public static class NSSampleSessionManager
{
    private const int UpdateInterval = 10; // seconds
    private const int MinUnexpiredTimeLeft = 60; // seconds
    private static CancellationTokenSource s_updateCts;

    private static string s_nsSampleSessionToken;

    public static string SampleToken => s_nsSampleSessionToken;

    public static bool IsSessionInProgress => !AuthPublicUtils.IsEmptyOrExpiring(s_nsSampleSessionToken, 0);

    public static void Start()
    {
        if (!string.IsNullOrEmpty(s_nsSampleSessionToken))
        {
            return;
        }
        LoadSessionData();

        if (AuthPublicUtils.IsEmptyOrExpiring(s_nsSampleSessionToken, 0))
        {
            s_nsSampleSessionToken = null;
            return;
        }

        UpdateSession();
    }

    /// <summary>
    /// Eagerly exchanges a restored sample session for NSDK access.
    /// Chains: sample session token → NSDK refresh token → NSDK access token,
    /// then calls <see cref="Metadata.SetAccessToken"/> directly.
    /// </summary>
    public static void SetupSessionAccess()
    {
        if (IsSessionInProgress)
        {
            _ = RequestAndApplyNsdkAccessAsync(s_nsSampleSessionToken);
        }
    }

    /// <summary>
    /// Exchange sample session token → NSDK refresh token → NSDK access token,
    /// then push the access token to native via <see cref="Metadata.SetAccessToken"/>.
    /// This performs the token exchange at the platform level, matching the Swift pattern.
    /// Returns true if the access token was successfully obtained and applied (or deferred).
    /// </summary>
    private static async Task<bool> RequestAndApplyNsdkAccessAsync(string nsSampleSessionToken)
    {
        var nsdkRefreshToken = await AuthRequests.RequestNsdkRefreshTokenAsync(nsSampleSessionToken);
        if (string.IsNullOrEmpty(nsdkRefreshToken))
        {
            Debug.LogError("NSSampleSessionManager: failed to request NSDK refresh token.");
            return false;
        }

        var nsdkAccessToken = await AuthRequests.RequestNsdkAccessTokenAsync(nsdkRefreshToken);
        if (string.IsNullOrEmpty(nsdkAccessToken))
        {
            Debug.LogError("NSSampleSessionManager: failed to request NSDK access token.");
            return false;
        }

        try
        {
            Metadata.SetAccessToken(nsdkAccessToken);
        }
        catch (InvalidOperationException)
        {
            // NSDK context not yet initialized — the refresh loop will retry once the context is available.
            Debug.LogWarning("NSSampleSessionManager: NSDK context not yet initialized, access token will be applied later.");
        }

        return true;
    }

    public static void SetNSSampleSession(string sessionToken)
    {
        _ = SetNSSampleSessionAsync(sessionToken);
    }

    private static async Task SetNSSampleSessionAsync(string sessionToken)
    {
        // Exchange for NSDK access token first; only persist and start the refresh loop on success.
        // If the exchange fails we discard the session token — the user can log in again.
        if (!await RequestAndApplyNsdkAccessAsync(sessionToken))
        {
            return;
        }

        s_nsSampleSessionToken = sessionToken;
        SaveSessionData();
        UpdateSession();
    }

    public static void StopNSSampleSession()
    {
        s_updateCts?.Cancel();
        s_updateCts = null;
        s_nsSampleSessionToken = null;
        SaveSessionData();
        AuthClient.StaticLogout();
    }

    private static void SaveSessionData()
    {
        PlayerPrefs.SetString("NSSampleSessionToken", s_nsSampleSessionToken);
    }

    private static void LoadSessionData()
    {
        s_nsSampleSessionToken = PlayerPrefs.GetString("NSSampleSessionToken");
    }

    private static void UpdateSession()
    {
        s_updateCts?.Cancel();
        s_updateCts = new();
        var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(s_updateCts.Token, Application.exitCancellationToken);

        _ = StartSessionAsync(linkedCts.Token);
    }

    private static async Task StartSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Refresh sample session token if close to expiry
                if (AuthPublicUtils.IsEmptyOrExpiring(s_nsSampleSessionToken, MinUnexpiredTimeLeft))
                {
                    var newToken = await AuthRequests.RequestSampleSessionAccessAsync(s_nsSampleSessionToken);
                    s_nsSampleSessionToken = newToken;
                    SaveSessionData();

                    if (string.IsNullOrEmpty(s_nsSampleSessionToken))
                    {
                        break;
                    }
                }

                // Re-fetch NSDK access token when it is missing or close to expiry
                if (!string.IsNullOrEmpty(s_nsSampleSessionToken))
                {
                    bool needsRefresh;
                    try
                    {
                        needsRefresh = !AuthClient.IsAuthorized();
                    }
                    catch (InvalidOperationException)
                    {
                        // NSDK context not yet initialized — treat as needing refresh
                        needsRefresh = true;
                    }

                    if (needsRefresh)
                    {
                        await RequestAndApplyNsdkAccessAsync(s_nsSampleSessionToken);
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
            Debug.Log("NS Sample Session Refresh loop stopped.");
        }
    }
}
