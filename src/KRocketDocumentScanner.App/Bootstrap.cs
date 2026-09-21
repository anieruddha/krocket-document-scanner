using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using KRocketDocumentScanner.App.ViewModels.Localization;
using KRocketDocumentScanner.App.Theming;
using KRocketDocumentScanner.Core.Registry;
using KRocketDocumentScanner.Scanning;

namespace KRocketDocumentScanner.App;

internal static class Bootstrap
{
    private static bool _anotherInstanceRunning;

    public static void Prepare(string[] args)
    {
        AppOptions.ParseArgs(args);
        _anotherInstanceRunning = !SingleInstance.TryAcquire();
    }

    public static void Start(IApplicationLifetime? lifetime)
    {
        if (_anotherInstanceRunning)
        {
            Strings.Load(AppOptions.LangArgValue);
            if (lifetime is IClassicDesktopStyleApplicationLifetime alone)
                alone.MainWindow = BuildAlreadyRunningWindow();
            return;
        }

        AppTheme.LoadAndApply();
        Strings.Load(AppOptions.LangArgValue);

        App.ScannerEngine = new Naps2ScannerEngine();
        App.ScannerEngine.NetworkCallTimeout = AppSettings.LoadOrCreate().NetworkTimeout;
        App.ScannerRegistry = new ScannerRegistryManager(new JsonScannerRegistryStore(), App.ScannerEngine);

        if (lifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose;

            App.ScannerRegistry.LoadStoredAsync().GetAwaiter().GetResult();
            desktop.MainWindow = AppOptions.ManageOnly
                ? new ManageScannersWindow()
                : new ScanWindow(App.ScannerRegistry.DefaultDriverId);

            _ = App.ScannerRegistry.LoadAndRefreshAsync();

            desktop.Exit += (_, _) => App.ScannerEngine.Dispose();
        }
    }

    private static Avalonia.Controls.Window BuildAlreadyRunningWindow()
    {
        var message = AppOptions.ManageOnly ? Strings.AlreadyRunningManageMessage : Strings.AlreadyRunningMessage;
        var close = new Avalonia.Controls.Button
        {
            Content = Strings.CloseTooltip,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };
        var window = new Avalonia.Controls.Window
        {
            Title = message,
            CanResize = false,
            SizeToContent = Avalonia.Controls.SizeToContent.WidthAndHeight,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen,
            Content = new Avalonia.Controls.StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 16,
                Children =
                {
                    new Avalonia.Controls.TextBlock { Text = message, MaxWidth = 420, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    close,
                },
            },
        };
        close.Click += (_, _) => window.Close();
        return window;
    }
}
