using KRocketDocumentScanner.App.ViewModels.Localization;
using KRocketDocumentScanner.App.ViewModels.Mvvm;
using KRocketDocumentScanner.Core.Models;
using KRocketDocumentScanner.Core.Registry;

namespace KRocketDocumentScanner.App.ViewModels;

/// <summary>One row in the Manage Scanners list.</summary>
public sealed class ScannerRowViewModel : ObservableObject
{
    public required string DriverId { get; init; }
    public required string DisplayName { get; init; }
    public required string StatusText { get; init; }
    public required bool IsReady { get; init; }
    public required bool IsDefault { get; init; }
}

/// <summary>
/// Drives the Manage Scanners window: the registry list with ready/not-ready status,
/// set-default, remove, and the entry point to Add Scanner.
/// </summary>
public sealed class ManageScannersViewModel : ObservableObject
{
    private readonly ScannerRegistryManager _registry;
    private readonly Func<Task> _showAddScanner;

    public ManageScannersViewModel(ScannerRegistryManager registry, Func<Task> showAddScanner)
    {
        _registry = registry;
        _showAddScanner = showAddScanner;

        AddScannerCommand = new AsyncRelayCommand(AddScannerAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        Rebuild();
    }

    private IReadOnlyList<ScannerRowViewModel> _scanners = Array.Empty<ScannerRowViewModel>();
    public IReadOnlyList<ScannerRowViewModel> Scanners
    {
        get => _scanners;
        private set { SetField(ref _scanners, value); OnPropertyChanged(nameof(HasScanners)); OnPropertyChanged(nameof(IsEmpty)); }
    }

    public bool HasScanners => _scanners.Count > 0;
    public bool IsEmpty => _scanners.Count == 0;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set { SetField(ref _errorMessage, value); OnPropertyChanged(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    public AsyncRelayCommand AddScannerCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>True only when there's more than one scanner — "make default" is meaningless
    /// with a single scanner, which is always already the default.</summary>
    public bool CanChooseDefault => _scanners.Count > 1;

    private void Rebuild()
    {
        Scanners = _registry.Entries.Select(e => new ScannerRowViewModel
        {
            DriverId = e.DriverId,
            DisplayName = e.DisplayName,
            IsReady = e.Reachability == ScannerReachability.Ready,
            StatusText = e.Reachability switch
            {
                ScannerReachability.Ready => Strings.ReadyStatus,
                ScannerReachability.NotReady => Strings.NotReachableStatus,
                _ => Strings.UnknownStatus,
            },
            IsDefault = e.DriverId == _registry.DefaultDriverId,
        }).ToList();
        OnPropertyChanged(nameof(CanChooseDefault));
    }

    private async Task AddScannerAsync()
    {
        await _showAddScanner();
        Rebuild();
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _registry.LoadAndRefreshAsync();
            Rebuild();
        }
        catch (Exception ex)
        {
            ErrorMessage = string.Format(Strings.ScannerStatusRefreshFailedMessage, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SetDefaultAsync(string driverId)
    {
        ErrorMessage = null;
        try
        {
            await _registry.SetDefaultAsync(driverId);
            Rebuild();
        }
        catch (Exception ex)
        {
            ErrorMessage = string.Format(Strings.SetDefaultScannerFailedMessage, ex.Message);
        }
    }

    public async Task RemoveAsync(string driverId)
    {
        ErrorMessage = null;
        try
        {
            await _registry.RemoveAsync(driverId);
            Rebuild();
        }
        catch (Exception ex)
        {
            ErrorMessage = string.Format(Strings.RemoveScannerFailedMessage, ex.Message);
        }
    }
}
