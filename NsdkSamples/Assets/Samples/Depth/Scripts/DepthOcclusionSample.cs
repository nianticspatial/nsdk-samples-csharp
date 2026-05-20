// Copyright 2022-2025 Niantic.

using NianticSpatial.NSDK.AR.Occlusion;
using NianticSpatial.NSDK.AR.SceneSegmentation;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

public class DepthOcclusionSample : MonoBehaviour
{
    [SerializeField] private AROcclusionManager _occlusionManager;
    [SerializeField] private ARSceneSegmentationManager _segmentationManager;
    [SerializeField] private NsdkOcclusionExtension _occlusionExtension;
    [SerializeField] private SliderToggle _suppressionToggle;
    [SerializeField] private SliderToggle _stabilizationToggle;
    [SerializeField] private Text _loadingText;

    private bool _occlusionReady;
    private bool _sceneSegmentationReady;

    private void OnEnable()
    {
        _suppressionToggle.interactable = false;
        _stabilizationToggle.interactable = false;

        _occlusionManager.frameReceived += OnOcclusionReady;
        _segmentationManager.MetadataInitialized += OnSceneSegmentationReady;
    }

    private void OnDisable()
    {
        _suppressionToggle.onValueChanged.RemoveListener(ToggleSuppression);
        _stabilizationToggle.onValueChanged.RemoveListener(ToggleStabilization);

        if (!_occlusionReady)
        {
            _occlusionManager.frameReceived -= OnOcclusionReady;
        }

        if (!_sceneSegmentationReady)
        {
            _segmentationManager.MetadataInitialized -= OnSceneSegmentationReady;
        }
    }

    private void OnOcclusionReady(AROcclusionFrameEventArgs frameEventArgs)
    {
        _occlusionReady = true;
        _occlusionManager.frameReceived -= OnOcclusionReady;

        TryActivateUI();
    }

    private void OnSceneSegmentationReady(ARSceneSegmentationModelEventArgs modelEventArgs)
    {
        _sceneSegmentationReady = true;
        _segmentationManager.MetadataInitialized -= OnSceneSegmentationReady;

        TryActivateUI();
    }

    private void TryActivateUI()
    {
        if (_occlusionReady && _sceneSegmentationReady)
        {
            _suppressionToggle.onValueChanged.AddListener(ToggleSuppression);
            _stabilizationToggle.onValueChanged.AddListener(ToggleStabilization);

            _suppressionToggle.interactable = true;
            _stabilizationToggle.interactable = true;

            _loadingText.gameObject.SetActive(false);
        }
    }

    private void ToggleSuppression(bool on)
    {
        _occlusionExtension.IsOcclusionSuppressionEnabled = on;
    }

    private void ToggleStabilization(bool on)
    {
        _occlusionExtension.IsOcclusionStabilizationEnabled = on;
    }
}
