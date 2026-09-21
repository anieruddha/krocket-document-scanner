using KRocketDocumentScanner.Core.Models;

namespace KRocketDocumentScanner.Core.Abstractions;

public interface IScannerRegistryStore
{
    Task<ScannerRegistry> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(ScannerRegistry registry, CancellationToken ct = default);
}

public interface IDocumentSource : IAsyncDisposable
{
    int PageCount { get; }
    string Title { get; }

    Task<CapturedPage> RenderPageAsync(int pageIndex, double zoom, CancellationToken ct = default);
}

public interface INetworkActivityLog
{
    Task LogAsync(NetworkActivityEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<NetworkActivityEntry>> ReadAllAsync(CancellationToken ct = default);
}
