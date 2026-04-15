// Copyright 2026 Niantic Spatial.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using NianticSpatial.NSDK.AR.VPS2;
using NianticSpatial.NSDK.AR.Mapping;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// This class handles localising to a map
/// It loads the anchor from a file on device
/// An has helpers functions for placing things relative to the maps anchor
/// </summary>
public class Tracker : MonoBehaviour
{
    [SerializeField]
    private ARVps2Manager _vps2Manager;
    
    
    // Invoked when root anchor of device map first localizes
    public Action Localized;
    
    //adding a handle in case we try to add items to the scene before localiseation is finished
    //this could happen while using the data store.
    GameObject _tempAnchor;

    private ARVps2Anchor _rootAnchor;
    private DeviceMapAccessController _deviceMapAccessController;

    /// <summary>
    /// Whether the tracker is currently localized and tracking.
    /// </summary>
    public bool IsLocalized { get; private set; }
    
    public Transform RootAnchorTransform => _rootAnchor ? _rootAnchor.transform : null;

    private void Awake()
    {
        _deviceMapAccessController = DeviceMapAccessController.Acquire();
    }

    private void OnDestroy()
    {
        _deviceMapAccessController.Release();
        _deviceMapAccessController = null;
    }

    public void StartTracking()
    {
        if (_deviceMapAccessController.CreateRootAnchor(out var anchorPayload))
        {
            Debug.Log("[Tracker] CreateRootAnchor succeeded, starting tracking with payload");
            StartTrackingWithPayload(anchorPayload);
        }
        else
        {
            Debug.LogWarning("[Tracker] CreateRootAnchor failed — no anchor to track");
        }
    }
    
    public void StopTracking()
    {
        if (_rootAnchor)
        {
            _vps2Manager.RemoveAnchor(_rootAnchor);
            _rootAnchor = null;
        }

        IsLocalized = false;
        _vps2Manager.enabled = false;
        _vps2Manager.trackablesChanged.RemoveListener(OnAnchorsChanged);
    }

    private void OnAnchorsChanged(ARTrackablesChangedEventArgs<ARVps2Anchor> args)
    {
        if (IsLocalized) return;

        if (_rootAnchor == null)
        {
            Debug.LogWarning("[Tracker] OnAnchorsChanged fired but _rootAnchor is null");
            return;
        }

        // Anchors with Tracking state will only appear in the Updated list
        var updatedRootAnchor = args.updated.FirstOrDefault(anchor => anchor.trackableId == _rootAnchor.trackableId);

        if (updatedRootAnchor?.trackingState == TrackingState.Tracking)
        {
            Debug.Log("[Tracker] Localized!");
            IsLocalized = true;
            Localized?.Invoke();
        }
    }

    public void Reset()
    {
        StopTracking();
        _deviceMapAccessController.ClearDeviceMaps();
    }

    public void LoadMap(string mapFilePath)
    {
        Debug.Log($"[Tracker] LoadMap: {mapFilePath}");
        var deviceMap = File.ReadAllBytes(mapFilePath);
        _deviceMapAccessController.AddMap(deviceMap);
    }
    
    /// <summary>
    /// Start tracking using an anchor payload directly (e.g. from a live mapping session).
    /// The map data is expected to already be in the mapping manager's storage.
    /// </summary>
    public void StartTrackingWithPayload(string anchorPayload)
    {
        _vps2Manager.enabled = true;
        _vps2Manager.DeviceMapLocalizationEnabled = true;
        _vps2Manager.trackablesChanged.AddListener(OnAnchorsChanged);

        var success = _vps2Manager.TryTrackAnchor(anchorPayload, out _rootAnchor);
        Debug.Log($"[Tracker] StartTrackingWithPayload: TryTrackAnchor={success}, rootAnchor={(_rootAnchor != null ? _rootAnchor.trackableId.ToString() : "null")}");
    }

    /// <summary>
    /// Convert a world position to an anchor relative position.
    /// </summary>
    /// <param name="pos"></param>
    public Vector3 GetAnchorRelativePosition(Vector3 pos)
    {
        return _rootAnchor.transform.InverseTransformPoint(pos);
    }

    /// <summary>
    /// Parent the game object under the anchor.
    /// </summary>
    /// <param name="go"></param>
    public void AddObjectToAnchor(GameObject go)
    {
        go.transform.SetParent(RootAnchorTransform.transform);
    }
    
    /// <summary>
    /// Extract map metadata (feature points) relative to the stored root anchor.
    /// </summary>
    public bool TryGetMapPointMatrixes(out Matrix4x4[] matrices)
    {
        if (!_vps2Manager.TryGetAnchorPayload(_rootAnchor, out string payload))
        {
            matrices = Array.Empty<Matrix4x4>();
            return false;
        }
        
        _deviceMapAccessController.GetMapData(out var mapData);
        var hasMetadata = _deviceMapAccessController.ExtractMapMetadataFromAnchor(payload, mapData, out var points, out _);
        if (!hasMetadata || points == null || points.Length == 0)
        {
            matrices = null;
            return false;
        }

        matrices = new Matrix4x4[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            matrices[i] = Matrix4x4.TRS(points[i], Quaternion.identity, Vector3.one * 0.02f);
        }
        return true;
    }
}
