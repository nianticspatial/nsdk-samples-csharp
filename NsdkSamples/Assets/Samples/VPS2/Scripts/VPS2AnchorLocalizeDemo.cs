// Copyright 2022-2026 Niantic.

using System;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR.VPS2;
using NianticSpatial.NSDK.AR.Subsystems;
using NianticSpatial.NSDK.AR.XRSubsystems;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARSubsystems;

using static NianticSpatial.NSDK.AR.XRSubsystems.Vps2LocalizationRequestStatus;

/// <summary>
/// Tracks the VPS anchor of a site selected from the Sites API and places content at
/// the anchor: a fallback marker, replaced by the site's downloaded mesh. This is the
/// anchor-payload flow; <see cref="VPS2AssetLocalizeDemo"/> shows the site localization
/// flow with asset tracking.
/// </summary>
/// <remarks>
/// Disabled by default in the VPS2Localization scene: enable this component and disable
/// <see cref="VPS2AssetLocalizeDemo"/> to run the anchor flow. The selected site's VPS
/// asset must carry an anchor payload.
/// </remarks>
public class VPS2AnchorLocalizeDemo : MonoBehaviour
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
    private Text _anchorStatusText;

    [SerializeField]
    private Text _anchorGeolocationText;

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
    private GameObject _anchorMarkerPrefab;

    private static readonly Color BlurWarningColor = new Color(1f, 0.65f, 0f);

    // Scene content under the tracked anchor
    private ARVps2Anchor _anchor;
    private GameObject _anchorContent;
    private GameObject _fallbackMarker;
    private GameObject _meshRoot;

    // Mesh download state
    private string _vpsMapAssetId;
    private string _meshAssetId;
    private bool _isMeshStreaming;
    private int _meshChunkCount;
    private double _siteLatitude;
    private double _siteLongitude;

    private float _lastBlurWarningTime = float.NegativeInfinity;

    private void Awake()
    {
        AssertComponent(_meshManager);
        AssertComponent(_arVps2Manager);
        AssertComponent(_sitesTargetListManager);

        AssertComponent(_downloadMeshToggle);
        AssertComponent(_localizationStatusText);
        AssertComponent(_localizationStatusPanel);
        AssertComponent(_downloadLoadingIcon);
        AssertComponent(_anchorStatusText);
        AssertComponent(_anchorGeolocationText);
        AssertComponent(_warningText);
    }

    private void OnEnable()
    {
        _downloadMeshToggle.onValueChanged.AddListener(DownloadMeshToggle_OnValueChanged);
        _sitesTargetListManager.SiteSelected += SitesTargetListManager_OnSiteSelected;
        _arVps2Manager.LocalizationRequestRecordAdded += ARVps2Manager_LocalizationRequestRecordAdded;
    }

    private void Start()
    {
        // Reset the UI
        _localizationStatusPanel.SetActive(false);
        _downloadLoadingIcon.enabled = false;
        _downloadMeshToggle.isOn = false;
        _localizationStatusText.text = "VPS2 Tracking State: UNAVAILABLE";
        _anchorStatusText.text = "Anchor: NOT TRACKED";
        _anchorGeolocationText.text = "Anchor Geolocation: N/A";
        _warningText.enabled = false;
    }

    private void Update()
    {
        UpdateWarning();
        UpdateLocalization();
        UpdateAnchorMarker();
    }

    private void OnDisable()
    {
        _downloadMeshToggle.onValueChanged.RemoveListener(DownloadMeshToggle_OnValueChanged);
        _sitesTargetListManager.SiteSelected -= SitesTargetListManager_OnSiteSelected;
        _arVps2Manager.LocalizationRequestRecordAdded -= ARVps2Manager_LocalizationRequestRecordAdded;
    }

    private void OnDestroy()
    {
        if (_anchorContent != null)
        {
            Destroy(_anchorContent);
        }
    }

    /// <summary>
    /// Assertion for the assignment of a serialized field.
    /// </summary>
    private static void AssertComponent<T>(T component) where T : UnityEngine.Object =>
        Debug.Assert(component != null, $"Component of type {typeof(T).Name} is missing!");

    /// <summary>
    /// Refreshes the tracking state label.
    /// </summary>
    private void UpdateLocalization()
    {
        var trackingState = _arVps2Manager.TryGetLatestLocalization(out var localization)
            ? localization.TrackingState
            : Vps2TrackingState.Unavailable;

        _localizationStatusText.text = trackingState switch
        {
            Vps2TrackingState.Unavailable => "VPS2 Tracking State: NOT TRACKING",
            Vps2TrackingState.Coarse => "VPS2 Tracking State: COARSE",
            Vps2TrackingState.Precise => "VPS2 Tracking State: PRECISE",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    /// <summary>
    /// Hangs fresh demo content under the tracked anchor. A previously tracked anchor is
    /// removed together with its content.
    /// </summary>
    private void AttachToAnchor(ARVps2Anchor anchor)
    {
        if (_anchor != null)
        {
            _arVps2Manager.RemoveAnchor(_anchor);
            _anchorContent = null;
            _fallbackMarker = null;
            _meshRoot = null;
            _meshChunkCount = 0;
        }

        _anchor = anchor;
        _anchorContent = new GameObject("AnchorContent");
        _anchorContent.transform.SetParent(_anchor.transform, false);
        _anchorContent.SetActive(false);
        if (_anchorMarkerPrefab != null)
        {
            _fallbackMarker = Instantiate(_anchorMarkerPrefab, _anchorContent.transform, false);
        }
    }

    /// <summary>
    /// Shows the anchor content while the anchor has a pose, and refreshes the anchor
    /// status labels.
    /// </summary>
    private void UpdateAnchorMarker()
    {
        if (_anchor == null)
        {
            _anchorStatusText.text = "Anchor: NOT TRACKED";
            _anchorGeolocationText.text = "Anchor Geolocation: N/A";
            return;
        }

        _anchorStatusText.text = _anchor.trackingState switch
        {
            TrackingState.None => $"Anchor: NONE\n(Reason: {_anchor.trackingStateReason})",
            TrackingState.Limited => $"Anchor: LIMITED\n(Reason: {_anchor.trackingStateReason})",
            TrackingState.Tracking => "Anchor: TRACKING",
            _ => throw new ArgumentOutOfRangeException()
        };

        var geolocation = _anchor.geolocation;
        _anchorGeolocationText.text = geolocation.HasValue && _anchor.trackingState == TrackingState.Tracking
            ? $"Anchor Geolocation: {geolocation.Value.Latitude:F5}, {geolocation.Value.Longitude:F5}"
            : "Anchor Geolocation: N/A";

        if (_anchorContent != null)
        {
            _anchorContent.SetActive(_anchor.trackingState != TrackingState.None);
        }
    }

    /// <summary>
    /// Shows the transient blur warning while flagged records keep arriving.
    /// </summary>
    private void UpdateWarning()
    {
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
    /// Streams the site mesh of the anchor's VPS asset under the anchor content, so the
    /// chunks land in the anchor's coordinate space. The fallback marker stays visible
    /// until the first chunk arrives, and remains the marker if the download fails.
    /// </summary>
    private async Task DownloadAnchorMeshAsync()
    {
        if (_isMeshStreaming || _vpsMapAssetId == _meshAssetId)
        {
            return;
        }

        // Any mesh on display belongs to a previous site: drop it so it does not
        // show up at the new anchor.
        ResetAnchorMesh();
        _meshAssetId = null;

        if (!_downloadMeshToggle.isOn || string.IsNullOrEmpty(_vpsMapAssetId) ||
            _anchorContent == null)
        {
            return;
        }

        _meshAssetId = _vpsMapAssetId;
        _isMeshStreaming = true;
        _downloadLoadingIcon.enabled = true;
        _meshRoot = new GameObject("DownloadedMesh");
        _meshRoot.transform.SetParent(_anchorContent.transform, false);

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

        // The tracked site changed while this mesh was streaming; fetch the new one.
        // This runs after the finally above so it cannot clobber the new stream's state,
        // and before the failure check so an aborted stream is not an error.
        if (_vpsMapAssetId != _meshAssetId)
        {
            _ = DownloadAnchorMeshAsync();
            return;
        }

        if (failed || _meshChunkCount == 0)
        {
            if (!failed)
            {
                Debug.LogError("Mesh download failed");
            }

            ResetAnchorMesh();
            // Allow the download toggle to retry the same asset.
            _meshAssetId = null;
        }
    }

    /// <summary>
    /// Consumes the mesh chunk stream into <paramref name="meshRoot"/>, revealing the
    /// mesh at the first chunk. Ends early when a site change destroys the root.
    /// </summary>
    /// <returns>False when the download fails.</returns>
    private async Task<bool> StreamMeshChunksAsync(GameObject meshRoot)
    {
        try
        {
            await foreach (var chunk in _meshManager.GetLocationMeshChunksByVpsMapAssetIdAsync(
                               _meshAssetId, _siteLatitude, _siteLongitude,
                               getTexture: true, maxChunks: 0, maxSizeKb: 0))
            {
                if (meshRoot == null)
                {
                    // A site change destroyed the content root, taking this stream's
                    // mesh with it; the caller fetches the new site's mesh.
                    Destroy(chunk);
                    return true;
                }

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

    private void ResetAnchorMesh()
    {
        if (_meshRoot != null)
        {
            Destroy(_meshRoot);
            _meshRoot = null;
        }

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
        if (string.IsNullOrEmpty(site.AnchorPayload))
        {
            Debug.LogWarning($"Cannot track an anchor: site {site.Name} has no anchor payload");
            return;
        }

        if (!_arVps2Manager.TryTrackAnchor(site.AnchorPayload, out var anchor))
        {
            Debug.LogWarning($"Failed to track the anchor of site {site.SiteId}");
            return;
        }

        AttachToAnchor(anchor);
        _vpsMapAssetId = site.VpsMapAssetId;
        _siteLatitude = site.Latitude;
        _siteLongitude = site.Longitude;
        _ = DownloadAnchorMeshAsync();

        _sitesTargetListManager.transform.Find("ScrollList").gameObject.SetActive(false);
        _localizationStatusPanel.SetActive(true);
    }

    private void ARVps2Manager_LocalizationRequestRecordAdded(XRVps2LocalizationRequestRecord record)
    {
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
            _ = DownloadAnchorMeshAsync();
        }
    }
}
