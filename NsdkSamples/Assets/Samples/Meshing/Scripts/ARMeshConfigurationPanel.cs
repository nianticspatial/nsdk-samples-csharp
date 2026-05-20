// Copyright 2022-2025 Niantic.

using NianticSpatial.NSDK.AR.Meshing;
using UnityEngine;
using UnityEngine.UI;

public class ARMeshConfigurationPanel : MonoBehaviour
{

    [SerializeField] private GameObject _settingsPanel;

    private NsdkMeshingExtension _nsdkMeshingExtension;
    private InputField _frameRateValue;
    private InputField _integrationDistanceValue;
    private InputField _voxelSize;
    private Toggle _enableDistanceBasedVolumetricCleanup;
    private InputField _blockSize;
    private InputField _cullingDistance;
    private Toggle _enableMeshDecimation;

    public void TogglePanel()
    {
        bool isActive = _settingsPanel.activeSelf;
        _settingsPanel.SetActive(!isActive);
    }

    public void Start()
    {
        _nsdkMeshingExtension = FindFirstObjectByType<NsdkMeshingExtension>();

        _frameRateValue = GameObject.Find("Frame Rate Value").GetComponent<InputField>();
        _frameRateValue.text = _nsdkMeshingExtension.TargetFrameRate.ToString();

        _integrationDistanceValue = GameObject.Find("Integration Distance Value").GetComponent<InputField>();
        _integrationDistanceValue.text = _nsdkMeshingExtension.MaximumIntegrationDistance.ToString();

        _voxelSize = GameObject.Find("Voxel Size Value").GetComponent<InputField>();
        _voxelSize.text = _nsdkMeshingExtension.VoxelSize.ToString();

        _enableDistanceBasedVolumetricCleanup = GameObject.Find("Distance Based Volumetric Cleanup Config").GetComponent<SliderToggle>();
        _enableDistanceBasedVolumetricCleanup.isOn = _nsdkMeshingExtension.EnableDistanceBasedVolumetricCleanup;

        _blockSize = GameObject.Find("Block Size Value").GetComponent<InputField>();
        _blockSize.text = _nsdkMeshingExtension.MeshBlockSize.ToString();

        _cullingDistance = GameObject.Find("Culling Distance Value").GetComponent<InputField>();
        _cullingDistance.text = _nsdkMeshingExtension.MeshCullingDistance.ToString();

        _enableMeshDecimation = GameObject.Find("Mesh Decimation Config").GetComponent<SliderToggle>();
        _enableMeshDecimation.isOn = _nsdkMeshingExtension.EnableMeshDecimation;
    }

    public void Configure()
    {
        _nsdkMeshingExtension.TargetFrameRate = int.Parse(_frameRateValue.text);
        _nsdkMeshingExtension.MaximumIntegrationDistance = float.Parse(_integrationDistanceValue.text);
        _nsdkMeshingExtension.VoxelSize = float.Parse(_voxelSize.text);
        _nsdkMeshingExtension.EnableDistanceBasedVolumetricCleanup = _enableDistanceBasedVolumetricCleanup.isOn;
        _nsdkMeshingExtension.MeshBlockSize = float.Parse(_blockSize.text);
        _nsdkMeshingExtension.MeshCullingDistance = float.Parse(_cullingDistance.text);
        _nsdkMeshingExtension.EnableMeshDecimation = _enableMeshDecimation.isOn;
        _nsdkMeshingExtension.Configure();
    }
}
