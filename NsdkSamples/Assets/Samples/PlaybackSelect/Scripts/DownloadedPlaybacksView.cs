// Copyright 2026 Niantic Spatial.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR.Loader;
using NianticSpatial.NSDK.AR.Sites;
using NianticSpatial.NSDK.AR.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace NianticSpatial.NSDK.AR.Samples
{
    /// <summary>
    /// Lists the scans in the playback cache, and selects, downloads again, deletes or clears them.
    /// </summary>
    public class DownloadedPlaybacksView : MonoBehaviour
    {
        private const float DeleteButtonWidth = 360;

        [SerializeField]
        private Text _detailsText;

        [SerializeField]
        private Transform _optionButtonsContainer;

        [SerializeField]
        private Button _optionButtonPrefab;

        private readonly List<GameObject> _rows = new();
        private readonly Dictionary<string, Text> _entryLabels = new();

        private PlaybackLibrary _library;
        private SitesClient _sitesClient;
        private AuthRetryHelper _retryHelper;
        private CancellationTokenSource _cancellation;
        private Action _onClose;
        private Func<Action<RuntimeNsdkSettings>, Task> _restartXr;

        /// <summary>Shows the list, and calls <paramref name="onClose"/> when the user goes back.</summary>
        /// <param name="restartXr">
        /// Restarts XR, calling its argument while XR is deinitialized, when the playing scan is deleted.
        /// </param>
        public void Show(Action onClose, Func<Action<RuntimeNsdkSettings>, Task> restartXr)
        {
            _onClose = onClose;
            _restartXr = restartXr;
            gameObject.SetActive(true);
        }

        private void OnEnable()
        {
            _library = PlaybackLibrary.Instance;
            _library.Changed += OnLibraryChanged;
            _cancellation = new CancellationTokenSource();
            _retryHelper = new AuthRetryHelper();
            _ = _library.ResolveScanNamesAsync(Rebuild(), LookUpNameAsync, _cancellation.Token);
        }

        private void OnDisable()
        {
            _library.Changed -= OnLibraryChanged;
            _cancellation.Cancel();
            _cancellation.Dispose();
            _sitesClient?.Dispose();
            _sitesClient = null;
            ClearRows();
        }

        // Lists the cache again, and returns the listed scan IDs.
        private List<string> Rebuild(string status = null)
        {
            if (this == null || !isActiveAndEnabled)
            {
                return new List<string>();
            }

            ClearRows();
            CreateButton("← Back", Close);
            List<string> scanIds;
            try
            {
                _sitesClient ??= new SitesClient();
                scanIds = _library.GetCachedPlaybacks()
                    .Select(entry => entry.ScanId)
                    .OrderBy(scanId => scanId, StringComparer.Ordinal)
                    .ToList();
            }
            catch (InvalidOperationException e)
            {
                _detailsText.text = $"NSDK is not running: {e.Message}";
                return new List<string>();
            }

            if (scanIds.Count == 0)
            {
                _detailsText.text = status ?? "No downloaded playbacks.";
                return scanIds;
            }

            CreateButton("Clear all", ClearAll);
            foreach (var scanId in scanIds)
            {
                CreateEntryRow(scanId);
            }

            _detailsText.text = status ?? "Tap a scan to select it or download it again.";
            return scanIds;
        }

        // TODO(NSDK-9089): Read the name from the playback cache's metadata instead of Sites.
        private async Task<string> LookUpNameAsync(string scanId, CancellationToken cancellationToken)
        {
            var result = await _retryHelper.WithRetryAsync(
                ct => _sitesClient.RequestScanInfoAsync(scanId, cancellationToken: ct), cancellationToken);
            return result.Status == SitesRequestStatus.Success && result.Scans?.Length > 0 ? result.Scans[0].Name : null;
        }

        private async void Delete(string scanId)
        {
            try
            {
                await _library.DeleteAsync(scanId, ConfirmCancelDownloadsAsync, RestartXrAsync);
                Rebuild();
            }
            catch (Exception e)
            {
                Rebuild($"Deleting {_library.GetScanName(scanId)} failed: {e.Message}");
            }
        }

        private async void ClearAll()
        {
            try
            {
                await _library.ClearAllAsync(RestartXrAsync);
                Rebuild();
            }
            catch (Exception e)
            {
                Rebuild($"Clearing failed: {e.Message}");
            }
        }

        private Task<bool> ConfirmCancelDownloadsAsync(int count)
        {
            var confirmation = new TaskCompletionSource<bool>();
            ClearRows();
            var downloads = count == 1 ? "1 download" : $"{count} downloads";
            _detailsText.text = $"Deleting the playing scan switches to the live camera and cancels {downloads}.";
            if (_library.HasPendingSelection)
            {
                _detailsText.text += " Your newest selection is still downloading and will be dropped.";
            }

            CreateButton("Continue", () => confirmation.TrySetResult(true));
            CreateButton("Cancel", () => confirmation.TrySetResult(false));
            return confirmation.Task;
        }

        // Stops name lookups and drops the Sites client, which belongs to the NSDK context the restart ends.
        private async Task RestartXrAsync(Action<RuntimeNsdkSettings> applySource)
        {
            ClearRows();
            _detailsText.text = "Switching to the live camera...";
            _cancellation.Cancel();
            _sitesClient?.Dispose();
            _sitesClient = null;
            try
            {
                await _restartXr(applySource);
            }
            finally
            {
                _cancellation = new CancellationTokenSource();
            }
        }

        private void Close()
        {
            gameObject.SetActive(false);
            _onClose?.Invoke();
        }

        private void OnLibraryChanged()
        {
            foreach (var (scanId, label) in _entryLabels)
            {
                label.text = EntryLabel(scanId);
            }
        }

        private string EntryLabel(string scanId) =>
            $"{_library.GetScanName(scanId)}\n{PlaybackSelectManager.DescribeScan(_library, scanId)}";

        // A row of the entry's button and its delete button.
        private void CreateEntryRow(string scanId)
        {
            var row = new GameObject(scanId, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.layer = _optionButtonsContainer.gameObject.layer;
            row.transform.SetParent(_optionButtonsContainer, false);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 16;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            _rows.Add(row);

            var entry = CreateButton(EntryLabel(scanId), () => PlaybackSelectManager.TapScan(_library, scanId), row.transform);
            _entryLabels[scanId] = entry.GetComponentInChildren<Text>();

            var delete = CreateButton("Delete", () => Delete(scanId), row.transform);
            var deleteLayout = delete.GetComponent<LayoutElement>();
            deleteLayout.flexibleWidth = 0;
            deleteLayout.preferredWidth = DeleteButtonWidth;
        }

        private Button CreateButton(string text, Action onClick, Transform parent = null)
        {
            var button = Instantiate(_optionButtonPrefab, parent == null ? _optionButtonsContainer : parent);
            button.GetComponentInChildren<Text>().text = text;
            button.onClick.AddListener(() => onClick());
            if (parent == null)
            {
                _rows.Add(button.gameObject);
            }

            return button;
        }

        private void ClearRows()
        {
            foreach (var row in _rows)
            {
                Destroy(row);
            }

            _rows.Clear();
            _entryLabels.Clear();
        }
    }
}
