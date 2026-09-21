using Avalonia;
using Avalonia.Markup.Xaml;
using KRocketDocumentScanner.Core.Registry;
using KRocketDocumentScanner.Scanning;

namespace KRocketDocumentScanner.App;

public partial class App : Application
{
    /// <summary>
    /// One scanner engine and one registry manager for the whole process, shared by every
    /// window. The registry being shared is what lets a change made in one window's Manage
    /// Scanners screen show up in every other window's menu (via its Changed event).
    /// Created in <see cref="Bootstrap.Start"/>.
    /// </summary>
    public static Naps2ScannerEngine ScannerEngine { get; internal set; } = null!;
    public static ScannerRegistryManager ScannerRegistry { get; internal set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Bootstrap.Start(ApplicationLifetime);
        base.OnFrameworkInitializationCompleted();
    }
}
