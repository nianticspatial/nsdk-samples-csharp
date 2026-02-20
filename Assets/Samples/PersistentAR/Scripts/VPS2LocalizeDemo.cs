// Copyright 2022-2026 Niantic.

using System;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR;
using NianticSpatial.NSDK.AR.PersistentAnchors;
using NianticSpatial.NSDK.AR.Subsystems;
using NianticSpatial.NSDK.AR.XRSubsystems;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARSubsystems;

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
    private Text _transformerStatusText;
    
    [SerializeField] 
    private Text _anchorStatusText;

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
    private ARPersistentAnchor _anchor;
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
        AssertComponent(_transformerStatusText);
        AssertComponent(_localizationStatusPanel);
        AssertComponent(_downloadLoadingIcon);
    }

    /// <summary>
    /// Assertion for the assignment of a serialized field.
    /// </summary>
    private static void AssertComponent<T>(T component) where T : UnityEngine.Object =>
        Debug.Assert(component != null, $"{nameof(VPS2LocalizeDemo)} missing {component.GetType().Name}!");

    private void OnEnable()
    {
        _downloadMeshToggle.onValueChanged.AddListener(DownloadMesh_OnToggleValueChanged);
        _sitesTargetListManager.OnAnchorButtonPressed +=
            SitesTargetListManager_OnLocationSelected;
    }

    private void OnDisable()
    {
        _downloadMeshToggle.onValueChanged.RemoveListener(DownloadMesh_OnToggleValueChanged);
        _sitesTargetListManager.OnAnchorButtonPressed -=
            SitesTargetListManager_OnLocationSelected;
    }

    private void Start()
    {
        // Reset the UI
        _localizationStatusPanel.SetActive(false);
        _downloadLoadingIcon.enabled = false;
        _downloadMeshToggle.isOn = true;
        _transformerStatusText.text = "VPS2 Tracking State: UNAVAILABLE";
        _anchorStatusText.text = "Anchor Tracking State: NOT TRACKED";
    }

    private void Update()
    {
        if (!_arVps2Manager.TryGetLatestTransformer(out var transformer))
            return;
        
        // Update the localization status label
        _transformerStatusText.text = transformer.TrackingState switch
        {
            Vps2TrackingState.Unavailable => "VPS2 Tracking State: NOT TRACKING",
            Vps2TrackingState.Coarse => "VPS2 Tracking State: COARSE",
            Vps2TrackingState.Precise => "VPS2 Tracking State: PRECISE",
            _ => throw new ArgumentOutOfRangeException()
        };

        // Toggle the appropriate anchor marker based on the tracking state
        ToggleAnchorMarker(transformer.TrackingState);

        if (_isAnchorSet)
        {
            _anchorStatusText.text = _anchor.trackingState switch
            {
                TrackingState.None => $"Anchor Tracking State: NONE\n(Reason: {_anchor.trackingStateReason})",
                TrackingState.Limited => $"Anchor Tracking State: LIMITED\n(Reason: {_anchor.trackingStateReason})",
                TrackingState.Tracking => "Anchor Tracking State: TRACKING",
                _ => throw new ArgumentOutOfRangeException()
            };
            // Enable navigation if the tracking is coarse
            _poiIndicatorArrow.Target = transformer.TrackingState == Vps2TrackingState.Coarse
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
        _refinedMarker.SetActive(true);
        _didDownloadMesh = true;
    }
    
    private void ToggleAnchorMarker(Vps2TrackingState trackingState)
    {
        // The textured mesh should only appear when we have precise tracking,
        // otherwise we render the default anchor visualization
        if (_refinedMarker != null)
            _refinedMarker.gameObject.SetActive(trackingState == Vps2TrackingState.Precise);
        if (_coarseMarker != null)
            _coarseMarker.gameObject.SetActive(trackingState == Vps2TrackingState.Coarse ||
                                               (trackingState == Vps2TrackingState.Precise && _refinedMarker == null));
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