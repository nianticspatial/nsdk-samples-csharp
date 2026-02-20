using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Class to request access to the NS API, using the sample user session access token.
/// </summary>
public static class RequestAuthAccess
{
    [Serializable]
    public class Response
    {
        public string error;
        public string accessToken;
        public int expiresIn;
    }

    public static async Task<string> ExecuteAsync(string userSessionAccessToken)
    {
        using UnityWebRequest webRequest = UnityWebRequest.Get(AuthEndpoints.Settings.AccessEndpoint);
        webRequest.SetRequestHeader("Authorization", $"Bearer {userSessionAccessToken}");

        var operation = webRequest.SendWebRequest();
        while (!operation.isDone) 
        {
            await Task.Yield(); // Manual wait
        }

        var response = JsonUtility.FromJson<Response>(webRequest.downloadHandler.text);
        
        if (webRequest.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Error in request for NS auth access token: {webRequest.error} : {response?.error}");
            return null;
        }
        
        if (response == null)
        {
            Debug.LogError("Failed to parse access token response");
            return null;
        }

        if (!string.IsNullOrEmpty(response.error))
        {
            Debug.LogError($"Error in response for NS auth access token: {response.error}");
            return null;
        }

        Debug.Log($"Access token received: {response.accessToken}");
        return response.accessToken;
    }
}
