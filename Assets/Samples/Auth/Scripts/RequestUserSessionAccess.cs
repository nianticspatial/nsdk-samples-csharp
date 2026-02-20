using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Class to request access for the sample user session.
/// </summary>
public static class RequestUserSessionAccess
{
    public class UserSessionResult
    {
        public string RefreshToken;
        public string AccessToken;
    }
    
    [Serializable]
    private class Request
    {
        public string grantType;
    }
    
    [Serializable]
    private class Response
    {
        public string token;
        public int expiresAt;
    }

    [Serializable]
    private class ErrorResponse
    {
        public string error;
    }

    public static async Task<UserSessionResult> ExecuteAsync(string userSessionRefreshToken)
    {
        var request = new Request { grantType = "refresh_user_session_access_token" };
        var requestJson = JsonUtility.ToJson(request);

        var url = AuthEndpoints.Settings.IdentityEndpoint;
        using var webRequest = UnityWebRequest.Post(url, requestJson, "application/json");
        webRequest.SetRequestHeader("Cookie", $"refresh_token={userSessionRefreshToken}");
        
        var operation = webRequest.SendWebRequest();
        while (!operation.isDone) 
        {
            await Task.Yield(); // Manual wait
        }

        if (webRequest.result != UnityWebRequest.Result.Success)
        {
            var errorResponse = JsonUtility.FromJson<ErrorResponse>(webRequest.downloadHandler.text);
            Debug.LogError($"Error requesting user-session access token: {webRequest.error}: {errorResponse?.error}");
            return null;
        }
        
        var response = JsonUtility.FromJson<Response>(webRequest.downloadHandler.text);
        if (response == null)
        {
            Debug.LogError("Failed to parse access token response");
            return null;
        }
        
        var header = webRequest.GetResponseHeader("set-cookie");
        var newRefreshToken = GetHeaderValue(header, "refresh_token");
        
        return new UserSessionResult { RefreshToken = newRefreshToken, AccessToken = response.token };
    }

    // Extract the value of a given parameter from a header string.
    // Header strings are of the form "parameterName=parameterValue; parameterName2=parameterValue2"
    private static string GetHeaderValue(string header, string parameterName)
    {
        // If no header supplied, just return the empty string
        if (header == null)
        {
            return string.Empty;
        }

        // The pattern looks for the parameter name, an equals sign,
        // and then captures everything until a semicolon or the end of the string.
        var match = Regex.Match(header, $@"{Regex.Escape(parameterName)}\s*=\s*([^;]*)", RegexOptions.IgnoreCase);

        // If the match was successful, return the captured group
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }
}
