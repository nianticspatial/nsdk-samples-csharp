// Copyright 2026 Niantic Spatial.

using System;
using System.Collections.Generic;
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
    /// Browses an organization's sites for validation scans, downloads them, and selects one to play in
    /// place of the camera in every sample.
    /// </summary>
    public class PlaybackSelectManager : MonoBehaviour
    {
        [SerializeField]
        private Text _detailsText;

        [SerializeField]
        private Transform _optionButtonsContainer;

        [SerializeField]
        private Button _optionButtonPrefab;

        [SerializeField]
        private DownloadedPlaybacksView _downloadedPlaybacksView;

        [SerializeField]
        private Transform _inputFieldsContainer;

        [SerializeField]
        private InputField _inputFieldPrefab;

        private readonly List<Button> _buttons = new();
        private readonly List<(GameObject Row, string[] Keys)> _filterRows = new();
        private InputField _filterField;
        private readonly Dictionary<string, ScanRow> _scanRows = new();

        private PlaybackLibrary _library;
        private SitesClient _sitesClient;
        private AuthRetryHelper _retryHelper;
        private CancellationTokenSource _cancellation;
        private Text _liveCameraLabel;
        private string _status = "";

        /// <summary>A scan length as m:ss or h:mm:ss, or null when unknown.</summary>
        public static string FormatDuration(double seconds)
        {
            if (double.IsNaN(seconds) || seconds <= 0)
            {
                return null;
            }

            var span = TimeSpan.FromSeconds(Math.Round(seconds));
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
                : $"{span.Minutes}:{span.Seconds:00}";
        }

        /// <summary>A scan's download state and what tapping it does.</summary>
        public static string DescribeState(PlaybackDownloadState state, float progress, string error) =>
            state switch
            {
                PlaybackDownloadState.Downloading => $"Downloading {Mathf.RoundToInt(progress * 100)}%, tap to cancel",
                PlaybackDownloadState.Cached => "Downloaded, tap to select",
                PlaybackDownloadState.Incomplete => "Incomplete, tap to download again",
                PlaybackDownloadState.Failed => $"Failed: {error}. Tap to retry",
                _ => "Tap to download"
            };

        /// <summary>Whether any key contains the filter, ignoring case. An empty filter matches everything.</summary>
        public static bool MatchesFilter(string filter, params string[] keys)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return true;
            }

            var trimmed = filter.Trim();
            foreach (var key in keys)
            {
                if (key != null && key.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A scan's selection or download state, and what tapping it does.</summary>
        public static string DescribeScan(PlaybackLibrary library, string scanId)
        {
            if (library.SelectedScanId == scanId)
            {
                return library.NeedsRestart ? "Selected, plays when you open a sample" : "Playing";
            }

            return DescribeState(library.GetState(scanId), library.GetProgress(scanId), library.GetError(scanId));
        }

        /// <summary>Cancels the scan's download, selects it once cached, or downloads it.</summary>
        public static void TapScan(PlaybackLibrary library, string scanId)
        {
            switch (library.GetState(scanId))
            {
                case PlaybackDownloadState.Downloading:
                    library.CancelDownload(scanId);
                    break;
                case PlaybackDownloadState.Cached:
                    _ = library.SelectAsync(scanId);
                    break;
                default:
                    _ = library.DownloadAsync(scanId);
                    break;
            }
        }

        private void Start()
        {
            _library = PlaybackLibrary.Instance;
            _library.Changed += OnLibraryChanged;
            _cancellation = new CancellationTokenSource();
            _retryHelper = new AuthRetryHelper();

            try
            {
                _sitesClient = new SitesClient();
                _library.GetCachedPlaybacks();
            }
            catch (InvalidOperationException e)
            {
                BeginView($"NSDK is not running: {e.Message}");
                return;
            }

            LoadOrganizations();
        }

        private void OnDestroy()
        {
            if (_library != null)
            {
                _library.Changed -= OnLibraryChanged;
            }

            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _sitesClient?.Dispose();
        }

        private async void LoadOrganizations()
        {
            BeginView("Loading organizations...");
            var result = await RequestAsync(ct => _sitesClient.RequestSelfOrganizationInfoAsync(cancellationToken: ct));
            if (result == null)
            {
                return;
            }

            if (result.Value.Status != SitesRequestStatus.Success)
            {
                ShowError($"Loading organizations failed: {result.Value.Error}", "Retry", LoadOrganizations);
                return;
            }

            var organizations = result.Value.Organizations;
            BeginView(organizations.Length > 0 ? "Select an organization:" : "No organizations found.");
            CreateFilter("Filter organizations...");
            foreach (var organization in organizations)
            {
                AddFilterRow(CreateButton(organization.Name, () => LoadSites(organization)), organization.Name);
            }
        }

        private async void LoadSites(OrganizationInfo organization)
        {
            BeginView($"Loading sites of {organization.Name}...");
            var result = await RequestAsync(
                ct => _sitesClient.RequestSitesForOrganizationAsync(organization.Id, cancellationToken: ct));
            if (result == null)
            {
                return;
            }

            if (result.Value.Status != SitesRequestStatus.Success)
            {
                ShowError($"Loading sites failed: {result.Value.Error}", "Retry", () => LoadSites(organization));
                return;
            }

            var sites = result.Value.Sites;
            BeginView(sites.Length > 0 ? $"{organization.Name}: select a site:" : $"{organization.Name} has no sites.");
            CreateButton("← Organizations", LoadOrganizations);
            CreateFilter("Filter sites...");
            foreach (var site in sites)
            {
                AddFilterRow(CreateButton(site.Name, () => LoadScans(organization, site)), site.Name);
            }
        }

        private async void LoadScans(OrganizationInfo organization, SiteInfo site)
        {
            BeginView($"Loading validation scans of {site.Name}...");
            var result = await RequestAsync(
                ct => _sitesClient.RequestScansForSiteAsync(site.Id, cancellationToken: ct));
            if (result == null)
            {
                return;
            }

            if (result.Value.Status != SitesRequestStatus.Success)
            {
                ShowError($"Loading scans failed: {result.Value.Error}", "Retry", () => LoadScans(organization, site));
                return;
            }

            BeginView($"{site.Name}: tap a scan to download it, then tap it again to play it in every sample.");
            CreateButton("← Sites", () => LoadSites(organization));
            CreateFilter("Filter scans by name or ID...");
            var count = 0;
            foreach (var scan in result.Value.Scans)
            {
                if (scan.ScanType != ScanType.Validation)
                {
                    continue;
                }

                var scanId = scan.Id;
                _library.SetScanName(scanId, scan.Name);
                var button = CreateButton(ScanLabel(scan), () => TapScan(_library, scanId));
                AddFilterRow(button, scan.Name, scanId);
                _scanRows[scanId] = new ScanRow(scan, button.GetComponentInChildren<Text>());
                count++;
            }

            if (count == 0)
            {
                SetStatus($"{site.Name} has no validation scans.");
            }
        }

        private async Task<T?> RequestAsync<T>(Func<CancellationToken, Task<T>> request) where T : struct
        {
            var cancellation = _cancellation;
            try
            {
                var result = await _retryHelper.WithRetryAsync(request, cancellation.Token);
                return cancellation.IsCancellationRequested ? null : result;
            }
            catch (Exception e)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    Debug.LogException(e);
                    ShowError(e.Message, "Back to organizations", LoadOrganizations);
                }

                return null;
            }
        }

        private void OnLibraryChanged()
        {
            foreach (var row in _scanRows.Values)
            {
                row.Label.text = ScanLabel(row.Scan);
            }

            if (_liveCameraLabel != null)
            {
                _liveCameraLabel.text = LiveCameraLabel();
            }

            RefreshDetails();
        }

        private string ScanLabel(ScanInfo scan)
        {
            var name = _library.GetScanName(scan.Id);
            var duration = FormatDuration(scan.DurationSeconds);
            var title = duration == null ? name : $"{name} ({duration})";
            return $"{title}\n{DescribeScan(_library, scan.Id)}";
        }

        private string LiveCameraLabel() =>
            _library.SelectedScanId == null && !_library.HasPendingSelection
                ? "Live camera (selected)"
                : "Use live camera";

        private void RefreshDetails()
        {
            var selected = _library.SelectedScanId;
            var source = selected == null ? "live camera" : _library.GetScanName(selected);
            var text = $"Playback source: {source}";
            if (_library.HasPendingSelection)
            {
                text += "\nOpening the newest selection...";
            }
            else if (_library.NeedsRestart)
            {
                text += "\nApplies when you open a sample from Home.";
            }

            _detailsText.text = $"{text}\n\n{_status}";
        }

        private void SetStatus(string status)
        {
            _status = status;
            RefreshDetails();
        }

        private void ShowError(string message, string actionLabel, Action action)
        {
            BeginView(message);
            CreateButton(actionLabel, action);
        }

        // Clears the list and its filter, and starts the list with the live camera button.
        private void BeginView(string status)
        {
            foreach (var button in _buttons)
            {
                Destroy(button.gameObject);
            }

            _buttons.Clear();
            _scanRows.Clear();
            _filterRows.Clear();
            if (_filterField != null)
            {
                Destroy(_filterField.gameObject);
                _filterField = null;
            }

            SetStatus(status);
            _liveCameraLabel = CreateButton(LiveCameraLabel(), _library.UseLiveCamera).GetComponentInChildren<Text>();
            CreateButton("Downloaded playbacks", ShowDownloadedPlaybacks);
        }

        private void ShowDownloadedPlaybacks()
        {
            _downloadedPlaybacksView.Show(OnDownloadedPlaybacksClosed, RestartXrAsync);
            gameObject.SetActive(false);
        }

        // Starts over from the organizations when a restart dropped the Sites client.
        private void OnDownloadedPlaybacksClosed()
        {
            gameObject.SetActive(true);
            if (_sitesClient != null)
            {
                return;
            }

            try
            {
                _sitesClient = new SitesClient();
            }
            catch (InvalidOperationException e)
            {
                BeginView($"NSDK is not running: {e.Message}");
                return;
            }

            LoadOrganizations();
        }

        // Drops the Sites client, which belongs to the NSDK context the restart ends.
        private async Task RestartXrAsync(Action<RuntimeNsdkSettings> applySource)
        {
            _cancellation.Cancel();
            _sitesClient?.Dispose();
            _sitesClient = null;
            try
            {
                await PlaybackLibrary.RestartXrAsync(applySource);
            }
            finally
            {
                _cancellation = new CancellationTokenSource();
            }
        }

        private void CreateFilter(string placeholder)
        {
            _filterField = Instantiate(_inputFieldPrefab, _inputFieldsContainer);
            if (_filterField.placeholder is Text placeholderText)
            {
                placeholderText.text = placeholder;
            }

            _filterField.onValueChanged.AddListener(ApplyFilter);
        }

        private void AddFilterRow(Button button, params string[] keys) => _filterRows.Add((button.gameObject, keys));

        private void ApplyFilter(string filter)
        {
            foreach (var (row, keys) in _filterRows)
            {
                row.SetActive(MatchesFilter(filter, keys));
            }
        }

        private Button CreateButton(string text, Action onClick)
        {
            var button = Instantiate(_optionButtonPrefab, _optionButtonsContainer);
            button.GetComponentInChildren<Text>().text = text;
            button.onClick.AddListener(() => onClick());
            _buttons.Add(button);
            return button;
        }

        private readonly struct ScanRow
        {
            public readonly ScanInfo Scan;
            public readonly Text Label;

            public ScanRow(ScanInfo scan, Text label)
            {
                Scan = scan;
                Label = label;
            }
        }
    }
}
