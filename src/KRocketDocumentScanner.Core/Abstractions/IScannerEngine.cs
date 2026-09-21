using KRocketDocumentScanner.Core.Models;

namespace KRocketDocumentScanner.Core.Abstractions;

/// <summary>
/// The scanning engine, abstracted away from whichever library implements it
/// (NAPS2.Sdk, in practice). Every implementation of this interface must guarantee
/// that all calls are internally serialized onto a single consistent thread/context —
/// a prior implementation crashed by calling into the scanner from multiple ad-hoc
/// threads, corrupting shared state in an underlying native library. Callers of this
/// interface should NOT need to worry about that; it's an implementation obligation.
/// </summary>
public interface IScannerEngine
{
    Task<IReadOnlyList<DiscoveredScanner>> ListDevicesAsync(CancellationToken ct = default);

    /// <summary>Looks up the IPv4 address a scanner host name (e.g. printer.local) points to, or
    /// null if it can't be resolved. Used only to recognise the same scanner under a different
    /// spelling of its address. Engines that can't resolve names leave the default (null).</summary>
    Task<string?> ResolveAddressAsync(string host, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    /// <summary>True if the given device currently responds (used for registry reachability checks).</summary>
    Task<bool> IsReachableAsync(string driverId, CancellationToken ct = default);

    /// <summary>Which paper sources (flatbed/feeder/duplex) the device actually supports —
    /// used to decide which Source options are even worth offering.</summary>
    Task<ScannerCapabilities> GetCapabilitiesAsync(string driverId, CancellationToken ct = default);

    Task<CapturedPage> PreviewAsync(string driverId, ScanOptions options, CancellationToken ct = default);

    Task<CapturedPage> ScanSingleAsync(string driverId, ScanOptions options, CancellationToken ct = default);

    /// <summary>
    /// Multi-page (ADF) scan. Yields pages as they're captured so the UI can show
    /// progress; the underlying engine call is still fully serialized.
    /// </summary>
    IAsyncEnumerable<CapturedPage> ScanBatchAsync(
        string driverId, ScanOptions options, CancellationToken ct = default);
}
