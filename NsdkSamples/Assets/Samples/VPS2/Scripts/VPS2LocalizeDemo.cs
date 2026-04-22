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

public class VPS2LocalizeDemo : MonoBehaviour
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
    private Text _geolocationText;

    [SerializeField] 
    private GameObject _localizationStatusPanel;

    [SerializeField] 
    private Toggle _downloadMeshToggle;

    [SerializeField] 
    private Image _downloadLoadingIcon;

    [Header("Resources")] 
    [SerializeField] 
    private GameObject _anchorMarkerPrefab;

    [SerializeField] 
    private ArrowIndicator _poiIndicatorArrow;

    // Components and resources for the selected anchor
    private ARVps2Anchor _anchor;
    private GameObject _refinedMarker;
    private GameObject _coarseMarker;
    private string _anchorPayload;

    // Helpers
    private bool _isAnchorSet;
    private bool _didDownloadMesh;

    private void Awake()
    {
        // Assert managers set
        AssertComponent(_meshManager);
        AssertComponent(_arVps2Manager);
        AssertComponent(_sitesTargetListManager);
        
        // Assert UI set
        AssertComponent(_downloadMeshToggle);
        AssertComponent(_localizationStatusText);
        AssertComponent(_localizationStatusPanel);
        AssertComponent(_downloadLoadingIcon);
        AssertComponent(_geolocationText);
    }

    /// <summary>
    /// Assertion for the assignment of a serialized field.
    /// </summary>
    private static void AssertComponent<T>(T component) where T : UnityEngine.Object =>
        Debug.Assert(component != null, $"Component of type {typeof(T).Name} is missing!");

    private void OnEnable()
    {
        _downloadMeshToggle.onValueChanged.AddListener(DownloadMesh_OnToggleValueChanged);
        _sitesTargetListManager.OnAnchorButtonPressed +=
            SitesTargetListManager_OnLocationSelected;
        _arVps2Manager.LocalizationRequestRecordAdded += OnLocalizationRequestRecord;
    }

    private void OnDisable()
    {
        _downloadMeshToggle.onValueChanged.RemoveListener(DownloadMesh_OnToggleValueChanged);
        _sitesTargetListManager.OnAnchorButtonPressed -=
            SitesTargetListManager_OnLocationSelected;
        _arVps2Manager.LocalizationRequestRecordAdded -= OnLocalizationRequestRecord;
    }

    private void Start()
    {
        // Reset the UI
        _localizationStatusPanel.SetActive(false);
        _downloadLoadingIcon.enabled = false;
        _downloadMeshToggle.isOn = false;
        _localizationStatusText.text = "VPS2 Tracking State: UNAVAILABLE";
        _anchorStatusText.text = "Anchor Tracking State: NOT TRACKED";
        _geolocationText.text = "Anchor Geolocation: N/A";
    }

    private void Update()
    {
        if (!_arVps2Manager.TryGetLatestLocalization(out var localization))
            return;
        
        // Update the localization status label
        _localizationStatusText.text = localization.TrackingState switch
        {
            Vps2TrackingState.Unavailable => "VPS2 Tracking State: NOT TRACKING",
            Vps2TrackingState.Coarse => "VPS2 Tracking State: COARSE",
            Vps2TrackingState.Precise => "VPS2 Tracking State: PRECISE",
            _ => throw new ArgumentOutOfRangeException()
        };

        if (_isAnchorSet)
        {
            // Toggle the appropriate anchor marker based on the anchor's tracking state
            ToggleAnchorMarker(_anchor.trackingState);

            _anchorStatusText.text = _anchor.trackingState switch
            {
                TrackingState.None => $"Anchor Tracking State: NONE\n(Reason: {_anchor.trackingStateReason})",
                TrackingState.Limited => $"Anchor Tracking State: LIMITED\n(Reason: {_anchor.trackingStateReason})",
                TrackingState.Tracking => "Anchor Tracking State: TRACKING",
                _ => throw new ArgumentOutOfRangeException()
            };
            var geo = _anchor.geolocation;
            if (geo.HasValue && _anchor.trackingState == TrackingState.Tracking)
            {
                _geolocationText.text = $"Anchor geolocation: {geo.Value.Latitude:F5}, {geo.Value.Longitude:F5}, {geo.Value.Heading:F1}°";
            }
            else
            {
                _geolocationText.text = "Anchor geolocation: N/A";
            }

            // Enable navigation if the tracking is coarse
            _poiIndicatorArrow.Target = localization.TrackingState == Vps2TrackingState.Coarse
                ? _anchor.transform
                : null;
        }
    }

    private void OnDestroy()
    {
        if (_refinedMarker != null)
        {
            Destroy(_refinedMarker);
        }

        if (_coarseMarker != null)
        {
            Destroy(_coarseMarker);
        }
    }

    private void SitesTargetListManager_OnLocationSelected(
        SitesTargetListManager.AnchorSelectedArgs location)
    {
        
        // Check arguments
        if (string.IsNullOrEmpty(location.Payload))
        {
            Debug.LogWarning("The selected location does not have a default anchor");
            return;
        }
        
        // Create the anchor
        _isAnchorSet = _arVps2Manager.TryTrackAnchor(
            anchorPayload: location.Payload,
            anchorOut: out _anchor);

        if (!_isAnchorSet)
        {
            Debug.Log("Failed to track anchor");
            return;
        }
        
        // Update the UI
        _sitesTargetListManager.transform.Find("ScrollList").gameObject.SetActive(false);
        _localizationStatusPanel.SetActive(true);

        // Instantiate the debug anchor visualization
        if (_anchorMarkerPrefab != null)
        {
            _coarseMarker = Instantiate(_anchorMarkerPrefab, _anchor.transform, true);
            _coarseMarker.transform.localPosition = Vector3.zero;
            _coarseMarker.transform.localRotation = Quaternion.identity;
            _coarseMarker.transform.localScale = Vector3.one;
        }

        // Initiate downloading the mesh
        _anchorPayload = location.Payload;
        _ = InstantiateAnchorMeshAsync();
    }

    private async Task InstantiateAnchorMeshAsync()
    {
        // In this sample we only attempt to download the mesh once
        if (_didDownloadMesh || !_downloadMeshToggle.isOn || !_isAnchorSet)
        {
            return;
        }

        if (string.IsNullOrEmpty(_anchorPayload))
        {
            Debug.LogError("Tried to download anchor mesh without a payload.");
            return;
        }
        
        // Show the download indicator
        _downloadLoadingIcon.enabled = true;
        
        // Await the download
        var mesh = await _meshManager.GetLocationMeshForPayloadAsync(_anchorPayload, 0, false, true);
        
        // Hide the download indicator
        _downloadLoadingIcon.enabled = false;

        if (mesh == null)
        {
            _downloadMeshToggle.isOn = false;
            Debug.LogError("Mesh download failed");
            return;
        }

        // Assign the mesh
        _refinedMarker = mesh;
        _refinedMarker.transform.SetParent(_anchor.transform, false);
        _refinedMarker.SetActive(false);
        _didDownloadMesh = true;
    }
    
    private void ToggleAnchorMarker(TrackingState trackingState)
    {
        // The textured mesh should only appear when the anchor is tracked,
        // otherwise we render the default anchor visualization
        if (_refinedMarker != null)
            _refinedMarker.gameObject.SetActive(trackingState == TrackingState.Tracking && _downloadMeshToggle.isOn);
        if (_coarseMarker != null)
            _coarseMarker.gameObject.SetActive(trackingState != TrackingState.Tracking ||
                                               (trackingState == TrackingState.Tracking && _refinedMarker == null));
    }

    private static void OnLocalizationRequestRecord(XRVps2LocalizationRequestRecord record)
    {
        if (record.Status is Failed or FrameRejected)
        {
            Debug.LogWarning(record);
        }
    }

    private void DownloadMesh_OnToggleValueChanged(bool isEnabled)
    {
        if (isEnabled && _refinedMarker == null)
        {
            // Try to download the mesh again
            _ = InstantiateAnchorMeshAsync();
        }
    }
}