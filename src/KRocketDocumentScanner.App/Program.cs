using Avalonia;

namespace KRocketDocumentScanner.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Bootstrap.Prepare(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
