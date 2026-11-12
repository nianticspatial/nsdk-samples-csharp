// Copyright 2022-2026 Niantic.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR.VPS2;
using NianticSpatial.NSDK.AR.Subsystems;
using NianticSpatial.NSDK.AR.XRSubsystems;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARSubsystems;

using static NianticSpatial.NSDK.AR.XRSubsystems.Vps2LocalizationRequestStatus;

/// <summary>
/// Localizes to a VPS asset selected from the Sites API, production or not, and places
/// content at the localized asset's origin: a fallback marker, replaced by the site's
/// downloaded mesh. The content hangs under the asset's <see cref="ARVps2Asset"/>
/// trackable, whose transform follows the origin. While localizing, Stop Localizing ends
/// the session and Switch Asset moves to another VPS asset of the site.
/// </summary>
/// <remarks>
/// A large asset is mapped in areas, and its mesh download carries every area. The raw
/// geometry of all areas is kept, but only the localized area's mesh is rendered: when
/// VPS2 localizes into another area, the previous area's Unity meshes are destroyed and
/// the new area's are built from the kept geometry, without downloading again.
///
/// See <see cref="VPS2AnchorLocalizeDemo"/> for the anchor-payload flow this replaces.
/// </remarks>
public class VPS2AssetLocalizeDemo : MonoBehaviour
{
    [Header("Managers")]
    [SerializeField]
    private SitesTargetListManager _sitesTargetListManager;

    [SerializeField]
    private ARVps2Manager _arVps2Manager;

    [SerializeField]
    private LocationMeshManager _meshManager;

    [Header("UI")]
    [SerializeField]
    private Text _localizationStatusText;

    [SerializeField]
    private Text _assetStatusText;

    [SerializeField]
    private Text _assetPoseText;

    [SerializeField]
    private Text _warningText;

    [SerializeField]
    private GameObject _localizationStatusPanel;

    [SerializeField]
    private Toggle _downloadMeshToggle;

    [SerializeField]
    private Image _downloadLoadingIcon;

    [Header("Resources")]
    [SerializeField]
    private GameObject _assetMarkerPrefab;

    private static readonly Color BlurWarningColor = new Color(1f, 0.65f, 0f);
    private static readonly Color SiteFailureColor = Color.red;

    // Scene content under the localized asset's trackable
    private ARVps2Asset _asset;
    private GameObject _assetContent;
    private GameObject _fallbackMarker;
    private GameObject _meshRoot;

    // Localized asset and mesh download state
    private string _localizedAssetId;
    private string _localizedAreaId;
    private string _meshAssetId;
    private bool _isMeshStreaming;

    // Raw geometry of every downloaded chunk of the mesh asset, keyed by area id (empty
    // when the asset has no areas).
    private readonly Dictionary<string, List<MeshChunkData>> _meshChunkDataByArea =
        new Dictionary<string, List<MeshChunkData>>();
    private int _downloadedChunkCount;
    private int _meshChunkCount;
    private double _siteLatitude;
    private double _siteLongitude;

    // Warnings
    private float _lastBlurWarningTime = float.NegativeInfinity;
    private string _siteFailureMessage;

    // Session controls, in the menu buttons' places while a session runs. The site is null
    // for a nearby session, which can target several sites.
    private Button _stopLocalizingButton;
    private Button _switchAssetButton;
    private string _sessionSiteId;

    private void Awake()
    {
        AssertComponent(_meshManager);
        AssertComponent(_arVps2Manager);
        AssertComponent(_sitesTargetListManager);

        AssertComponent(_downloadMeshToggle);
        AssertComponent(_localizationStatusText);
        AssertComponent(_localizationStatusPanel);
        AssertComponent(_downloadLoadingIcon);
        AssertComponent(_assetStatusText);
        AssertComponent(_assetPoseText);
        AssertComponent(_warningText);
    }

    private void OnEnable()
    {
        _downloadMeshToggle.onValueChanged.AddListener(DownloadMeshToggle_OnValueChanged);
        _sitesTargetListManager.SiteSelected += SitesTargetListManager_OnSiteSelected;
        _sitesTargetListManager.NearbyLocalizeSelected += SitesTargetListManager_OnNearbyLocalizeSelected;
        _arVps2Manager.LocalizationRequestRecordAdded += ARVps2Manager_LocalizationRequestRecordAdded;
    }

    private void Start()
    {
        // Reset the UI
        _localizationStatusPanel.SetActive(false);
        _downloadLoadingIcon.enabled = false;
        _downloadMeshToggle.isOn = false;
        // There is nothing to download until a localization session starts.
        _downloadMeshToggle.gameObject.SetActive(false);
        _localizationStatusText.text = "VPS2 Tracking State: UNAVAILABLE";
        _assetStatusText.text = "Localized Asset: NONE";
        _assetPoseText.text = "Asset Pose: NO DATA";
        _warningText.enabled = false;

        _stopLocalizingButton = _sitesTargetListManager.CreateMenuButton(
            "StopLocalizingButton", "Stop Localizing", StopLocalizing, slot: 0);
        _switchAssetButton = _sitesTargetListManager.CreateMenuButton(
            "SwitchAssetButton", "Switch Asset", OpenSwitchList, slot: 1);
        SetSessionControlsVisible(false);
    }

    private void Update()
    {
        UpdateWarning();
        UpdateLocalization();
        UpdateAssetMarker();
    }

    private void OnDisable()
    {
        // Drop this demo's sites so they are not localization targets when the scene is
        // re-entered. Must run before ARVps2Manager stops the subsystem; the manager's
        // earlier execution order gives it the later OnDisable.
        _arVps2Manager.TryStopLocalize();

        _downloadMeshToggle.onValueChanged.RemoveListener(DownloadMeshToggle_OnValueChanged);
        _sitesTargetListManager.SiteSelected -= SitesTargetListManager_OnSiteSelected;
        _sitesTargetListManager.NearbyLocalizeSelected -= SitesTargetListManager_OnNearbyLocalizeSelected;
        _arVps2Manager.LocalizationRequestRecordAdded -= ARVps2Manager_LocalizationRequestRecordAdded;
    }

    private void OnDestroy()
    {
        if (_assetContent != null)
        {
            Destroy(_assetContent);
        }
    }

    /// <summary>
    /// Assertion for the assignment of a serialized field.
    /// </summary>
    private static void AssertComponent<T>(T component) where T : UnityEngine.Object =>
        Debug.Assert(component != null, $"Component of type {typeof(T).Name} is missing!");

    /// <summary>
    /// Requests localization to one VPS asset of a site, or to the site's production assets
    /// when <paramref name="assetId"/> is empty, and on success swaps the UI over to the
    /// localization session.
    /// </summary>
    private bool TryLocalizeToSite(string siteId, string assetId, double latitude, double longitude)
    {
        var requested = string.IsNullOrEmpty(assetId)
            ? _arVps2Manager.TryLocalize(siteId)
            : _arVps2Manager.TryLocalize(siteId, assetId);
        if (!requested)
        {
            return false;
        }

        _sessionSiteId = siteId;
        BeginLocalizationSession(latitude, longitude);
        return true;
    }

    /// <summary>
    /// Stops localizing to the session's site, or to every site of a nearby session, and
    /// brings the menu back. The SDK clears the localized asset, which drops its content in
    /// <see cref="UpdateLocalizedAsset"/>.
    /// </summary>
    private void StopLocalizing()
    {
        if (!_arVps2Manager.TryStopLocalize(_sessionSiteId))
        {
            Debug.LogWarning("Failed to stop localizing");
        }

        EndLocalizationSession();
    }

    /// <summary>
    /// Lists the site's other VPS assets in place of the session controls, so the session
    /// can't be stopped while the list is open. Cancelling brings the controls back.
    /// </summary>
    private void OpenSwitchList()
    {
        _localizationStatusPanel.SetActive(false);
        _stopLocalizingButton.gameObject.SetActive(false);
        _switchAssetButton.gameObject.SetActive(false);
        _sitesTargetListManager.ShowSwitchList(SwitchAsset, () =>
        {
            _localizationStatusPanel.SetActive(true);
            SetSessionControlsVisible(true);
        });
    }

    /// <summary>
    /// Moves the session to another VPS asset of its site. Localizing to a second asset of a
    /// site that is already a target would add it rather than replace the first, so the site
    /// is stopped first. If the new request fails, the session ends, since its old asset is
    /// already stopped.
    /// </summary>
    private void SwitchAsset(NianticSpatial.NSDK.AR.Sites.AssetInfo asset)
    {
        _arVps2Manager.TryStopLocalize(_sessionSiteId);
        if (!_arVps2Manager.TryLocalize(_sessionSiteId, asset.Id))
        {
            Debug.LogWarning($"Failed to request localization to asset {asset.Id}");
            EndLocalizationSession();
            return;
        }

        _siteFailureMessage = null;
        _localizationStatusPanel.SetActive(true);
        SetSessionControlsVisible(true);
    }

    /// <summary>
    /// Swaps the menu's Request Sites and Localize to Nearby Sites buttons for Stop
    /// Localizing and Switch Asset while a session runs. Switch Asset needs a site with
    /// another VPS asset, so a nearby session never offers it.
    /// </summary>
    private void SetSessionControlsVisible(bool sessionActive)
    {
        _sitesTargetListManager.SetTargetControlsVisible(!sessionActive);
        if (_stopLocalizingButton != null)
        {
            _stopLocalizingButton.gameObject.SetActive(sessionActive);
        }

        if (_switchAssetButton != null)
        {
            _switchAssetButton.gameObject.SetActive(
                sessionActive && _sessionSiteId != null && _sitesTargetListManager.CanSwitchAsset);
        }
    }

    /// <summary>
    /// Refreshes the tracking state label and follows the localized asset.
    /// </summary>
    private void UpdateLocalization()
    {
        if (!_arVps2Manager.TryGetLatestLocalization(out var localization))
        {
            _localizationStatusText.text = "VPS2 Tracking State: NOT TRACKING";
            return;
        }

        _localizationStatusText.text = localization.TrackingState switch
        {
            Vps2TrackingState.Unavailable => "VPS2 Tracking State: NOT TRACKING",
            Vps2TrackingState.Coarse => "VPS2 Tracking State: COARSE",
            Vps2TrackingState.Precise => "VPS2 Tracking State: PRECISE",
            _ => throw new ArgumentOutOfRangeException()
        };

        // The localized asset is independent of the tracking state: an asset without
        // a georeference localizes visually while the state stays unavailable.
        UpdateLocalizedAsset(localization.LocalizedAsset);
    }

    /// <summary>
    /// Tracks the asset and area behind the latest visual localization. A new asset kicks
    /// off its mesh download; a new area of the same asset swaps the rendered mesh.
    /// </summary>
    private void UpdateLocalizedAsset(Vps2LocalizedAsset? localizedAsset)
    {
        var assetId = localizedAsset?.AssetId;
        // Chunks of an asset without areas carry an empty area id, not null.
        var areaId = localizedAsset?.AreaId ?? string.Empty;
        if (assetId == _localizedAssetId)
        {
            if (areaId != _localizedAreaId)
            {
                _localizedAreaId = areaId;
                UpdateAssetStatusText();
                RenderLocalizedArea();
            }

            return;
        }

        _localizedAssetId = assetId;
        _localizedAreaId = areaId;
        UpdateAssetStatusText();
        AttachToAssetTrackable();
        _ = DownloadAssetMeshAsync();
    }

    private void UpdateAssetStatusText()
    {
        if (_localizedAssetId == null)
        {
            _assetStatusText.text = "Localized Asset: NONE";
            return;
        }

        var areaText = string.IsNullOrEmpty(_localizedAreaId)
            ? string.Empty
            : $"\nArea: {_localizedAreaId}";
        _assetStatusText.text = $"Localized Asset: {_localizedAssetId}{areaText}";
    }

    /// <summary>
    /// Hangs fresh demo content under the localized asset's trackable, whose transform
    /// follows the asset's origin. A previous asset's content is destroyed: it must not
    /// appear at the new asset's origin.
    /// </summary>
    private void AttachToAssetTrackable()
    {
        if (_assetContent != null)
        {
            Destroy(_assetContent);
            _assetContent = null;
            _fallbackMarker = null;
            _meshRoot = null;
            _meshChunkCount = 0;
        }

        _asset = null;
        if (_localizedAssetId == null ||
            !_arVps2Manager.TryTrackAsset(_localizedAssetId, out _asset))
        {
            return;
        }

        _assetContent = new GameObject("AssetContent");
        _assetContent.transform.SetParent(_asset.transform, false);
        _assetContent.SetActive(false);
        if (_assetMarkerPrefab != null)
        {
            _fallbackMarker = Instantiate(_assetMarkerPrefab, _assetContent.transform, false);
        }
    }

    /// <summary>
    /// Shows the asset content only while the asset's origin is tracked.
    /// </summary>
    private void UpdateAssetMarker()
    {
        if (_asset == null)
        {
            _assetPoseText.text = "Asset Pose: NO DATA";
            return;
        }

        var isTracked = _asset.trackingState == TrackingState.Tracking;
        if (_assetContent != null)
        {
            _assetContent.SetActive(isTracked);
        }

        _assetPoseText.text = isTracked
            ? $"Asset Pose: TRACKED ({_asset.Confidence:F2})"
            : "Asset Pose: NO DATA";
    }

    /// <summary>
    /// Shows the sticky site failure message, or the transient blur warning while
    /// flagged records keep arriving.
    /// </summary>
    private void UpdateWarning()
    {
        if (_siteFailureMessage != null)
        {
            _warningText.text = _siteFailureMessage;
            _warningText.color = SiteFailureColor;
            _warningText.enabled = true;
            return;
        }

        if (Time.unscaledTime - _lastBlurWarningTime < 2f)
        {
            _warningText.text = "Image too blurry - hold the device steady";
            _warningText.color = BlurWarningColor;
            _warningText.enabled = true;
            return;
        }

        _warningText.enabled = false;
    }

    /// <summary>
    /// Streams the localized asset's site mesh under the asset content, so the chunks
    /// land in the asset's coordinate space. A previous asset's mesh is dropped even
    /// when the download toggle is off. The fallback marker stays visible until the
    /// first chunk of the localized area arrives, and remains the marker if the download
    /// fails.
    /// </summary>
    private async Task DownloadAssetMeshAsync()
    {
        if (_isMeshStreaming || _localizedAssetId == _meshAssetId)
        {
            return;
        }

        // Any mesh on display belongs to a previous asset: drop it so it does not
        // show up at the new asset's origin.
        ResetAssetMesh();
        _meshAssetId = null;

        if (!_downloadMeshToggle.isOn || string.IsNullOrEmpty(_localizedAssetId) ||
            _assetContent == null)
        {
            return;
        }

        _meshAssetId = _localizedAssetId;
        _isMeshStreaming = true;
        _downloadLoadingIcon.enabled = true;
        _meshRoot = new GameObject("DownloadedMesh");
        _meshRoot.transform.SetParent(_assetContent.transform, false);

        bool failed;
        try
        {
            failed = !await StreamMeshChunksAsync(_meshRoot);
        }
        finally
        {
            _isMeshStreaming = false;
            _downloadLoadingIcon.enabled = false;
        }

        // The localized asset changed while this mesh was streaming; fetch the new one.
        // This runs after the finally above so it cannot clobber the new stream's state,
        // and before the failure check so an aborted stream is not an error.
        if (_localizedAssetId != _meshAssetId)
        {
            _ = DownloadAssetMeshAsync();
            return;
        }

        // Counts every area: the localized area may have no mesh of its own.
        if (failed || _downloadedChunkCount == 0)
        {
            if (!failed)
            {
                Debug.LogError("Mesh download failed");
            }

            ResetAssetMesh();
            // Allow the download toggle to retry the same asset.
            _meshAssetId = null;
        }
    }

    /// <summary>
    /// Consumes the mesh chunk stream, keeping the raw geometry of every area and building
    /// Unity meshes under <paramref name="meshRoot"/> for the localized area's chunks only.
    /// Ends early when an asset change destroys the root.
    /// </summary>
    /// <returns>False when the download fails.</returns>
    private async Task<bool> StreamMeshChunksAsync(GameObject meshRoot)
    {
        try
        {
            await foreach (var chunkData in _meshManager.GetLocationMeshChunkDataByVpsMapAssetIdAsync(
                               _meshAssetId, _siteLatitude, _siteLongitude,
                               getTexture: true, maxChunks: 0, maxSizeKb: 0))
            {
                // An asset change destroyed the content root, taking this stream's mesh
                // with it. Destroy is deferred, so also compare with the current root,
                // which the change clears at once.
                if (meshRoot == null || meshRoot != _meshRoot)
                {
                    return true;
                }

                if (!_meshChunkDataByArea.TryGetValue(chunkData.AreaId, out var areaChunks))
                {
                    areaChunks = new List<MeshChunkData>();
                    _meshChunkDataByArea.Add(chunkData.AreaId, areaChunks);
                }

                areaChunks.Add(chunkData);
                _downloadedChunkCount++;

                if (chunkData.AreaId == _localizedAreaId)
                {
                    AddMeshChunk(meshRoot, chunkData);
                }
            }

            return true;
        }
        catch (Exception e)
        {
            // Cancellation is not an error: the stream is canceled when the mesh
            // manager is disabled.
            if (e is not OperationCanceledException)
            {
                Debug.LogException(e);
            }

            return false;
        }
    }

    /// <summary>
    /// Builds Unity mesh assets for a kept chunk under <paramref name="meshRoot"/>,
    /// revealing the mesh at the first chunk.
    /// </summary>
    private void AddMeshChunk(GameObject meshRoot, MeshChunkData chunkData)
    {
        var chunk = _meshManager.CreateMeshChunk(chunkData);
        chunk.transform.SetParent(meshRoot.transform, false);
        chunk.SetActive(true);
        _meshChunkCount++;

        if (_meshChunkCount == 1)
        {
            // The real site geometry replaces the fallback marker.
            _downloadLoadingIcon.enabled = false;
            UpdateMeshVisibility();
        }
    }

    /// <summary>
    /// Swaps the rendered mesh for the localized area's, building the area's chunks
    /// downloaded so far.
    /// </summary>
    private void RenderLocalizedArea()
    {
        if (_meshRoot == null)
        {
            return;
        }

        foreach (Transform chunk in _meshRoot.transform)
        {
            // Destroy is deferred to the end of the frame; hide the chunk now so the two
            // areas never render together.
            chunk.gameObject.SetActive(false);
            Destroy(chunk.gameObject);
        }

        _meshChunkCount = 0;
        if (_meshChunkDataByArea.TryGetValue(_localizedAreaId, out var areaChunks))
        {
            foreach (var chunkData in areaChunks)
            {
                AddMeshChunk(_meshRoot, chunkData);
            }
        }

        UpdateMeshVisibility();
    }

    private void ResetAssetMesh()
    {
        if (_meshRoot != null)
        {
            Destroy(_meshRoot);
            _meshRoot = null;
        }

        _meshChunkDataByArea.Clear();
        _downloadedChunkCount = 0;
        _meshChunkCount = 0;
        UpdateMeshVisibility();
    }

    /// <summary>
    /// Shows the downloaded mesh while the download toggle is on and chunks have
    /// arrived, and the fallback marker otherwise.
    /// </summary>
    private void UpdateMeshVisibility()
    {
        bool showMesh = _downloadMeshToggle.isOn && _meshRoot != null && _meshChunkCount > 0;
        if (_meshRoot != null)
        {
            _meshRoot.SetActive(showMesh);
        }

        if (_fallbackMarker != null)
        {
            _fallbackMarker.SetActive(!showMesh);
        }
    }

    private void SitesTargetListManager_OnSiteSelected(
        SitesTargetListManager.SiteSelectedArgs site)
    {
        if (!TryLocalizeToSite(site.SiteId, site.VpsMapAssetId, site.Latitude, site.Longitude))
        {
            Debug.LogWarning($"Failed to request localization to site {site.SiteId}");
        }
    }

    private void SitesTargetListManager_OnNearbyLocalizeSelected(
        SitesTargetListManager.NearbyLocalizeArgs nearby)
    {
        if (!_arVps2Manager.TryLocalize(nearby.Latitude, nearby.Longitude, nearby.RadiusMeters))
        {
            Debug.LogWarning("Failed to request nearby localization at "
                             + $"({nearby.Latitude}, {nearby.Longitude})");
            return;
        }

        _sessionSiteId = null;
        BeginLocalizationSession(nearby.Latitude, nearby.Longitude);
    }

    /// <summary>
    /// Swaps the site list for the localization status panel, reveals the mesh download
    /// toggle, and records the coordinate used for the follow-up mesh download.
    /// </summary>
    private void BeginLocalizationSession(double latitude, double longitude)
    {
        _siteLatitude = latitude;
        _siteLongitude = longitude;
        _siteFailureMessage = null;

        // Both localization paths get the toggle here. The shared UI prefab also shows it
        // from the "Request Sites" button's own onClick, which the nearby-localize button
        // does not inherit: it is a clone of that button with its onClick calls dropped.
        _downloadMeshToggle.gameObject.SetActive(true);

        var scrollList = _sitesTargetListManager.transform.Find("ScrollList");
        if (scrollList != null)
        {
            scrollList.gameObject.SetActive(false);
        }

        _localizationStatusPanel.SetActive(true);
        SetSessionControlsVisible(true);
    }

    /// <summary>
    /// Hides the localization status panel and the mesh download toggle, and brings the
    /// menu back.
    /// </summary>
    private void EndLocalizationSession()
    {
        _sessionSiteId = null;
        _siteFailureMessage = null;
        _localizationStatusPanel.SetActive(false);
        _downloadMeshToggle.gameObject.SetActive(false);
        SetSessionControlsVisible(false);
    }

    private void ARVps2Manager_LocalizationRequestRecordAdded(XRVps2LocalizationRequestRecord record)
    {
        // A failed asset fetch is terminal for the site flow (nothing gets enrolled for
        // localization), so it surfaces in the UI, unlike frame localize failures, which
        // retry continuously. The latest fetch outcome wins: a retry that succeeds
        // clears the message. A fetch that TryStopLocalize cancelled (Stop Localizing, or
        // Switch Asset) ends Failed with Unknown, and is not a failure to report.
        if (record.RequestType == Vps2LocalizationRequestType.AssetInfo &&
            record.ErrorCode != Vps2LocalizationError.Unknown)
        {
            _siteFailureMessage = record.ErrorCode == Vps2LocalizationError.None
                ? null
                : $"Cannot localize to this site: {record.ErrorCode}";
        }

        if (record.Status is Failed or FrameRejected)
        {
            Debug.LogWarning(record);
        }
        else if (record.Status == FrameFlagged &&
                 record.ErrorCode == Vps2LocalizationError.ImageTooBlurry)
        {
            _lastBlurWarningTime = Time.unscaledTime;
        }
    }

    private void DownloadMeshToggle_OnValueChanged(bool isEnabled)
    {
        UpdateMeshVisibility();
        if (isEnabled)
        {
            _ = DownloadAssetMeshAsync();
        }
    }
}
