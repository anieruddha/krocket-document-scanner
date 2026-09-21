using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using KRocketDocumentScanner.App.ViewModels;

namespace KRocketDocumentScanner.App;

public partial class ScannerUnavailableWindow : Window
{
    // Needed by Avalonia's XAML loader and previewer, which can only create a window with no
    // arguments; the app itself always uses the constructor below.
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

    // Fixed-size dialog: only Close, no minimize/maximize (see AppOptions/ScanWindow's fuller
    // chrome for the windows that actually need those).
    private void OnToolbarPointerPressed(object? sender, PointerPressedEventArgs e) =>
        this.OnHeaderPointerPressed(e, supportsMaximizeToggle: false);

    private void OnCloseWindow(object? sender, RoutedEventArgs e) => Close();
}
