using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using KRocketDocumentScanner.App.ViewModels.Localization;

namespace KRocketDocumentScanner.App;

public partial class ConfirmCloseWindow : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmCloseWindow() : this(0)
    {
    }

    public ConfirmCloseWindow(int pageCount, bool scannerBusy = false, bool hasPreview = false)
    {
        InitializeComponent();

        var text = this.FindControl<TextBlock>("MessageText");
        if (text is not null)
            text.Text = !scannerBusy
                ? pageCount == 0 ? Strings.PreviewNotScannedMessage : string.Format(Strings.UnsavedPagesMessage, Strings.Pages(pageCount))
                : pageCount == 0 ? Strings.ScannerBusyMessage
                : string.Format(Strings.ScannerBusyWithPagesMessage, Strings.Pages(pageCount));

        if (!scannerBusy && pageCount == 0 && hasPreview)
            Title = Strings.PreviewNotScannedTitle;

        if (scannerBusy)
        {
            Title = Strings.ScannerBusyTitle;
        }

        var header = this.FindControl<Border>("HeaderBorder");
        if (header is not null) this.AttachActiveStateDimming(header);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDiscard(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnToolbarPointerPressed(object? sender, PointerPressedEventArgs e) =>
        this.OnHeaderPointerPressed(e, supportsMaximizeToggle: false);
}
