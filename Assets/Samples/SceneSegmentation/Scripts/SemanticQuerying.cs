// Copyright 2022-2025 Niantic.
using System.Linq;
using NianticSpatial.NSDK.AR.Semantics;
using NianticSpatial.NSDK.AR.Subsystems.Semantics;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using Utilities;

public class SemanticQuerying : MonoBehaviour
{
    public ARCameraManager _cameraMan;
    public ARSemanticSegmentationManager _semanticMan;

    public RawImage _image;
    public Material _material;

    private SemanticsChannel _semanticChannel = SemanticsChannel.Ground;

    [SerializeField]
    private Dropdown _channelDropdown;

    void OnEnable()
    {
        _cameraMan.frameReceived += OnCameraFrameUpdate;
        _channelDropdown.onValueChanged.AddListener(ChannelDropdown_OnValueChanged);

        _semanticMan.MetadataInitialized += SemanticsManager_OnDataInitialized;

    }

    private void OnDisable()
    {
        _cameraMan.frameReceived -= OnCameraFrameUpdate;
    }

    private void OnCameraFrameUpdate(ARCameraFrameEventArgs args)
    {
        if (!_semanticMan.subsystem.running)
        {
            return;
        }

        //get the semantic texture
        Matrix4x4 mat = Matrix4x4.identity;
        var texture = _semanticMan.GetSemanticChannelTexture(_semanticChannel, out mat);

        if (texture)
        {
            //the texture needs to be aligned to the screen so get the display matrix
            //and use a shader that will rotate/scale things.
            Matrix4x4 cameraMatrix = args.displayMatrix ?? Matrix4x4.identity;
            _image.material = _material;
            _image.material.SetTexture("_SemanticTex", texture);
            _image.material.SetMatrix("_DisplayMatrix", mat);
        }
    }

    private void SemanticsManager_OnDataInitialized(ARSemanticSegmentationModelEventArgs args)
    {
        // Initialize the channel names in the dropdown menu.
        var channels = _semanticMan.Channels;
        // Convert channels to strings for UI display
        var channelNames = channels.Select(ARSemanticSegmentationManager.GetChannelNameFromEnum).ToList();
        _channelDropdown.AddOptions(channelNames);

        // Display artificial ground by default.
        if (channels.Count > 3)
        {
            _semanticChannel = channels[3];
        }
        var channelName = channelNames[3];
        var dropdownList = _channelDropdown.options.Select(option => option.text).ToList();
        _channelDropdown.value = dropdownList.IndexOf(channelName);
    }
    private void ChannelDropdown_OnValueChanged(int val)
    {
        // Update the display channel from the dropdown value.
        var channelName = _channelDropdown.options[val].text;
        var channel = _semanticMan.GetChannelFromName(channelName);
        if (channel.HasValue)
        {
            _semanticChannel = channel.Value;
        }
    }
}
