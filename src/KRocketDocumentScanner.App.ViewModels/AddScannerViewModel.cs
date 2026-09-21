using KRocketDocumentScanner.App.ViewModels.Localization;
using KRocketDocumentScanner.App.ViewModels.Mvvm;
using KRocketDocumentScanner.Core.Models;
using KRocketDocumentScanner.Core.Registry;

namespace KRocketDocumentScanner.App.ViewModels;

/// <summary>One selectable connection for a discovered scanner (e.g. its AirScan path and its
/// eSCL path). Several of these can belong to the same physical device — see
/// <see cref="ScannerGroupViewModel"/>.</summary>
public sealed class ConnectionOptionViewModel : ObservableObject
{
    public required DiscoveredScanner Scanner { get; init; }

    /// <summary>The connection's protocol name ("AirScan", "eSCL"), or "" for a backend that
    /// isn't one of those two (USB, unrecognized) — there's nothing meaningful to label it with.</summary>
    public required string Label { get; init; }
    public bool HasLabel => Label.Length > 0;

    public required string Address { get; init; }

    /// <summary>What the connection picker (a ComboBox when a card has more than one
    /// connection) shows for this row.</summary>
    public string DisplayText => HasLabel ? $"{Label} ({Address})" : Address;

    internal ScannerGroupViewModel? Group { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            if (value) Group?.OnConnectionSelected(this);
        }
    }

    /// <summary>Sets IsSelected without notifying the owning group back — used by the group
    /// itself when it deselects every other connection in response to one being picked.</summary>
    internal void SetSelectedSilently(bool value)
    {
        if (_isSelected == value) return;
        _isSelected = value;
        OnPropertyChanged(nameof(IsSelected));
    }
}

/// <summary>One card in the Discover tab: a single physical scanner and every connection it was
/// found under. <see cref="ConnectionOptionViewModel.IsSelected"/> tracks which connection the
/// user has picked (defaults to the first, highest-ranked one); exactly one is ever selected at
/// a time within a group.</summary>
public sealed class ScannerGroupViewModel : ObservableObject
{
    public required string DisplayName { get; init; }
    public required IReadOnlyList<ConnectionOptionViewModel> Connections { get; init; }

    /// <summary>The row's own checkbox — whether this card is included in the next "Add
    /// scanner" click (a single footer button adds every checked card at once, rather than
    /// each card having its own Add). Every freshly-discovered card starts checked.</summary>
    private bool _isChecked = true;
    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value);
    }

    /// <summary>Only worth showing a connection picker at all when there's more than one —
    /// a single-connection card has nothing to choose between.</summary>
    public bool HasMultipleConnections => Connections.Count > 1;

    /// <summary>The connection picker's own SelectedItem — set drives IsSelected the same way
    /// picking a RadioButton used to (still exactly one selected at a time within a group; see
    /// ConnectionOptionViewModel.IsSelected/OnConnectionSelected below).</summary>
    public ConnectionOptionViewModel SelectedConnection
    {
        get => Connections.FirstOrDefault(c => c.IsSelected) ?? Connections[0];
        set
        {
            if (ReferenceEquals(SelectedConnection, value)) return;
            value.IsSelected = true;
            OnPropertyChanged();
        }
    }

    public DiscoveredScanner SelectedScanner => SelectedConnection.Scanner;

    internal void OnConnectionSelected(ConnectionOptionViewModel selected)
    {
        foreach (var c in Connections)
            if (!ReferenceEquals(c, selected)) c.SetSelectedSilently(false);
        OnPropertyChanged(nameof(SelectedConnection));
    }
}

/// <summary>
/// Drives the Add Scanner window: a Discover tab (search, then pick from results) and a
/// Manual entry tab (type an address for devices that won't auto-discover).
///
/// A single physical scanner commonly reports itself more than once (e.g. once via eSCL and
/// once via the SANE AirScan bridge — two internal paths to the same device). Rather than
/// silently picking one, every distinct report is grouped into one card per physical device
/// (see <see cref="BuildGroups"/>), with each connection shown as its own selectable option so
/// the user can tell they're the same device and choose which one to add. Only an exact literal
/// duplicate report (identical driver id) is collapsed.
/// </summary>
public sealed class AddScannerViewModel : ObservableObject
{
    private readonly ScannerRegistryManager _registry;
    private readonly Func<string, string, string> _buildManualDriverId;
    private readonly Action _close;

    public AddScannerViewModel(
        ScannerRegistryManager registry,
        Func<string, string, string> buildManualDriverId,
        Action close)
    {
        _registry = registry;
        _buildManualDriverId = buildManualDriverId;
        _close = close;

        DiscoverCommand = new AsyncRelayCommand(DiscoverAsync);
        AddSelectedCommand = new AsyncRelayCommand(AddSelectedAsync);
        AddManualCommand = new AsyncRelayCommand(AddManualAsync, () => CanAddManual);
        CancelCommand = new RelayCommand(() => _close());
    }

    // ---------------- Discover tab ----------------
    private IReadOnlyList<ScannerGroupViewModel> _found = Array.Empty<ScannerGroupViewModel>();
    public IReadOnlyList<ScannerGroupViewModel> Found
    {
        get => _found;
        private set { SetField(ref _found, value); OnPropertyChanged(nameof(HasResults)); }
    }
    public bool HasResults => _found.Count > 0;

    private bool _isSearching;
    public bool IsSearching
    {
        get => _isSearching;
        private set => SetField(ref _isSearching, value);
    }

    private bool _hasSearched;
    public bool HasSearched
    {
        get => _hasSearched;
        private set { SetField(ref _hasSearched, value); OnPropertyChanged(nameof(ShowNoResultsMessage)); }
    }

    /// <summary>Only show "nothing found" after an actual completed search — never before one,
    /// and never while one is still running, which would read as broken rather than searching.</summary>
    public bool ShowNoResultsMessage => _hasSearched && !_isSearching && _found.Count == 0;

    public AsyncRelayCommand DiscoverCommand { get; }
    public AsyncRelayCommand AddSelectedCommand { get; }

    private async Task DiscoverAsync()
    {
        IsSearching = true;
        OnPropertyChanged(nameof(ShowNoResultsMessage));
        StatusMessage = null;
        try
        {
            var found = await _registry.DiscoverAvailableAsync();
            Found = BuildGroups(found, _buildManualDriverId);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Strings.ScannerSearchFailedMessage, ex.Message);
            Found = Array.Empty<ScannerGroupViewModel>();
        }
        finally
        {
            IsSearching = false;
            HasSearched = true;
            OnPropertyChanged(nameof(ShowNoResultsMessage));
        }
    }

    /// <summary>
    /// One card per physical device — every distinct driver id report is a candidate
    /// connection, and reports that share an address (the same device reached via different
    /// protocols, e.g. AirScan and eSCL) are grouped onto the same card. Only an exact literal
    /// duplicate report (identical driver id) is collapsed. Within a card, the AirScan
    /// connection is listed first and selected by default — it's the connection that works
    /// reliably for the widest range of scanners, so it's the safe default pick.
    ///
    /// SANE's own eSCL backend and sane-airscan each run their own, independent mDNS probe,
    /// and in practice one frequently finds a device on a given search that the other misses
    /// (confirmed against a real network scanner). Rather than let that live
    /// flakiness decide whether the user ever gets to see a second, genuinely different
    /// connection option, <paramref name="buildManualDriverId"/> (the same direct-eSCL builder
    /// the Manual entry tab uses, bypassing SANE entirely) synthesizes the missing connection
    /// whenever the AirScan entry is the only live report for its address. An eSCL-only report
    /// is left alone — there's no reliable way to construct a working AirScan id ourselves
    /// (it's a SANE-backend-assigned index, not just an address), so this app never pretends to
    /// offer one.
    /// </summary>
    internal static List<ScannerGroupViewModel> BuildGroups(
        IReadOnlyList<DiscoveredScanner> found, Func<string, string, string> buildManualDriverId)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinct = new List<DiscoveredScanner>();
        foreach (var scanner in found)
            if (seen.Add(scanner.DriverId))
                distinct.Add(scanner);

        var addressCounts = distinct
            .Select(ExtractAddress)
            .Where(a => a is not null)
            .GroupBy(a => a!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // Reports that share an address belong to the same physical device and land on the
        // same card; a report with no address (e.g. USB) gets a card of its own, keyed by its
        // own driver id so unrelated addressless devices never merge.
        var entries = new List<(string GroupKey, int Rank, string DisplayName, ConnectionOptionViewModel Connection)>();

        foreach (var scanner in distinct)
        {
            var address = ExtractAddress(scanner);
            var (rank, label) = ClassifyProtocol(scanner);
            var rawName = $"{scanner.Vendor} {scanner.Model}".Trim();
            var displayName = ScannerRegistryEntry.StripProtocolSuffix(rawName);
            var groupKey = address ?? scanner.DriverId;

            entries.Add((groupKey, rank, displayName, new ConnectionOptionViewModel
            {
                Scanner = scanner,
                Label = label,
                Address = address ?? Strings.ConnectedDeviceAddress,
            }));

            if (rank == 0 && address is not null && addressCounts[address] == 1)
            {
                var alternateId = buildManualDriverId(address, displayName);
                if (seen.Add(alternateId))
                {
                    entries.Add((groupKey, 1, displayName, new ConnectionOptionViewModel
                    {
                        Scanner = new DiscoveredScanner(alternateId, scanner.Vendor, scanner.Model),
                        Label = Strings.EsclLabel,
                        Address = address,
                    }));
                }
            }
        }

        var groups = new List<ScannerGroupViewModel>();
        foreach (var byKey in entries.GroupBy(e => e.GroupKey, StringComparer.OrdinalIgnoreCase))
        {
            var connections = byKey.OrderBy(e => e.Rank).Select(e => e.Connection).ToList();
            var group = new ScannerGroupViewModel
            {
                DisplayName = byKey.First().DisplayName,
                Connections = connections,
            };
            foreach (var c in connections) c.Group = group;
            connections[0].SetSelectedSilently(true);
            groups.Add(group);
        }

        return groups;
    }

    private static string? ExtractAddress(DiscoveredScanner s) =>
        ScannerAddressExtractor.Extract(s.DriverId, s.Model, s.Vendor);

    private static (int Rank, string Label) ClassifyProtocol(DiscoveredScanner s)
    {
        var id = s.DriverId.ToLowerInvariant();
        if (id.Contains("airscan")) return (0, Strings.AirScanLabel);
        if (id.Contains("escl")) return (1, Strings.EsclLabel);
        return (2, "");
    }

    /// <summary>Adds every checked card in one go (see ScannerGroupViewModel.IsChecked),
    /// instead of each card having its own Add button. A jam on one device shouldn't lose
    /// the others — every checked card is attempted regardless of earlier failures, the
    /// same "keep partial progress" rule the scan batch loop uses. Closes only once
    /// everything checked succeeded; any failures stay listed so the user can see what to
    /// retry, with whatever did succeed already saved.</summary>
    private async Task AddSelectedAsync()
    {
        StatusMessage = null;
        var selected = _found.Where(g => g.IsChecked).ToList();
        if (selected.Count == 0) return;

        var failures = new List<string>();
        foreach (var group in selected)
        {
            try
            {
                await _registry.AddFromDiscoveryAsync(group.SelectedScanner);
            }
            catch (Exception ex)
            {
                failures.Add($"{group.DisplayName}: {ex.Message}");
            }
        }

        if (failures.Count == 0)
            _close();
        else
            StatusMessage = string.Format(Strings.AddScannerFailedMessage, string.Join("; ", failures));
    }

    // ---------------- Manual entry tab ----------------
    private string _manualAddress = "";
    public string ManualAddress
    {
        get => _manualAddress;
        set { SetField(ref _manualAddress, value); OnPropertyChanged(nameof(CanAddManual)); AddManualCommand.RaiseCanExecuteChanged(); }
    }

    public bool CanAddManual => !string.IsNullOrWhiteSpace(_manualAddress) && !_isAddingManual;

    private bool _isAddingManual;
    /// <summary>True while the typed address is being checked and saved — the reachability
    /// check can take a few seconds, and Add must not be clickable again meanwhile.</summary>
    public bool IsAddingManual
    {
        get => _isAddingManual;
        private set { SetField(ref _isAddingManual, value); OnPropertyChanged(nameof(CanAddManual)); AddManualCommand.RaiseCanExecuteChanged(); }
    }

    public AsyncRelayCommand AddManualCommand { get; }

    /// <summary>Cheap check of the typed address before any network attempt: no spaces, only an
    /// http/https scheme if one is given, a real host name or IP (IPv4 octets 0-255) and a valid
    /// port. Anything else is rejected with the "isn't a valid address" message straight away.</summary>
    internal static bool IsValidManualAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Any(char.IsWhiteSpace)) return false;

        var withScheme = address.Contains("://") ? address : $"https://{address}";
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrEmpty(uri.Host)) return false;
        if (Uri.CheckHostName(uri.Host) == UriHostNameType.Unknown) return false;

        // "10.0.0.999" and "192.168.1" look like IPs, and Uri quietly rewrites them into other
        // addresses — so judge the host exactly as typed: digits and dots must be a real IPv4.
        var typedHost = withScheme[(withScheme.IndexOf("://", StringComparison.Ordinal) + 3)..];
        if (!typedHost.StartsWith('['))
            typedHost = typedHost.Split('/', ':', '?', '#')[0];
        if (typedHost.Length > 0 && typedHost.All(c => char.IsAsciiDigit(c) || c == '.'))
        {
            var parts = typedHost.Split('.');
            return parts.Length == 4 && parts.All(p => p.Length is >= 1 and <= 3 && int.Parse(p) <= 255);
        }
        return true;
    }
    public RelayCommand CancelCommand { get; }

    private async Task AddManualAsync()
    {
        ManualStatusMessage = null;
        var address = ManualAddress.Trim();
        IsAddingManual = true;
        try
        {
            if (!IsValidManualAddress(address)) throw new FormatException();
            var name = address; // the address is the scanner's name; there is no separate name field

            // A bare host/IP: many scanners answer only on https, others only on plain http,
            // so try both (https first). An address typed with its own scheme is used as is.
            var candidates = address.Contains("://")
                ? new[] { address }
                : new[] { $"https://{address}", $"http://{address}" };

            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    var driverId = _buildManualDriverId(candidates[i], name);
                    await _registry.AddManualAsync(driverId, name, candidates[i], requireReachable: true);
                    _close();
                    return;
                }
                catch (KRocketDocumentScanner.Core.Registry.ScannerUnreachableException) when (i < candidates.Length - 1)
                {
                    // Not answering on this scheme — fall through to the next one.
                }
            }
        }
        catch (FormatException)
        {
            // Malformed text (e.g. a doubled dot) — the raw parser message talks about
            // "hostname"/"URI", which isn't what the field is called.
            ManualStatusMessage = string.Format(Strings.ManualAddressInvalidFormat, address);
        }
        catch (KRocketDocumentScanner.Core.Registry.ScannerUnreachableException)
        {
            // Not added, window stays open so the address can be corrected in place.
            ManualStatusMessage = string.Format(Strings.ManualScannerUnreachableFormat, address);
        }
        catch (Exception ex)
        {
            ManualStatusMessage = string.Format(Strings.AddScannerFailedMessage, ex.Message);
        }
        finally
        {
            IsAddingManual = false;
        }
    }

    // ---------------- Shared ----------------
    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        private set { SetField(ref _statusMessage, value); OnPropertyChanged(nameof(HasStatusMessage)); }
    }
    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    // The Manual entry tab's own error line — separate so a failure there doesn't also show
    // under the Discover tab (and vice versa).
    private string? _manualStatusMessage;
    public string? ManualStatusMessage
    {
        get => _manualStatusMessage;
        private set { SetField(ref _manualStatusMessage, value); OnPropertyChanged(nameof(HasManualStatusMessage)); }
    }
    public bool HasManualStatusMessage => !string.IsNullOrEmpty(_manualStatusMessage);
}
