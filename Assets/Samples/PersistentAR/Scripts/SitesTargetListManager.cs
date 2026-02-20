// Copyright 2022-2026 Niantic.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR.Sites;
using NianticSpatial.NSDK.AR.Utilities;
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

    [SerializeField]
    private int _maxItemInstances;

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
        _cancellationTokenSource = new CancellationTokenSource();
        _retryHelper = new AuthRetryHelper();
        _requestButton.interactable = true;
        _scrollListContent = _scrollList.content.gameObject;
    }

    private void OnDestroy()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        ClearListContent();
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
    }

    private async void LoadInitialData()
    {
        if (_sitesClientManager == null)
        {
            _requestStatusText.text = "❌ SitesClientManager is not assigned!";
            Debug.LogError("VpsCoverageTargetListManagerSites: _sitesClientManager is not assigned!");
            return;
        }

        _requestStatusText.text = "⏳ Loading user info...";
        ClearListContent();

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
                _requestStatusText.text = "❌ Failed to retrieve user information.\nMake sure you're authenticated.";
                return;
            }

            _currentUser = userResult.User.Value;

            // Request organizations for user with retry
            var orgsResult = await _retryHelper.WithRetryAsync(() => _sitesClientManager.GetOrganizationsForUserAsync(_currentUser.Value.Id));

            if (_cancellationTokenSource.IsCancellationRequested)
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
                _requestStatusText.text = "⚠️ No organizations found";
            }
        }
        catch (System.Exception e)
        {
            if (!_cancellationTokenSource.IsCancellationRequested)
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

        try
        {
            var result = await _retryHelper.WithRetryAsync(() => _sitesClientManager.GetSitesForOrganizationAsync(org.Id));

            if (_cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

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
                    if (_cancellationTokenSource.IsCancellationRequested)
                        return (Tuple<SiteInfo, AssetInfo>?)null;

                    try
                    {
                        var assetsResult = await _retryHelper.WithRetryAsync(() => _sitesClientManager.GetAssetsForSiteAsync(site.Id));
                        if (assetsResult.Status != SitesRequestStatus.Success)
                            return (Tuple<SiteInfo, AssetInfo>?)null;

                        // Check if site has at least one VPS asset (optionally Production only).
                        AssetInfo? matchedAsset = null;
                        foreach (var asset in assetsResult.Assets)
                        {
                            if (asset.VpsData.HasValue &&
                                !string.IsNullOrEmpty(asset.VpsData.Value.AnchorPayload) &&
                                (!_filterProductionAssetsToggle || asset.Deployment == AssetDeploymentType.Production))
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

                    return (Tuple<SiteInfo, AssetInfo>?)null;
                }).ToList();

                var fetchResults = await Task.WhenAll(fetchTasks);

                if (_cancellationTokenSource.IsCancellationRequested)
                {
                    return;
                }

                foreach (var tuple in fetchResults)
                {
                    if (tuple != null)
                        _currentSites.Add(tuple);
                }

                if (_cancellationTokenSource.IsCancellationRequested)
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

        var max = _maxItemInstances == 0 ? _currentOrganizations.Count :
                    Math.Min(_maxItemInstances, _currentOrganizations.Count);

        for (int i = 0; i < max; ++i)
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

        var max = _maxItemInstances == 0 ? _currentSites.Count :
                    Math.Min(_maxItemInstances, _currentSites.Count);

        for (int i = 0; i < max; ++i)
        {
            var siteTuple = _currentSites[i];
            if (_scrollListContent == null)
                return;
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
