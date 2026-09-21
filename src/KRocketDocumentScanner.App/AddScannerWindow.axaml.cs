using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using KRocketDocumentScanner.App.ViewModels;
using KRocketDocumentScanner.Scanning;

namespace KRocketDocumentScanner.App;

public partial class AddScannerWindow : Window
{
    private readonly AddScannerViewModel _vm;

    public AddScannerWindow()
    {
        InitializeComponent();
        _vm = new AddScannerViewModel(
            App.ScannerRegistry,
            Naps2ScannerEngine.BuildManualNetworkScannerDriverId,
            Close);
        DataContext = _vm;

        // Start discovery as soon as the dialog opens, rather than waiting for a click on the
        // Search button (which stays available for re-scanning).
        Opened += (_, _) => _vm.DiscoverCommand.Execute(null);

        var header = this.FindControl<Border>("HeaderBorder");
        if (header is not null) this.AttachActiveStateDimming(header);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // Fixed-size dialog: only Close, no minimize/maximize (see AppOptions).
    private void OnToolbarPointerPressed(object? sender, PointerPressedEventArgs e) =>
        this.OnHeaderPointerPressed(e, supportsMaximizeToggle: false);

    private void OnCloseWindow(object? sender, RoutedEventArgs e) => Close();
}
