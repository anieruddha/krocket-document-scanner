using KRocketDocumentScanner.Core.Abstractions;
using KRocketDocumentScanner.Core.Models;
using KRocketDocumentScanner.Core.Registry;

namespace KRocketDocumentScanner.Tests.Support;

public sealed class MemoryStore : IScannerRegistryStore
{
    public ScannerRegistry Saved { get; private set; } = new();
    public int Saves { get; private set; }

    public MemoryStore(params ScannerRegistryEntry[] entries)
    {
        Saved.Entries.AddRange(entries);
        if (entries.Length > 0) Saved.DefaultDriverId = entries[0].DriverId;
    }

    public Task<ScannerRegistry> LoadAsync(CancellationToken ct = default) =>
        Task.FromResult(Clone(Saved));

    public Task SaveAsync(ScannerRegistry registry, CancellationToken ct = default)
    {
        Saved = Clone(registry);
        Saves++;
        return Task.CompletedTask;
    }

    private static ScannerRegistry Clone(ScannerRegistry r)
    {
        var copy = new ScannerRegistry { DefaultDriverId = r.DefaultDriverId };
        foreach (var e in r.Entries)
            copy.Entries.Add(new ScannerRegistryEntry
            {
                DriverId = e.DriverId, DisplayName = e.DisplayName, Vendor = e.Vendor, Model = e.Model,
                AddMethod = e.AddMethod, ManualAddress = e.ManualAddress, DeviceId = e.DeviceId,
                ResolvedAddress = e.ResolvedAddress, AddedAt = e.AddedAt, LastSeenReadyAt = e.LastSeenReadyAt,
                Reachability = e.Reachability,
            });
        return copy;
    }
}

public static class Make
{
    public static ScannerRegistryEntry Entry(string driverId, string name = "Test Scanner", string? manualAddress = null) =>
        new() { DriverId = driverId, DisplayName = name, ManualAddress = manualAddress };

    /// <summary>A registry manager on a fake engine, already loaded (no reachability check yet).</summary>
    public static async Task<(ScannerRegistryManager Registry, FakeEngine Engine, MemoryStore Store)> RegistryAsync(
        IEnumerable<string>? reachable = null, params ScannerRegistryEntry[] saved)
    {
        var engine = new FakeEngine();
        foreach (var id in reachable ?? Array.Empty<string>()) engine.Reachable.Add(id);
        var store = new MemoryStore(saved);
        var registry = new ScannerRegistryManager(store, engine);
        await registry.LoadStoredAsync();
        return (registry, engine, store);
    }

    /// <summary>Polls until the condition holds (for work that is posted to another thread).</summary>
    public static async Task EventuallyAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new Xunit.Sdk.XunitException("condition was not met in time");
            await Task.Delay(20);
        }
    }
}
