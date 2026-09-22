using KRocketDocumentScanner.App.ViewModels.Localization;
using KRocketDocumentScanner.App.ViewModels.Mvvm;
using KRocketDocumentScanner.Core.Abstractions;
using KRocketDocumentScanner.Core.Models;
using KRocketDocumentScanner.Core.Registry;

namespace KRocketDocumentScanner.App.ViewModels;

public sealed class ScannedPageViewModel : ObservableObject
{
    public required CapturedPage Page { get; set; }
    public required int Number { get; set; }

    public string? PresetName { get; init; }
    public string Label => string.Format(Strings.PageLabelFormat, Number);
}

public static class ScanDefaults
{
    public static string PresetName { get; set; } = ScanViewModel.CustomPresetName;
    public static bool KeepPresetRatio { get; set; } = true;
    public static bool StretchToPage { get; set; } = false;
    public static bool MetricUnits { get; set; } = false;
}

public readonly record struct AspectLock(double Ratio, double MinWidth, double MaxWidth);

public sealed class ScannerChoice : ObservableObject
{
    public ScannerChoice(string driverId, string displayName, ScannerReachability reachability)
    {
        DriverId = driverId; DisplayName = displayName; _reachability = reachability;
    }

    public string DriverId { get; set; }
    public string DisplayName { get; }

    private ScannerReachability _reachability;
    public ScannerReachability Reachability
    {
        get => _reachability;
        set
        {
            if (!SetField(ref _reachability, value)) return;
            OnPropertyChanged(nameof(IsReady));
        }
    }

    public bool IsReady => _reachability == ScannerReachability.Ready;
}

public enum ScanScreenState
{
    Ready,
    Checking,
    ScannerUnavailable,
    Previewing,
    Scanning,
}

public sealed class ScanViewModel : ObservableObject
{
    private readonly IScannerEngine _engine;
    private readonly ScannerRegistryManager _registry;
    private readonly Func<string, Task<bool>> _saveAs;
    private readonly Func<Task> _openManageScanners;

    public ScanViewModel(
        IScannerEngine engine,
        ScannerRegistryManager registry,
        string? preselectedDriverId,
        Func<string, Task<bool>> saveAs,
        Func<Task> openManageScanners)
    {
        _engine = engine;
        _registry = registry;
        _saveAs = saveAs;
        _openManageScanners = openManageScanners;

        _driverId = preselectedDriverId ?? registry.DefaultDriverId;
        registry.Changed += SyncChoiceDots;
        _uiContext = SynchronizationContext.Current;
        registry.ScannerRemoved += OnScannerRemoved;
        registry.ScannerRepointed += OnScannerRepointed;

        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => State == ScanScreenState.Ready);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => State == ScanScreenState.Ready && HasPreview);
        ChooseAnotherScannerCommand = new AsyncRelayCommand(ChooseAnotherAsync);
        RefreshScannersCommand = new AsyncRelayCommand(RefreshScannersAsync, () => !IsWorking);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => Pages.Count > 0);

        RefreshScannerName();
    }

    private string? _driverId;
    private readonly SynchronizationContext? _uiContext;

    public void Detach()
    {
        _registry.Changed -= SyncChoiceDots;
        _registry.ScannerRemoved -= OnScannerRemoved;
        _registry.ScannerRepointed -= OnScannerRepointed;
    }

    private void OnUi(Action action)
    {
        if (_uiContext is null) action();
        else _uiContext.Post(_ => action(), null);
    }

    private void OnScannerRemoved(string removedId) => OnUi(() =>
    {
        _scannerChoices = null;
        OnPropertyChanged(nameof(ScannerChoices));
        if (removedId == _driverId)
        {
            ResetForScannerChange();
            _driverId = _registry.DefaultDriverId;
            RefreshScannerName();
            _ = InitializeAsync();
        }
        else
        {
            RefreshScannerName();
        }
    });

    private void OnScannerRepointed(string oldId, string newId) => OnUi(() =>
    {
        foreach (var choice in _scannerChoices ?? [])
            if (choice.DriverId == oldId) choice.DriverId = newId;
        if (_driverId == oldId) _driverId = newId;
    });

    private string _scannerName = "";
    public string ScannerName
    {
        get => _scannerName;
        private set => SetField(ref _scannerName, value);
    }

    private bool _isReady;
    public bool IsReady
    {
        get => _isReady;
        private set => SetField(ref _isReady, value);
    }

    public IReadOnlyList<ScannerChoice> ScannerChoices => _scannerChoices ??= BuildChoices();
    private List<ScannerChoice>? _scannerChoices;

    private List<ScannerChoice> BuildChoices() => _registry.Entries
        .Select(e => new ScannerChoice(e.DriverId, e.DisplayName, e.Reachability))
        .ToList();

    private void SyncChoiceDots()
    {
        foreach (var choice in _scannerChoices ?? [])
        {
            var entry = _registry.Entries.FirstOrDefault(e => e.DriverId == choice.DriverId);
            if (entry is not null) choice.Reachability = entry.Reachability;
        }
    }

    public bool NoScannersRegistered => _registry.Entries.Count == 0;

    private int _scannerIndex = -1;
    public int ScannerIndex
    {
        get => _scannerIndex;
        set
        {
            if (!SetField(ref _scannerIndex, value)) return;
            var entries = _registry.Entries;
            if (value < 0 || value >= entries.Count) return;
            if (_driverId != entries[value].DriverId) ResetForScannerChange();
            _driverId = entries[value].DriverId;
            RefreshScannerName();
            _ = InitializeAsync();
        }
    }

    private ScanScreenState _state = ScanScreenState.Checking;
    public ScanScreenState State
    {
        get => _state;
        private set
        {
            SetField(ref _state, value);
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsWorking));
            OnPropertyChanged(nameof(ShowUnavailable));
            PreviewCommand.RaiseCanExecuteChanged();
            ScanCommand.RaiseCanExecuteChanged();
            RefreshScannersCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsBusy => _state is ScanScreenState.Previewing or ScanScreenState.Scanning;
    public bool IsWorking => IsBusy || _state == ScanScreenState.Checking;
    public bool ShowUnavailable => _state == ScanScreenState.ScannerUnavailable;

    public IReadOnlyList<int> ResolutionChoices { get; } = new[] { 100, 150, 200, 300, 400, 600 };

    private int _resolutionDpi = 300;
    public int ResolutionDpi { get => _resolutionDpi; set => SetField(ref _resolutionDpi, value); }

    public IReadOnlyList<string> ColorModeChoices { get; } = new[] { Strings.ColorModeColor, Strings.ColorModeGrayscale, Strings.ColorModeBlackAndWhite, Strings.ColorModeBlackAndWhiteClean };

    private int _colorModeIndex;
    public int ColorModeIndex { get => _colorModeIndex; set => SetField(ref _colorModeIndex, value); }

    private IReadOnlyList<ScanSource> _availableSources = new[] { ScanSource.Flatbed };

    public IReadOnlyList<string> SourceChoices => _availableSources.Select(SourceLabel).ToList();

    public bool ShowSourcePicker => _availableSources.Count > 1;

    private static string SourceLabel(ScanSource source) => source switch
    {
        ScanSource.Flatbed => Strings.SourceFlatbed,
        ScanSource.AutomaticDocumentFeeder => Strings.SourceFeeder,
        ScanSource.AutomaticDocumentFeederDuplex => Strings.SourceFeederDuplex,
        _ => Strings.SourceFlatbed,
    };

    private int _sourceIndex;
    public int SourceIndex
    {
        get => _sourceIndex;
        set { SetField(ref _sourceIndex, value); OnPropertyChanged(nameof(IsFeederSelected)); }
    }

    private ScanSource CurrentSource =>
        _sourceIndex >= 0 && _sourceIndex < _availableSources.Count ? _availableSources[_sourceIndex] : ScanSource.Flatbed;

    public bool IsFeederSelected => CurrentSource != ScanSource.Flatbed;

    private void ApplySourceCapabilities(ScannerCapabilities caps)
    {
        var sources = new List<ScanSource>();
        if (caps.SupportsFlatbed) sources.Add(ScanSource.Flatbed);
        if (caps.SupportsFeeder) sources.Add(ScanSource.AutomaticDocumentFeeder);
        if (caps.SupportsFeeder && caps.SupportsDuplex) sources.Add(ScanSource.AutomaticDocumentFeederDuplex);
        if (sources.Count == 0) sources.Add(ScanSource.Flatbed);

        _availableSources = sources;
        _sourceIndex = 0;
        OnPropertyChanged(nameof(SourceChoices));
        OnPropertyChanged(nameof(ShowSourcePicker));
        OnPropertyChanged(nameof(SourceIndex));
        OnPropertyChanged(nameof(IsFeederSelected));
    }

    private bool _autoDeskew = true;
    public bool AutoDeskew { get => _autoDeskew; set => SetField(ref _autoDeskew, value); }

    private bool _dropBlankPages = true;
    public bool DropBlankPages { get => _dropBlankPages; set => SetField(ref _dropBlankPages, value); }

    private ScanOptions BuildOptions() => new()
    {
        ResolutionDpi = ResolutionDpi,
        ColorMode = ColorModeIndex switch
        {
            0 => ColorMode.Color,
            1 => ColorMode.Grayscale,
            2 => ColorMode.BlackAndWhiteText,
            _ => ColorMode.BlackAndWhiteClean,
        },
        Source = CurrentSource,
        AutoDeskew = AutoDeskew && IsFeederSelected,
        DropBlankPages = DropBlankPages && IsFeederSelected,
        Area = _selectedArea,
    };

    private CapturedPage? _previewImage;
    public CapturedPage? PreviewImage
    {
        get => _previewImage;
        private set { SetField(ref _previewImage, value); OnPropertyChanged(nameof(HasPreview)); NotifyAreaSize(); ScanCommand.RaiseCanExecuteChanged(); }
    }
    public bool HasPreview => _previewImage is not null;

    private const int PreviewDpi = 100;
    private ScanArea? _selectedArea;

    public void SetSelectedArea(double normLeft, double normTop, double normRight, double normBottom)
    {
        if (_previewImage is null) { _selectedArea = null; return; }

        (_selL, _selT, _selR, _selB) = (normLeft, normTop, normRight, normBottom);
        const double mmPerInch = 25.4;
        double widthMm = _previewImage.WidthPx / (double)PreviewDpi * mmPerInch;
        double heightMm = _previewImage.HeightPx / (double)PreviewDpi * mmPerInch;

        if (normLeft <= 0.01 && normTop <= 0.01 && normRight >= 0.99 && normBottom >= 0.99)
        {
            _selectedArea = null;
            NotifyAreaSize();
            return;
        }

        _selectedArea = new ScanArea(normLeft * widthMm, normTop * heightMm, normRight * widthMm, normBottom * heightMm);
        NotifyAreaSize();
    }

    private double _selL, _selT, _selR = 1, _selB = 1;

    public double BedWidthMm => _previewImage is null ? 0 : _previewImage.WidthPx / (double)PreviewDpi * 25.4;
    public double BedHeightMm => _previewImage is null ? 0 : _previewImage.HeightPx / (double)PreviewDpi * 25.4;

    public static readonly IReadOnlyList<string> UnitChoices = new[] { "in", "cm" };

    private int _unitIndex = ScanDefaults.MetricUnits ? 1 : 0;
    public int UnitIndex
    {
        get => _unitIndex;
        set { if (SetField(ref _unitIndex, value)) NotifyAreaSize(); }
    }
    public bool IsMetric
    {
        get => _unitIndex == 1;
        set { UnitIndex = value ? 1 : 0; OnPropertyChanged(nameof(UnitLabel)); }
    }
    public string UnitLabel => UnitChoices[_unitIndex];
    private double MmPerUnit => _unitIndex == 1 ? 10.0 : 25.4;

    public decimal? AreaWidth
    {
        get => DisplaySize((_selR - _selL) * BedWidthMm);
        set
        {
            if (value is not null) ResizeArea((double)value.Value * MmPerUnit, null);
        }
    }
    public decimal? AreaHeight
    {
        get => DisplaySize((_selB - _selT) * BedHeightMm);
        set
        {
            if (value is not null) ResizeArea(null, (double)value.Value * MmPerUnit);
        }
    }

    public decimal AreaMinWidth => MinDisplaySize(lk => lk.MinWidth * BedWidthMm);
    public decimal AreaMinHeight => MinDisplaySize(lk => lk.MinWidth / lk.Ratio * BedHeightMm);

    public string? AreaMinWidthTip => MinTip(AreaMinWidth);
    public string? AreaMinHeightTip => MinTip(AreaMinHeight);

    private string? MinTip(decimal min) =>
        _minimapStretch && _keepPresetRatio && _presetLock is not null
            ? string.Format(Strings.SizeMinimumTip, min, UnitLabel) : null;

    private decimal MinDisplaySize(Func<AspectLock, double> minMm)
    {
        if (!_keepPresetRatio || EffectiveLock is not { } lk) return 1;
        int digits = IsCustomPreset ? 0 : (_unitIndex == 1 ? 1 : 2);
        double factor = Math.Pow(10, digits);
        return Math.Max(1, (decimal)(Math.Ceiling(minMm(lk) / MmPerUnit * factor) / factor));
    }

    private decimal DisplaySize(double mm)
    {
        int digits = IsCustomPreset ? 0 : (_unitIndex == 1 ? 1 : 2);
        return (decimal)Math.Round(mm / MmPerUnit, digits, MidpointRounding.AwayFromZero);
    }

    public string SizeFormat => IsCustomPreset ? "0" : (_unitIndex == 1 ? "0.#" : "0.##");

    public (double WidthMm, double HeightMm)? PresetPageSizeMm =>
        !IsCustomPreset && PagePresetSizes.TryGetValue(PagePresetChoices[_pagePresetIndex], out var size) ? size : null;

    public bool IsCustomPreset => _pagePresetIndex == CustomIndex;
    public bool IsSizeEditable => HasPreview && IsCustomPreset;

    public void RefreshAreaSize() => NotifyAreaSize();

    private void NotifyAreaSize()
    {
        OnPropertyChanged(nameof(AreaWidth));
        OnPropertyChanged(nameof(AreaHeight));
        OnPropertyChanged(nameof(AreaMinWidth));
        OnPropertyChanged(nameof(AreaMinHeight));
        OnPropertyChanged(nameof(AreaMinWidthTip));
        OnPropertyChanged(nameof(AreaMinHeightTip));
        OnPropertyChanged(nameof(IsSizeEditable));
        OnPropertyChanged(nameof(SizeFormat));
    }

    private void ResizeArea(double? widthMm, double? heightMm)
    {
        if (_previewImage is null || !HasPreview || BedWidthMm <= 0 || BedHeightMm <= 0) return;
        const double minNorm = 0.02;
        double r = widthMm is { } w ? _selL + Math.Clamp(w / BedWidthMm, minNorm, 1 - _selL) : _selR;
        double b = heightMm is { } h ? _selT + Math.Clamp(h / BedHeightMm, minNorm, 1 - _selT) : _selB;
        if (_keepPresetRatio && EffectiveLock is { } lk)
            (_, _, r, b) = widthMm is not null
                ? FitToRatio(_selL, _selT, r - _selL, null, lk)
                : FitToRatio(_selL, _selT, null, b - _selT, lk);
        SetSelectedArea(_selL, _selT, r, b);
        PresetSelectionRequested?.Invoke(this, (_selL, _selT, r, b));
    }

    public const string CustomPresetName = "Custom";

    private const double PresetMinScale = 0.7;

    public static readonly IReadOnlyList<string> PagePresetChoices =
        PaperDetector.KnownSizes.Select(p => p.Name).Append(CustomPresetName).ToArray();

    private static readonly Dictionary<string, (double WidthMm, double HeightMm)> PagePresetSizes =
        PaperDetector.KnownSizes.ToDictionary(p => p.Name, p => (p.WidthMm, p.HeightMm));

    private int _pagePresetIndex = DefaultPresetIndex();

    private static int CustomIndex => PagePresetChoices.Count - 1;
    private static int DefaultPresetIndex()
    {
        var i = Array.IndexOf(PagePresetChoices.ToArray(), ScanDefaults.PresetName);
        return i >= 0 ? i : CustomIndex;
    }
    public int PagePresetIndex
    {
        get => _pagePresetIndex;
        set
        {
            _presetChosen = true;
            SetField(ref _pagePresetIndex, value);
            ApplyPagePreset(PagePresetChoices[value]);
            NotifyAreaSize();
        }
    }

    private bool _presetChosen;

    public event EventHandler<(double Left, double Top, double Right, double Bottom)>? PresetSelectionRequested;

    public event EventHandler<AspectLock?>? AspectLockChanged;

    private AspectLock? _presetLock;
    private bool _keepPresetRatio = ScanDefaults.KeepPresetRatio;

    public bool KeepPresetRatio
    {
        get => _keepPresetRatio;
        set
        {
            if (!value && _minimapStretch) value = true;
            if (!SetField(ref _keepPresetRatio, value)) return;
            PublishAspectLock();
            if (value) SnapSelectionToPresetRatio();
        }
    }

    private bool _minimapStretch = ScanDefaults.StretchToPage;

    public bool MinimapStretch
    {
        get => _minimapStretch;
        set
        {
            if (!SetField(ref _minimapStretch, value)) return;
            if (value) KeepPresetRatio = true;
            PublishAspectLock();
            if (value) SnapSelectionToPresetRatio();
        }
    }

    public RelayCommand ResetMinimapCommand => _resetMinimapCommand ??= new RelayCommand(ResetToPreviewState);
    private RelayCommand? _resetMinimapCommand;

    private bool _hasPreviewState;
    private int _previewPresetIndex;
    private AspectLock? _previewLock;
    private (double L, double T, double R, double B) _previewSel;

    private void CapturePreviewState()
    {
        _previewPresetIndex = _pagePresetIndex;
        _previewLock = _presetLock;
        _previewSel = (_selL, _selT, _selR, _selB);
        _hasPreviewState = true;
    }

    private void ResetMinimapOptions()
    {
        _minimapStretch = ScanDefaults.StretchToPage;
        _keepPresetRatio = ScanDefaults.KeepPresetRatio || _minimapStretch;
        OnPropertyChanged(nameof(MinimapStretch));
        OnPropertyChanged(nameof(KeepPresetRatio));
        PublishAspectLock();
    }

    private void ResetCropSelection()
    {
        _selectedArea = null;
        (_selL, _selT, _selR, _selB) = (0, 0, 1, 1);
        _hasPreviewState = false;
        ResetMinimapOptions();
        NotifyAreaSize();
        SetPresetLock(null);
        PresetSelectionRequested?.Invoke(this, (0, 0, 1, 1));
    }

    private void ResetToPreviewState()
    {
        ResetMinimapOptions();
        if (!_hasPreviewState || _previewImage is null) return;
        _pagePresetIndex = _previewPresetIndex;
        OnPropertyChanged(nameof(PagePresetIndex));
        SetPresetLock(_previewLock);
        SetSelectedArea(_previewSel.L, _previewSel.T, _previewSel.R, _previewSel.B);
        PresetSelectionRequested?.Invoke(this, _previewSel);
    }

    private void SetPresetLock(AspectLock? presetLock)
    {
        _presetLock = presetLock;
        PublishAspectLock();
    }

    private AspectLock? EffectiveLock =>
        _presetLock is not { } lk ? null : _minimapStretch ? lk : lk with { MinWidth = MinCropNorm };

    private const double MinCropNorm = 0.02;

    private void PublishAspectLock()
    {
        AspectLockChanged?.Invoke(this, _keepPresetRatio ? EffectiveLock : null);
        OnPropertyChanged(nameof(AreaMinWidth));
        OnPropertyChanged(nameof(AreaMinHeight));
        OnPropertyChanged(nameof(AreaMinWidthTip));
        OnPropertyChanged(nameof(AreaMinHeightTip));
    }

    private void SnapSelectionToPresetRatio()
    {
        if (_previewImage is null || EffectiveLock is not { } lk) return;
        var (l, t, r, b) = FitToRatio(_selL, _selT, _selR - _selL, null, lk);
        SetSelectedArea(l, t, r, b);
        PresetSelectionRequested?.Invoke(this, (l, t, r, b));
    }

    private static (double L, double T, double R, double B) FitToRatio(
        double left, double top, double? width, double? height, AspectLock lk)
    {
        double w = Math.Clamp(width ?? (height!.Value * lk.Ratio), lk.MinWidth, lk.MaxWidth);
        double h = w / lk.Ratio;
        if (left + w > 1) { w = 1 - left; h = w / lk.Ratio; }
        if (top + h > 1) { h = 1 - top; w = h * lk.Ratio; }
        return (left, top, left + w, top + h);
    }

    private void ApplyPagePreset(string preset)
    {
        if (preset == CustomPresetName)
        {
            SetPresetLock(null);
            return;
        }
        if (_previewImage is null || !PagePresetSizes.TryGetValue(preset, out var size)) return;

        const double mmPerInch = 25.4;
        double bedWidthMm = _previewImage.WidthPx / (double)PreviewDpi * mmPerInch;
        double bedHeightMm = _previewImage.HeightPx / (double)PreviewDpi * mmPerInch;
        if (bedWidthMm <= 0 || bedHeightMm <= 0) return;

        double fit = Math.Min(1.0, Math.Min(bedWidthMm / size.WidthMm, bedHeightMm / size.HeightMm));
        double normRight = size.WidthMm * fit / bedWidthMm;
        double normBottom = size.HeightMm * fit / bedHeightMm;

        SetPresetLock(new AspectLock(normRight / normBottom, normRight * PresetMinScale, normRight));
        SetSelectedArea(0, 0, normRight, normBottom);
        PresetSelectionRequested?.Invoke(this, (0, 0, normRight, normBottom));
    }

    private bool TryApplyDetectedPaper()
    {
        if (_previewImage is null) return false;
        var found = PaperDetector.Detect(_previewImage, PreviewDpi);
        if (found is null) return false;

        double bedW = _previewImage.WidthPx, bedH = _previewImage.HeightPx;
        double cx = (found.LeftPx + found.RightPx) / 2.0 / bedW, cy = (found.TopPx + found.BottomPx) / 2.0 / bedH;
        double pxPerMm = PreviewDpi / 25.4;
        double w = (found.Landscape ? found.Size.HeightMm : found.Size.WidthMm) * pxPerMm / bedW;
        double h = (found.Landscape ? found.Size.WidthMm : found.Size.HeightMm) * pxPerMm / bedH;
        double l = Math.Clamp(cx - w / 2, 0, Math.Max(0, 1 - w)), t = Math.Clamp(cy - h / 2, 0, Math.Max(0, 1 - h));
        double r = Math.Min(1, l + w), b = Math.Min(1, t + h);

        _pagePresetIndex = Array.IndexOf(PagePresetChoices.ToArray(), found.Size.Name);
        OnPropertyChanged(nameof(PagePresetIndex));
        SetPresetLock(new AspectLock((r - l) / (b - t), (r - l) * PresetMinScale, r - l));
        SetSelectedArea(l, t, r, b);
        PresetSelectionRequested?.Invoke(this, (l, t, r, b));
        return true;
    }

    public AsyncRelayCommand PreviewCommand { get; }

    private async Task PreviewAsync()
    {
        if (_driverId is null) { SetUnavailable(Strings.NoScannerSelectedMessage); return; }
        State = ScanScreenState.Previewing;
        StatusMessage = Strings.PreviewingStatus;
        ResetCropSelection();
        try
        {
            PreviewImage = await _engine.PreviewAsync(_driverId, BuildOptions());
            if (_presetChosen)
            {
                ApplyPagePreset(PagePresetChoices[_pagePresetIndex]);
                NotifyAreaSize();
                StatusMessage = Strings.PreviewReadyStatus;
            }
            else if (TryApplyDetectedPaper())
                StatusMessage = string.Format(Strings.PreviewReadyLooksLikeStatus, PagePresetChoices[_pagePresetIndex]);
            else
            {
                _pagePresetIndex = Array.IndexOf(PagePresetChoices.ToArray(), "Letter");
                OnPropertyChanged(nameof(PagePresetIndex));
                ApplyPagePreset("Letter");
                NotifyAreaSize();
                StatusMessage = Strings.PreviewReadyStatus;
            }
            CapturePreviewState();
            State = ScanScreenState.Ready;
        }
        catch (Exception ex)
        {
            State = ScanScreenState.Ready;
            StatusMessage = string.Format(Strings.PreviewFailedStatus, ex.Message);
        }
    }

    public List<ScannedPageViewModel> Pages { get; } = new();

    private int _pageCount;
    public int PageCount
    {
        get => _pageCount;
        private set { SetField(ref _pageCount, value); OnPropertyChanged(nameof(HasPages)); SaveCommand.RaiseCanExecuteChanged(); }
    }
    public bool HasPages => _pageCount > 0;

    public AsyncRelayCommand ScanCommand { get; }

    private ScanOptions BuildScanOptions()
    {
        var options = BuildOptions();
        if (options.Area is null && _previewImage is null && !IsCustomPreset
            && PagePresetSizes.TryGetValue(PagePresetChoices[_pagePresetIndex], out var size))
            options = options with { Area = new ScanArea(0, 0, size.WidthMm, size.HeightMm) };
        return options;
    }

    private async Task ScanAsync()
    {
        if (_driverId is null) { SetUnavailable(Strings.NoScannerSelectedMessage); return; }
        State = ScanScreenState.Scanning;
        StatusMessage = IsFeederSelected ? Strings.ScanningFeederStatus : Strings.ScanningStatus;

        try
        {
            if (IsFeederSelected)
            {
                await foreach (var page in _engine.ScanBatchAsync(_driverId, BuildScanOptions()))
                {
                    AddPage(page);
                    StatusMessage = string.Format(Strings.ScannedProgressStatus, Strings.Pages(PageCount));
                }
                StatusMessage = PageCount == 0
                    ? Strings.NoPagesScannedStatus
                    : string.Format(Strings.BatchFinishedStatus, Strings.Pages(PageCount));
            }
            else
            {
                var options = BuildScanOptions();
                var page = await _engine.ScanSingleAsync(_driverId, options);
                if (_previewImage is null && options.Area is null && options.Source == ScanSource.Flatbed
                    && PaperDetector.Detect(page, options.ResolutionDpi) is { } found)
                {
                    page = CapturedPageOps.Crop(page, found.LeftPx, found.TopPx,
                        Math.Min(page.WidthPx, found.RightPx), Math.Min(page.HeightPx, found.BottomPx));
                }
                AddPage(page);
                StatusMessage = string.Format(Strings.ScannedTotalStatus, Strings.Pages(PageCount));
            }
            PreviewImage = null;
            ResetCropSelection();
            State = ScanScreenState.Ready;
        }
        catch (Exception ex)
        {
            State = ScanScreenState.Ready;
            StatusMessage = PageCount > 0
                ? string.Format(Strings.ScanStoppedStatus, ex.Message, Strings.Pages(PageCount))
                : string.Format(Strings.ScanFailedStatus, ex.Message);
        }
    }

    private void AddPage(CapturedPage page)
    {
        string? preset = IsCustomPreset ? null : PagePresetChoices[_pagePresetIndex];
        if (_minimapStretch && preset is not null)
            page.StretchToSheet = PaperDetector.KnownSizes.FirstOrDefault(k => k.Name == preset);
        Pages.Add(new ScannedPageViewModel { Page = page, Number = Pages.Count + 1, PresetName = preset });
        PageCount = Pages.Count;
        OnPropertyChanged(nameof(Pages));
    }

    public void MovePage(int index, int delta)
    {
        int target = index + delta;
        if (index < 0 || index >= Pages.Count || target < 0 || target >= Pages.Count) return;
        (Pages[index], Pages[target]) = (Pages[target], Pages[index]);
        for (int i = 0; i < Pages.Count; i++) Pages[i].Number = i + 1;
        OnPropertyChanged(nameof(Pages));
    }

    public void RemovePage(int index)
    {
        if (index < 0 || index >= Pages.Count) return;
        Pages.RemoveAt(index);
        for (int i = 0; i < Pages.Count; i++) Pages[i].Number = i + 1;
        PageCount = Pages.Count;
        OnPropertyChanged(nameof(Pages));
    }

    private string _unavailableMessage = "";
    public string UnavailableMessage
    {
        get => _unavailableMessage;
        private set => SetField(ref _unavailableMessage, value);
    }

    public AsyncRelayCommand ChooseAnotherScannerCommand { get; }
    public AsyncRelayCommand RefreshScannersCommand { get; }

    private async Task RefreshScannersAsync()
    {
        State = ScanScreenState.Checking;
        StatusMessage = Strings.CheckingScannerStatus;
        try { await _registry.LoadAndRefreshAsync(); }
        catch {  }

        _scannerChoices = null;
        OnPropertyChanged(nameof(ScannerChoices));
        if (_driverId is null || _registry.Entries.All(e => e.DriverId != _driverId))
        {
            if (_driverId != _registry.DefaultDriverId) ResetForScannerChange();
            _driverId = _registry.DefaultDriverId;
        }
        RefreshScannerName();
        await InitializeAsync();
    }

    private void ResetForScannerChange()
    {
        PreviewImage = null;
        ResetCropSelection();
        StatusMessage = null;
    }

    private void SetUnavailable(string message)
    {
        UnavailableMessage = message;
        State = ScanScreenState.ScannerUnavailable;
    }

    public async Task InitializeAsync()
    {
        State = ScanScreenState.Checking;
        StatusMessage = Strings.CheckingScannerStatus;
        try { await CheckScannerAsync(); }
        finally
        {
            if (StatusMessage == Strings.CheckingScannerStatus) StatusMessage = null;
        }
    }

    private async Task CheckScannerAsync()
    {
        if (_driverId is null && !NoScannersRegistered)
        {
            _driverId = await _registry.EnsureDefaultAsync();
            RefreshScannerName();
        }
        if (_driverId is null)
        {
            SetUnavailable(Strings.NoScannerSetUpMessage);
            return;
        }
        var entry = _registry.Entries.FirstOrDefault(e => e.DriverId == _driverId);
        var name = entry?.DisplayName ?? Strings.SelectedScannerFallbackName;
        try
        {
            bool reachable = await _engine.IsReachableAsync(_driverId);
            if (!reachable)
            {
                _driverId = await _registry.TryRecoverAsync(_driverId);
                reachable = await _engine.IsReachableAsync(_driverId);
            }
            IsReady = reachable;
            if (entry is not null)
            {
                entry.Reachability = reachable ? ScannerReachability.Ready : ScannerReachability.NotReady;
                SyncChoiceDots();
            }
            if (!reachable)
            {
                SetUnavailable(string.Format(Strings.ScannerNotReachableMessage, name));
                return;
            }

            try
            {
                ApplySourceCapabilities(await _engine.GetCapabilitiesAsync(_driverId));
            }
            catch
            {
                ApplySourceCapabilities(new ScannerCapabilities(SupportsFlatbed: true, SupportsFeeder: false, SupportsDuplex: false));
            }
            State = ScanScreenState.Ready;
        }
        catch (Exception ex)
        {
            IsReady = false;
            if (entry is not null)
            {
                entry.Reachability = ScannerReachability.NotReady;
                SyncChoiceDots();
            }
            SetUnavailable(string.Format(Strings.ScannerReachFailedMessage, name, ex.Message));
        }
    }

    private async Task ChooseAnotherAsync()
    {
        await _openManageScanners();
        _scannerChoices = null;
        OnPropertyChanged(nameof(ScannerChoices));
        _driverId ??= _registry.DefaultDriverId;
        RefreshScannerName();
        await InitializeAsync();
    }

    private void RefreshScannerName()
    {
        var entries = _registry.Entries;
        ScannerRegistryEntry? entry = null;
        int index = -1;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].DriverId != _driverId) continue;
            entry = entries[i];
            index = i;
            break;
        }
        ScannerName = entry?.DisplayName ?? Strings.NoScannerSelectedName;
        IsReady = entry?.Reachability == KRocketDocumentScanner.Core.Models.ScannerReachability.Ready;

        _scannerIndex = index;
        OnPropertyChanged(nameof(ScannerIndex));
    }

    public PaperSize? PdfSheet
    {
        get
        {
            if (Pages.Count < 2) return null;
            var first = Pages[0].PresetName;
            if (first is null || Pages.Any(p => p.PresetName != first)) return null;
            return PaperDetector.KnownSizes.FirstOrDefault(k => k.Name == first);
        }
    }

    public AsyncRelayCommand SaveCommand { get; }

    private async Task SaveAsync()
    {
        if (Pages.Count == 0) return;
        string suggested = Strings.SuggestedFileName;
        try
        {
            if (await _saveAs(suggested))
                Refresh();
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Strings.SaveFailedStatus, ex.Message);
        }
    }

    private void Refresh()
    {
        Pages.Clear();
        PageCount = Pages.Count;
        OnPropertyChanged(nameof(Pages));
        PreviewImage = null;
        ResetCropSelection();
        StatusMessage = null;
    }

    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        private set { SetField(ref _statusMessage, value); OnPropertyChanged(nameof(HasStatusMessage)); }
    }
    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);
}
