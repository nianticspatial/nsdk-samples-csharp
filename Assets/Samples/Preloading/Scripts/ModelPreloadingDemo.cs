// Copyright 2022-2026 Niantic Spatial.

using System;
using System.Collections.Generic;
using System.Linq;
using NianticSpatial.NSDK.AR.Utilities.Preloading;

using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.XR.Management;

namespace NianticSpatial.NSDK.AR.Samples
{
    public class ModelPreloadingDemo : MonoBehaviour
    {
        [SerializeField]
        private Button _depthButton;

        [SerializeField]
        private Dropdown _depthDropdown;

        [SerializeField]
        private Button _semanticsButton;

        [SerializeField]
        private Dropdown _sceneSegmentationDropdown;

        [SerializeField]
        private Button _sqcButton;

        [SerializeField]
        private Dropdown _sqcDropdown;

        [SerializeField]
        private Text _depthStatusText;

        [SerializeField]
        private Text _semanticsStatusText;

        [SerializeField]
        private Text _sqcStatusText;

        [SerializeField]
        private Button _clearCacheButton;

        [SerializeField]
        private Text _preloadStatusText;

        [SerializeField]
        private Slider _percentageSlider;

        [SerializeField]
        private Text _percentageText;

        [SerializeField]
        private Text _cacheStatusText;

        [SerializeField]
        private string _localModelPath;

        private IModelPreloader _preloader;

        private DepthMode _depthMode;
        private SceneSegmentationMode _sceneSegmentationMode;
        private ScanningSQCMode _sqcMode;

        private void Start()
        {
            TryInitializePreloader();
            InitializeDropdownNames<DepthMode>(_depthDropdown);
            InitializeDropdownNames<SceneSegmentationMode>(_sceneSegmentationDropdown);
            InitializeDropdownNames<ScanningSQCMode>(_sqcDropdown);
        }

        private void OnDisable()
        {
            _preloader?.Dispose();
            _preloader = null;
        }

        private void InitializeDropdownNames<T>(Dropdown dropdown)
        {
            // Initialize the feature mode names in the dropdown menu.
            SetFeatureModeNames<T>(dropdown);
            dropdown.value = dropdown.options.Select(option => option.text).ToList().IndexOf("Medium");
        }

        private void SetFeatureModeNames<T>(Dropdown dropdown)
        {
            List<string> modeNames = new();
            foreach (var i in Enum.GetValues(typeof(T)))
            {
                var modeName = Enum.GetName(typeof(T), i);
                if (modeName != "Unspecified" && modeName != "Custom")
                    modeNames.Add(modeName);
            }
            dropdown.AddOptions(modeNames);
        }

        private void Update()
        {
            if (_preloader == null && !TryInitializePreloader())
                return;

            UpdateDownloadProgress();
            UpdateCacheStatusText();
        }

        private bool TryInitializePreloader()
        {
            if (!XRGeneralSettings.Instance.Manager.isInitializationComplete)
            {
                // Need to wait for XR to initialize before we can preload
                return false;
            }

            _depthMode = DepthMode.Medium;
            _sceneSegmentationMode = SceneSegmentationMode.Medium;
            _sqcMode = ScanningSQCMode.Default;

            _preloader = ModelPreloaderFactory.Create();

            if (null == _preloader)
            {
                // Need to wait for XR to initialize before we can preload
                return false;
            }

            _depthButton.onClick.AddListener
            (
                () =>
                {
                    _depthMode = ParseFeatureMode<DepthMode>(_depthDropdown);
                    PreloaderStatusCode result;

                    if (_localModelPath.Length > 0)
                    {
                        result = _preloader.RegisterModel(_depthMode, _localModelPath);
                        _preloadStatusText.text = "Depth file registration: " + (IsError(result) ? "failed" : "success");
                    }
                    else
                    {
                        result = _preloader.DownloadModel(_depthMode);
                        _preloadStatusText.text = IsError(result) ? "Depth download failed to start" : "Depth download starting";
                    }
                }
            );

            _semanticsButton.onClick.AddListener
            (
                () =>
                {
                    _sceneSegmentationMode = ParseFeatureMode<SceneSegmentationMode>(_sceneSegmentationDropdown);
                    PreloaderStatusCode result;

                    if (_localModelPath.Length > 0)
                    {
                        result = _preloader.RegisterModel(_sceneSegmentationMode, _localModelPath);
                        _preloadStatusText.text = "Semantics file registration: " + (IsError(result) ? "failed" : "success");
                    }
                    else
                    {
                        result = _preloader.DownloadModel(_sceneSegmentationMode);
                        _preloadStatusText.text = IsError(result) ? "Semantic segmentation download failed to start" : "Semantic segmentation download starting";
                    }
                }
            );

            _sqcButton.onClick.AddListener
            (
                () =>
                {
                    _sqcMode = ParseFeatureMode<ScanningSQCMode>(_sqcDropdown);
                    PreloaderStatusCode result;

                    if (_localModelPath.Length > 0)
                    {
                        result = _preloader.RegisterModel(_sqcMode, _localModelPath);
                        _preloadStatusText.text = "Scanning SQC file registration: " + (IsError(result) ? "failed" : "success");
                    }
                    else
                    {
                        result = _preloader.DownloadModel(_sqcMode);
                        _preloadStatusText.text = IsError(result) ? "Scanning SQC download failed to start" : "Scanning SQC download starting";
                    }
                }
            );

            _clearCacheButton.onClick.AddListener
            (
                () =>
                {
                    int successes = 0;
                    if (_preloader.ClearFromCache(_depthMode))
                        successes++;

                    if (_preloader.ClearFromCache(_sceneSegmentationMode))
                        successes++;

                    if (_preloader.ClearFromCache(_sqcMode))
                        successes++;

                    _preloadStatusText.text = "Clear cache: " + successes + " successes";
                }
            );

            _depthDropdown.onValueChanged.AddListener
            (
                (int val) =>
                {
                    var mode = val + (int)DepthMode.Fast; // We skip the 'unspecified' and 'custom' modes
                    _depthMode = (DepthMode)mode;
                }
            );

            _sceneSegmentationDropdown.onValueChanged.AddListener
            (
                (int val) =>
                {
                    var mode = val + (int)SceneSegmentationMode.Fast; // We skip the 'unspecified' and 'custom' modes
                    _sceneSegmentationMode = (SceneSegmentationMode)mode;
                }
            );

            _sqcDropdown.onValueChanged.AddListener
            (
                (int val) =>
                {
                    _sqcMode = (ScanningSQCMode)val;
                }
            );

            return true;
        }

        private static T ParseFeatureMode<T>(Dropdown dropdown)
        {
            var modeName = dropdown.options[dropdown.value].text;

            T mode = (T)Enum.Parse(typeof(T), modeName);
            return mode;
        }

        private void UpdateDownloadProgress()
        {
            // Display the progress of the feature modes that are selected with the dropdown menus
            var depthStatus = _preloader.CurrentProgress(_depthMode, out var selectedDepthProgress);
            if (IsError(depthStatus))
            {
                if (depthStatus == PreloaderStatusCode.RequestNotFound)
                {
                    _depthStatusText.text = "0%";
                }
                else
                {
                    _depthStatusText.text = "Failure: " + depthStatus;
                    _preloadStatusText.text = "Download failure";
                }
            }
            else
            {
                _depthStatusText.text = (selectedDepthProgress * 100).ToString("0") + "%";
            }

            var semanticsStatus = _preloader.CurrentProgress(_sceneSegmentationMode, out var selectedSemanticsProgress);
            if (IsError(semanticsStatus))
            {
                if (semanticsStatus == PreloaderStatusCode.RequestNotFound)
                {
                    _semanticsStatusText.text = "0%";
                }
                else
                {
                    _semanticsStatusText.text = "Failure: " + semanticsStatus;
                    _preloadStatusText.text = "Download failure";
                }
            }
            else
            {
                _semanticsStatusText.text = (selectedSemanticsProgress * 100).ToString("0") + "%";
            }

            var sqcStatus = _preloader.CurrentProgress(_sqcMode, out var selectedSqcProgress);
            if (IsError(sqcStatus))
            {
                if (sqcStatus == PreloaderStatusCode.RequestNotFound)
                {
                    _sqcStatusText.text = "0%";
                }
                else
                {
                    _sqcStatusText.text = "Failure: " + sqcStatus;
                    _preloadStatusText.text = "Download failure";
                }
            }
            else
            {
                _sqcStatusText.text = (selectedSqcProgress * 100).ToString("0") + "%";
            }

            // Summarize their download progress
            float combinedProgress = 0, activeDownloads = 0;
            if (selectedDepthProgress > 0 && selectedDepthProgress < 1)
            {
                combinedProgress += selectedDepthProgress;
                activeDownloads++;
            }

            if (selectedSemanticsProgress > 0 && selectedSemanticsProgress < 1)
            {
                combinedProgress += selectedSemanticsProgress;
                activeDownloads++;
            }

            if (selectedSqcProgress > 0 && selectedSqcProgress < 1)
            {
                combinedProgress += selectedSqcProgress;
                activeDownloads++;
            }

            float totalProgress = activeDownloads > 0 ? combinedProgress / activeDownloads : 0;
            _percentageText.text = (totalProgress * 100).ToString("0") + "%";
            _percentageSlider.value = totalProgress;
        }

        private void UpdateCacheStatusText()
        {
            // Cache status
            List<string> modeNames = new();
            DepthMode depthMode = new DepthMode();
            SceneSegmentationMode semanticsMode = new SceneSegmentationMode();
            ScanningSQCMode sqcMode = new ScanningSQCMode();
            string cacheStatusText = "Model files preloaded in cache: " + System.Environment.NewLine;

            // Update the cache status for all depth modes
            foreach (DepthMode i in Enum.GetValues(typeof(DepthMode)))
            {
                if (i == DepthMode.Unspecified)
                    continue;

                depthMode = i;
                var present = _preloader.ExistsInCache(depthMode);

                if (present)
                    modeNames.Add(Enum.GetName(typeof(DepthMode), i) + " Depth");
            }

            // Update the cache status for all semantics modes
            foreach (SceneSegmentationMode i in Enum.GetValues(typeof(SceneSegmentationMode)))
            {
                if (i == SceneSegmentationMode.Unspecified)
                    continue;

                semanticsMode = i;
                var present = _preloader.ExistsInCache(semanticsMode);

                if (present)
                    modeNames.Add(Enum.GetName(typeof(SceneSegmentationMode), i) + " Semantics");
            }

            // Update the cache status for all semantics modes
            foreach (ScanningSQCMode i in Enum.GetValues(typeof(ScanningSQCMode)))
            {
                sqcMode = i;
                var present = _preloader.ExistsInCache(sqcMode);

                if (present)
                    modeNames.Add(Enum.GetName(typeof(ScanningSQCMode), i) + " SQC");
            }


            // Summarize cache status
            for (int i = 0; i < modeNames.Count; i++)
            {
                cacheStatusText += modeNames[i] + (i < modeNames.Count - 1 ? ", " : "");
            }

            _cacheStatusText.text = cacheStatusText;
        }

        private static bool IsError(PreloaderStatusCode statusCode)
        {
            if (statusCode is PreloaderStatusCode.Success or
                              PreloaderStatusCode.RequestInProgress or
                              PreloaderStatusCode.FileExistsInCache)
                return false;

            return true;
        }
    }
}
