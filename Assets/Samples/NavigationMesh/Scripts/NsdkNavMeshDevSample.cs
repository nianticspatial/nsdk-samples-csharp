// Copyright 2022-2025 Niantic.
using System.Collections;
using System.Collections.Generic;
using NianticSpatial.NSDK.AR.NavigationMesh;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// This sample shows how to quickly used Niantic's NavMesh to add user driven point and click navigation
/// when you first touch the screen it will place your agent prefab
/// then if you tap again the agent will walk to that location
/// there is a toggle button to show hide the navigation mesh and path.
/// It assumes the _agentPrefab has LightshipNavMeshAgent on it.
/// You can overload it if you want to.
/// </summary>
public class NsdkNavMeshDevSample : MonoBehaviour
{
    [SerializeField]
    private Camera _camera;

    [FormerlySerializedAs("_gameboardManager")]
    [SerializeField]
    private NsdkNavMeshManager _navMeshManager;

    [FormerlySerializedAs("_agentPrefab")]
    [SerializeField]
    private GameObject agentPrefab;

    [FormerlySerializedAs("_Visualization")]
    [SerializeField]
    private GameObject visualization;

    private GameObject _creature;
    private NsdkNavMeshAgent _agent;

    private PlayerInput _nsdkInput;
    private InputAction _primaryTouch;

    private void Awake()
    {
        //Get the input actions.
        _nsdkInput = GetComponent<PlayerInput>();
        _primaryTouch = _nsdkInput.actions["Point"];
    }

    void Update()
    {
        HandleTouch();
    }

    public void ToggleVisualisation()
    {
        if (_creature != null)
        {
            //turn off the rendering for the nav mesh
            _navMeshManager.GetComponent<NsdkNavMeshRenderer>().enabled =
                !_navMeshManager.GetComponent<NsdkNavMeshRenderer>().enabled;

            //turn off the path rendering on any agent
            _agent.GetComponent<NsdkNavMeshAgentPathRenderer>().enabled =
                !_agent.GetComponent<NsdkNavMeshAgentPathRenderer>().enabled;
        }
    }

    private void HandleTouch()
    {
        //Get the primaryTouch from our input actions.
        if (!_primaryTouch.WasPerformedThisFrame())
            return;
        else
        {
            //project the touch point from screen space into 3d and pass that to your agent as a destination
            Ray ray = _camera.ScreenPointToRay(_primaryTouch.ReadValue<Vector2>());
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit))
            {
                if (_navMeshManager.NsdkNavMesh.IsOnNavMesh(hit.point, 0.2f))
                {
                    if (!UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                    {
                        if (_creature == null)
                        {

                            //TODO: Add the is there enough space to place.
                            //have a nice fits/dont fit in the space.

                            _creature = Instantiate(agentPrefab);
                            _creature.transform.position = hit.point;
                            _agent = _creature.GetComponent<NsdkNavMeshAgent>();
                            visualization.SetActive(true);

                        }
                        else
                        {
                            _agent.SetDestination(hit.point);
                        }
                    }
                }
            }
        }
    }

}
