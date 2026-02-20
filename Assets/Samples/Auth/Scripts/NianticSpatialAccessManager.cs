using NianticSpatial.NSDK.AR.Auth;
using NianticSpatial.NSDK.AR.Loader;

/// <summary>
/// Class that manages access to Niantic Spatial Auth at a high level.
/// It starts either the Niantic Spatial refresh loop, or the sample backend service (depending on which is selected).
/// These services (when running), make sure that the current Niantic Spatial Access token is valid.
/// </summary>
public class NianticSpatialAccessManager
{
    static NianticSpatialAccessManager()
    {
        LoginManager.LoginComplete += SetupAccess;
        LoginManager.LogoutComplete += StopAccess;
    }

    public static void Start()
    {
        SetupAccess();
    }

    public static bool UseSampleBackend
    {
        get => _useSampleBackend;
        set
        {
            _useSampleBackend = value;
            StopAccess();
            SetupAccess();
        }
    }

    private static bool _useSampleBackend;

    private static void StopAccess()
    {
        // Stop sample backend, if running
        AuthAccessManager.StopAuthAccess();
        //Stop the runtime refresh loop, if running
        AuthRuntimeRefreshManager.CancelRefreshLoop();

        NsdkSettingsHelper.ActiveSettings.RefreshToken = string.Empty;
        NsdkSettingsHelper.ActiveSettings.AccessToken = string.Empty;
    }

    private static void SetupAccess()
    {
        if (!LoginManager.IsLoggedIn)
        {
            return;
        }

        if (_useSampleBackend)
        {
            AuthAccessManager.StartAuthAccess(UserSessionManager.AccessToken);
        }
        else
        {
            _ = AuthRuntimeRefreshManager.StartRefreshLoop(UserSessionManager.RefreshToken);
        }
    }
}
