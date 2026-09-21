using KRocketDocumentScanner.Core.Models;

namespace KRocketDocumentScanner.Core.Abstractions;

/// <summary>Persists the user's known-scanners registry across app restarts.</summary>
public interface IScannerRegistryStore
{
    Task<ScannerRegistry> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(ScannerRegistry registry, CancellationToken ct = default);
}

/// <summary>Document-format-agnostic way to open and render a document's pages.</summary>
public interface IDocumentSource : IAsyncDisposable
{
    int PageCount { get; }
    string Title { get; }

    /// <summary>Renders a page as RGB24 pixel data at the given zoom factor (1.0 = 96 DPI baseline).</summary>
    Task<CapturedPage> RenderPageAsync(int pageIndex, double zoom, CancellationToken ct = default);
}

/// <summary>
/// Records every network-adjacent action the app takes, so it's auditable rather than merely
/// asserted. Implementations must be safe to call frequently and must never themselves be the
/// thing that sends data anywhere — this is a local, passive record, not a reporting mechanism.
/// </summary>
public interface INetworkActivityLog
{
    Task LogAsync(NetworkActivityEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<NetworkActivityEntry>> ReadAllAsync(CancellationToken ct = default);
}
