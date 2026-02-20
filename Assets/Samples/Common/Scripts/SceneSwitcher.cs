// Copyright 2022-2025 Niantic.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class SceneSwitcher : MonoBehaviour, ISerializationCallbackReceiver
{
    [SerializeField]
    private Object _targetScene;

    [SerializeField] [HideInInspector]
    private string _targetSceneName;

    [SerializeField] private bool _waitForARSessionReady = true;
    [SerializeField] private float _arSessionTimeout = 10f;
    [SerializeField] private bool _waitForCameraFrames = true;

    private HashSet<string> PortraitScenes = new HashSet<string>() {"Home","RemoteAuthoring","CompassScene" };
    private bool _isARSessionReady = false;
    private bool _hasReceivedCameraFrame = false;

    private void Start()
    {
        //in-case no button
        if (TryGetComponent<Button>(out Button button))
        {
            //check if it has a listener, if then hook it up.
            int listenerCount = button.onClick.GetPersistentEventCount();
            if (listenerCount == 0)
            {
                button.onClick.AddListener(SwitchToScene);
            }
        }

        if (_waitForARSessionReady)
        {
            StartCoroutine(WaitForARSessionReady());
        }
        else
        {
            _isARSessionReady = true;
        }
    }
    // Wait for ARSession to be ready before exiting AR scene to prevent race condition on ARCore XR Plugin.
    private IEnumerator WaitForARSessionReady()
    {
        _isARSessionReady = false;
        _hasReceivedCameraFrame = false;
        
        ARSession arSession = FindFirstObjectByType<ARSession>();
        ARCameraManager cameraManager = FindFirstObjectByType<ARCameraManager>();
        
        if (arSession == null)
        {
            Debug.LogWarning("SceneSwitcher: No ARSession found in scene. Allowing scene switching without AR check.");
            _isARSessionReady = true;
            _hasReceivedCameraFrame = true;
            yield break;
        }
        
        // Subscribe to state changes to know if the ARSession is ready.
        ARSession.stateChanged += OnARSessionStateChanged;
        
        // Subscribe to camera frame events to know if the camera has started sending frames.
        if (_waitForCameraFrames && cameraManager != null)
        {
            cameraManager.frameReceived += OnCameraFrameReceived;
        }else
        {
            _hasReceivedCameraFrame = true;
        }
        
        bool sessionReady = IsARSessionStateReady(ARSession.state);
        bool cameraReady = _hasReceivedCameraFrame || !_waitForCameraFrames;
        
        if (sessionReady && cameraReady)
        {
            _isARSessionReady = true;
            ARSession.stateChanged -= OnARSessionStateChanged;
            if (cameraManager != null)
            {
                cameraManager.frameReceived -= OnCameraFrameReceived;
            }
            yield break;
        }
        
        // Wait and timeout if ARSession is not ready.
        float startTime = Time.time;
        float elapsedTime = 0f;
        while ((!sessionReady || !cameraReady) && elapsedTime < _arSessionTimeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsedTime = Time.time - startTime;
            
            sessionReady = IsARSessionStateReady(ARSession.state);
            cameraReady = _hasReceivedCameraFrame || !_waitForCameraFrames;
        }
        
        ARSession.stateChanged -= OnARSessionStateChanged;
        if (cameraManager != null)
        {
            cameraManager.frameReceived -= OnCameraFrameReceived;
        }
        
        if (!sessionReady || !cameraReady)
        {
            Debug.LogWarning($"SceneSwitcher: ARSession timeout after {_arSessionTimeout} seconds. Allowing scene switching anyway.");
        }
        _isARSessionReady = true;
    }
    
    private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
    {
        if (args.textures != null && args.textures.Count > 0)
        {
            _hasReceivedCameraFrame = true;
        }
    }
    
    private void OnARSessionStateChanged(ARSessionStateChangedEventArgs args)
    {
        _isARSessionReady = IsARSessionStateReady(args.state);
    }
    
    private bool IsARSessionStateReady(ARSessionState state)
    {
        // ARSession is considered ready when it's at least initializing or tracking.
        return state == ARSessionState.SessionInitializing || 
               state == ARSessionState.SessionTracking ||
               state == ARSessionState.Ready;
    }

    public void SwitchToScene()
    {
        // If ARSession is not ready, don't allow scene switch.
        if (_waitForARSessionReady && !_isARSessionReady)
        {
            return;
        }
        
        OrientationPicker();
        SceneManager.LoadScene(_targetSceneName, LoadSceneMode.Single);
    }

    public void OnBeforeSerialize()
    {
#if UNITY_EDITOR
        if (_targetScene != null && _targetScene is SceneAsset sceneAsset)
        {
            _targetSceneName = sceneAsset.name;
        }
#endif
    }

    public void OnAfterDeserialize()
    {
    }
    private void OrientationPicker()
    {
        if (PortraitScenes.Contains(_targetSceneName))
        {
            Screen.orientation = ScreenOrientation.Portrait;
        }
        else
        {
            Screen.orientation = ScreenOrientation.AutoRotation;
        }
    }
}
