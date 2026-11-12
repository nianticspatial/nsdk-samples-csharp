// Copyright Niantic Spatial

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

public class SceneSwitcher : MonoBehaviour
{
    private const string DEFAULT_TARGET_SCENE = "Home";

    [SerializeField] private bool _waitForARSessionReady = true;
    [SerializeField] private float _arSessionTimeout = 10f;
    [SerializeField] private bool _waitForCameraFrames = true;
    private string _targetScene = DEFAULT_TARGET_SCENE;

    private bool _isARSessionReady = false;
    private bool _hasReceivedCameraFrame = false;


    public string TargetScene
    {
        get => _targetScene;
        set => _targetScene = value;
    }

    private void Start()
    {
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

    /// <summary>Runs before the scene loads; the switch goes ahead when it returns true.</summary>
    public Func<Task<bool>> BeforeSwitchAsync { get; set; }

    public async void SwitchToScene()
    {
        // If ARSession is not ready, don't allow scene switch.
        if (_waitForARSessionReady && !_isARSessionReady)
        {
            return;
        }

        if (BeforeSwitchAsync != null)
        {
            bool proceed;
            try
            {
                proceed = await BeforeSwitchAsync();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }

            if (!proceed || this == null)
            {
                return;
            }
        }

        Screen.orientation = ScreenOrientation.AutoRotation;
        SceneManager.LoadScene(TargetScene, LoadSceneMode.Single);
    }
}
