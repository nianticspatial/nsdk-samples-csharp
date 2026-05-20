// Copyright 2022-2026 Niantic.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR.Sites;
using NianticSpatial.NSDK.AR.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SitesTargetListManager : MonoBehaviour
{
    public struct AnchorSelectedArgs
    {
        public string Payload;
        public string Name;
    }

    [Header("ScrollList Setup")]
    [SerializeField]
    [Tooltip("The scroll list holding the items for each target information")]
    private ScrollRect _scrollList;

    [SerializeField]
    [Tooltip("Template item for target information")]
    private Vps2TargetListItem _itemPrefab;

    [SerializeField]
    [Tooltip("Back navigation button")]
    private Vps2TargetListItem _backButtonPrefab;

    [Header("UI Setup")]
    [SerializeField]
    [Tooltip("Button to request to reload the list")]
    private Button _requestButton;

    [SerializeField]
    [Tooltip("Text to display request status")]
    private Text _requestStatusText;

    [Header("Sites API")]
    [SerializeField]
    [Tooltip("Sites Client Manager for API calls")]
    private SitesClientManager _sitesClientManager;

    [SerializeField]
    [Tooltip("Filter sites to only include those with Production assets")]
    private bool _filterProductionAssetsToggle;

    [SerializeField]
    [Tooltip("Input field to filter sites")]
    private TMP_InputField _siteFilterInputField;

    public event Action<AnchorSelectedArgs> OnAnchorButtonPressed;

    private readonly List<Vps2TargetListItem> _targetListItemInstances = new();
    private GameObject _scrollListContent;

    // Sites API state
    private UserInfo? _currentUser;
    private List<OrganizationInfo> _currentOrganizations = new();
    private List<Tuple<SiteInfo, AssetInfo>> _currentSites = new();
    private AuthRetryHelper _retryHelper;
    private CancellationTokenSource _cancellationTokenSource;

    private OrganizationInfo? _selectedOrganization;

    private void Start()
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            destroyCancellationToken, _sitesClientManager.destroyCancellationToken);
        _retryHelper = new AuthRetryHelper();
        _requestButton.interactable = true;
        _scrollListContent = _scrollList.content.gameObject;
        _siteFilterInputField.onValueChanged.AddListener(OnFilterChanged);
        _siteFilterInputField.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        _cancellationTokenSource?.Dispose();
        ClearListContent();
        _siteFilterInputField.onValueChanged.RemoveListener(OnFilterChanged);
    }
    private void OnFilterChanged(string filterText)
        {
            ApplyFilter(filterText);
        }

        private void ApplyFilter(string filterText)
        {
            var lowerFilter = filterText?.ToLower() ?? "";
            for (int i = 0; i < _targetListItemInstances.Count; i++)
            {
                var item = _targetListItemInstances[i];
                // Skip filtering the first button if it's a back button
                bool isBackButton = i == 0 && (item.TitleLabelText?.StartsWith("←") ?? false);

                if (string.IsNullOrEmpty(lowerFilter) || isBackButton)
                {
                    item.gameObject.SetActive(true);
                }
                else
                {
                    var itemText = item.TitleLabelText?.ToLower() ?? "";
                    item.gameObject.SetActive(itemText.Contains(lowerFilter));
                }
            }
        }

        private void ClearFilter()
        {
            _siteFilterInputField.text = "";
        }


    public void RequestAreas()
    {
        _requestStatusText.text = "Loading organizations...";
        _scrollList.gameObject.SetActive(true);
        _currentSites.Clear();
        LoadInitialData();
    }

    public void CloseList()
    {
        ClearListContent();
        _scrollList.gameObject.SetActive(false);
        _siteFilterInputField.gameObject.SetActive(false);
    }

    private async void LoadInitialData()
    {
        if (_sitesClientManager == null)
        {
            _requestStatusText.text = "❌ SitesClientManager is not assigned!";
            Debug.LogError("SitesTargetListManager: _sitesClientManager is not assigned!");
            return;
        }

        _requestStatusText.text = "⏳ Loading organizations...";
        ClearListContent();

        var token = _cancellationTokenSource.Token;

        try
        {
            var orgsResult = await _retryHelper.WithRetryAsync((ct) => _sitesClientManager.GetSelfOrganizationInfoAsync(ct), token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            if (orgsResult.Status == SitesRequestStatus.Success)
            {
                _currentOrganizations = new List<OrganizationInfo>(orgsResult.Organizations);
                CreateOrganizationListItems();
            }
            else
            {
                _requestStatusText.text = "⚠️ No organizations found. Make sure you're authenticated.";
            }
        }
        catch (System.Exception e)
        {
            if (!token.IsCancellationRequested)
            {
                Debug.LogException(e);
                _requestStatusText.text = $"❌ Error loading data: {e.Message}";
            }
        }
    }

    private async void LoadSitesForOrganization(OrganizationInfo org)
    {
        _selectedOrganization = org;
        _requestStatusText.text = $"⏳ Loading sites for {org.Name}...";
        ClearListContent();
        _currentSites.Clear();

        var token = _cancellationTokenSource.Token;

        try
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            var result = await _retryHelper.WithRetryAsync((ct) =>
                _sitesClientManager.GetSitesForOrganizationAsync(org.Id, ct), token);

            if (result.Status == SitesRequestStatus.Success)
            {
                if (result.Sites.Length == 0)
                {
                    _requestStatusText.text = $"⚠️ No sites with location found for {org.Name}";
                    CreateBackButton("← Back", BackToOrganizations);
                    return;
                }

                // Filter sites to only include those with Production VPS assets.
                // Fetch assets for all sites in parallel to avoid 10+ second sequential waits.
                var fetchTasks = result.Sites.Select(async site =>
                {
                    if (token.IsCancellationRequested)
                        return null;

                    try
                    {
                        var assetsResult = await _retryHelper.WithRetryAsync((ct) =>
                            _sitesClientManager.GetAssetsForSiteAsync(site.Id, ct), token);
                        if (assetsResult.Status != SitesRequestStatus.Success)
                            return null;

                        // Check if site has at least one VPS asset (optionally Production only).
                        AssetInfo? matchedAsset = null;
                        foreach (var asset in assetsResult.Assets)
                        {
                            if (asset.VpsData.HasValue &&
                                !string.IsNullOrEmpty(asset.VpsData.Value.AnchorPayload) &&
                                (!_filterProductionAssetsToggle ||
                                 asset.Deployment == AssetDeploymentType.Production))
                            {
                                matchedAsset = asset;
                                break;
                            }
                        }

                        if (matchedAsset.HasValue)
                            return new Tuple<SiteInfo, AssetInfo>(site, matchedAsset.Value);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"Failed to load assets for site {site.Name}: {e.Message}");
                    }

                    return null;
                }).ToList();

                var fetchResults = await Task.WhenAll(fetchTasks);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                foreach (var tuple in fetchResults)
                {
                    if (tuple != null)
                    {
                        _currentSites.Add(tuple);
                    }
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                CreateSiteListItems();
            }
            else
            {
                _requestStatusText.text = $"⚠️ No sites found for {org.Name}";
                CreateBackButton("← Back", BackToOrganizations);
            }
        }
        catch (System.Exception e)
        {
            _requestStatusText.text = $"❌ Error loading sites: {e.Message}";
            CreateBackButton("← Back", BackToOrganizations);
        }
    }
    

    private void LocalizeToSite(SiteInfo site)
    {
        ClearListContent();

        // Find the site and its associated asset from the cached tuple list
        var siteTuple = _currentSites.FirstOrDefault(tuple => tuple.Item1.Id == site.Id);
        
        if (siteTuple == null)
        {
            _requestStatusText.text = $"⚠️ Site {site.Name} not found";
            CreateBackButton("← Back", BackToSites);
            return;
        }

        var asset = siteTuple.Item2;

        if (asset.VpsData.HasValue && !string.IsNullOrEmpty(asset.VpsData.Value.AnchorPayload))
        {
            var payload = asset.VpsData.Value.AnchorPayload;
            OnAnchorButtonPressed?.Invoke(new AnchorSelectedArgs
            {
                Payload = payload,
                Name = asset.Name
            });
        }
        else
        {
            _requestStatusText.text = $"⚠️ No VPS payload found for {site.Name}";
            CreateBackButton("← Back", BackToSites);
        }
    }

    private void CreateOrganizationListItems()
    {
        ClearListContent();

        if (_currentOrganizations.Count == 0)
        {
            _requestStatusText.text = "⚠️ No organizations available";
            return;
        }

        _requestStatusText.text = $"Found {_currentOrganizations.Count} organization(s)";
        _siteFilterInputField.gameObject.SetActive(false);

        for (int i = 0; i < _currentOrganizations.Count; ++i)
        {
            var org = _currentOrganizations[i];
            if (_scrollListContent == null)
                return;

            var targetListItemInstance = Instantiate(_itemPrefab, _scrollListContent.transform, false);
            FillOrganizationItem(targetListItemInstance, org);
            _targetListItemInstances.Add(targetListItemInstance);
        }

        UpdateScrollListSize();
    }

    private void CreateSiteListItems()
    {
        ClearListContent();

        // Add back button
        if (_selectedOrganization.HasValue)
        {
            CreateBackButton("← Back", BackToOrganizations);
        }

        if (_currentSites.Count == 0)
        {
            _requestStatusText.text = "⚠️ No sites available";
            return;
        }

        _requestStatusText.text = $"Found {_currentSites.Count} site(s)";
        _siteFilterInputField.gameObject.SetActive(true);
        
        if (_scrollListContent == null)
            return;
        
        _currentSites = _currentSites.OrderBy(tuple => tuple.Item1.Name).ToList();
        foreach (var siteTuple in _currentSites)
        {
            var targetListItemInstance = Instantiate(_itemPrefab, _scrollListContent.transform, false);
            FillSiteItem(targetListItemInstance, siteTuple.Item1);
            _targetListItemInstances.Add(targetListItemInstance);
        }

        UpdateScrollListSize();
    }


    private void CreateBackButton(string text, Action onClick)
    {
        if (_scrollListContent == null || _backButtonPrefab == null)
            return;

        var backButtonInstance = Instantiate(_backButtonPrefab, _scrollListContent.transform, false);
        
        // Use navigate button for back navigation
        SetButtonText(backButtonInstance, true, text);
        backButtonInstance.SubscribeToNavigateButton(() => onClick?.Invoke());
        _targetListItemInstances.Add(backButtonInstance);
    }

    private void FillOrganizationItem(Vps2TargetListItem item, OrganizationInfo org)
    {
        item.transform.name = org.Name;
        item.TitleLabelText = org.Name;
        item.DistanceLabelText = $"Organization ID: {org.Id}";

        // Set navigate button text and action
        SetButtonText(item, true, "Select");
        item.SubscribeToNavigateButton(() =>
        {
            LoadSitesForOrganization(org);
        });
        // Hide copy button
        HideCopyButton(item);
    }

    private void FillSiteItem(Vps2TargetListItem item, SiteInfo site)
    {
        item.transform.name = site.Name;
        item.TitleLabelText = site.Name;
        
        string distanceText = $"Site ID: {site.Id}";
        if (site.HasLocation)
        {
            distanceText += $"\nLocation: ({site.Latitude:F6}, {site.Longitude:F6})";
        }
        item.DistanceLabelText = distanceText;
        
        // Set navigate button text and action (selects site)
        SetButtonText(item, true, "Localize");
        item.SubscribeToNavigateButton(() =>
        {
            LocalizeToSite(site);
        });
        
        // Use copy button for map navigation if location is available
        if (site.HasLocation)
        {
            SetButtonText(item, false, "Open in Maps");
            item.SubscribeToCopyButton(() =>
            {
                OpenRouteInMapApp(site.Latitude, site.Longitude);
            });
        }
        else
        {
            // Hide copy button if no location
            HideCopyButton(item);
        }
    }

    private void BackToOrganizations()
    {
        _selectedOrganization = null;
        CreateOrganizationListItems();
    }

    private void BackToSites()
    {
        if (_selectedOrganization.HasValue)
        {
            CreateSiteListItems();
        }
        else
        {
            BackToOrganizations();
        }
    }

    private void UpdateScrollListSize()
    {
        if (_scrollListContent == null || _itemPrefab == null)
            return;

        var layout = _scrollListContent.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
            return;

        var contentTransform = _scrollListContent.GetComponent<RectTransform>();
        float itemHeight = _itemPrefab.GetComponent<RectTransform>().sizeDelta.y;
        contentTransform.sizeDelta = new Vector2
        (
            contentTransform.sizeDelta.x,
            layout.padding.top + _scrollListContent.transform.childCount * (layout.spacing + itemHeight)
        );

        // Scroll all the way up
        contentTransform.anchoredPosition = new Vector2(0, int.MinValue);
    }

    private void ClearListContent()
    {
        foreach (var item in _targetListItemInstances)
        {
            if (item != null)
            {
                Destroy(item.gameObject);
            }
        }

        _targetListItemInstances.Clear();
    }

    private void OpenRouteInMapApp(double latitude, double longitude)
    {
        var sb = new System.Text.StringBuilder();
        
#if !UNITY_EDITOR && UNITY_ANDROID
        sb.Append("https://www.google.com/maps/dir/?api=1&destination=");
        sb.Append(latitude);
        sb.Append(",");
        sb.Append(longitude);
        sb.Append("&travelmode=walking");
#elif !UNITY_EDITOR && UNITY_IOS
        sb.Append("http://maps.apple.com/?daddr=");
        sb.Append(latitude);
        sb.Append(",");
        sb.Append(longitude);
        sb.Append("&dirflg=w");
#else
        // Default to Google Maps for editor
        sb.Append("https://www.google.com/maps/dir/?api=1&destination=");
        sb.Append(latitude);
        sb.Append(",");
        sb.Append(longitude);
        sb.Append("&travelmode=walking");
#endif

        Application.OpenURL(sb.ToString());
    }

    private void SetButtonText(Vps2TargetListItem item, bool isNavigateButton, string text)
    {
        string buttonName = isNavigateButton ? "NavigateButton" : "CopyButton";
        // Buttons are nested under "Info" container
        Transform infoContainer = item.transform.Find("Info")?.Find("ButtonsArea");
        if (infoContainer != null)
        {
            Transform buttonTransform = infoContainer.Find(buttonName);
            if (buttonTransform != null)
            {
                Text buttonText = buttonTransform.GetComponentInChildren<Text>();
                if (buttonText != null)
                {
                    buttonText.text = text;
                }
            }
        }
    }

    private void HideCopyButton(Vps2TargetListItem item)
    {
        // CopyButton is nested under "Info" container
        Transform infoContainer = item.transform.Find("Info")?.Find("ButtonsArea");
        if (infoContainer != null)
        {
            Transform copyButtonTransform = infoContainer.Find("CopyButton");
            if (copyButtonTransform != null)
            {
                copyButtonTransform.gameObject.SetActive(false);
            }
        }
    }
}
