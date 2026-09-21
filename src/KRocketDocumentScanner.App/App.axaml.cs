using Avalonia;
using Avalonia.Markup.Xaml;
using KRocketDocumentScanner.Core.Registry;
using KRocketDocumentScanner.Scanning;

namespace KRocketDocumentScanner.App;

public partial class App : Application
{
    public static Naps2ScannerEngine ScannerEngine { get; internal set; } = null!;
    public static ScannerRegistryManager ScannerRegistry { get; internal set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Bootstrap.Start(ApplicationLifetime);
        base.OnFrameworkInitializationCompleted();
    }
}
