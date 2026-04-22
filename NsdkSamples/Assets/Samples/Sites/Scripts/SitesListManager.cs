using System;
using System.Collections.Generic;
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
        private Button _optionButtonPrefab;

        [SerializeField]
        private Transform _inputFieldsContainer;

        [SerializeField]
        private InputField _inputFieldPrefab;

        // State
        private UserInfo? _currentUser;
        private List<OrganizationInfo> _currentOrganizations = new();
        private List<SiteInfo> _currentSites = new();
        private List<AssetInfo> _currentAssets = new();
        private List<SiteAssetsInfo> _currentNearMeEntries = new();
        private readonly List<Button> _optionButtons = new();
        private readonly List<InputField> _inputFields = new();

        // Retry helper
        private AuthRetryHelper _retryHelper;

        private OrganizationInfo? _selectedOrganization;
        private SiteInfo? _selectedSite;
        private bool _fromNearMe;

        private CancellationTokenSource _cancellationTokenSource;

        private const string DefaultLatitude = "";
        private const string DefaultLongitude = "";
        private const string DefaultRadius = "";

        private void Start()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _retryHelper = new AuthRetryHelper();
            ShowModeSelection();
        }

        private void OnDestroy()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
        }

        // ============================================================================
        // Mode Selection
        // ============================================================================

        private void ShowModeSelection()
        {
            ClearInputFields();
            ClearButtons();
            SetDetailsText("Select a mode to explore sites:");
            CreateButton("Display Sites Near Me", StartNearMeFlow);
            CreateButton("Display Sites From Orgs", StartFromOrgsFlow);
        }

        // ============================================================================
        // Near Me Flow
        // ============================================================================

        private void StartNearMeFlow()
        {
            ClearInputFields();
            ClearButtons();
            SetDetailsText("Enter coordinates to search for nearby sites:");
            // _inputFields[0]=lat, [1]=lng, [2]=radius
            CreateInputField("Latitude", DefaultLatitude);
            CreateInputField("Longitude", DefaultLongitude);
            CreateInputField("Radius (meters)", DefaultRadius);
            CreateBackButton("← Back to Mode Selection", ShowModeSelection);
            CreateButton("Search Nearby Sites", OnSearchNearMeClicked);
        }

        private void OnSearchNearMeClicked()
        {
            if (_inputFields.Count < 3)
            {
                SetDetailsText("❌ Coordinate input fields are missing.");
                return;
            }

            if (!double.TryParse(_inputFields[0].text, out double lat))
            {
                SetDetailsText("❌ Invalid latitude value.");
                return;
            }
            if (!double.TryParse(_inputFields[1].text, out double lng))
            {
                SetDetailsText("❌ Invalid longitude value.");
                return;
            }
            if (!double.TryParse(_inputFields[2].text, out double radius) || radius <= 0)
            {
                SetDetailsText("❌ Invalid radius value.");
                return;
            }
            LoadNearMeSites(lat, lng, radius);
        }

        private async void LoadNearMeSites(double lat, double lng, double radiusMeters)
        {
            if (_sitesClientManager == null)
            {
                SetDetailsText("❌ SitesClientManager is not assigned!");
                Debug.LogError("SitesListManager: _sitesClientManager is not assigned!");
                return;
            }

            ClearInputFields();
            ClearButtons();
            SetDetailsText("⏳ Searching for sites near you...");

            var token = _cancellationTokenSource.Token;

            try
            {
                var result = await _retryHelper.WithRetryAsync(
                    (ct) => _sitesClientManager.GetSiteAssetsByLocationAsync(
                        lat, lng, radiusMeters, AssetType.VpsInfo, ct),
                    token);

                if (_cancellationTokenSource.IsCancellationRequested) return;

                if (result.Status == SitesRequestStatus.Success && result.Entries.Length > 0)
                {
                    _currentNearMeEntries = new List<SiteAssetsInfo>(result.Entries);
                    SetDetailsText($"Found {_currentNearMeEntries.Count} site(s) near ({lat:F4}, {lng:F4}):");
                    CreateNearMeSiteButtons();
                }
                else
                {
                    SetDetailsText($"⚠️ No sites found within {radiusMeters:F0}m of ({lat:F4}, {lng:F4}).");
                    CreateBackButton("← Back", StartNearMeFlow);
                }
            }
            catch (System.Exception e)
            {
                if (!_cancellationTokenSource.IsCancellationRequested)
                {
                    Debug.LogException(e);
                    SetDetailsText($"❌ Error searching nearby sites: {e.Message}");
                    CreateBackButton("← Back", StartNearMeFlow);
                }
            }
        }

        private void CreateNearMeSiteButtons()
        {
            ClearButtons();
            // _inputFields[0] = filter
            var filterField = CreateInputField("Filter sites...", "");
            filterField.onValueChanged.AddListener(OnFilterChanged);
            CreateBackButton("← Back to Search", StartNearMeFlow);
            foreach (var entry in _currentNearMeEntries)
            {
                var entryCapture = entry;
                int distanceM = Mathf.RoundToInt((float)entry.Distance);
                string label = $"{entry.Site.Name} ({distanceM}m · {entry.Assets.Length} assets)";
                CreateButton(label, () => NearMeSiteTapped(entryCapture));
            }
        }

        private void NearMeSiteTapped(SiteAssetsInfo entry)
        {
            _selectedSite = entry.Site;
            _currentAssets = new List<AssetInfo>(entry.Assets);
            _currentSites = new List<SiteInfo> { entry.Site };
            _fromNearMe = true;
            UpdateDetailsForSite(entry.Site);
            CreateAssetButtons(fromNearMe: true);
        }

        private void BackToNearMeView()
        {
            ClearInputFields();
            SetDetailsText($"Found {_currentNearMeEntries.Count} site(s) nearby:");
            CreateNearMeSiteButtons();
        }

        // ============================================================================
        // From Orgs Flow
        // ============================================================================

        private void StartFromOrgsFlow()
        {
            ClearInputFields();
            ClearButtons();
            LoadInitialData();
        }

        private async void LoadInitialData()
        {
            if (_sitesClientManager == null)
            {
                SetDetailsText("❌ SitesClientManager is not assigned!");
                Debug.LogError("SitesListManager: _sitesClientManager is not assigned!");
                return;
            }

            SetDetailsText("⏳ Loading user info...");

            var token = _cancellationTokenSource.Token;

            try
            {
                var userResult = await _retryHelper.WithRetryAsync(
                    (ct) => _sitesClientManager.GetSelfUserInfoAsync(ct), token);

                if (_cancellationTokenSource.IsCancellationRequested) return;

                if (userResult.Status != SitesRequestStatus.Success || !userResult.User.HasValue)
                {
                    SetDetailsText("❌ Failed to retrieve user information.\nMake sure you're authenticated.");
                    CreateBackButton("← Back to Mode Selection", ShowModeSelection);
                    return;
                }

                _currentUser = userResult.User.Value;
                _fromNearMe = false;
                UpdateDetailsForUser(_currentUser.Value);

                var orgsResult = await _retryHelper.WithRetryAsync(
                    (ct) => _sitesClientManager.GetOrganizationsForUserAsync(_currentUser.Value.Id, ct), token);

                if (_cancellationTokenSource.IsCancellationRequested) return;

                if (orgsResult.Status == SitesRequestStatus.Success)
                {
                    _currentOrganizations = new List<OrganizationInfo>(orgsResult.Organizations);
                    CreateOrganizationButtons();
                }
                else
                {
                    AppendDetailsText("\n\n⚠️ No organizations found");
                    CreateBackButton("← Back to Mode Selection", ShowModeSelection);
                }
            }
            catch (System.Exception e)
            {
                if (!_cancellationTokenSource.IsCancellationRequested)
                {
                    Debug.LogException(e);
                    SetDetailsText($"❌ Error loading data: {e.Message}\n\nStack: {e.StackTrace}");
                    CreateBackButton("← Back to Mode Selection", ShowModeSelection);
                }
            }
        }

        private async void LoadSitesForOrganization(OrganizationInfo org)
        {
            _selectedOrganization = org;
            UpdateDetailsForOrganization(org);
            ClearInputFields();
            ClearButtons();
            AppendDetailsText("\n\n⏳ Loading sites...");

            try
            {
                var token = _cancellationTokenSource.Token;
                var result = await _retryHelper.WithRetryAsync(
                    (ct) => _sitesClientManager.GetSitesForOrganizationAsync(org.Id, ct), token);

                if (_cancellationTokenSource.IsCancellationRequested) return;

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
            ClearInputFields();
            ClearButtons();
            AppendDetailsText("\n\n⏳ Loading assets...");

            try
            {
                var token = _cancellationTokenSource.Token;
                var result = await _retryHelper.WithRetryAsync(
                    (ct) => _sitesClientManager.GetAssetsForSiteAsync(site.Id, ct), token);

                if (_cancellationTokenSource.IsCancellationRequested) return;

                if (result.Status == SitesRequestStatus.Success)
                {
                    _currentAssets = new List<AssetInfo>(result.Assets);
                    UpdateDetailsForSite(site);
                    CreateAssetButtons(fromNearMe: false);
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

        private void SetDetailsText(string text) => _detailsText.text = text;
        private void AppendDetailsText(string text) => _detailsText.text += text;

        private void UpdateDetailsForUser(UserInfo user)
        {
            var text = "👤 USER INFORMATION\n";
            text += "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n";
            text += $"Name: {user.FirstName} {user.LastName}\n";
            text += $"ID: {user.Id}\n";
            text += $"Email: {user.Email}\n";
            text += $"Status: {user.Status}\n";
            text += $"Created: {FormatTimestamp(user.CreatedTimestamp)}\n";
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
                text += $"Location: ({site.Latitude:F6}, {site.Longitude:F6})\n";
            else
                text += "Location: Not available\n";
            if (!string.IsNullOrEmpty(site.ParentSiteId))
                text += $"Parent Site ID: {site.ParentSiteId}\n";
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
                text += $"Description: {asset.Description}\n";
            if (asset.Deployment != AssetDeploymentType.Unspecified)
                text += $"Deployment: {asset.Deployment}\n";
            if (!string.IsNullOrEmpty(asset.PipelineJobId))
                text += $"Pipeline Job ID: {asset.PipelineJobId}\n";
            if (asset.PipelineJobStatus != AssetPipelineJobStatus.Unspecified)
                text += $"Pipeline Status: {asset.PipelineJobStatus}\n";
            if (asset.MeshData.HasValue)
            {
                var mesh = asset.MeshData.Value;
                text += $"Mesh Root Node ID: {mesh.RootNodeId}\n";
                text += $"Mesh Coverage: {mesh.MeshCoverage} m²\n";
                if (mesh.NodeIds.Count > 0)
                    text += $"Node IDs ({mesh.NodeIds.Count}): {string.Join(", ", mesh.NodeIds)}\n";
            }
            if (asset.SplatData.HasValue)
                text += $"Splat Root Node ID: {asset.SplatData.Value.RootNodeId}\n";
            if (asset.VpsData.HasValue)
                text += $"VPS Anchor Payload: {asset.VpsData.Value.AnchorPayload}\n";
            if (asset.SourceScanIds.Count > 0)
                text += $"Source Scan IDs ({asset.SourceScanIds.Count}): {string.Join(", ", asset.SourceScanIds)}\n";
            SetDetailsText(text);
        }

        private string FormatTimestamp(long timestamp)
        {
            return DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime.ToString("g");
        }

        // ============================================================================
        // Button Management
        // ============================================================================

        private void ClearButtons()
        {
            foreach (var button in _optionButtons)
                Destroy(button.gameObject);
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
            button.GetComponentInChildren<Text>().text = text;
            button.onClick.AddListener(() => onClick?.Invoke());
            _optionButtons.Add(button);
        }

        private void CreateBackButton(string text, Action onClick) => CreateButton(text, onClick);

        private void CreateOrganizationButtons()
        {
            ClearButtons();
            var filterField = CreateInputField("Filter organizations...", "");
            filterField.onValueChanged.AddListener(OnFilterChanged);
            CreateBackButton("← Back to Mode Selection", ShowModeSelection);
            foreach (var org in _currentOrganizations)
            {
                var orgCapture = org;
                CreateButton(org.Name, () => OrganizationTapped(orgCapture));
            }
            if (_currentOrganizations.Count == 0)
                AppendDetailsText("\n\n⚠️ No organizations available");
        }

        private void CreateSiteButtons()
        {
            ClearButtons();
            var filterField = CreateInputField("Filter sites...", "");
            filterField.onValueChanged.AddListener(OnFilterChanged);
            CreateBackButton("← Back to Organizations", BackToOrganizations);
            foreach (var site in _currentSites)
            {
                var siteCapture = site;
                CreateButton(site.Name, () => SiteTapped(siteCapture));
            }
            if (_currentSites.Count == 0)
                AppendDetailsText("\n\n⚠️ No sites available");
        }

        private void CreateAssetButtons(bool fromNearMe = false)
        {
            ClearButtons();
            string backLabel = fromNearMe ? "← Back to Sites Near Me" : "← Back to Sites";
            Action backAction = fromNearMe ? (Action)BackToNearMeView : BackToSites;
            CreateBackButton(backLabel, backAction);
            foreach (var asset in _currentAssets)
            {
                var assetCapture = asset;
                CreateButton($"{asset.Name} ({asset.AssetType})", () => AssetTapped(assetCapture));
            }
            if (_currentAssets.Count == 0)
                AppendDetailsText("\n\n⚠️ No assets available");
        }

        // ============================================================================
        // Input Field Management
        // ============================================================================

        private InputField CreateInputField(string placeholder, string defaultValue)
        {
            if (_inputFieldPrefab == null)
            {
                Debug.LogError("SitesListManager: _inputFieldPrefab is not assigned!");
                return null;
            }
            if (_inputFieldsContainer == null)
            {
                Debug.LogError("SitesListManager: _inputFieldsContainer is not assigned!");
                return null;
            }
            var field = Instantiate(_inputFieldPrefab, _inputFieldsContainer);
            field.text = defaultValue;
            if (field.placeholder is Text placeholderText)
                placeholderText.text = placeholder;
            _inputFields.Add(field);
            return field;
        }

        private void ClearInputFields()
        {
            foreach (var field in _inputFields)
                Destroy(field.gameObject);
            _inputFields.Clear();
        }

        // ============================================================================
        // Filter
        // ============================================================================

        private void OnFilterChanged(string filterText)
        {
            var lowerFilter = filterText?.ToLower() ?? "";
            for (int i = 0; i < _optionButtons.Count; i++)
            {
                var button = _optionButtons[i];
                bool isBackButton = i == 0 && (button.GetComponentInChildren<Text>()?.text.StartsWith("←") ?? false);
                if (string.IsNullOrEmpty(lowerFilter) || isBackButton)
                    button.gameObject.SetActive(true);
                else
                    button.gameObject.SetActive(
                        button.GetComponentInChildren<Text>()?.text.ToLower().Contains(lowerFilter) ?? false);
            }
        }

        // ============================================================================
        // Navigation Actions
        // ============================================================================

        private void OrganizationTapped(OrganizationInfo org) => LoadSitesForOrganization(org);
        private void SiteTapped(SiteInfo site) => LoadAssetsForSite(site);

        private void AssetTapped(AssetInfo asset)
        {
            ClearInputFields();
            UpdateDetailsForAsset(asset);
            ClearButtons();
            string backLabel = _fromNearMe ? "← Back to Assets (Near Me)" : "← Back to Assets";
            CreateBackButton(backLabel, BackToAssets);
        }

        private void BackToOrganizations()
        {
            ClearInputFields();
            if (_currentUser.HasValue) UpdateDetailsForUser(_currentUser.Value);
            CreateOrganizationButtons();
        }

        private void BackToSites()
        {
            ClearInputFields();
            if (_selectedOrganization.HasValue) UpdateDetailsForOrganization(_selectedOrganization.Value);
            CreateSiteButtons();
        }

        private void BackToAssets()
        {
            ClearInputFields();
            if (_selectedSite.HasValue) UpdateDetailsForSite(_selectedSite.Value);
            CreateAssetButtons(fromNearMe: _fromNearMe);
        }
    }
}
