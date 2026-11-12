// Copyright 2022-2026 Niantic.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR.Sites;
using NianticSpatial.NSDK.AR.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// NSDK's location facade, not UnityEngine.Input: it honors the SDK's location data source
// (device sensors or playback/spoof) and permission handling. This class sits in the global
// namespace, so `Input` would otherwise bind to UnityEngine.Input and never start on device.
using NsdkInput = NianticSpatial.NSDK.AR.Input;

public class SitesTargetListManager : MonoBehaviour
{
    public struct SiteSelectedArgs
    {
        public string SiteId;
        public string Name;
        public double Latitude;
        public double Longitude;

        // The VPS asset picked in the list, production or not. The payload is for the
        // anchor flow and is null when the asset carries no anchor payload.
        public string VpsMapAssetId;
        public string AnchorPayload;
    }

    // Payload for the "localize to nearby sites" flow: a coordinate and search radius
    // rather than a chosen site, forwarded to the ARVps2Manager coordinate overload.
    public struct NearbyLocalizeArgs
    {
        public double Latitude;
        public double Longitude;
        public double RadiusMeters;
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
    [Tooltip("List production VPS assets only at first. A row at the top of the list switches " +
             "between production only and all assets.")]
    private bool _filterProductionAssetsToggle;

    [SerializeField]
    [Tooltip("Input field to filter sites")]
    private TMP_InputField _siteFilterInputField;

    public event Action<SiteSelectedArgs> SiteSelected;

    // Fired when the user localizes to nearby sites via their current location instead of
    // picking a specific site. Carries the GPS fix and search radius.
    public event Action<NearbyLocalizeArgs> NearbyLocalizeSelected;

    // Radius for the nearby-localize flow. Kept small: a wide radius pulls in more
    // candidate maps than VPS2 needs to enroll for a single localization.
    private const double DefaultNearbyRadiusMeters = 100.0;

    private readonly List<Vps2TargetListItem> _targetListItemInstances = new();
    private GameObject _scrollListContent;
    private Button _nearbyLocalizeButton;
    private bool _nearbyLocalizeInProgress;

    // Sites API state
    private UserInfo? _currentUser;
    private List<OrganizationInfo> _currentOrganizations = new();
    // Every VPS asset of the organization's sites, production or not, one entry per asset.
    // The list shows the ones in scope, so switching the scope does not fetch again.
    private List<Tuple<SiteInfo, AssetInfo>> _currentSites = new();
    private bool _productionOnly;
    private Vps2TargetListItem _scopeToggleItem;
    // The asset last picked, which Switch Asset offers the alternatives to.
    private string _selectedSiteId;
    private string _selectedAssetId;
    private AuthRetryHelper _retryHelper;
    private CancellationTokenSource _cancellationTokenSource;

    private OrganizationInfo? _selectedOrganization;

    private void Start()
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            destroyCancellationToken, _sitesClientManager.destroyCancellationToken);
        _retryHelper = new AuthRetryHelper();
        _productionOnly = _filterProductionAssetsToggle;
        _requestButton.interactable = true;
        _scrollListContent = _scrollList.content.gameObject;
        _siteFilterInputField.onValueChanged.AddListener(OnFilterChanged);
        _siteFilterInputField.gameObject.SetActive(false);

        SetButtonLabel(_requestButton, "Request Sites");
        CreateNearbyLocalizeButton();
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
                // Skip filtering the first button if it's a back button, and the scope row
                bool isBackButton = i == 0 && (item.TitleLabelText?.StartsWith("←") ?? false);

                if (string.IsNullOrEmpty(lowerFilter) || isBackButton || item == _scopeToggleItem)
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

                // Keep every VPS asset of every site, production or not: the scope row filters
                // them without fetching again. Fetch assets for all sites in parallel to avoid
                // 10+ second sequential waits.
                var fetchTasks = result.Sites.Select(async site =>
                {
                    var siteAssets = new List<Tuple<SiteInfo, AssetInfo>>();
                    if (token.IsCancellationRequested)
                        return siteAssets;

                    try
                    {
                        // Every asset, production or not: this endpoint has no scope.
                        var assetsResult = await _retryHelper.WithRetryAsync((ct) =>
                            _sitesClientManager.GetAssetsForSiteAsync(site.Id, ct), token);
                        if (assetsResult.Status != SitesRequestStatus.Success)
                            return siteAssets;

                        foreach (var asset in assetsResult.Assets)
                        {
                            if (asset.VpsData.HasValue)
                            {
                                siteAssets.Add(new Tuple<SiteInfo, AssetInfo>(site, asset));
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"Failed to load assets for site {site.Name}: {e.Message}");
                    }

                    return siteAssets;
                }).ToList();

                var fetchResults = await Task.WhenAll(fetchTasks);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                foreach (var fetched in fetchResults)
                {
                    _currentSites.AddRange(fetched);
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


    private void SelectAsset(SiteInfo site, AssetInfo asset)
    {
        ClearListContent();
        _selectedSiteId = site.Id;
        _selectedAssetId = asset.Id;

        SiteSelected?.Invoke(new SiteSelectedArgs
        {
            SiteId = site.Id,
            Name = site.Name,
            Latitude = site.Latitude,
            Longitude = site.Longitude,
            VpsMapAssetId = asset.Id,
            AnchorPayload = asset.VpsData?.AnchorPayload,
        });
    }

    private bool IsInScope(AssetInfo asset) =>
        !_productionOnly || asset.Deployment == AssetDeploymentType.Production;

    private static string DeploymentLabel(AssetDeploymentType deployment) =>
        deployment == AssetDeploymentType.Production ? "Production" : "Not production";

    /// <summary>
    /// Whether the site picked last has another VPS asset in scope to switch to.
    /// </summary>
    public bool CanSwitchAsset => SwitchableAssets().Count > 0;

    private List<Tuple<SiteInfo, AssetInfo>> SwitchableAssets() =>
        _currentSites
            .Where(pair => pair.Item1.Id == _selectedSiteId && pair.Item2.Id != _selectedAssetId &&
                           IsInScope(pair.Item2))
            .OrderBy(pair => pair.Item2.Name)
            .ToList();

    /// <summary>
    /// Lists the other VPS assets of the site picked last. Picking one closes the list and
    /// hands it to <paramref name="onPicked"/>; the back row closes it and calls
    /// <paramref name="onCancelled"/>.
    /// </summary>
    public void ShowSwitchList(Action<AssetInfo> onPicked, Action onCancelled)
    {
        ClearListContent();
        _scrollList.gameObject.SetActive(true);
        _siteFilterInputField.gameObject.SetActive(false);

        CreateBackButton("← Cancel", () =>
        {
            CloseList();
            onCancelled?.Invoke();
        });

        var switchable = SwitchableAssets();
        _requestStatusText.text = switchable.Count == 0
            ? "⚠️ No other VPS asset to switch to"
            : $"Switch to another VPS asset of {switchable[0].Item1.Name}:";
        if (_scrollListContent == null)
            return;

        foreach (var pair in switchable)
        {
            var asset = pair.Item2;
            var item = Instantiate(_itemPrefab, _scrollListContent.transform, false);
            item.transform.name = asset.Name;
            item.TitleLabelText = asset.Name;
            item.DistanceLabelText = $"{DeploymentLabel(asset.Deployment)}\nAsset ID: {asset.Id}";
            SetButtonText(item, true, "Switch");
            item.SubscribeToNavigateButton(() =>
            {
                _selectedAssetId = asset.Id;
                CloseList();
                onPicked?.Invoke(asset);
            });
            HideCopyButton(item);
            _targetListItemInstances.Add(item);
        }

        UpdateScrollListSize();
    }

    /// <summary>
    /// Shows or hides the menu buttons that start a localization session: Request Sites and
    /// Localize to Nearby Sites. A demo hides them while a session runs.
    /// </summary>
    public void SetTargetControlsVisible(bool visible)
    {
        if (_requestButton != null) _requestButton.gameObject.SetActive(visible);
        if (_nearbyLocalizeButton != null) _nearbyLocalizeButton.gameObject.SetActive(visible);
    }

    /// <summary>
    /// Adds a menu button cloned from the request button, so it inherits the sample's button
    /// styling without hand-editing the shared UI prefab. <paramref name="slot"/> is how many
    /// button heights below the request button it sits: 0 takes the request button's place.
    /// </summary>
    public Button CreateMenuButton(string buttonName, string label, Action onClick, int slot)
    {
        if (_requestButton == null)
            return null;

        var button = Instantiate(_requestButton, _requestButton.transform.parent, false);
        button.name = buttonName;
        // The prefab saves Request Sites non-interactable and Start() enables it, which may
        // run after the caller's Start(), so don't inherit its state.
        button.interactable = true;

        // Drop the clone's inherited persistent onClick (which calls RequestAreas) before
        // wiring our own handler.
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => onClick?.Invoke());
        SetButtonLabel(button, label);

        var source = (RectTransform)_requestButton.transform;
        var clone = (RectTransform)button.transform;
        clone.anchoredPosition =
            source.anchoredPosition - new Vector2(0f, slot * (source.rect.height + 10f));
        return button;
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
            _requestStatusText.text = "⚠️ No sites with a VPS asset available";
            return;
        }

        CreateScopeToggleItem();

        var inScope = _currentSites
            .Where(pair => IsInScope(pair.Item2))
            .OrderBy(pair => pair.Item1.Name)
            .ThenBy(pair => pair.Item2.Name)
            .ToList();
        var siteCount = inScope.Select(pair => pair.Item1.Id).Distinct().Count();
        _requestStatusText.text = inScope.Count == 0
            ? "⚠️ No production VPS assets. Show all assets to list the others."
            : $"Found {inScope.Count} VPS asset(s) in {siteCount} site(s)";
        _siteFilterInputField.gameObject.SetActive(true);

        if (_scrollListContent == null)
            return;

        foreach (var pair in inScope)
        {
            var targetListItemInstance = Instantiate(_itemPrefab, _scrollListContent.transform, false);
            FillSiteItem(targetListItemInstance, pair.Item1, pair.Item2);
            _targetListItemInstances.Add(targetListItemInstance);
        }

        ApplyFilter(_siteFilterInputField.text);
        UpdateScrollListSize();
    }

    /// <summary>
    /// The row that switches the list between production assets only and every asset.
    /// </summary>
    private void CreateScopeToggleItem()
    {
        if (_scrollListContent == null)
            return;

        _scopeToggleItem = Instantiate(_itemPrefab, _scrollListContent.transform, false);
        _scopeToggleItem.transform.name = "AssetScope";
        _scopeToggleItem.TitleLabelText = _productionOnly ? "Assets: Production only" : "Assets: All";
        _scopeToggleItem.DistanceLabelText = _productionOnly
            ? "Assets that are not in production localize by asset ID too."
            : "Includes assets that are not in production.";
        SetButtonText(_scopeToggleItem, true, _productionOnly ? "Show all" : "Production only");
        _scopeToggleItem.SubscribeToNavigateButton(() =>
        {
            _productionOnly = !_productionOnly;
            CreateSiteListItems();
        });
        HideCopyButton(_scopeToggleItem);
        _targetListItemInstances.Add(_scopeToggleItem);
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

    /// <summary>
    /// Adds a persistent "Localize to Nearby Sites" button directly below the request
    /// button, cloned from it so it inherits the sample's button styling without
    /// hand-editing the shared UI prefab.
    /// </summary>
    private void CreateNearbyLocalizeButton()
    {
        // Sits one button-height (plus a small gap) below the request button.
        _nearbyLocalizeButton = CreateMenuButton(
            "NearbyLocalizeButton", "Localize to Nearby Sites", StartNearbyLocalize, slot: 1);
    }

    private static void SetButtonLabel(Button button, string text)
    {
        if (button == null)
            return;

        var label = button.GetComponentInChildren<Text>();
        if (label != null)
            label.text = text;
    }

    private void StartNearbyLocalize()
    {
        if (_nearbyLocalizeInProgress)
            return;

        StartCoroutine(UseMyLocationThenLocalizeCoroutine());
    }

    /// <summary>
    /// Fetches a one-shot GPS fix and hands it to <see cref="NearbyLocalizeSelected"/>,
    /// which drives the ARVps2Manager coordinate overload. Mirrors the Sites sample's
    /// "Use My Location" flow, including the Android runtime-permission handshake.
    /// </summary>
    private IEnumerator UseMyLocationThenLocalizeCoroutine()
    {
        _nearbyLocalizeInProgress = true;
        if (_nearbyLocalizeButton != null) _nearbyLocalizeButton.interactable = false;

#if UNITY_ANDROID
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.FineLocation))
        {
            UnityEngine.Android.Permission.RequestUserPermission(
                UnityEngine.Android.Permission.FineLocation);
            // Wait for the dialog to appear, then for the app to regain focus after dismissal.
            yield return new WaitForSeconds(0.5f);
            while (!Application.isFocused)
                yield return null;
            yield return new WaitForSeconds(0.3f);

            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.FineLocation))
            {
                _requestStatusText.text =
                    "❌ Location permission denied. Enable it in app settings.";
                _nearbyLocalizeInProgress = false;
                if (_nearbyLocalizeButton != null) _nearbyLocalizeButton.interactable = true;
                yield break;
            }
        }
#endif

        _requestStatusText.text = "⏳ Getting your location...";
        // On iOS, Start() triggers the system permission dialog automatically on first use.
        NsdkInput.location.Start(1f, 1f);

        int timeout = 10;
        while (NsdkInput.location.status == LocationServiceStatus.Initializing && timeout > 0)
        {
            yield return new WaitForSeconds(1);
            timeout--;
        }

        if (NsdkInput.location.status != LocationServiceStatus.Running)
        {
            _requestStatusText.text =
                "❌ Could not get location. Enable Location in app settings and try again.";
            _nearbyLocalizeInProgress = false;
            if (_nearbyLocalizeButton != null) _nearbyLocalizeButton.interactable = true;
            yield break;
        }

        var data = NsdkInput.location.lastData;
        _nearbyLocalizeInProgress = false;
        if (_nearbyLocalizeButton != null) _nearbyLocalizeButton.interactable = true;

        _requestStatusText.text =
            $"Localizing to sites within {DefaultNearbyRadiusMeters:F0}m of "
            + $"({data.latitude:F6}, {data.longitude:F6})...";

        NearbyLocalizeSelected?.Invoke(new NearbyLocalizeArgs
        {
            Latitude = data.latitude,
            Longitude = data.longitude,
            RadiusMeters = DefaultNearbyRadiusMeters,
        });
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

    private void FillSiteItem(Vps2TargetListItem item, SiteInfo site, AssetInfo vpsAsset)
    {
        item.transform.name = site.Name;
        item.TitleLabelText = site.Name;

        string distanceText =
            $"Asset: {vpsAsset.Name} · {DeploymentLabel(vpsAsset.Deployment)}\nSite ID: {site.Id}";
        if (site.HasLocation)
        {
            distanceText += $"\nLocation: ({site.Latitude:F6}, {site.Longitude:F6})";
        }

        // Only worth saying when the map is split: one area is the ordinary case. A scan too
        // large for a single mapping run produces several, each with its own anchor and extent,
        // and VPS2 localizes against whichever one the device is standing in.
        if (vpsAsset.Areas != null && vpsAsset.Areas.Count > 1)
        {
            distanceText += $"\nAreas: {vpsAsset.Areas.Count}";
        }

        item.DistanceLabelText = distanceText;

        // Set navigate button text and action (localizes to this asset)
        SetButtonText(item, true, "Localize");
        item.SubscribeToNavigateButton(() =>
        {
            SelectAsset(site, vpsAsset);
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
