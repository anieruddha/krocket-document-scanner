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

    private bool _closeConfirmed;

    public string? SavedFilePath { get; private set; }

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
            OpenManageScannersAsync);

        DataContext = _vm;
        _vm.PropertyChanged += OnViewModelChanged;
        _vm.PresetSelectionRequested += OnPresetSelectionRequested;
        _vm.AspectLockChanged += (_, aspectLock) =>
            this.FindControl<ScanAreaSelector>("AreaSelector")?.SetLockedAspectRatio(aspectLock);

        var header = this.FindControl<Border>("HeaderBorder");
        if (header is not null) this.AttachActiveStateDimming(header);

        Closing += OnWindowClosing;
        Closed += (_, _) => _vm.Detach();

        Opened += async (_, _) =>
        {
            await _vm.InitializeAsync();
            if (_vm.ShowUnavailable && _vm.NoScannersRegistered) ShowUnavailablePopup();
        };
        Opened += (_, _) => { UpdateMinimapPresetName(); UpdateMinimapSizes(); };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || (_vm.Pages.Count == 0 && !_vm.IsBusy && !_vm.HasPreview)) return;

        e.Cancel = true;
        var dialog = new ConfirmCloseWindow(_vm.Pages.Count, _vm.IsBusy, _vm.HasPreview);
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed) return;

        _closeConfirmed = true;
        Close();
    }

    private void OnSizeBoxLostFocus(object? sender, RoutedEventArgs e) => _vm.RefreshAreaSize();

    private void OnToggleCropPanel(object? sender, RoutedEventArgs e)
    {
        var body = this.FindControl<Border>("CropPanelBody");
        if (body is null) return;
        body.IsVisible = !body.IsVisible;

        var chevron = this.FindControl<TextBlock>("CropPanelChevron");
        if (chevron is not null) chevron.Text = body.IsVisible ? "⌃" : "⌄";
    }

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
        var section = this.FindControl<Control>("MinimapSection");
        if (section is not null) section.IsVisible = preset != ScanViewModel.CustomPresetName;
        var keepRatio = this.FindControl<ToggleSwitch>("KeepRatioToggle");
        if (keepRatio is not null) {
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
        if (e.PropertyName == nameof(ScanViewModel.PreviewImage))
        {
            var selector = this.FindControl<ScanAreaSelector>("AreaSelector");
            if (selector is null) return;
            if (_vm.PreviewImage is { } preview)
            {
                selector.Source = CapturedPageImage.ToBitmap(preview);
                selector.ResetSelection();
                UpdateMinimapSizes();
            }
            else
            {
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

        if (Path.GetExtension(path).ToLowerInvariant() is not (".pdf" or ".png" or ".jpg" or ".jpeg"))
        {
            var pattern = result.SelectedFileType?.Patterns?.FirstOrDefault() ?? "*.pdf";
            path += pattern.TrimStart('*');
        }

        ScanOutputWriter.Save(_vm.Pages.Select(p => p.Page).ToList(), path, _vm.PdfSheet);
        SavedFilePath = path;
        await Launcher.LaunchFileInfoAsync(new FileInfo(path));
        return true;
    }

    private async Task OpenManageScannersAsync()
    {
        _unavailablePopup?.Close();
        var manage = new ManageScannersWindow();
        await manage.ShowDialog(this);
    }

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
