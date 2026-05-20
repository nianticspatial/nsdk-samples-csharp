using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class MapListItem: MonoBehaviour
{
    [SerializeField]
    private Text _label;

    [SerializeField] private Button _loadButton;
    [SerializeField] private Button _deleteButton;
    
    public event Action<string> LoadButtonClicked;
    public event Action<string> DeleteButtonClicked;

    private string _path;

    public void SetPath(string path)
    {
        _label.text = FormatMapDisplayName(path);
        _path = path;
    }
    
    private static string FormatMapDisplayName(string dirPath)
    {
        Debug.Log(dirPath);
        var dirName = Path.GetFileName(dirPath);
        
        // "map_20260318_143022" -> "2026-03-18 14:30:22"
        if (dirName.StartsWith("map_") && dirName.Length == 19)
        {
            var ts = dirName.Substring(4);
            return ts.Substring(0, 4) + "-" + ts.Substring(4, 2) + "-" + ts.Substring(6, 2)
                   + " " + ts.Substring(9, 2) + ":" + ts.Substring(11, 2) + ":" + ts.Substring(13, 2);
        }
        return dirName;
    }
    
    private void OnEnable()
    {
        _loadButton.onClick.AddListener(HandleLoadButtonClicked);
        _deleteButton.onClick.AddListener(HandleDeleteButtonClicked);
    }

    private void OnDisable()
    {
        _loadButton.onClick.RemoveListener(HandleLoadButtonClicked);
        _deleteButton.onClick.RemoveListener(HandleDeleteButtonClicked);
    }

    private void HandleLoadButtonClicked()
    {
        LoadButtonClicked?.Invoke(_path);
    }

    private void HandleDeleteButtonClicked()
    {
        DeleteButtonClicked?.Invoke(_path);
    }
}