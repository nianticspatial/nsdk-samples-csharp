// Copyright 2022-2025 Niantic.
using System;
using System.IO;
using NianticSpatial.NSDK.AR.Mapping;
using UnityEngine;

/// <summary>
/// This class manages creating local maps that are stored to a file on the device
/// </summary>
public class Mapper : MonoBehaviour
{
    [SerializeField]
    private ARDeviceMappingManager _deviceMappingManager;

    public Action<byte[]> DeviceMapUpdated;

    /// <summary>
    /// The root anchor payload string, available after the first map update during mapping.
    /// </summary>
    public string RootAnchorPayload => _deviceMappingManager.RootAnchorPayload;
    
    private bool _mappingInProgress;

    private void OnEnable()
    {
        _deviceMappingManager.MapUpdated += HandleMapUpdated;
    }
    
    
    private void OnDisable()
    {
        _deviceMappingManager.MapUpdated -= HandleMapUpdated;
    }
    
    public void StartMapping()
    {
        _mappingInProgress = true;
        _deviceMappingManager.enabled = true;
        _deviceMappingManager.StartMapping();
    }

    public void StopMapping()
    {
        if (_mappingInProgress)
        {
            _deviceMappingManager.StopMapping();
            _deviceMappingManager.enabled = false;
            _mappingInProgress = false;
        }
    }
    
    public void Reset()
    {
        StopMapping();
        _deviceMappingManager.ClearMap();
    }

    public void SaveMap(string path)
    {
        if (_deviceMappingManager.TryGetMapData(out var mapData))
        {
            File.WriteAllBytes(path, mapData);
            Debug.Log($"Saved map ({mapData.Length} to: {path}");
        }
    }
    
    /// <summary>
    /// Extract map metadata (feature points) relative to the stored root anchor.
    /// </summary>
    public bool TryGetMapPointMatrixes(byte[] mapData, out Matrix4x4[] matrices)
    {
        var hasMetadata = _deviceMappingManager.ExtractMapMetadataFromRootAnchor(mapData, out var points, out _);
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
    
    private void HandleMapUpdated(byte[] mapData)
    {
        DeviceMapUpdated?.Invoke(mapData);
    }
}
