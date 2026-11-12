// Copyright Niantic Spatial

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR.Samples;
using NianticSpatial.NSDK.Samples;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Management;

public class HomeNavManager : MonoBehaviour
{
    [Serializable]
    public struct HomeNavEntry
    {
        public string sceneAsset;
        public string labelText;

        /// <summary>Opens the scene without applying the playback selection.</summary>
        public bool keepsPlaybackSource;
    }

    [SerializeField]
    private HomeNavEntry[] _homeNavEntries;

    [SerializeField]
    private HomeNavButton _buttonPrefab;

    [SerializeField]
    private Transform _buttonContainer;

    [SerializeField]
    private GameObject _confirmPanel;

    [SerializeField]
    private Text _confirmText;

    [SerializeField]
    private Button _confirmButton;

    [SerializeField]
    private Button _cancelButton;

    private readonly List<HomeNavButton> _navButtons = new();

    private TaskCompletionSource<bool> _confirmation;
    private bool _applying;

    private void Start()
    {
        if (_confirmPanel != null)
        {
            _confirmPanel.SetActive(false);
            if (_confirmButton == null || _cancelButton == null)
            {
                Debug.LogError("HomeNavManager: '_confirmButton' and '_cancelButton' must be assigned with '_confirmPanel'.", this);
            }
            else
            {
                _confirmButton.onClick.AddListener(() => Answer(true));
                _cancelButton.onClick.AddListener(() => Answer(false));
            }
        }

        GenerateButtons();
    }

    private void OnDestroy()
    {
        Answer(false);
    }

    /// <summary>
    /// (Re)builds the navigation buttons under <see cref="_buttonContainer"/>. The container is a
    /// VerticalLayoutGroup, so it positions the instantiated buttons automatically. Each button
    /// navigates via the prefab's baked onClick → <see cref="SceneSwitcher.SwitchToScene"/>, so we
    /// only set its label, its target scene, and the playback source to apply before it switches.
    ///
    /// Buttons come from two sources: the authored <see cref="_homeNavEntries"/> (the built-in
    /// samples), followed by <see cref="SampleRegistry.Entries"/> — samples contributed at runtime
    /// by the active build variant. The registry is empty in the default build, so nothing changes
    /// there; a build variant's registrar may populate it before this runs, so its scenes appear
    /// without the shared Home scene ever naming them.
    /// </summary>
    public void GenerateButtons()
    {
        if (_buttonPrefab == null || _buttonContainer == null)
        {
            Debug.LogError("HomeNavManager: '_buttonPrefab' and '_buttonContainer' must be assigned.", this);
            return;
        }

        ClearButtons();

        if (_homeNavEntries != null)
        {
            foreach (var entry in _homeNavEntries)
            {
                CreateButton(entry.labelText, entry.sceneAsset, !entry.keepsPlaybackSource);
            }
        }

        // Samples contributed at runtime via SampleRegistry (empty by default).
        foreach (var entry in SampleRegistry.Entries)
        {
            CreateButton(entry.Title, entry.SceneName, true);
        }
    }

    private void CreateButton(string label, string sceneName, bool appliesPlayback)
    {
        var navButton = Instantiate(_buttonPrefab, _buttonContainer);
        navButton.name = string.IsNullOrEmpty(label) ? "HomeNavButton" : label;
        navButton.Label = label;
        navButton.SceneSwitcher.TargetScene = sceneName;
        navButton.SceneSwitcher.BeforeSwitchAsync = appliesPlayback ? ApplyPlaybackSelectionAsync : null;
        _navButtons.Add(navButton);
    }

    private async Task<bool> ApplyPlaybackSelectionAsync()
    {
        if (_applying)
        {
            return false;
        }

        _applying = true;
        try
        {
            // Retries a restart that left XR down.
            if (!XRGeneralSettings.Instance.Manager.isInitializationComplete)
            {
                await PlaybackLibrary.RestartXrAsync(PlaybackLibrary.Instance.ApplySource);
                return true;
            }

            return await PlaybackLibrary.Instance.ApplySelectionAsync(ConfirmCancelDownloadsAsync, PlaybackLibrary.RestartXrAsync);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            ShowMessage("Couldn't start XR with the selected playback source. Try again, or select another scan or the live camera.");
            return false;
        }
        finally
        {
            _applying = false;
        }
    }

    private Task<bool> ConfirmCancelDownloadsAsync(int count)
    {
        if (_confirmPanel == null)
        {
            Debug.LogError("HomeNavManager: '_confirmPanel' is not assigned; not opening the sample.", this);
            return Task.FromResult(false);
        }

        var message = count == 1 ? "1 download will be cancelled." : $"{count} downloads will be cancelled.";
        if (PlaybackLibrary.Instance.HasPendingSelection)
        {
            message += " Your newest selection is still downloading and will be dropped.";
        }

        _confirmText.text = message;
        if (_confirmButton != null)
        {
            _confirmButton.gameObject.SetActive(true);
        }

        _confirmPanel.SetActive(true);
        _confirmation = new TaskCompletionSource<bool>();
        return _confirmation.Task;
    }

    // Shows the modal with only its dismiss button.
    private void ShowMessage(string message)
    {
        if (_confirmPanel == null)
        {
            return;
        }

        _confirmText.text = message;
        if (_confirmButton != null)
        {
            _confirmButton.gameObject.SetActive(false);
        }

        _confirmPanel.SetActive(true);
    }

    private void Answer(bool confirmed)
    {
        if (_confirmPanel != null)
        {
            _confirmPanel.SetActive(false);
        }

        var confirmation = _confirmation;
        _confirmation = null;
        confirmation?.TrySetResult(confirmed);
    }

    private void ClearButtons()
    {
        foreach (var navButton in _navButtons)
        {
            if (navButton != null)
            {
                Destroy(navButton.gameObject);
            }
        }
        _navButtons.Clear();
    }
}
