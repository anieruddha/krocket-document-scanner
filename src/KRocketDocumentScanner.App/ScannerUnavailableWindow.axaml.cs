using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using KRocketDocumentScanner.App.ViewModels;

namespace KRocketDocumentScanner.App;

public partial class ScannerUnavailableWindow : Window
{
    public ScannerUnavailableWindow()
    {
        InitializeComponent();
    }

    public ScannerUnavailableWindow(ScanViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        var header = this.FindControl<Border>("HeaderBorder");
        if (header is not null) this.AttachActiveStateDimming(header);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnToolbarPointerPressed(object? sender, PointerPressedEventArgs e) =>
        this.OnHeaderPointerPressed(e, supportsMaximizeToggle: false);

    private void OnCloseWindow(object? sender, RoutedEventArgs e) => Close();
}
