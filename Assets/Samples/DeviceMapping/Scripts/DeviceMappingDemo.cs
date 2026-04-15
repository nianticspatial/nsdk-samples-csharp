// Copyright 2026 Niantic Spatial.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

public class DeviceMappingDemo : MonoBehaviour
{
    [Header("Managers")]
    [SerializeField]
    private Mapper _mapper;

    [SerializeField]
    private Tracker _tracker;

    [Header("UX - Status")]
    [SerializeField]
    private Text _statusText;

    [Header("Map Visualization")]
    [SerializeField]
    private Mesh _pointMesh;

    [SerializeField]
    private Material _pointMaterial;

    [Header("UX - Create/Load")]
    [SerializeField]
    private GameObject _selectionPanel;

    [SerializeField]
    private Button _createMapButton;

    [SerializeField]
    private Button _loadMapButton;

    [Header("UX - Scan Map")]
    [SerializeField]
    private GameObject _scanningPanel;

    [SerializeField]
    private Button _saveMapButton;

    [SerializeField]
    private Button _toCubesMenuButton;

    [SerializeField]
    private Button _exitScanMapButton;

    [Header("UX - Scanning Animation")]
    [SerializeField]
    private GameObject _scanningAnimationPanel;

    [Header("UX - In Game")]
    [SerializeField]
    private GameObject _inGamePanel;

    [SerializeField]
    private Button _placeCubeButton;

    [SerializeField]
    private Button _deleteCubesButton;

    [SerializeField]
    private Button _exitInGameButton;

    [Header("UX - Map List")]
    [SerializeField]
    private GameObject _mapListPanel;

    [SerializeField]
    private ScrollRect _mapListScrollRect;

    [SerializeField]
    private GameObject _mapListItemPrefab;

    [SerializeField]
    private Button _mapListBackButton;

    // Storage constants
    private const string k_mapsRootDir = "DeviceMaps";
    private const string k_mapDataFile = "map.bin";
    private const string k_objectsDataFile = "objects.txt";

    // Currently selected map directory (full path)
    private string _selectedMapDir;

    // Map visualization state
    private const int MaxRenderBatch = 1023;
    private List<Matrix4x4[]> _renderedPointSets = new List<Matrix4x4[]>();
    private bool _hasStartedAutoLocalize = false;

    // Path helpers
    private string MapsRootPath => Path.Combine(Application.persistentDataPath, k_mapsRootDir);

    private void Start()
    {
        SwitchToSelectorView();
    }

    private void OnEnable()
    {
        _mapper.DeviceMapUpdated += HandleMapUpdated;
        _tracker.Localized += OnLocalized;

        // Mode menu buttons
        _createMapButton.onClick.AddListener(SwitchToScanningView);
        _loadMapButton.onClick.AddListener(ShowMapList);

        // Scanning menu buttons
        _saveMapButton.onClick.AddListener(SaveMap);
        _exitScanMapButton.onClick.AddListener(ExitToSelectorState);
        _toCubesMenuButton.onClick.AddListener(SwitchToCubesView);

        // Cube menu buttons
        _placeCubeButton.onClick.AddListener(PlaceCube);
        _deleteCubesButton.onClick.AddListener(DeleteCubes);
        _exitInGameButton.onClick.AddListener(ExitToSelectorState);

        // Map list buttons
        _mapListBackButton.onClick.AddListener(ExitToSelectorState);
    }

    private void OnDisable()
    {
        _tracker.Localized -= OnLocalized;
        _mapper.DeviceMapUpdated -= HandleMapUpdated;

        // Mode menu buttons
        _createMapButton.onClick.RemoveAllListeners();
        _loadMapButton.onClick.RemoveAllListeners();

        // Scanning menu buttons
        _saveMapButton.onClick.RemoveAllListeners();
        _exitScanMapButton.onClick.RemoveAllListeners();
        _toCubesMenuButton.onClick.RemoveAllListeners();

        // Cube menu items
        _placeCubeButton.onClick.RemoveAllListeners();
        _exitInGameButton.onClick.RemoveAllListeners();
        _deleteCubesButton.onClick.RemoveAllListeners();

        // Map list buttons
        _mapListBackButton.onClick.RemoveAllListeners();
    }

    // ── Panel state management ──────────────────────────────────────────

    private void SwitchToSelectorView()
    {
        _mapper.StopMapping();

        HideCubesMenu();
        HideScanningView();
        ExitLocalizeView();
        HideMapListPanel();
        
        _statusText.text = "";

        _selectionPanel.SetActive(true);
        _createMapButton.interactable = true;

        // Gate load button on whether any saved maps exist
        _loadMapButton.interactable = GetSavedMapDirectories().Count > 0;
    }

    private void HideModeMenu()
    {
        _selectionPanel.gameObject.SetActive(false);
    }

    private void SwitchToScanningView()
    {
        HideModeMenu();

        _scanningPanel.SetActive(true);
        _saveMapButton.interactable = false;
        _toCubesMenuButton.interactable = false;

        _statusText.text = "Look around to create map";

        _hasStartedAutoLocalize = false;
        _selectedMapDir = null;
        _mapper.StartMapping();

        _scanningAnimationPanel.SetActive(true);
    }

    private void HideScanningView()
    {
        _scanningPanel.gameObject.SetActive(false);
        _scanningAnimationPanel.SetActive(false);
        _mapper.StopMapping();
    }

    private void ExitLocalizeView()
    {
        _scanningAnimationPanel.SetActive(false);
    }

    private void SwitchToCubesView()
    {
        ExitLocalizeView();
        HideScanningView();

        _inGamePanel.SetActive(true);

        _placeCubeButton.interactable=true;
        _exitInGameButton.interactable=true;
    }

    private void HideCubesMenu()
    {
        _inGamePanel.gameObject.SetActive(false);
    }

    private void ExitToSelectorState()
    {
        _hasStartedAutoLocalize = false;
        _selectedMapDir = null;
        ClearMapVisualization();
        ClearCubes();

        _mapper.Reset();
        _tracker.Reset();

        SwitchToSelectorView();
    }

    // ── Map list panel ──────────────────────────────────────────────────

    private void ShowMapList()
    {
        HideModeMenu();
        _mapListPanel.SetActive(true);
        PopulateMapList();
    }

    private void HideMapListPanel()
    {
        if (_mapListPanel != null)
            _mapListPanel.SetActive(false);
        ClearMapList();
    }

    private void PopulateMapList()
    {
        ClearMapList();
        var directories = GetSavedMapDirectories();
        var parent = _mapListScrollRect.content;

        foreach (var mapDir in directories)
        {
            var item = Instantiate(_mapListItemPrefab, parent).GetComponent<MapListItem>();
            item.SetPath(mapDir);
            item.LoadButtonClicked += OnMapSelected;
            item.DeleteButtonClicked += OnMapDeleted;
        }
    }

    private void ClearMapList()
    {
        if (_mapListScrollRect == null) return;
        var content = _mapListScrollRect.content;
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            Destroy(content.GetChild(i).gameObject);
        }
    }
    
    private void OnMapSelected(string mapDir)
    {
        _selectedMapDir = mapDir;
        HideMapListPanel();
        HideModeMenu();

        _statusText.text = "Loading map... move phone to localize";
        _tracker.LoadMap(GetMapFilePath(mapDir));
        _tracker.StartTracking();
        _scanningAnimationPanel.SetActive(true);
    }

    private void OnMapDeleted(string mapDir)
    {
        Directory.Delete(mapDir, true);
        PopulateMapList();

        if (GetSavedMapDirectories().Count == 0)
        {
            ExitToSelectorState();
        }
    }

    // ── Save / Load ─────────────────────────────────────────────────────

    private void SaveMap()
    {
        var mapDir = Path.Combine(MapsRootPath, "map_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(mapDir);

        _mapper.SaveMap(GetMapFilePath(mapDir));
        _selectedMapDir = mapDir;

        _statusText.text = "Map saved";
    }

    private void OnLocalized()
    {
        _statusText.text = "Localized";

        // Localized from Load Map flow
        if (_selectedMapDir != null)
        {
            // Extract and store points for visualization
            if (_tracker.TryGetMapPointMatrixes(out var points))
            {
                _renderedPointSets.Add(points);
            }
            
            SwitchToCubesView();
            LoadCubes();
            return;
        }

        // Localized from Create Map flow
        _toCubesMenuButton.interactable = true;
    }

    // ── Map update and visualization ────────────────────────────────────

    private void HandleMapUpdated(byte[] mapData)
    {
        var rootAnchorPayload = _mapper.RootAnchorPayload;

        // Auto-start localization once the root anchor is available
        if (!_hasStartedAutoLocalize && !string.IsNullOrEmpty(rootAnchorPayload))
        {
            _hasStartedAutoLocalize = true;
            _tracker.StartTrackingWithPayload(rootAnchorPayload);
            _statusText.text = "Scanning... localizing to map";
            _saveMapButton.interactable = true;
        }

        // Extract and store points for visualization
        if (_mapper.TryGetMapPointMatrixes(mapData, out var points))
        {
            _renderedPointSets.Add(points);
        }
    }

    private void ClearMapVisualization()
    {
        _renderedPointSets.Clear();
    }

    void Update()
    {
        // Render map point visualization
        if (_pointMesh == null || _pointMaterial == null) return;
        if (_renderedPointSets.Count == 0) return;
        if (!_tracker.IsLocalized) return;

        var anchorTransform = _tracker.RootAnchorTransform;
        if (anchorTransform == null) return;

        var rootMatrix = anchorTransform.localToWorldMatrix;
        var renderParams = new RenderParams(_pointMaterial);

        foreach (var localPoints in _renderedPointSets)
        {
            // Transform local points to world space
            var worldPoints = new Matrix4x4[localPoints.Length];
            for (var i = 0; i < localPoints.Length; i++)
            {
                worldPoints[i] = rootMatrix * localPoints[i];
            }

            // Render in batches
            for (var i = 0; i < worldPoints.Length; i += MaxRenderBatch)
            {
                var count = Math.Min(MaxRenderBatch, worldPoints.Length - i);
                Graphics.RenderMeshInstanced(renderParams, _pointMesh, 0, worldPoints, count, i);
            }
        }
    }

    // ── Cube placement / persistence ────────────────────────────────────

    private void PlaceCube()
    {
        var pos = Camera.main.transform.position + (Camera.main.transform.forward * 2.0f);
        var go = CreateAndPlaceCube(_tracker.GetAnchorRelativePosition(pos));

        if (_selectedMapDir != null)
        {
            var path = GetObjectsFilePath(_selectedMapDir);
            using (StreamWriter sw = File.AppendText(path))
            {
                sw.WriteLine(go.transform.localPosition);
            }
        }
    }

    private void LoadCubes()
    {
        if (_selectedMapDir == null) return;

        var path = GetObjectsFilePath(_selectedMapDir);
        if (!File.Exists(path)) return;

        using (StreamReader sr = new StreamReader(path))
        {
            while (sr.Peek() >= 0)
            {
                var pos = sr.ReadLine();
                var split1 = pos.Split("(");
                var split2 = split1[1].Split(")");
                var parts = split2[0].Split(",");
                Vector3 localPos = new Vector3(
                    Convert.ToSingle(parts[0]),
                    Convert.ToSingle(parts[1]),
                    Convert.ToSingle(parts[2])
                );

                CreateAndPlaceCube(localPos);
            }
        }
    }

    private GameObject CreateAndPlaceCube(Vector3 localPos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var cubeMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        go.GetComponent<Renderer>().material = cubeMaterial;

        // Add it under the anchor on our map.
        _tracker.AddObjectToAnchor(go);
        go.transform.localPosition = localPos;
        go.transform.localScale = new Vector3(0.2f,0.2f,0.2f);
        return go;
    }

    private void DeleteCubes()
    {
        if (_selectedMapDir != null)
        {
            var path = GetObjectsFilePath(_selectedMapDir);
            if (File.Exists(path))
                File.Delete(path);
        }

        ClearCubes();
    }

    private void ClearCubes()
    {
        if (_tracker.RootAnchorTransform)
        {
            for (var i = 0; i < _tracker.RootAnchorTransform.transform.childCount; i++)
                Destroy(_tracker.RootAnchorTransform.transform.GetChild(i).gameObject);
        }
    }
    
    private List<string> GetSavedMapDirectories()
    {
        var mapDirectories = new List<string>();
        var root = MapsRootPath;
        if (!Directory.Exists(root))
            return mapDirectories;

        var dirs = Directory.GetDirectories(root);

        foreach (var dir in dirs)
        {
            if (File.Exists(GetMapFilePath(dir)))
            {
                mapDirectories.Add(dir);
            }
        }
        
        mapDirectories.Sort();
        mapDirectories.Reverse(); // Newest first
        return mapDirectories;
    }

    private string GetMapFilePath(string mapDir) => Path.Combine(mapDir, k_mapDataFile);
    private string GetObjectsFilePath(string mapDir) => Path.Combine(mapDir, k_objectsDataFile);
}
