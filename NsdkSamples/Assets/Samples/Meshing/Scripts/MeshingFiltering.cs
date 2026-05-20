// Copyright 2022-2025 Niantic.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NianticSpatial.NSDK.AR.Meshing;
using NianticSpatial.NSDK.AR.Subsystems.SceneSegmentation;
using UnityEngine.UI;

public class MeshingFiltering : MonoBehaviour
{
    [SerializeField]
    private NsdkMeshingExtension _meshingExtension;

    [SerializeField]
    private Toggle _disableMeshFiltering;
    [SerializeField]
    private Toggle _enableAllowList;

    // Start is called before the first frame update
    void Start()
    {
        _meshingExtension.AllowList = new List<SceneSegmentationChannel>() { SceneSegmentationChannel.Ground };
        DisableMeshFiltering();
    }

    // Update is called once per frame
    public void DisableMeshFiltering()
    {
        _meshingExtension.IsMeshFilteringEnabled = false;
        _meshingExtension.IsFilteringAllowListEnabled = false;
    }

    public void ConfigureMeshFiltering()
    {
        _meshingExtension.IsMeshFilteringEnabled = !_disableMeshFiltering.isOn;
        _meshingExtension.IsFilteringAllowListEnabled = _enableAllowList.isOn;
    }
}
