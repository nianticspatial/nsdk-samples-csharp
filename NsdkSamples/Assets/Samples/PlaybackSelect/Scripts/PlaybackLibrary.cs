// Copyright 2026 Niantic Spatial.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR.Loader;
using NianticSpatial.NSDK.AR.Subsystems.Playback;
using UnityEngine;
using UnityEngine.XR.Management;

namespace NianticSpatial.NSDK.AR.Samples
{
    /// <summary>A scan's download state in this app run.</summary>
    public enum PlaybackDownloadState
    {
        /// <summary>Not known to be cached, and not downloading.</summary>
        None,
        /// <summary>Downloading, or opening from the cache.</summary>
        Downloading,
        /// <summary>Fully downloaded.</summary>
        Cached,
        /// <summary>Partly on disk with nothing downloading it; downloading again replaces it.</summary>
        Incomplete,
        /// <summary>The last download failed.</summary>
        Failed
    }

    /// <summary>
    /// App-wide playback downloads and source. Downloads run concurrently and survive scene changes; the
    /// selected scan plays on a loop in place of the camera once <see cref="ApplySource"/> runs over an XR
    /// restart.
    /// </summary>
    /// <remarks>Call from the main thread.</remarks>
    public class PlaybackLibrary : MonoBehaviour
    {
        /// <summary>The most scan name lookups <see cref="ResolveScanNamesAsync"/> runs at once.</summary>
        public const int MaxConcurrentNameLookups = 6;

        private static PlaybackLibrary s_instance;

        private readonly Dictionary<string, ScanEntry> _scans = new();
        private readonly Dictionary<string, Download> _downloads = new();
        private readonly Dictionary<string, string> _scanNames = new();

        private PlaybackCacheClient _cache = new();

        // The scan to select once its open finishes.
        private string _pendingScanId;

        private string _selectedScanId;
        private PlaybackDataset _selected;
        private string _runningScanId;
        private PlaybackDataset _running;

        // UsePlayback and LoopPlaybackInfinitely as they were before a dataset became the source.
        private bool _usePlaybackWithoutSource;
        private bool _loopPlaybackWithoutSource;

        /// <summary>The library, created on first use.</summary>
        public static PlaybackLibrary Instance
        {
            get
            {
                if (s_instance == null)
                {
                    var go = new GameObject(nameof(PlaybackLibrary));
                    DontDestroyOnLoad(go);
                    s_instance = go.AddComponent<PlaybackLibrary>();
                }

                return s_instance;
            }
        }

        /// <summary>Raised when a download, the selection or the source changes.</summary>
        public event Action Changed;

        /// <summary>The scan to play in place of the camera, or null for the live camera.</summary>
        public string SelectedScanId => _selectedScanId;

        /// <summary>Whether a selected scan is still opening.</summary>
        public bool HasPendingSelection => _pendingScanId != null;

        /// <summary>How many downloads and opens are running.</summary>
        public int ActiveDownloadCount => _downloads.Count;

        /// <summary>Whether the selection differs from the source XR runs with.</summary>
        public bool NeedsRestart => _selected != _running;

        /// <summary>Whether <see cref="ClearAllAsync"/> is running.</summary>
        public bool IsClearing { get; private set; }

        /// <summary>The scan's download state.</summary>
        public PlaybackDownloadState GetState(string scanId) =>
            _scans.TryGetValue(scanId, out var entry) ? entry.State : PlaybackDownloadState.None;

        /// <summary>The scan's download progress in [0, 1] while it downloads.</summary>
        public float GetProgress(string scanId) => _scans.TryGetValue(scanId, out var entry) ? entry.Progress : 0;

        /// <summary>Why the scan's last download failed, or null.</summary>
        public string GetError(string scanId) => _scans.TryGetValue(scanId, out var entry) ? entry.Error : null;

        /// <summary>The scan's name, or its ID when the name is unknown.</summary>
        public string GetScanName(string scanId) => _scanNames.GetValueOrDefault(scanId, scanId);

        /// <summary>Records the scan's name; an empty name is ignored.</summary>
        public void SetScanName(string scanId, string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                _scanNames[scanId] = name;
            }
        }

        /// <summary>
        /// Looks up the name of each scan with no known name, running at most
        /// <see cref="MaxConcurrentNameLookups"/> lookups at once. A lookup that throws or returns an empty
        /// name leaves the scan unnamed. Raises <see cref="Changed"/> for each name found.
        /// </summary>
        /// <param name="lookup">Returns the scan's name, or null.</param>
        public async Task ResolveScanNamesAsync(
            IEnumerable<string> scanIds, Func<string, CancellationToken, Task<string>> lookup,
            CancellationToken cancellationToken)
        {
            var pending = new Queue<string>(scanIds.Where(id => !_scanNames.ContainsKey(id)).Distinct());
            var workers = new List<Task>();
            for (var i = 0; i < MaxConcurrentNameLookups && pending.Count > 0; i++)
            {
                workers.Add(ResolveNextNamesAsync(pending, lookup, cancellationToken));
            }

            await Task.WhenAll(workers);
        }

        /// <summary>
        /// Lists the cached scans, and marks each with no state in this run as cached or incomplete.
        /// </summary>
        /// <exception cref="InvalidOperationException">NSDK is not initialized.</exception>
        public IReadOnlyList<CachedPlayback> GetCachedPlaybacks()
        {
            var cached = _cache.GetCachedPlaybacks();
            foreach (var entry in cached)
            {
                if (_scans.ContainsKey(entry.ScanId))
                {
                    continue;
                }

                switch (entry.State)
                {
                    case CachedPlaybackState.Ready:
                        SetState(entry.ScanId, PlaybackDownloadState.Cached);
                        break;
                    case CachedPlaybackState.Incomplete:
                        SetState(entry.ScanId, PlaybackDownloadState.Incomplete);
                        break;
                }
            }

            Changed?.Invoke();
            return cached;
        }

        /// <summary>Downloads the scan into the cache. Completes when the download ends, and never throws.</summary>
        public Task DownloadAsync(string scanId) => StartOpen(scanId);

        /// <summary>
        /// Opens the scan, downloading it if needed, and selects it once open. A later selection or
        /// <see cref="UseLiveCamera"/> drops it. Completes when the open ends, and never throws.
        /// </summary>
        public Task SelectAsync(string scanId)
        {
            if (IsClearing)
            {
                return Task.CompletedTask;
            }

            if (scanId == _runningScanId)
            {
                _pendingScanId = null;
                SetSelection(scanId, _running);
                return Task.CompletedTask;
            }

            if (scanId == _selectedScanId)
            {
                _pendingScanId = null;
                return Task.CompletedTask;
            }

            _pendingScanId = scanId;
            return StartOpen(scanId);
        }

        /// <summary>Cancels the scan's download or open, if one runs.</summary>
        public void CancelDownload(string scanId)
        {
            if (_pendingScanId == scanId)
            {
                _pendingScanId = null;
            }

            if (_downloads.TryGetValue(scanId, out var download))
            {
                download.Cancellation.Cancel();
            }
        }

        /// <summary>Clears the selection and closes its dataset unless XR still runs with it.</summary>
        public void UseLiveCamera()
        {
            _pendingScanId = null;
            SetSelection(null, null);
        }

        /// <summary>
        /// Cancels the scan's download and waits for it, drops the scan from the selection, then deletes its
        /// cached recording. When XR runs with the scan, first switches to the live camera and restarts XR,
        /// asking <paramref name="confirmCancelDownloads"/> with the running download count first.
        /// </summary>
        /// <param name="restartXr">Restarts XR, calling its argument while XR is deinitialized.</param>
        /// <returns>False when the user declines to cancel the downloads.</returns>
        public async Task<bool> DeleteAsync(
            string scanId, Func<int, Task<bool>> confirmCancelDownloads,
            Func<Action<RuntimeNsdkSettings>, Task> restartXr)
        {
            if (IsClearing)
            {
                return true;
            }

            if (scanId == _runningScanId && !await RestartWithLiveCameraAsync(confirmCancelDownloads, restartXr))
            {
                return false;
            }

            if (scanId == _selectedScanId)
            {
                SetSelection(null, null);
            }

            CancelDownload(scanId);
            if (_downloads.TryGetValue(scanId, out var download))
            {
                await download.Task;
            }

            _cache.DeleteCached(scanId);
            SetState(scanId, PlaybackDownloadState.None);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Selects the live camera, cancels every download and waits for them, then empties the cache. When
        /// XR runs with a cached scan, restarts XR with the live camera first.
        /// </summary>
        /// <param name="restartXr">Restarts XR, calling its argument while XR is deinitialized.</param>
        public async Task ClearAllAsync(Func<Action<RuntimeNsdkSettings>, Task> restartXr)
        {
            if (IsClearing)
            {
                return;
            }

            IsClearing = true;
            try
            {
                if (_running != null)
                {
                    await RestartWithLiveCameraAsync(null, restartXr);
                }

                UseLiveCamera();
                await CancelDownloadsAsync();
                _cache.ClearCache();
                _scans.Clear();
            }
            finally
            {
                IsClearing = false;
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// Makes the selection the playback source for the next XR initialization, looping it until the live
        /// camera takes over, and closes the dataset it replaces. Call while XR is deinitialized.
        /// </summary>
        /// <exception cref="InvalidOperationException">XR is initialized.</exception>
        public void ApplySource(RuntimeNsdkSettings settings)
        {
            if (XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null &&
                XRGeneralSettings.Instance.Manager.isInitializationComplete)
            {
                throw new InvalidOperationException("Deinitialize XR before applying the playback source.");
            }

            if (_selected != null)
            {
                if (_running == null)
                {
                    _usePlaybackWithoutSource = settings.UsePlayback;
                    _loopPlaybackWithoutSource = settings.LoopPlaybackInfinitely;
                }

                settings.UsePlayback = true;
                settings.LoopPlaybackInfinitely = true;
            }
            else if (_running != null)
            {
                settings.UsePlayback = _usePlaybackWithoutSource;
                settings.LoopPlaybackInfinitely = _loopPlaybackWithoutSource;
            }

            settings.PlaybackDataset = _selected;
            var previous = _running;
            _running = _selected;
            _runningScanId = _selectedScanId;
            CloseIfUnused(previous);
            Changed?.Invoke();
        }

        /// <summary>
        /// Restarts XR with the selection when it differs from the running source. With downloads running,
        /// asks <paramref name="confirmCancelDownloads"/> with their count first, then cancels them and waits.
        /// </summary>
        /// <param name="restartXr">Restarts XR, calling its argument while XR is deinitialized.</param>
        /// <returns>False when the user declines to cancel the downloads.</returns>
        public async Task<bool> ApplySelectionAsync(
            Func<int, Task<bool>> confirmCancelDownloads, Func<Action<RuntimeNsdkSettings>, Task> restartXr)
        {
            if (!NeedsRestart)
            {
                return true;
            }

            if (ActiveDownloadCount > 0)
            {
                if (!await confirmCancelDownloads(ActiveDownloadCount))
                {
                    return false;
                }

                await CancelDownloadsAsync();
            }

            await restartXr(ApplySource);
            return true;
        }

        /// <summary>
        /// Restarts XR, calling <paramref name="applySource"/> while XR is deinitialized.
        /// </summary>
        /// <exception cref="InvalidOperationException">XR fails to initialize again.</exception>
        public static async Task RestartXrAsync(Action<RuntimeNsdkSettings> applySource)
        {
            Debug.Log("[PlaybackLibrary] Restarting XR to apply the playback source.");
            var manager = XRGeneralSettings.Instance.Manager;
            if (manager.isInitializationComplete)
            {
                manager.StopSubsystems();
                // ARFoundation needs two frames between stopping subsystems and deinitializing the loader.
                await Awaitable.NextFrameAsync();
                await Awaitable.NextFrameAsync();
                manager.DeinitializeLoader();
            }

            applySource(NsdkSettingsHelper.ActiveSettings);
            manager.InitializeLoaderSync();
            if (!manager.isInitializationComplete)
            {
                throw new InvalidOperationException("XR failed to initialize with the selected playback source.");
            }
        }

        /// <summary>Replaces the playback cache client.</summary>
        public void SetCacheClient(PlaybackCacheClient cache) => _cache = cache;

        /// <summary>Cancels every download and closes the selection unless XR runs with it.</summary>
        public void Shutdown()
        {
            _pendingScanId = null;
            var selected = _selected;
            _selectedScanId = null;
            _selected = null;
            CloseIfUnused(selected);
            foreach (var download in _downloads.Values.ToList())
            {
                download.Cancellation.Cancel();
            }
        }

        private void OnDestroy()
        {
            Shutdown();
            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        // Asks before cancelling running downloads when confirmCancelDownloads is set.
        private async Task<bool> RestartWithLiveCameraAsync(
            Func<int, Task<bool>> confirmCancelDownloads, Func<Action<RuntimeNsdkSettings>, Task> restartXr)
        {
            if (confirmCancelDownloads != null && ActiveDownloadCount > 0 &&
                !await confirmCancelDownloads(ActiveDownloadCount))
            {
                return false;
            }

            UseLiveCamera();
            await CancelDownloadsAsync();
            await restartXr(ApplySource);
            return true;
        }

        private Task CancelDownloadsAsync()
        {
            var downloads = _downloads.Values.ToList();
            foreach (var download in downloads)
            {
                download.Cancellation.Cancel();
            }

            return Task.WhenAll(downloads.Select(download => download.Task));
        }

        private async Task ResolveNextNamesAsync(
            Queue<string> pending, Func<string, CancellationToken, Task<string>> lookup,
            CancellationToken cancellationToken)
        {
            while (pending.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                var scanId = pending.Dequeue();
                string name;
                try
                {
                    name = await lookup(scanId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PlaybackLibrary] Looking up the name of scan {scanId} failed: {e.Message}");
                    continue;
                }

                if (!string.IsNullOrEmpty(name))
                {
                    _scanNames[scanId] = name;
                    Changed?.Invoke();
                }
            }
        }

        private Task StartOpen(string scanId)
        {
            if (_downloads.TryGetValue(scanId, out var running))
            {
                return running.Task;
            }

            if (IsClearing)
            {
                return Task.CompletedTask;
            }

            var download = new Download();
            _downloads[scanId] = download;
            download.Task = RunOpenAsync(scanId, download.Cancellation.Token);
            return download.Task;
        }

        private async Task RunOpenAsync(string scanId, CancellationToken cancellationToken)
        {
            var wasCached = GetState(scanId) == PlaybackDownloadState.Cached;
            PlaybackDataset dataset = null;
            try
            {
                SetState(scanId, PlaybackDownloadState.Downloading);
                Changed?.Invoke();
                dataset = await _cache.OpenAsync(scanId, new InlineProgress(p => SetProgress(scanId, p)), cancellationToken);
                SetState(scanId, PlaybackDownloadState.Cached);
            }
            catch (OperationCanceledException)
            {
                SetState(scanId, wasCached ? PlaybackDownloadState.Cached : PlaybackDownloadState.None);
            }
            // An XR restart ends the session's downloads; treat it like a cancel.
            catch (PlaybackOpenException e) when (e.Error == PlaybackOpenError.SessionEnded)
            {
                SetState(scanId, wasCached ? PlaybackDownloadState.Cached : PlaybackDownloadState.None);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlaybackLibrary] Opening scan {scanId} failed: {e.Message}");
                SetState(scanId, PlaybackDownloadState.Failed, e.Message);
            }
            finally
            {
                _downloads.Remove(scanId, out var download);
                download.Cancellation.Dispose();
            }

            try
            {
                if (_pendingScanId == scanId)
                {
                    _pendingScanId = null;
                    if (dataset != null)
                    {
                        SetSelection(scanId, dataset);
                        return;
                    }
                }

                dataset?.Close();
                Changed?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void SetSelection(string scanId, PlaybackDataset dataset)
        {
            var previous = _selected;
            _selectedScanId = scanId;
            _selected = dataset;
            CloseIfUnused(previous);
            Changed?.Invoke();
        }

        private void CloseIfUnused(PlaybackDataset dataset)
        {
            if (dataset != null && dataset != _selected && dataset != _running)
            {
                dataset.Close();
            }
        }

        private void SetState(string scanId, PlaybackDownloadState state, string error = null)
        {
            if (state == PlaybackDownloadState.None)
            {
                _scans.Remove(scanId);
                return;
            }

            _scans[scanId] = new ScanEntry(state, 0, error);
        }

        private void SetProgress(string scanId, float progress)
        {
            if (_scans.TryGetValue(scanId, out var entry) && entry.State == PlaybackDownloadState.Downloading &&
                entry.Progress != progress)
            {
                _scans[scanId] = new ScanEntry(entry.State, progress, null);
                Changed?.Invoke();
            }
        }

        private readonly struct ScanEntry
        {
            public readonly PlaybackDownloadState State;
            public readonly float Progress;
            public readonly string Error;

            public ScanEntry(PlaybackDownloadState state, float progress, string error)
            {
                State = state;
                Progress = progress;
                Error = error;
            }
        }

        private class Download
        {
            public readonly CancellationTokenSource Cancellation = new();
            public Task Task;
        }

        private sealed class InlineProgress : IProgress<float>
        {
            private readonly Action<float> _report;

            public InlineProgress(Action<float> report) => _report = report;

            public void Report(float value) => _report(value);
        }
    }
}
