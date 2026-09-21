using KRocketDocumentScanner.Core.Abstractions;
using KRocketDocumentScanner.Core.Models;

namespace KRocketDocumentScanner.Tests.Support;

/// <summary>Scriptable scanner engine: which ids answer, what discovery returns, how long each
/// call takes. Never touches the network or the disk.</summary>
public sealed class FakeEngine : IScannerEngine
{
    public HashSet<string> Reachable { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DiscoveredScanner> Discoverable { get; } = new();
    public Dictionary<string, string> Resolvable { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ScannerCapabilities Capabilities { get; set; } = new(true, false, false);
    public int DelayMs { get; set; }
    public TaskCompletionSource<bool>? ReachableGate { get; set; }
    public bool DiscoveryThrows { get; set; }

    public List<string> ReachabilityChecks { get; } = new();
    public List<string> Calls { get; } = new();
    public ScanOptions? LastScanOptions { get; private set; }
    public string? LastDriverId { get; private set; }

    public async Task<IReadOnlyList<DiscoveredScanner>> ListDevicesAsync(CancellationToken ct = default)
    {
        Calls.Add("list");
        if (DelayMs > 0) await Task.Delay(DelayMs, ct);
        if (DiscoveryThrows) throw new InvalidOperationException("discovery failed");
        return Discoverable.ToList();
    }

    public Task<string?> ResolveAddressAsync(string host, CancellationToken ct = default) =>
        Task.FromResult(Resolvable.TryGetValue(host, out var ip) ? ip : null);

    public async Task<bool> IsReachableAsync(string driverId, CancellationToken ct = default)
    {
        ReachabilityChecks.Add(driverId);
        if (ReachableGate is not null) await ReachableGate.Task;
        if (DelayMs > 0) await Task.Delay(DelayMs, ct);
        return Reachable.Contains(driverId);
    }

    public Task<ScannerCapabilities> GetCapabilitiesAsync(string driverId, CancellationToken ct = default) =>
        Task.FromResult(Capabilities);

    public static CapturedPage Page(int widthPx = 200, int heightPx = 280, int dpi = 100) => new()
    {
        WidthPx = widthPx, HeightPx = heightPx, Format = PixelFormat.Rgb24, Dpi = dpi,
        PixelData = new byte[widthPx * heightPx * 3],
    };

    public async Task<CapturedPage> PreviewAsync(string driverId, ScanOptions options, CancellationToken ct = default)
    {
        Calls.Add("preview");
        LastDriverId = driverId;
        if (DelayMs > 0) await Task.Delay(DelayMs, ct);
        return Page();
    }

    public async Task<CapturedPage> ScanSingleAsync(string driverId, ScanOptions options, CancellationToken ct = default)
    {
        Calls.Add("scan");
        LastDriverId = driverId;
        LastScanOptions = options;
        if (DelayMs > 0) await Task.Delay(DelayMs, ct);
        return Page();
    }

    public async IAsyncEnumerable<CapturedPage> ScanBatchAsync(
        string driverId, ScanOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        Calls.Add("batch");
        LastDriverId = driverId;
        LastScanOptions = options;
        for (int i = 0; i < 2; i++)
        {
            if (DelayMs > 0) await Task.Delay(DelayMs, ct);
            yield return Page();
        }
    }
}
