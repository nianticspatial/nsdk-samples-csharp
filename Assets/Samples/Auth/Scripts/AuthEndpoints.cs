using UnityEngine;

[CreateAssetMenu(fileName = "AuthEndpoints", menuName = "Scriptable Objects/AuthEndpoints")]
public class AuthEndpoints : ScriptableObject
{
    [SerializeField]
    private string signInEndpoint = "https://sample-app-frontend-internal.nianticspatial.com/signin";
    [SerializeField]
    private string signOutEndpoint = "https://sample-app-frontend-internal.nianticspatial.com/signout";
    [SerializeField]
    private string identityEndpoint = "https://spatial-identity.nianticspatial.com/oauth/token";
    [SerializeField]
    private string accessEndpoint = "https://sample-app-backend-internal.nianticspatial.com/api/access-token";
    
    public string SignInEndpoint => signInEndpoint;
    public string SignOutEndpoint => signOutEndpoint;
    public string IdentityEndpoint => identityEndpoint;
    public string AccessEndpoint => accessEndpoint;

    public static AuthEndpoints Settings { get; private set; }

    public void SetAsSettings()
    {
        Settings = this;
    }
}
