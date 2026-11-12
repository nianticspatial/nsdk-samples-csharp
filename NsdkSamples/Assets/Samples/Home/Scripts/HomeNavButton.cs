// Copyright Niantic Spatial

using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class HomeNavButton : MonoBehaviour
{
    [SerializeField]
    private Text _label;

    [SerializeField]
    private Button _button;

    [SerializeField]
    private SceneSwitcher _sceneSwitcher;

    public string Label
    {
        get => _label.text;
        set => _label.text = value;
    }

    public SceneSwitcher SceneSwitcher
    {
        get => _sceneSwitcher;
    }

    public void AddListener(UnityAction action)
    {
        _button.onClick.AddListener(action);
    }

    public void RemoveListener(UnityAction action)
    {
        _button.onClick.RemoveListener(action);
    }
}
