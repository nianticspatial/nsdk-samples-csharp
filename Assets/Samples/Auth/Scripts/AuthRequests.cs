// Copyright 2022-2026 Niantic Spatial.

using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

// MARK: - Request functions

/// <summary>
/// Consolidated auth request helpers for the Unity external samples.
/// Mirrors the Swift AuthRequests.swift pattern — three top-level static methods
/// with shared HTTP boilerplate.
/// </summary>
public static class AuthRequests
{
    /// <summary>
    /// Refreshes the sample session token. Returns the rotated token from Set-Cookie,
    /// or null if the server did not rotate it.
    /// </summary>
    public static async Task<string> RequestSampleSessionAccessAsync(string sessionToken)
    {
        using var webRequest = MakeIdentityRequest();
        webRequest.SetRequestHeader("Cookie", $"refresh_token={sessionToken}");
        SetJsonBody(webRequest, new GrantTypeRequest { grantType = "refresh_user_session_access_token" });

        await SendAsync(webRequest);

        if (!CheckSuccess(webRequest, "sample session access"))
        {
            return null;
        }

        var header = webRequest.GetResponseHeader("set-cookie");
        return ExtractHeaderValue(header, "refresh_token");
    }

    /// <summary>
    /// Exchanges a sample session token for an NSDK refresh token.
    /// </summary>
    public static async Task<string> RequestNsdkRefreshTokenAsync(string sessionToken)
    {
        using var webRequest = MakeIdentityRequest();
        webRequest.SetRequestHeader("Cookie", $"refresh_token={sessionToken}");
        SetJsonBody(webRequest, new GrantTypeRequest { grantType = "exchange_build_refresh_token" });

        await SendAsync(webRequest);

        if (!CheckSuccess(webRequest, "NSDK refresh token"))
        {
            return null;
        }

        var response = JsonUtility.FromJson<RefreshTokenResponse>(webRequest.downloadHandler.text);
        if (response == null || string.IsNullOrEmpty(response.buildRefreshToken))
        {
            Debug.LogError("AuthRequests: missing buildRefreshToken in response.");
            return null;
        }

        return response.buildRefreshToken;
    }

    /// <summary>
    /// Exchanges an NSDK refresh token for an NSDK access token.
    /// </summary>
    public static async Task<string> RequestNsdkAccessTokenAsync(string nsdkRefreshToken)
    {
        using var webRequest = MakeIdentityRequest();
        SetJsonBody(webRequest, new AccessTokenRequest
        {
            grantType = "refresh_build_access_token",
            buildRefreshToken = nsdkRefreshToken
        });

        await SendAsync(webRequest);

        if (!CheckSuccess(webRequest, "NSDK access token"))
        {
            return null;
        }

        var response = JsonUtility.FromJson<AccessTokenResponse>(webRequest.downloadHandler.text);
        if (response == null || string.IsNullOrEmpty(response.buildAccessToken))
        {
            Debug.LogError("AuthRequests: missing buildAccessToken in response.");
            return null;
        }

        return response.buildAccessToken;
    }

    // MARK: - Helpers

    private static UnityWebRequest MakeIdentityRequest()
    {
        var url = AuthEndpoints.Settings.IdentityEndpoint;
        var webRequest = new UnityWebRequest(url, "POST");
        webRequest.downloadHandler = new DownloadHandlerBuffer();
        webRequest.SetRequestHeader("Content-Type", "application/json");
        return webRequest;
    }

    private static void SetJsonBody(UnityWebRequest webRequest, object body)
    {
        var json = JsonUtility.ToJson(body);
        webRequest.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        webRequest.uploadHandler.contentType = "application/json";
    }

    private static async Task SendAsync(UnityWebRequest webRequest)
    {
        var operation = webRequest.SendWebRequest();
        while (!operation.isDone)
        {
            await Task.Yield();
        }
    }

    private static bool CheckSuccess(UnityWebRequest webRequest, string context)
    {
        if (webRequest.result != UnityWebRequest.Result.Success)
        {
            var body = webRequest.downloadHandler?.text ?? string.Empty;
            var errorResponse = string.IsNullOrEmpty(body) ? null : JsonUtility.FromJson<ErrorResponse>(body);
            Debug.LogError($"AuthRequests: error requesting {context}: {webRequest.error}: {errorResponse?.error ?? body}");
            return false;
        }

        return true;
    }

    private static string ExtractHeaderValue(string header, string parameterName)
    {
        if (header == null)
        {
            return null;
        }

        var match = Regex.Match(header, $@"{Regex.Escape(parameterName)}\s*=\s*([^;]*)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    // MARK: - Request/Response types

    [Serializable]
    private class GrantTypeRequest
    {
        public string grantType;
    }

    [Serializable]
    private class AccessTokenRequest
    {
        public string grantType;
        public string buildRefreshToken;
    }

    [Serializable]
    private class RefreshTokenResponse
    {
        public string buildRefreshToken;
    }

    [Serializable]
    private class AccessTokenResponse
    {
        public string buildAccessToken;
    }

    [Serializable]
    private class ErrorResponse
    {
        public string error;
    }
}
