using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using KRocketDocumentScanner.App.ViewModels;

namespace KRocketDocumentScanner.App;

public partial class ManageScannersWindow : Window
{
    private readonly ManageScannersViewModel _vm;

    public ManageScannersWindow()
    {
        InitializeComponent();
        _vm = new ManageScannersViewModel(App.ScannerRegistry, ShowAddScannerAsync);
        DataContext = _vm;

        var header = this.FindControl<Border>("HeaderBorder");
        if (header is not null) this.AttachActiveStateDimming(header);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async Task ShowAddScannerAsync()
    {
        var add = new AddScannerWindow();
        await add.ShowDialog(this);
    }

    private async void OnMakeDefault(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string driverId)
            await _vm.SetDefaultAsync(driverId);
    }

    private async void OnRemove(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string driverId)
            await _vm.RemoveAsync(driverId);
    }

    private void OnToolbarPointerPressed(object? sender, PointerPressedEventArgs e) =>
        this.OnHeaderPointerPressed(e, supportsMaximizeToggle: false);

    private void OnCloseWindow(object? sender, RoutedEventArgs e) => Close();
}
