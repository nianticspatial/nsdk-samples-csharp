// Copyright 2022-2026 Niantic.

using UnityEngine;

public class ArrowIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] 
    private Transform _target;
    
    [SerializeField] 
    private Camera _cam;
    
    [Header("Arrow Placement")]
    [SerializeField] 
    private float _distanceInFront = 2f;
    
    [SerializeField] 
    private float _verticalOffset = -0.3f;
    
    [Header("Visibility")]
    [SerializeField] 
    private float _hideDistance = 3f;

    // Cached renderers to toggle visibility
    private Renderer[] _renderers;
    private bool _isVisible = true;

    private Renderer[] Renderers
    {
        get
        {
            _renderers ??= GetComponentsInChildren<Renderer>(includeInactive: true);
            return _renderers;
        }
    }

    /// <summary>
    /// The target transform the arrow points to.
    /// </summary>
    public Transform Target
    {
        get => _target;
        set => _target = value;
    }

    private void Start()
    {
        SetVisible(false);
    }

    private void LateUpdate()
    {
        if (_cam == null || _target == null)
        {
            SetVisible(false);
            return;
        }

        // Position: in front of camera, but parallel to ground (flatten forward on XZ)
        var flatForward = _cam.transform.forward;
        flatForward.y = 0f;

        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;

        flatForward.Normalize();

        var desiredPos = _cam.transform.position + flatForward * _distanceInFront;
        desiredPos.y = _cam.transform.position.y + _verticalOffset;

        transform.position = desiredPos;

        // Rotation: point toward target, staying level (flatten direction on XZ)
        var toTarget = _target.position - transform.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);

        // Visibility: hide if close AND camera is facing target
        var camToTargetDist = Vector3.Distance(_cam.transform.position, _target.position);
        var facing = IsTargetInFrontOfCameraGround(_target.position, _cam, 45f);
        var shouldHide = camToTargetDist <= _hideDistance && facing;

        SetVisible(!shouldHide);
    }

    private static bool IsTargetInFrontOfCameraGround(Vector3 worldPos, Camera camera, float maxAngleDeg)
    {
        var camForward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
        var toTarget = Vector3.ProjectOnPlane(worldPos - camera.transform.position, Vector3.up).normalized;
        return Vector3.Angle(camForward, toTarget) < maxAngleDeg;
    }

    private void SetVisible(bool visible)
    {
        if (_isVisible == visible) 
            return;
        
        _isVisible = visible;

        var renderers = Renderers;
        foreach (var rend in renderers)
        {
            if (rend == null) 
                continue;
            
            rend.enabled = visible;
        }
    }
}