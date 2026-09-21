using KRocketDocumentScanner.Core.Models;

namespace KRocketDocumentScanner.Core.Abstractions;

public interface IScannerEngine
{
    Task<IReadOnlyList<DiscoveredScanner>> ListDevicesAsync(CancellationToken ct = default);

    Task<string?> ResolveAddressAsync(string host, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    Task<bool> IsReachableAsync(string driverId, CancellationToken ct = default);

    Task<ScannerCapabilities> GetCapabilitiesAsync(string driverId, CancellationToken ct = default);

    Task<CapturedPage> PreviewAsync(string driverId, ScanOptions options, CancellationToken ct = default);

    Task<CapturedPage> ScanSingleAsync(string driverId, ScanOptions options, CancellationToken ct = default);

    IAsyncEnumerable<CapturedPage> ScanBatchAsync(
        string driverId, ScanOptions options, CancellationToken ct = default);
}
