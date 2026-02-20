// Copyright 2022-2025 Niantic.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NianticSpatial.NSDK.AR.Meshing;
using NianticSpatial.NSDK.AR.Subsystems.Semantics;
using UnityEngine.UI;

public class MeshingFiltering : MonoBehaviour
{
    [SerializeField]
    private NsdkMeshingExtension _meshingExtension;


    [SerializeField]
    private Toggle _disableMeshFiltering;
    [SerializeField]
    private Toggle _enableAllowList;
    [SerializeField]
    private Toggle _enableBlockList;

    // Start is called before the first frame update
    void Start()
    {
        _meshingExtension.AllowList = new List<SemanticsChannel>() { SemanticsChannel.Ground };
        _meshingExtension.BlockList = new List<SemanticsChannel>() { SemanticsChannel.Sky, SemanticsChannel.Person, SemanticsChannel.Water, SemanticsChannel.PetExperimental, SemanticsChannel.LoungeableExperimental };
        DisableMeshFiltering();
    }

    // Update is called once per frame
    public void DisableMeshFiltering()
    {
        _meshingExtension.IsMeshFilteringEnabled = false;
        _meshingExtension.IsFilteringAllowListEnabled = false;
        _meshingExtension.IsFilteringBlockListEnabled = false;
    }

    public void ConfigureMeshFiltering()
    {
        _meshingExtension.IsMeshFilteringEnabled = !_disableMeshFiltering.isOn;
        _meshingExtension.IsFilteringAllowListEnabled = _enableAllowList.isOn;
        _meshingExtension.IsFilteringBlockListEnabled = _enableBlockList.isOn;
    }
}
