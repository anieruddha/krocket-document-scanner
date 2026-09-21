using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using KRocketDocumentScanner.App.ViewModels.Localization;
using KRocketDocumentScanner.App.Theming;
using KRocketDocumentScanner.Core.Registry;
using KRocketDocumentScanner.Scanning;

namespace KRocketDocumentScanner.App;

/// <summary>
/// Everything the app does to get started, in order, in one place:
/// <list type="number">
///   <item><see cref="Prepare"/> (before Avalonia starts): command-line options, then the
///   single-instance check.</item>
///   <item><see cref="Start"/> (once Avalonia is ready): theme, language, settings file, scanner
///   engine and registry, saved scanner list, main window, background scanner check.</item>
/// </list>
/// </summary>
internal static class Bootstrap
{
    // Another copy of the app is already running, so this one only shows a short message and exits.
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

        // Load before creating any window so the first paint already has the right theme —
        // no flash of default styling before it switches.
        AppTheme.LoadAndApply();
        Strings.Load(AppOptions.LangArgValue);

        // One scanner engine and one registry manager for the whole process, shared by every
        // window (see App.ScannerEngine and App.ScannerRegistry).
        App.ScannerEngine = new Naps2ScannerEngine();
        App.ScannerEngine.NetworkCallTimeout = AppSettings.LoadOrCreate().NetworkTimeout;
        App.ScannerRegistry = new ScannerRegistryManager(new JsonScannerRegistryStore(), App.ScannerEngine);

        if (lifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Close only when the LAST window closes — with the one-document-per-window model,
            // closing the first window must not take the whole app down with it.
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose;

            // Only the saved list is read up front (a quick local file), so the window knows
            // the default scanner from the start; reachability is checked in the background.
            App.ScannerRegistry.LoadStoredAsync().GetAwaiter().GetResult();
            // --manage opens the scanner registry on its own, with no scan window; the app then
            // closes when that window closes.
            desktop.MainWindow = AppOptions.ManageOnly
                ? new ManageScannersWindow()
                : new ScanWindow(App.ScannerRegistry.DefaultDriverId);

            // Runs in the background so it never delays the window appearing — refreshes
            // reachability status now that the window is already showing.
            _ = App.ScannerRegistry.LoadAndRefreshAsync();

            desktop.Exit += (_, _) => App.ScannerEngine.Dispose();
        }
    }

    // Plain window built in code: at this point only the message is needed, not the theme,
    // the scanners or the rest of start-up.
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
