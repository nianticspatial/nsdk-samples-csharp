using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using NianticSpatial.NSDK.AR.Sites;
using NianticSpatial.NSDK.AR.Auth;
using NianticSpatial.NSDK.AR.Utilities;
using UnityEngine.UI;

namespace NianticSpatial.NSDK.AR.Samples
{
    public class SitesListManager : MonoBehaviour
    {
        [SerializeField]
        private SitesClientManager _sitesClientManager;

        [SerializeField]
        private Text _detailsText;

        [SerializeField]
        private Transform _optionButtonsContainer;

        [SerializeField]
        private SitesListManagerButton _optionButtonPrefab;

        [SerializeField]
        private InputField _buttonFilterInputField;

        // State
        private UserInfo? _currentUser;
        private List<OrganizationInfo> _currentOrganizations = new();
        private List<SiteInfo> _currentSites = new();
        private List<AssetInfo> _currentAssets = new();
        private readonly List<SitesListManagerButton> _optionButtons = new();

        // Retry helper
        private AuthRetryHelper _retryHelper;

        private OrganizationInfo? _selectedOrganization;
        private SiteInfo? _selectedSite;

        private CancellationTokenSource _cancellationTokenSource;

        private void Start()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _retryHelper = new AuthRetryHelper();
            _buttonFilterInputField.onValueChanged.AddListener(OnFilterChanged);
            LoadInitialData();
        }

        private void OnDestroy()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _buttonFilterInputField.onValueChanged.RemoveListener(OnFilterChanged);
        }

        private void OnFilterChanged(string filterText)
        {
            ApplyFilter(filterText);
        }

        private void ApplyFilter(string filterText)
        {
            var lowerFilter = filterText?.ToLower() ?? "";
            for (int i = 0; i < _optionButtons.Count; i++)
            {
                var button = _optionButtons[i];
                // Skip filtering the first button if it's a back button
                bool isBackButton = i == 0 && (button.DetailsText?.StartsWith("←") ?? false);

                if (string.IsNullOrEmpty(lowerFilter) || isBackButton)
                {
                    button.gameObject.SetActive(true);
                }
                else
                {
                    var buttonText = button.DetailsText?.ToLower() ?? "";
                    button.gameObject.SetActive(buttonText.Contains(lowerFilter));
                }
            }
        }

        private void ClearFilter()
        {
            _buttonFilterInputField.text = "";
        }

        // ============================================================================
        // Data Loading
        // ============================================================================

        private async void LoadInitialData()
        {
            if (_sitesClientManager == null)
            {
                SetDetailsText("❌ SitesClientManager is not assigned!");
                Debug.LogError("SitesListManager: _sitesClientManager is not assigned!");
                return;
            }

            SetDetailsText("⏳ Loading user info...");
            ClearButtons();

            try
            {
                // Request user info with retry
                var userResult = await _retryHelper.WithRetryAsync(() => _sitesClientManager.GetSelfUserInfoAsync());

                if (_cancellationTokenSource.IsCancellationRequested)
                {
                    return;
                }

                if (userResult.Status != SitesRequestStatus.Success || !userResult.User.HasValue)
                {
                    SetDetailsText("❌ Failed to retrieve user information.\nMake sure you're authenticated.");
                    return;
                }

                _currentUser = userResult.User.Value;
                UpdateDetailsForUser(_currentUser.Value);

                // Request organizations for user with retry
                var orgsResult = await _retryHelper.WithRetryAsync(() => _sitesClientManager.GetOrganizationsForUserAsync(_currentUser.Value.Id));

                if (_cancellationTokenSource.IsCancellationRequested)
                {
                    return;
                }

                if (orgsResult.Status == SitesRequestStatus.Success)
                {
                    _currentOrganizations = new List<OrganizationInfo>(orgsResult.Organizations);
                    CreateOrganizationButtons();
                }
                else
                {
                    AppendDetailsText("\n\n⚠️ No organizations found");
                }
            }
            catch (System.Exception e)
            {
                if (!_cancellationTokenSource.IsCancellationRequested)
                {
                    Debug.LogException(e);
                    SetDetailsText($"❌ Error loading data: {e.Message}\n\nStack: {e.StackTrace}");
                }
            }
        }

        private async void LoadSitesForOrganization(OrganizationInfo org)
        {
            _selectedOrganization = org;
            UpdateDetailsForOrganization(org);
            ClearButtons();
            AppendDetailsText("\n\n⏳ Loading sites...");

            try
            {
                var result = await _retryHelper.WithRetryAsync(() => _sitesClientManager.GetSitesForOrganizationAsync(org.Id));

                if (_cancellationTokenSource.IsCancellationRequested)
                {
                    return;
                }

                if (result.Status == SitesRequestStatus.Success)
                {
                    _currentSites = new List<SiteInfo>(result.Sites);
                    UpdateDetailsForOrganization(org);
                    CreateSiteButtons();
                }
                else
                {
                    UpdateDetailsForOrganization(org);
                    AppendDetailsText("\n\n⚠️ No sites found");
                    CreateBackButton("← Back to Organizations", BackToOrganizations);
                }
            }
            catch (System.Exception e)
            {
                UpdateDetailsForOrganization(org);
                AppendDetailsText($"\n\n❌ Error loading sites: {e.Message}");
                CreateBackButton("← Back to Organizations", BackToOrganizations);
            }
        }

        private async void LoadAssetsForSite(SiteInfo site)
        {
            _selectedSite = site;
            UpdateDetailsForSite(site);
            ClearButtons();
            AppendDetailsText("\n\n⏳ Loading assets...");

            try
            {
                var result = await _retryHelper.WithRetryAsync(() => _sitesClientManager.GetAssetsForSiteAsync(site.Id));

                if (_cancellationTokenSource.IsCancellationRequested)
                {
                    return;
                }

                if (result.Status == SitesRequestStatus.Success)
                {
                    _currentAssets = new List<AssetInfo>(result.Assets);
                    UpdateDetailsForSite(site);
                    CreateAssetButtons();
                }
                else
                {
                    UpdateDetailsForSite(site);
                    AppendDetailsText("\n\n⚠️ No assets found");
                    CreateBackButton("← Back to Sites", BackToSites);
                }
            }
            catch (System.Exception e)
            {
                UpdateDetailsForSite(site);
                AppendDetailsText($"\n\n❌ Error loading assets: {e.Message}");
                CreateBackButton("← Back to Sites", BackToSites);
            }
        }

        // ============================================================================
        // Details Text Updates
        // ============================================================================

        private void SetDetailsText(string text)
        {
            _detailsText.text = text;
        }

        private void AppendDetailsText(string text)
        {
            _detailsText.text += text;
        }

        private void UpdateDetailsForUser(UserInfo user)
        {
            var text = "👤 USER INFORMATION\n";
            text += "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n";
            text += $"Name: {user.FirstName} {user.LastName}\n";
            text += $"ID: {user.Id}\n";
            text += $"Email: {user.Email}\n";
            text += $"Status: {user.Status}\n";
            text += $"Created: {FormatTimestamp(user.CreatedTimestamp)}\n";
            if (!string.IsNullOrEmpty(user.OrganizationId))
            {
                text += $"Organization ID: {user.OrganizationId}\n";
            }
            SetDetailsText(text);
        }

        private void UpdateDetailsForOrganization(OrganizationInfo org)
        {
            var text = "🏢 ORGANIZATION INFORMATION\n";
            text += "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n";
            text += $"Name: {org.Name}\n";
            text += $"ID: {org.Id}\n";
            text += $"Status: {org.Status}\n";
            text += $"Created: {FormatTimestamp(org.CreatedTimestamp)}\n";
            SetDetailsText(text);
        }

        private void UpdateDetailsForSite(SiteInfo site)
        {
            var text = "📍 SITE INFORMATION\n";
            text += "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n";
            text += $"Name: {site.Name}\n";
            text += $"ID: {site.Id}\n";
            text += $"Status: {site.Status}\n";
            text += $"Organization ID: {site.OrganizationId}\n";
            if (site.HasLocation)
            {
                text += $"Location: ({site.Latitude:F6}, {site.Longitude:F6})\n";
            }
            else
            {
                text += "Location: Not available\n";
            }
            if (!string.IsNullOrEmpty(site.ParentSiteId))
            {
                text += $"Parent Site ID: {site.ParentSiteId}\n";
            }
            SetDetailsText(text);
        }

        private void UpdateDetailsForAsset(AssetInfo asset)
        {
            var text = "📦 ASSET INFORMATION\n";
            text += "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n";
            text += $"Name: {asset.Name}\n";
            text += $"ID: {asset.Id}\n";
            text += $"Type: {asset.AssetType}\n";
            text += $"Status: {asset.AssetStatus}\n";
            text += $"Site ID: {asset.SiteId}\n";
            if (!string.IsNullOrEmpty(asset.Description))
            {
                text += $"Description: {asset.Description}\n";
            }
            if (asset.Deployment != AssetDeploymentType.Unspecified)
            {
                text += $"Deployment: {asset.Deployment}\n";
            }
            if (!string.IsNullOrEmpty(asset.PipelineJobId))
            {
                text += $"Pipeline Job ID: {asset.PipelineJobId}\n";
            }
            if (asset.PipelineJobStatus != AssetPipelineJobStatus.Unspecified)
            {
                text += $"Pipeline Status: {asset.PipelineJobStatus}\n";
            }
            // Typed asset data
            if (asset.MeshData.HasValue)
            {
                var mesh = asset.MeshData.Value;
                text += $"Mesh Root Node ID: {mesh.RootNodeId}\n";
                text += $"Mesh Coverage: {mesh.MeshCoverage} m²\n";
                if (mesh.NodeIds.Count > 0)
                {
                    text += $"Node IDs ({mesh.NodeIds.Count}): {string.Join(", ", mesh.NodeIds)}\n";
                }
            }
            if (asset.SplatData.HasValue)
            {
                text += $"Splat Root Node ID: {asset.SplatData.Value.RootNodeId}\n";
            }
            if (asset.VpsData.HasValue)
            {
                text += $"VPS Anchor Payload: {asset.VpsData.Value.AnchorPayload}\n";
            }
            if (asset.SourceScanIds.Count > 0)
            {
                text += $"Source Scan IDs ({asset.SourceScanIds.Count}): {string.Join(", ", asset.SourceScanIds)}\n";
            }
            SetDetailsText(text);
        }

        private string FormatTimestamp(long timestamp)
        {
            var dateTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
            return dateTime.ToString("g");
        }

        // ============================================================================
        // Button Management
        // ============================================================================

        private void ClearButtons()
        {
            foreach (var button in _optionButtons)
            {
                Destroy(button.gameObject);
            }
            _optionButtons.Clear();
        }

        private void CreateButton(string text, Action onClick)
        {
            if (_optionButtonPrefab == null)
            {
                Debug.LogError("SitesListManager: _optionButtonPrefab is not assigned!");
                return;
            }

            if (_optionButtonsContainer == null)
            {
                Debug.LogError("SitesListManager: _optionButtonsContainer is not assigned!");
                return;
            }

            var button = Instantiate(_optionButtonPrefab, _optionButtonsContainer);
            button.DetailsText = text;
            button.OnClickedEvent.AddListener(() => onClick?.Invoke());
            _optionButtons.Add(button);
        }

        private void CreateBackButton(string text, Action onClick)
        {
            CreateButton(text, onClick);
        }

        private void CreateOrganizationButtons()
        {
            ClearButtons();

            foreach (var org in _currentOrganizations)
            {
                var orgCapture = org; // Capture for closure
                CreateButton(org.Name, () => OrganizationTapped(orgCapture));
            }

            if (_currentOrganizations.Count == 0)
            {
                AppendDetailsText("\n\n⚠️ No organizations available");
            }
        }

        private void CreateSiteButtons()
        {
            ClearButtons();

            CreateBackButton("← Back to Organizations", BackToOrganizations);

            foreach (var site in _currentSites)
            {
                var siteCapture = site; // Capture for closure
                CreateButton(site.Name, () => SiteTapped(siteCapture));
            }

            if (_currentSites.Count == 0)
            {
                AppendDetailsText("\n\n⚠️ No sites available");
            }
        }

        private void CreateAssetButtons()
        {
            ClearButtons();

            CreateBackButton("← Back to Sites", BackToSites);

            foreach (var asset in _currentAssets)
            {
                var assetCapture = asset; // Capture for closure
                CreateButton($"{asset.Name} ({asset.AssetType})", () => AssetTapped(assetCapture));
            }

            if (_currentAssets.Count == 0)
            {
                AppendDetailsText("\n\n⚠️ No assets available");
            }
        }

        // ============================================================================
        // Navigation Actions
        // ============================================================================

        private void OrganizationTapped(OrganizationInfo org)
        {
            ClearFilter();
            LoadSitesForOrganization(org);
        }

        private void SiteTapped(SiteInfo site)
        {
            ClearFilter();
            LoadAssetsForSite(site);
        }

        private void AssetTapped(AssetInfo asset)
        {
            ClearFilter();
            UpdateDetailsForAsset(asset);

            ClearButtons();
            CreateBackButton("← Back to Assets", BackToAssets);
        }

        private void BackToOrganizations()
        {
            ClearFilter();
            if (_currentUser.HasValue)
            {
                UpdateDetailsForUser(_currentUser.Value);
            }
            CreateOrganizationButtons();
        }

        private void BackToSites()
        {
            ClearFilter();
            if (_selectedOrganization.HasValue)
            {
                UpdateDetailsForOrganization(_selectedOrganization.Value);
            }
            CreateSiteButtons();
        }

        private void BackToAssets()
        {
            ClearFilter();
            if (_selectedSite.HasValue)
            {
                UpdateDetailsForSite(_selectedSite.Value);
            }
            CreateAssetButtons();
        }
    }
}
