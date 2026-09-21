using KRocketDocumentScanner.App.ViewModels.Localization;
using KRocketDocumentScanner.App.ViewModels.Mvvm;
using KRocketDocumentScanner.Core.Abstractions;
using KRocketDocumentScanner.Core.Models;
using KRocketDocumentScanner.Core.Registry;

namespace KRocketDocumentScanner.App.ViewModels;

/// <summary>One captured page in the current scan session, before saving.</summary>
public sealed class ScannedPageViewModel : ObservableObject
{
    public required CapturedPage Page { get; set; }
    public required int Number { get; set; }

    /// <summary>The preset (Letter, A4, …) this page was scanned/cropped with; null for Custom.</summary>
    public string? PresetName { get; init; }
    public string Label => string.Format(Strings.PageLabelFormat, Number);
}

/// <summary>Crop shape constraint for a preset: fixed proportions, and how far (as a fraction of
/// the whole image's width, same normalized space as the crop itself) the crop may be resized.</summary>
/// <summary>Starting values for the scan screen's crop options, kept together so a settings
/// menu can change them later. Read once when a scan window opens.</summary>
public static class ScanDefaults
{
    /// <summary>Preset the crop dropdown starts on (a name from ScanViewModel.PagePresetChoices).</summary>
    public static string PresetName { get; set; } = ScanViewModel.CustomPresetName;
    /// <summary>Whether "Keep {preset} page ratio" starts on.</summary>
    public static bool KeepPresetRatio { get; set; } = true;
    /// <summary>Whether "Stretch to page" starts on.</summary>
    public static bool StretchToPage { get; set; } = false;
    /// <summary>Whether the size boxes start in centimetres (true) or inches (false).</summary>
    public static bool MetricUnits { get; set; } = false;
}

public readonly record struct AspectLock(double Ratio, double MinWidth, double MaxWidth);

/// <summary>One row in the scanner picker dropdown — just enough to show a status icon next
/// to the name, no status text. Reachability has three states: Ready (checked, available),
/// NotReady (checked, not available) and Unknown (not checked).</summary>
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
    /// <summary>Ready to scan — options shown.</summary>
    Ready,
    /// <summary>Checking whether the selected scanner answers; nothing can be started yet.</summary>
    Checking,
    /// <summary>The selected scanner isn't reachable; offer Retry / pick another.</summary>
    ScannerUnavailable,
    Previewing,
    Scanning,
}

/// <summary>
/// Drives the Scan window: options, preview, capture (single or multi-page), per-page
/// edits, and save. Deliberately holds captured pages even if a later page fails —
/// a feeder jam on page 7 shouldn't throw away pages 1-6.
/// </summary>
public sealed class ScanViewModel : ObservableObject
{
    private readonly IScannerEngine _engine;
    private readonly ScannerRegistryManager _registry;
    private readonly Func<string, Task<bool>> _saveAs;   // returns false if cancelled
    private readonly Func<Task> _openManageScanners;
    private readonly Action _close;

    public ScanViewModel(
        IScannerEngine engine,
        ScannerRegistryManager registry,
        string? preselectedDriverId,
        Func<string, Task<bool>> saveAs,
        Func<Task> openManageScanners,
        Action close)
    {
        _engine = engine;
        _registry = registry;
        _saveAs = saveAs;
        _openManageScanners = openManageScanners;
        _close = close;

        _driverId = preselectedDriverId ?? registry.DefaultDriverId;
        // The startup reachability check finishes after this window exists; keep the dots true.
        registry.Changed += SyncChoiceDots;
        // Registry events can arrive on any thread; handle them on the thread that made this
        // window so screen state is only touched there.
        _uiContext = SynchronizationContext.Current;
        registry.ScannerRemoved += OnScannerRemoved;
        registry.ScannerRepointed += OnScannerRepointed;

        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => State == ScanScreenState.Ready);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => State == ScanScreenState.Ready);
        ChooseAnotherScannerCommand = new AsyncRelayCommand(ChooseAnotherAsync);
        RefreshScannersCommand = new AsyncRelayCommand(RefreshScannersAsync, () => !IsWorking);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => Pages.Count > 0);

        RefreshScannerName();
    }

    private string? _driverId;
    private readonly SynchronizationContext? _uiContext;

    /// <summary>Stops listening to the registry; call when the window closes.</summary>
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

    // The scanner this window is using was removed (from any window's Manage Scanners): use the
    // default scanner and reset the preview. Removing and re-adding counts as a change too.
    // Some other scanner being removed only refreshes the dropdown list.
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

    // The scanner this window is using is the same saved scanner over a different connection
    // now: follow it quietly — nothing resets, unsaved work stays.
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
    /// <summary>Drives the status dot next to the scanner name — the same status display
    /// that used to live on the main window now lives here, since this is the one window
    /// that actually needs it (see ScanWindow.axaml).</summary>
    public bool IsReady
    {
        get => _isReady;
        private set => SetField(ref _isReady, value);
    }

    /// <summary>Every saved scanner, for the picker dropdown — same source list
    /// ManageScannersViewModel shows, not a fresh discovery scan. IsReady drives each row's
    /// status dot so available vs. not is visible without adding any status text.</summary>
    public IReadOnlyList<ScannerChoice> ScannerChoices => _scannerChoices ??= BuildChoices();
    private List<ScannerChoice>? _scannerChoices;

    private List<ScannerChoice> BuildChoices() => _registry.Entries
        .Select(e => new ScannerChoice(e.DriverId, e.DisplayName, e.Reachability))
        .ToList();

    /// <summary>Pushes the registry's current reachability into the existing dropdown rows
    /// (same list instance, so the ComboBox selection is untouched — see RefreshScannerName).</summary>
    private void SyncChoiceDots()
    {
        foreach (var choice in _scannerChoices ?? [])
        {
            var entry = _registry.Entries.FirstOrDefault(e => e.DriverId == choice.DriverId);
            if (entry is not null) choice.Reachability = entry.Reachability;
        }
    }

    /// <summary>True only when nothing is registered at all — the one case the startup popup
    /// (ScannerUnavailableWindow) should interrupt for. A registered-but-unreachable default
    /// scanner still lands in ScannerUnavailable state (disabled Preview/Scan, dropdown shows
    /// it as not-ready) but must not pop up a dialog over it.</summary>
    public bool NoScannersRegistered => _registry.Entries.Count == 0;

    private int _scannerIndex = -1;
    /// <summary>Picking a different saved scanner switches the active driver immediately —
    /// no need to go through Manage Scanners just to change which one is in use.</summary>
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
    /// <summary>The app is waiting on the scanner (checking, previewing or scanning) — drives
    /// the progress bar so the window never looks idle while it waits.</summary>
    public bool IsWorking => IsBusy || _state == ScanScreenState.Checking;
    public bool ShowUnavailable => _state == ScanScreenState.ScannerUnavailable;

    // ---------------- Options ----------------
    public IReadOnlyList<int> ResolutionChoices { get; } = new[] { 100, 150, 200, 300, 400, 600 };

    private int _resolutionDpi = 300;
    public int ResolutionDpi { get => _resolutionDpi; set => SetField(ref _resolutionDpi, value); }

    public IReadOnlyList<string> ColorModeChoices { get; } = new[] { Strings.ColorModeColor, Strings.ColorModeGrayscale, Strings.ColorModeBlackAndWhite, Strings.ColorModeBlackAndWhiteClean };

    private int _colorModeIndex;
    public int ColorModeIndex { get => _colorModeIndex; set => SetField(ref _colorModeIndex, value); }

    // Which sources are actually worth offering, driven by the scanner's own reported
    // capabilities (see ApplySourceCapabilities) — starts flatbed-only, the safe common
    // denominator, until InitializeAsync learns otherwise.
    private IReadOnlyList<ScanSource> _availableSources = new[] { ScanSource.Flatbed };

    public IReadOnlyList<string> SourceChoices => _availableSources.Select(SourceLabel).ToList();

    /// <summary>Only worth showing the picker at all when there's more than one real choice —
    /// a flatbed-only scanner has nothing to pick between.</summary>
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

    /// <summary>Applies the scanner's reported capabilities to the Source list. Called once
    /// InitializeAsync knows what this device actually supports.</summary>
    private void ApplySourceCapabilities(ScannerCapabilities caps)
    {
        var sources = new List<ScanSource>();
        if (caps.SupportsFlatbed) sources.Add(ScanSource.Flatbed);
        if (caps.SupportsFeeder) sources.Add(ScanSource.AutomaticDocumentFeeder);
        if (caps.SupportsFeeder && caps.SupportsDuplex) sources.Add(ScanSource.AutomaticDocumentFeederDuplex);
        if (sources.Count == 0) sources.Add(ScanSource.Flatbed); // always leave at least one usable option

        _availableSources = sources;
        // Set directly (not via the SourceIndex setter) and always notify — SourceIndex may
        // already happen to be 0, in which case the setter's usual "skip if unchanged" would
        // leave the ComboBox's selection stale right after SourceChoices swaps to a new list.
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
        // Both are feeder/batch-scan concepts — a flatbed capture is always exactly one
        // hand-placed page, so "straighten a crooked feed" and "drop this blank page from the
        // batch" don't apply. The checkboxes are disabled (not hidden) for Flatbed in
        // ScanWindow.axaml, but disabling a CheckBox doesn't stop it feeding its last-checked
        // value into the binding, so guard here too — otherwise a flatbed scan of a genuinely
        // blank page could come back with zero images if DropBlankPages was left checked.
        AutoDeskew = AutoDeskew && IsFeederSelected,
        DropBlankPages = DropBlankPages && IsFeederSelected,
        Area = _selectedArea,
    };

    // ---------------- Preview ----------------
    private CapturedPage? _previewImage;
    public CapturedPage? PreviewImage
    {
        get => _previewImage;
        private set { SetField(ref _previewImage, value); OnPropertyChanged(nameof(HasPreview)); NotifyAreaSize(); }
    }
    public bool HasPreview => _previewImage is not null;

    // Resolution the preview itself was captured at — fixed by PreviewAsync below. Needed to
    // convert the user's on-screen crop selection (a fraction of the preview image) into a
    // physical size in millimeters, which is what ScanArea/the engine actually work in.
    private const int PreviewDpi = 100;
    private ScanArea? _selectedArea;

    /// <summary>
    /// Called by the view when the user finishes dragging the crop selection. Normalized
    /// coordinates (0..1, relative to the preview image) are converted to millimeters here,
    /// using the preview's own known pixel size and DPI — a physical unit that stays correct
    /// regardless of what DPI the eventual full-resolution scan uses.
    /// </summary>
    public void SetSelectedArea(double normLeft, double normTop, double normRight, double normBottom)
    {
        if (_previewImage is null) { _selectedArea = null; return; }

        (_selL, _selT, _selR, _selB) = (normLeft, normTop, normRight, normBottom);
        const double mmPerInch = 25.4;
        double widthMm = _previewImage.WidthPx / (double)PreviewDpi * mmPerInch;
        double heightMm = _previewImage.HeightPx / (double)PreviewDpi * mmPerInch;

        // A selection covering (almost) the whole preview means "no crop" — store null rather
        // than a redundant full-bed rectangle, so a scanner with a differently-sized bed isn't
        // artificially constrained to the preview's exact dimensions.
        if (normLeft <= 0.01 && normTop <= 0.01 && normRight >= 0.99 && normBottom >= 0.99)
        {
            _selectedArea = null;
            NotifyAreaSize();
            return;
        }

        _selectedArea = new ScanArea(normLeft * widthMm, normTop * heightMm, normRight * widthMm, normBottom * heightMm);
        NotifyAreaSize();
    }

    // Current crop as a fraction of the preview (0..1) — the same space the view reports in.
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
    /// <summary>Toggle between inches (false) and centimetres (true).</summary>
    public bool IsMetric
    {
        get => _unitIndex == 1;
        set { UnitIndex = value ? 1 : 0; OnPropertyChanged(nameof(UnitLabel)); }
    }
    public string UnitLabel => UnitChoices[_unitIndex];
    private double MmPerUnit => _unitIndex == 1 ? 10.0 : 25.4;

    /// <summary>Width/height of whatever will be scanned (the crop, or the whole bed when
    /// nothing is cropped), in the chosen unit. Custom shows whole numbers and can be edited
    /// (resizing from the top-left corner); a preset shows its true size, fractions included,
    /// and is read-only.</summary>
    // Nullable because an emptied box reports null while the user is still typing — that is
    // ignored (the crop keeps its size); the view refreshes the box when it loses focus.
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

    /// <summary>Smallest width/height the size boxes accept: the kept-ratio preset's 70% floor,
    /// or 1 when the crop resizes freely.</summary>
    public decimal AreaMinWidth => MinDisplaySize(lk => lk.MinWidth * BedWidthMm);
    public decimal AreaMinHeight => MinDisplaySize(lk => lk.MinWidth / lk.Ratio * BedHeightMm);

    /// <summary>Tooltip text explaining a size box's minimum — only while "Stretch to page" makes
    /// the 70% floor apply; null (no tooltip) otherwise.</summary>
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
        // Rounded up, so the box never allows (or shows) a value under the true floor.
        return Math.Max(1, (decimal)(Math.Ceiling(minMm(lk) / MmPerUnit * factor) / factor));
    }

    private decimal DisplaySize(double mm)
    {
        int digits = IsCustomPreset ? 0 : (_unitIndex == 1 ? 1 : 2);
        return (decimal)Math.Round(mm / MmPerUnit, digits, MidpointRounding.AwayFromZero);
    }

    /// <summary>Number format for the size boxes: whole numbers for Custom, fractions for presets.</summary>
    public string SizeFormat => IsCustomPreset ? "0" : (_unitIndex == 1 ? "0.#" : "0.##");

    /// <summary>The selected preset's page size in mm (portrait), or null for Custom.</summary>
    public (double WidthMm, double HeightMm)? PresetPageSizeMm =>
        !IsCustomPreset && PagePresetSizes.TryGetValue(PagePresetChoices[_pagePresetIndex], out var size) ? size : null;

    public bool IsCustomPreset => _pagePresetIndex == CustomIndex;
    public bool IsSizeEditable => HasPreview && IsCustomPreset;

    /// <summary>Puts the size boxes back to the crop's current (valid) size.</summary>
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
        // Keeping the preset's ratio: the other side follows the one that was edited.
        if (_keepPresetRatio && EffectiveLock is { } lk)
            (_, _, r, b) = widthMm is not null
                ? FitToRatio(_selL, _selT, r - _selL, null, lk)
                : FitToRatio(_selL, _selT, null, b - _selT, lk);
        // Always re-notify (inside SetSelectedArea) so an out-of-range entry snaps back on screen.
        SetSelectedArea(_selL, _selT, r, b);
        PresetSelectionRequested?.Invoke(this, (_selL, _selT, r, b));
    }

    // ---------------- Preset: Custom (free crop) or a common paper size ----------------
    public const string CustomPresetName = "Custom";

    /// <summary>How small a preset's crop may be resized, as a fraction of the preset's size —
    /// small enough to trim an ad off an edge, not so small the shape looks odd.</summary>
    private const double PresetMinScale = 0.7;

    public static readonly IReadOnlyList<string> PagePresetChoices =
        PaperDetector.KnownSizes.Select(p => p.Name).Append(CustomPresetName).ToArray();

    private static readonly Dictionary<string, (double WidthMm, double HeightMm)> PagePresetSizes =
        PaperDetector.KnownSizes.ToDictionary(p => p.Name, p => (p.WidthMm, p.HeightMm));

    private int _pagePresetIndex = DefaultPresetIndex();

    // Custom is the last choice; an unknown default name falls back to it.
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
            _presetChosen = true; // the user picked it — a preview must not override it
            SetField(ref _pagePresetIndex, value);
            ApplyPagePreset(PagePresetChoices[value]);
            NotifyAreaSize();
        }
    }

    // True once the user has picked a preset themselves; until then a preview may pick one
    // (from the detected paper, or Letter) and that isn't treated as the user's choice.
    private bool _presetChosen;

    /// <summary>Raised when a page-size preset (not Free form) is chosen, so the view can move
    /// the actual on-screen crop rectangle to match — the ViewModel doesn't touch the View's
    /// control directly, same separation as everywhere else in this class.</summary>
    public event EventHandler<(double Left, double Top, double Right, double Bottom)>? PresetSelectionRequested;

    /// <summary>Raised whenever the crop shape should become locked to a fixed aspect ratio
    /// (a preset was chosen — the ratio is in the SAME normalized [0,1] coordinate space the
    /// crop selection itself uses, not raw mm, since the bed isn't necessarily square) with
    /// the size range it may be resized within, or unlocked (Custom — null).</summary>
    public event EventHandler<AspectLock?>? AspectLockChanged;

    // The current preset's shape lock (null for Custom). It only takes effect while
    // KeepPresetRatio is on; otherwise the crop resizes freely.
    private AspectLock? _presetLock;
    private bool _keepPresetRatio = ScanDefaults.KeepPresetRatio;

    /// <summary>When on, the crop keeps the proportions of the selected preset.</summary>
    public bool KeepPresetRatio
    {
        get => _keepPresetRatio;
        set
        {
            if (!value && _minimapStretch) value = true; // stretch-to-page needs the ratio kept
            if (!SetField(ref _keepPresetRatio, value)) return;
            PublishAspectLock();
            if (value) SnapSelectionToPresetRatio();
        }
    }

    private bool _minimapStretch = ScanDefaults.StretchToPage;

    /// <summary>"Stretch to page" on the minimap. Turning it on also turns on
    /// <see cref="KeepPresetRatio"/> (and the crop then stays within 70%..100% of the preset).</summary>
    public bool MinimapStretch
    {
        get => _minimapStretch;
        set
        {
            if (!SetField(ref _minimapStretch, value)) return;
            if (value) KeepPresetRatio = true;
            // The 70% floor switches on/off with stretch: re-publish it and, when it just
            // applied, bring an undersized crop up to it.
            PublishAspectLock();
            if (value) SnapSelectionToPresetRatio();
        }
    }

    public RelayCommand ResetMinimapCommand => _resetMinimapCommand ??= new RelayCommand(ResetToPreviewState);
    private RelayCommand? _resetMinimapCommand;

    // What the crop panel looked like the moment the latest preview became available: the
    // preset, its shape lock and the crop rectangle. The Reset button restores exactly this.
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

    /// <summary>Puts "Keep {preset} page ratio" and "Stretch to page" back to their
    /// <see cref="ScanDefaults"/> (no crop change).</summary>
    private void ResetMinimapOptions()
    {
        _minimapStretch = ScanDefaults.StretchToPage;
        _keepPresetRatio = ScanDefaults.KeepPresetRatio || _minimapStretch;
        OnPropertyChanged(nameof(MinimapStretch));
        OnPropertyChanged(nameof(KeepPresetRatio));
        PublishAspectLock();
    }

    /// <summary>Clears the current crop selection and its shape lock back to "nothing picked
    /// yet" (used before a new preview, and after a scan — see PreviewAsync/ScanAsync). The
    /// chosen preset itself is left alone; only the rectangle/lock/minimap-switch state that
    /// was anchored to the now-stale preview image resets.</summary>
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

    /// <summary>Resets the whole crop panel to how it was when the preview just became
    /// available: both switches at their defaults, the preset and crop as they were then.</summary>
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

    /// <summary>The preset lock as it currently applies: its 70% size floor only counts while
    /// "Stretch to page" is on — otherwise the crop only has to keep the ratio.</summary>
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

    /// <summary>Reshapes the current crop to the preset's ratio (top-left stays put, width kept
    /// unless the bed edge forces it smaller).</summary>
    private void SnapSelectionToPresetRatio()
    {
        if (_previewImage is null || EffectiveLock is not { } lk) return;
        var (l, t, r, b) = FitToRatio(_selL, _selT, _selR - _selL, null, lk);
        SetSelectedArea(l, t, r, b);
        PresetSelectionRequested?.Invoke(this, (l, t, r, b));
    }

    /// <summary>A crop at (left, top) with the given normalized width or height, completed to
    /// the lock's ratio (normalized width / height), kept within the lock's size range (the
    /// preset's 70%..100%) and inside the bed.</summary>
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
        // "Custom" itself doesn't move anything — it just means the NEXT drag is
        // unconstrained, which is already how dragging works — but it DOES need to release any
        // aspect-ratio lock a previous preset left in place.
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

        // Anchored to the top-left corner — where a page is conventionally placed against a
        // flatbed's corner guides — and clamped to the bed in case the preset is larger than
        // what this particular scanner can actually capture.
        // If the sheet is bigger than the bed, shrink it uniformly (never one side only), so it
        // keeps the paper's true proportions.
        double fit = Math.Min(1.0, Math.Min(bedWidthMm / size.WidthMm, bedHeightMm / size.HeightMm));
        double normRight = size.WidthMm * fit / bedWidthMm;
        double normBottom = size.HeightMm * fit / bedHeightMm;

        SetPresetLock(new AspectLock(normRight / normBottom, normRight * PresetMinScale, normRight));
        SetSelectedArea(0, 0, normRight, normBottom);
        PresetSelectionRequested?.Invoke(this, (0, 0, normRight, normBottom));
    }

    /// <summary>Guesses the paper on the bed from the fresh preview; on a match, selects the
    /// preset and puts the crop exactly over the sheet (at the preset's true size) instead of
    /// leaving Custom. Returns false — changing nothing — when nothing is recognised.</summary>
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
        // Reset the crop BEFORE contacting the scanner: BuildOptions feeds _selectedArea to the
        // engine, so a leftover crop would otherwise make the preview cover only that region.
        // The preset itself is kept — it's an option like Color or Resolution — but the crop
        // box and its shape lock start over on the new preview.
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
                // Couldn't tell what's on the bed — assume Letter rather than an unsized crop.
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

    // ---------------- Capture ----------------
    public List<ScannedPageViewModel> Pages { get; } = new();

    private int _pageCount;
    public int PageCount
    {
        get => _pageCount;
        private set { SetField(ref _pageCount, value); OnPropertyChanged(nameof(HasPages)); SaveCommand.RaiseCanExecuteChanged(); }
    }
    public bool HasPages => _pageCount > 0;

    public AsyncRelayCommand ScanCommand { get; }

    /// <summary>Options for the real scan. With a preset picked but no preview to have positioned
    /// a crop on, the preset's own size is used from the top-left corner (clamped to the bed by
    /// the engine), so the preset still applies.</summary>
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
                // No preview to have set a crop from: guess the sheet from the scan itself and
                // trim to it when its size is recognised, else keep the full bed.
                if (_previewImage is null && options.Area is null && options.Source == ScanSource.Flatbed
                    && PaperDetector.Detect(page, options.ResolutionDpi) is { } found)
                {
                    page = CapturedPageOps.Crop(page, found.LeftPx, found.TopPx,
                        Math.Min(page.WidthPx, found.RightPx), Math.Min(page.HeightPx, found.BottomPx));
                }
                AddPage(page);
                StatusMessage = string.Format(Strings.ScannedTotalStatus, Strings.Pages(PageCount));
            }
            // The preview (and its crop selection) is of the bed *before* this scan — stale
            // now that a page has actually been captured. Clear it so the preview area goes
            // back to "Preview the scan to see the page here", and the crop settings reset
            // with it; scanning again needs a fresh Preview.
            PreviewImage = null;
            ResetCropSelection();
            State = ScanScreenState.Ready;
        }
        catch (Exception ex)
        {
            State = ScanScreenState.Ready;
            // Pages captured before the failure are deliberately kept — a jam on page 7
            // shouldn't discard pages 1-6.
            StatusMessage = PageCount > 0
                ? string.Format(Strings.ScanStoppedStatus, ex.Message, Strings.Pages(PageCount))
                : string.Format(Strings.ScanFailedStatus, ex.Message);
        }
    }

    private void AddPage(CapturedPage page)
    {
        // Remember the preset this page was made with (Custom → null).
        string? preset = IsCustomPreset ? null : PagePresetChoices[_pagePresetIndex];
        // "Stretch to page" is fixed per page at capture: only pages captured while it is on
        // fill their sheet; toggling it afterwards affects the next page, not this one.
        if (_minimapStretch && preset is not null)
            page.StretchToSheet = PaperDetector.KnownSizes.FirstOrDefault(k => k.Name == preset);
        Pages.Add(new ScannedPageViewModel { Page = page, Number = Pages.Count + 1, PresetName = preset });
        PageCount = Pages.Count;
        OnPropertyChanged(nameof(Pages));
    }

    /// <summary>Moves a page one position up (-1) or down (+1); the saved file follows this
    /// order, so pages are renumbered to match.</summary>
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

    // ---------------- Scanner availability ----------------
    private string _unavailableMessage = "";
    public string UnavailableMessage
    {
        get => _unavailableMessage;
        private set => SetField(ref _unavailableMessage, value);
    }

    public AsyncRelayCommand ChooseAnotherScannerCommand { get; }
    public AsyncRelayCommand RefreshScannersCommand { get; }

    /// <summary>The refresh icon beside the scanner list: re-checks every saved scanner (which
    /// also finds one switched on after the app started), rebuilds the dropdown, then
    /// re-checks the selected one.</summary>
    private async Task RefreshScannersAsync()
    {
        State = ScanScreenState.Checking;
        StatusMessage = Strings.CheckingScannerStatus;
        try { await _registry.LoadAndRefreshAsync(); }
        catch { /* leave the list as it was; the selected scanner's own check below reports */ }

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

    /// <summary>A different scanner was chosen: the preview (of the old scanner's bed), the crop
    /// on it and any status text no longer apply, so they reset. Pages already scanned are
    /// unsaved work and are kept.</summary>
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

    /// <summary>Checks the selected scanner is actually reachable. Call when the window opens.</summary>
    public async Task InitializeAsync()
    {
        // Nothing can be previewed or scanned until this check has passed.
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
        // Nothing selected but scanners exist: use the first one that is online (and remember
        // it as the default) rather than making the user pick.
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
                // Possibly switched on after the app started: a fresh discovery may know it
                // under another id. Adopt that id so scanning uses the working connection.
                _driverId = await _registry.TryRecoverAsync(_driverId);
                reachable = await _engine.IsReachableAsync(_driverId);
            }
            // The dot reflects THIS fresh check, not the (possibly stale) registry snapshot
            // taken at construction time — otherwise it could show ready/green while the
            // unavailable screen is showing, which is exactly the kind of inconsistency this
            // status display exists to prevent.
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
                // Which sources to offer is a nice-to-have — if the capability query itself
                // fails, fall back to flatbed-only (the safe common denominator) rather than
                // blocking the whole screen over it.
                ApplySourceCapabilities(new ScannerCapabilities(SupportsFlatbed: true, SupportsFeeder: false, SupportsDuplex: false));
            }
            State = ScanScreenState.Ready;
        }
        catch (Exception ex)
        {
            IsReady = false;
            // The check ran and failed, so the scanner is known to be unavailable (red), not unknown.
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
        // Manage Scanners may have added/removed/renamed entries — this is the only path
        // that needs to rebuild the dropdown's item list; a plain scanner switch (ScannerIndex
        // setter) never does, see RefreshScannerName.
        _scannerChoices = null;
        OnPropertyChanged(nameof(ScannerChoices));
        // Keep the scanner this window is using; only fall back to the default when it has none
        // (its removal is handled by OnScannerRemoved).
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

        // Deliberately does NOT touch/renotify ScannerChoices — replacing the ComboBox's
        // ItemsSource with a new list instance on every plain selection change was resetting
        // its SelectedIndex a tick after this method set it, which is what made the FIRST
        // scanner switch appear to silently fail (the second one "worked" only because by
        // then no further ItemsSource swap raced with it). ScannerChoices is refreshed
        // separately, only where entries can actually change (ChooseAnotherAsync).
        _scannerIndex = index;
        OnPropertyChanged(nameof(ScannerIndex));
    }

    // ---------------- Save ----------------
    // ---------------- PDF page size ----------------
    /// <summary>The sheet every page of a multi-page PDF is centred on: the preset all pages were
    /// made with. Null — each page keeps its own size — for a single page, any Custom page, or
    /// pages made with different presets (one sheet wouldn't suit them).</summary>
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
        string suggested = Strings.SuggestedFileName; // extension comes from the file type picked in the dialog
        try
        {
            if (await _saveAs(suggested))
                _close();
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(Strings.SaveFailedStatus, ex.Message);
        }
    }

    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        private set { SetField(ref _statusMessage, value); OnPropertyChanged(nameof(HasStatusMessage)); }
    }
    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);
}
