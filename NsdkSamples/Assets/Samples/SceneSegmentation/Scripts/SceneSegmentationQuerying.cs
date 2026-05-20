// Copyright 2022-2025 Niantic.
using System.Linq;
using NianticSpatial.NSDK.AR.SceneSegmentation;
using NianticSpatial.NSDK.AR.Subsystems.SceneSegmentation;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using Utilities;

public class SceneSegmentationQuerying : MonoBehaviour
{
    public ARCameraManager _cameraMan;
    [FormerlySerializedAs("_semanticMan")]
    public ARSceneSegmentationManager _sceneSegmentationMan;

    public RawImage _image;
    public Material _material;

    private SceneSegmentationChannel _sceneSegmentationChannel = SceneSegmentationChannel.Ground;

    [SerializeField]
    private Dropdown _channelDropdown;

    void OnEnable()
    {
        _cameraMan.frameReceived += OnCameraFrameUpdate;
        _channelDropdown.onValueChanged.AddListener(ChannelDropdown_OnValueChanged);

        _sceneSegmentationMan.MetadataInitialized += SceneSegmentationManager_OnDataInitialized;

    }

    private void OnDisable()
    {
        _cameraMan.frameReceived -= OnCameraFrameUpdate;
    }

    private void OnCameraFrameUpdate(ARCameraFrameEventArgs args)
    {
        if (!_sceneSegmentationMan.subsystem.running)
        {
            return;
        }

        //get the scene segmentation texture
        Matrix4x4 mat = Matrix4x4.identity;
        var texture = _sceneSegmentationMan.GetSceneSegmentationChannelTexture(_sceneSegmentationChannel, out mat);

        if (texture)
        {
            //the texture needs to be aligned to the screen so get the display matrix
            //and use a shader that will rotate/scale things.
            Matrix4x4 cameraMatrix = args.displayMatrix ?? Matrix4x4.identity;
            _image.material = _material;
            _image.material.SetTexture("_SceneSegmentationTex", texture);
            _image.material.SetMatrix("_DisplayMatrix", mat);
        }
    }

    private void SceneSegmentationManager_OnDataInitialized(ARSceneSegmentationModelEventArgs args)
    {
        // Initialize the channel names in the dropdown menu.
        var channels = _sceneSegmentationMan.Channels;
        // Convert channels to strings for UI display
        var channelNames = channels.Select(ARSceneSegmentationManager.GetChannelNameFromEnum).ToList();
        _channelDropdown.AddOptions(channelNames);

        // Display artificial ground by default.
        if (channels.Count > 3)
        {
            _sceneSegmentationChannel = channels[3];
        }
        var channelName = channelNames[3];
        var dropdownList = _channelDropdown.options.Select(option => option.text).ToList();
        _channelDropdown.value = dropdownList.IndexOf(channelName);
    }
    private void ChannelDropdown_OnValueChanged(int val)
    {
        // Update the display channel from the dropdown value.
        var channelName = _channelDropdown.options[val].text;
        var channel = _sceneSegmentationMan.GetChannelFromName(channelName);
        if (channel.HasValue)
        {
            _sceneSegmentationChannel = channel.Value;
        }
    }
}
