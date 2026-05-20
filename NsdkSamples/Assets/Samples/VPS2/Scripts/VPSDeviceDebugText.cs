// Copyright 2022-2025 Niantic.
using NianticSpatial.NSDK.AR.VPS2;
using NianticSpatial.NSDK.AR.Subsystems;

using Unity.XR.CoreUtils;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class VPSDeviceDebugText : MonoBehaviour
{
    [SerializeField]
    private XROrigin _xrOrigin;

    [SerializeField]
    private ARVps2Manager _vps2Manager;

    [SerializeField]
    private Text _trackingStateText;

    [SerializeField]
    private Text _devicePoseText;

    [SerializeField]
    private Text _anchorPoseText;

    private ARVps2Anchor _arPersistentAnchor;

    private void OnEnable()
    {
        _vps2Manager.trackablesChanged.AddListener(HandleTrackablesChanged);
    }
    
    private void OnDisable()
    {
        _vps2Manager.trackablesChanged.RemoveListener(HandleTrackablesChanged);
    }

    // Start is called before the first frame update
    void Start()
    {
        _trackingStateText.text = "Waiting for AR Session state";
        if (_vps2Manager == null)
        {
            _vps2Manager = FindFirstObjectByType<ARVps2Manager>();
            if (_vps2Manager == null)
            {
                _anchorPoseText.text = "Could not find ARVps2Manager";
            }
            else
            {
                _anchorPoseText.text = "Waiting for anchor creation";
            }
        }

        if (_xrOrigin == null)
        {
            _xrOrigin = FindFirstObjectByType<XROrigin>();
            if (_xrOrigin == null)
            {
                if (_xrOrigin == null)
                {
                    _devicePoseText.text = "Could not find XROrigin";
                }
                else
                {
                    _devicePoseText.text = "Waiting for device pose";
                }
            }
        }
    }

    void Update()
    {
        // Update pose every 4 frames
        if (Time.frameCount % 4 == 0)
        {
            if (_xrOrigin && _xrOrigin.Camera && _devicePoseText)
            {
                _devicePoseText.text =
                    $"Camera position is {_xrOrigin.Camera.gameObject.transform.position}";
            }

            if (_arPersistentAnchor && _anchorPoseText)
            {
                _anchorPoseText.text =
                    $"Anchor position is {_arPersistentAnchor.transform.position} " +
                    $"with rotation {_arPersistentAnchor.transform.rotation}";
            }
        }
    }

    private void HandleTrackablesChanged(ARTrackablesChangedEventArgs<ARVps2Anchor> args)
    {
        if (args.updated.Count == 0) return;
        
        var anchor = args.updated[0];
        if (anchor.trackingState == TrackingState.Tracking)
        {
            _arPersistentAnchor = anchor;
            var stateText = $"Tracking state is {anchor.trackingState:G} with reason {anchor.trackingStateReason:G}";
            _trackingStateText.text = stateText;
        }
    }
}
