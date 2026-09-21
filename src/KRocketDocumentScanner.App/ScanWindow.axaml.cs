using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using KRocketDocumentScanner.App.Theming;
using KRocketDocumentScanner.App.ViewModels;
using KRocketDocumentScanner.App.ViewModels.Localization;
using KRocketDocumentScanner.Core.Models;

namespace KRocketDocumentScanner.App;

public partial class ScanWindow : Window
{
    private readonly ScanViewModel _vm;
    private ScannerUnavailableWindow? _unavailablePopup;

    // Set right before the window is allowed to actually close without asking — either the
    // user already confirmed discarding unsaved pages (OnWindowClosing), or ScanViewModel is
    // closing us itself after a successful Save (CloseAfterSave), where there's nothing left
    // to lose and asking again would be wrong. Closing (any other path — the × button,
    // Alt+F4, the window manager) always checks this first.
    private bool _closeConfirmed;

    /// <summary>Set once the user saves; the caller opens this file afterwards.</summary>
    public string? SavedFilePath { get; private set; }

    // Needed by Avalonia's XAML loader and previewer, which can only create a window with no
    // arguments; the app itself always uses the constructor below.
    public ScanWindow() : this(null)
    {
    }

    public ScanWindow(string? preselectedDriverId, Window? owner = null)
    {
        InitializeComponent();
        var selector0 = this.FindControl<ScanAreaSelector>("AreaSelector");
        var minimap = this.FindControl<MinimapView>("Minimap");
        if (selector0 is not null) selector0.Minimap = minimap;
        var stretch = this.FindControl<ToggleSwitch>("StretchToggle");
        if (stretch is not null && minimap is not null)
            stretch.IsCheckedChanged += (_, _) => minimap.Stretch = stretch.IsChecked == true;

        _vm = new ScanViewModel(
            App.ScannerEngine,
            App.ScannerRegistry,
            preselectedDriverId,
            SaveAsync,
            OpenManageScannersAsync,
            CloseAfterSave);

        DataContext = _vm;
        _vm.PropertyChanged += OnViewModelChanged;
        _vm.PresetSelectionRequested += OnPresetSelectionRequested;
        _vm.AspectLockChanged += (_, aspectLock) =>
            this.FindControl<ScanAreaSelector>("AreaSelector")?.SetLockedAspectRatio(aspectLock);

        var header = this.FindControl<Border>("HeaderBorder");
        if (header is not null) this.AttachActiveStateDimming(header);

        Closing += OnWindowClosing;
        Closed += (_, _) => _vm.Detach();

        // Scanner-unavailable is only ever popped up right after this initial check, not
        // every time State later flips back to ScannerUnavailable (e.g. after picking a
        // different, also-unreachable default scanner from Manage Scanners) — the user only
        // wants it at window-open. And even then, only when nothing is registered at all —
        // a registered default that just happens to be unreachable right now shows inline
        // (disabled Preview/Scan, dropdown marks it not-ready) without interrupting with a
        // popup.
        Opened += async (_, _) =>
        {
            await _vm.InitializeAsync();
            if (_vm.ShowUnavailable && _vm.NoScannersRegistered) ShowUnavailablePopup();
        };
        Opened += (_, _) => { UpdateMinimapPresetName(); UpdateMinimapSizes(); };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Passed to ScanViewModel as the "close" action — called after a successful
    /// Save, when there's nothing left to lose, so it skips the unsaved-pages confirmation
    /// that OnWindowClosing would otherwise show.</summary>
    private void CloseAfterSave()
    {
        _closeConfirmed = true;
        Close();
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || (_vm.Pages.Count == 0 && !_vm.IsBusy && !_vm.HasPreview)) return;

        // Cancel first, then ask — Closing can't itself be awaited, so the only way to make
        // a close attempt wait on user input is to block it here and re-issue Close() below
        // if they confirm (that second attempt takes the _closeConfirmed shortcut above).
        e.Cancel = true;
        var dialog = new ConfirmCloseWindow(_vm.Pages.Count, _vm.IsBusy, _vm.HasPreview);
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed) return;

        _closeConfirmed = true;
        Close();
    }

    // A box left empty (or unparseable) goes back to the crop's current size.
    private void OnSizeBoxLostFocus(object? sender, RoutedEventArgs e) => _vm.RefreshAreaSize();

    private void OnToggleCropPanel(object? sender, RoutedEventArgs e)
    {
        var body = this.FindControl<Border>("CropPanelBody");
        if (body is null) return;
        body.IsVisible = !body.IsVisible;

        var chevron = this.FindControl<TextBlock>("CropPanelChevron");
        if (chevron is not null) chevron.Text = body.IsVisible ? "⌃" : "⌄";
    }

    /// <summary>Tells the minimap the real size of the scan area and of the selected preset page.</summary>
    private void UpdateMinimapSizes()
    {
        var minimap = this.FindControl<MinimapView>("Minimap");
        if (minimap is null) return;
        var page = _vm.PresetPageSizeMm;
        minimap.SetSizes(new Avalonia.Size(_vm.BedWidthMm, _vm.BedHeightMm),
            page is { } p ? new Avalonia.Size(p.WidthMm, p.HeightMm) : default);
    }

    private void UpdateMinimapPresetName()
    {
        var preset = ScanViewModel.PagePresetChoices[_vm.PagePresetIndex];
        // No minimap (or its stretch toggle) for the free-form Custom preset.
        var section = this.FindControl<Control>("MinimapSection");
        if (section is not null) section.IsVisible = preset != ScanViewModel.CustomPresetName;
        var keepRatio = this.FindControl<ToggleSwitch>("KeepRatioToggle");
        if (keepRatio is not null) {
            // The preset name is bold; the rest of the sentence comes from the localized template.
            TextBlock Label()
            {
                var parts = Strings.KeepPageRatio.Split("{0}");
                var text = new TextBlock();
                text.Inlines!.Add(new Avalonia.Controls.Documents.Run(parts[0]));
                text.Inlines.Add(new Avalonia.Controls.Documents.Run(preset) { FontWeight = FontWeight.Bold });
                if (parts.Length > 1) text.Inlines.Add(new Avalonia.Controls.Documents.Run(parts[1]));
                return text;
            }
            keepRatio.OnContent = Label();
            keepRatio.OffContent = Label();
        }
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Bitmap creation has to happen on the UI thread, so images are rebuilt here in
        // response to VM changes rather than being bound directly.
        if (e.PropertyName == nameof(ScanViewModel.PreviewImage))
        {
            var selector = this.FindControl<ScanAreaSelector>("AreaSelector");
            if (selector is null) return;
            if (_vm.PreviewImage is { } preview)
            {
                selector.Source = CapturedPageImage.ToBitmap(preview);
                // A fresh preview starts uncropped — matches ScanViewModel resetting
                // _selectedArea to null at the same moment (see ScanViewModel.PreviewAsync).
                selector.ResetSelection();
                UpdateMinimapSizes();
            }
            else
            {
                // Cleared after a scan (see ScanViewModel.ScanAsync) — back to the
                // "Preview the scan to see the page here" placeholder.
                selector.Source = null;
                selector.ResetSelection();
            }
        }
        else if (e.PropertyName == nameof(ScanViewModel.PagePresetIndex))
        {
            UpdateMinimapPresetName();
            UpdateMinimapSizes();
        }
        else if (e.PropertyName == nameof(ScanViewModel.Pages) || e.PropertyName == nameof(ScanViewModel.PageCount))
        {
            RebuildPageList();
        }
    }

    /// <summary>Pops up the scanner-unavailable notice once, right after the window's initial
    /// check — not modal, so the header's own minimize/close stay usable while it's up. Only
    /// call site is the Opened handler; State flipping back to ScannerUnavailable later (e.g.
    /// after picking a different, also-unreachable scanner from Manage Scanners) does NOT
    /// reopen it.</summary>
    private void ShowUnavailablePopup()
    {
        if (_unavailablePopup is not null) return;
        _unavailablePopup = new ScannerUnavailableWindow(_vm);
        _unavailablePopup.Closed += (_, _) => _unavailablePopup = null;
        _unavailablePopup.Show(this);
    }

    private void OnAreaSelectionChanged(object? sender, EventArgs e)
    {
        var selector = (ScanAreaSelector)sender!;
        var (l, t, r, b) = selector.SelectionNormalized;
        _vm.SetSelectedArea(l, t, r, b);
    }

    /// <summary>A page-size preset (A4/Letter/Legal) was picked — move the actual on-screen
    /// crop rectangle to match. The ViewModel computed the rectangle; only the View can touch
    /// the View's own control.</summary>
    private void OnPresetSelectionRequested(object? sender, (double Left, double Top, double Right, double Bottom) rect)
    {
        var selector = this.FindControl<ScanAreaSelector>("AreaSelector");
        selector?.SetSelectionNormalized(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private void RebuildPageList()
    {
        var list = this.FindControl<ItemsControl>("PagesList");
        if (list is null) return;

        var items = new List<Control>();
        for (int i = 0; i < _vm.Pages.Count; i++)
        {
            int index = i;
            var page = _vm.Pages[i];

            var removeButton = new Button { Content = Strings.RemovePageButton, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
            removeButton.Click += (_, _) => { _vm.RemovePage(index); RebuildPageList(); };

            var upButton = new Button { Content = "↑", FontSize = 11, IsEnabled = index > 0 };
            ToolTip.SetTip(upButton, Strings.MovePageUpTooltip);
            upButton.Click += (_, _) => { _vm.MovePage(index, -1); RebuildPageList(); };
            var downButton = new Button { Content = "↓", FontSize = 11, IsEnabled = index < _vm.Pages.Count - 1 };
            ToolTip.SetTip(downButton, Strings.MovePageDownTooltip);
            downButton.Click += (_, _) => { _vm.MovePage(index, 1); RebuildPageList(); };
            var actions = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(removeButton, Dock.Right);
            actions.Children.Add(removeButton);
            actions.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { upButton, downButton } });

            items.Add(new Border
            {
                BorderThickness = new Avalonia.Thickness(1),
                BorderBrush = ThemeBrushes.Get("ThemeBorderStrongBrush"),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(8),
                Child = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new Border
                        {
                            Background = ThemeBrushes.Get("ThemePaperBrush"),
                            // Tall enough to actually recognise the page at a glance.
                            Child = new Image
                            {
                                Source = CapturedPageImage.ToBitmap(page.Page),
                                Stretch = Stretch.Uniform,
                                Height = AppTheme.Sizes.ThumbnailHeight,
                            },
                        },
                        new TextBlock { Text = page.Label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center },
                        actions,
                    },
                },
            });
        }
        list.ItemsSource = items;
    }

    private async Task<bool> SaveAsync(string suggestedName)
    {
        var result = await StorageProvider.SaveFilePickerWithResultAsync(new FilePickerSaveOptions
        {
            Title = Strings.SaveScanDialogTitle,
            SuggestedFileName = suggestedName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PDF document") { Patterns = new[] { "*.pdf" } },
                new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } },
                new FilePickerFileType("JPEG image") { Patterns = new[] { "*.jpg" } },
            },
        });

        if (result.File?.TryGetLocalPath() is not { } path) return false;

        // The suggested name carries no extension, so it comes from the file type chosen in
        // the dialog — unless the user typed a supported one themselves, which wins.
        if (Path.GetExtension(path).ToLowerInvariant() is not (".pdf" or ".png" or ".jpg" or ".jpeg"))
        {
            var pattern = result.SelectedFileType?.Patterns?.FirstOrDefault() ?? "*.pdf";
            path += pattern.TrimStart('*');
        }

        ScanOutputWriter.Save(_vm.Pages.Select(p => p.Page).ToList(), path, _vm.PdfSheet);
        SavedFilePath = path;
        return true;
    }

    private async Task OpenManageScannersAsync()
    {
        // Opening Manage Scanners is this popup's only other action (besides its own ×), so
        // it closes right as Manage Scanners opens rather than sitting behind it.
        _unavailablePopup?.Close();
        var manage = new ManageScannersWindow();
        await manage.ShowDialog(this);
    }

    // ------------------------------------------------------------------ //
    // Custom window chrome (see AppOptions).
    // ------------------------------------------------------------------ //
    private void OnToolbarPointerPressed(object? sender, PointerPressedEventArgs e) =>
        this.OnHeaderPointerPressed(e, supportsMaximizeToggle: true);

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximizeRestore(object? sender, RoutedEventArgs e) => this.ToggleMaximize();
    private void OnCloseWindow(object? sender, RoutedEventArgs e) => Close();

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WindowStateProperty) return;

        var maximizeButton = this.FindControl<Button>("MaximizeRestoreButton");
        if (maximizeButton is not null)
            maximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "▢";
    }
}
