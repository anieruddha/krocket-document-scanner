using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KRocketDocumentScanner.App;
using KRocketDocumentScanner.App.ViewModels;
using KRocketDocumentScanner.Core.Registry;
using KRocketDocumentScanner.Tests.Support;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(KRocketDocumentScanner.Tests.Ux.TestAppBuilder))]

namespace KRocketDocumentScanner.Tests.Ux;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<KRocketDocumentScanner.App.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public static class UxHost
{
    public static void UseRegistry(ScannerRegistryManager registry) =>
        typeof(KRocketDocumentScanner.App.App).GetProperty(nameof(KRocketDocumentScanner.App.App.ScannerRegistry))!.SetValue(null, registry);

    public static void Flush() => Dispatcher.UIThread.RunJobs();

    public static async Task PumpAsync(Func<bool> until, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!until())
        {
            Flush();
            if (DateTime.UtcNow > deadline) throw new Xunit.Sdk.XunitException("UI did not reach the expected state in time");
            await Task.Delay(20);
        }
        Flush();
    }

    public static IEnumerable<T> All<T>(Visual root) where T : class => root.GetVisualDescendants().OfType<T>();

    public static string? TextOf(Button b) =>
        b.Content as string ?? All<TextBlock>(b).Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrEmpty(t) && t != "⟳");

    public static Button? ButtonWith(Visual root, string text) =>
        All<Button>(root).FirstOrDefault(b => b.Content as string == text || All<TextBlock>(b).Any(t => t.Text == text));

    public static IEnumerable<string> Texts(Visual root) =>
        All<TextBlock>(root).Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "");

    public static ScanViewModel InjectViewModel(ScanWindow window, FakeEngine engine, ScannerRegistryManager registry, string? driverId)
    {
        var vm = new ScanViewModel(engine, registry, driverId, _ => Task.FromResult(false), () => Task.CompletedTask, window.Close, _ => { });
        typeof(ScanWindow).GetField("_vm", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, vm);
        window.DataContext = vm;
        return vm;
    }
}
