using KRocketDocumentScanner.App.ViewModels.Localization;
using KRocketDocumentScanner.App.ViewModels.Mvvm;
using KRocketDocumentScanner.Core.Models;
using KRocketDocumentScanner.Core.Registry;

namespace KRocketDocumentScanner.App.ViewModels;

public sealed class ConnectionOptionViewModel : ObservableObject
{
    public required DiscoveredScanner Scanner { get; init; }

    public required string Label { get; init; }
    public bool HasLabel => Label.Length > 0;

    public required string Address { get; init; }

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

    internal void SetSelectedSilently(bool value)
    {
        if (_isSelected == value) return;
        _isSelected = value;
        OnPropertyChanged(nameof(IsSelected));
    }
}

public sealed class ScannerGroupViewModel : ObservableObject
{
    public required string DisplayName { get; init; }
    public required IReadOnlyList<ConnectionOptionViewModel> Connections { get; init; }

    private bool _isChecked = true;
    public bool IsChecked
    {
        get => _isChecked;
        set => SetField(ref _isChecked, value);
    }

    public bool HasMultipleConnections => Connections.Count > 1;

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

    private string _manualAddress = "";
    public string ManualAddress
    {
        get => _manualAddress;
        set { SetField(ref _manualAddress, value); OnPropertyChanged(nameof(CanAddManual)); AddManualCommand.RaiseCanExecuteChanged(); }
    }

    public bool CanAddManual => !string.IsNullOrWhiteSpace(_manualAddress) && !_isAddingManual;

    private bool _isAddingManual;
    public bool IsAddingManual
    {
        get => _isAddingManual;
        private set { SetField(ref _isAddingManual, value); OnPropertyChanged(nameof(CanAddManual)); AddManualCommand.RaiseCanExecuteChanged(); }
    }

    public AsyncRelayCommand AddManualCommand { get; }

    internal static bool IsValidManualAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Any(char.IsWhiteSpace)) return false;

        var withScheme = address.Contains("://") ? address : $"https://{address}";
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrEmpty(uri.Host)) return false;
        if (Uri.CheckHostName(uri.Host) == UriHostNameType.Unknown) return false;

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
            var name = address;

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
                }
            }
        }
        catch (FormatException)
        {
            ManualStatusMessage = string.Format(Strings.ManualAddressInvalidFormat, address);
        }
        catch (KRocketDocumentScanner.Core.Registry.ScannerUnreachableException)
        {
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

    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        private set { SetField(ref _statusMessage, value); OnPropertyChanged(nameof(HasStatusMessage)); }
    }
    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    private string? _manualStatusMessage;
    public string? ManualStatusMessage
    {
        get => _manualStatusMessage;
        private set { SetField(ref _manualStatusMessage, value); OnPropertyChanged(nameof(HasManualStatusMessage)); }
    }
    public bool HasManualStatusMessage => !string.IsNullOrEmpty(_manualStatusMessage);
}
