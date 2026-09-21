using KRocketDocumentScanner.Core.Abstractions;
using KRocketDocumentScanner.Core.Models;

namespace KRocketDocumentScanner.Core.Registry;

public sealed class ScannerRegistryManager
{
    private readonly IScannerRegistryStore _store;
    private readonly IScannerEngine _engine;
    private ScannerRegistry _registry = new();

    public ScannerRegistryManager(IScannerRegistryStore store, IScannerEngine engine)
    {
        _store = store;
        _engine = engine;
    }

    public event Action? Changed;

    public event Action<string>? ScannerRemoved;

    public event Action<string, string>? ScannerRepointed;

    public IReadOnlyList<ScannerRegistryEntry> Entries => _registry.Entries;
    public string? DefaultDriverId => _registry.DefaultDriverId;

    public ScannerRegistryEntry? DefaultEntry =>
        _registry.DefaultDriverId is null
            ? null
            : _registry.Entries.FirstOrDefault(e => e.DriverId == _registry.DefaultDriverId);

    public async Task LoadStoredAsync(CancellationToken ct = default)
    {
        _registry = await _store.LoadAsync(ct).ConfigureAwait(false);
        ForgetSavedReachability();
        Changed?.Invoke();
    }

    private void ForgetSavedReachability()
    {
        foreach (var e in _registry.Entries) e.Reachability = ScannerReachability.Unknown;
    }

    public async Task<IReadOnlyList<ScannerRegistryEntry>> LoadAndRefreshAsync(CancellationToken ct = default)
    {
        _registry = await _store.LoadAsync(ct).ConfigureAwait(false);
        ForgetSavedReachability();

        foreach (var entry in _registry.Entries)
        {
            entry.Reachability = await CheckReachabilityAsync(entry, ct).ConfigureAwait(false);
            if (entry.Reachability == ScannerReachability.Ready)
                entry.LastSeenReadyAt = DateTimeOffset.UtcNow;
        }

        var notReady = _registry.Entries.Where(e => e.Reachability == ScannerReachability.NotReady).ToList();
        if (notReady.Count > 0)
        {
            var found = await TryDiscoverAsync(ct).ConfigureAwait(false);
            foreach (var entry in notReady)
                await RecoverFromAsync(entry, found, ct).ConfigureAwait(false);
        }

        Changed?.Invoke();
        return _registry.Entries;
    }

    public async Task<string> TryRecoverAsync(string driverId, CancellationToken ct = default)
    {
        var entry = _registry.Entries.FirstOrDefault(e => e.DriverId == driverId);
        if (entry is null) return driverId;
        var found = await TryDiscoverAsync(ct).ConfigureAwait(false);
        await RecoverFromAsync(entry, found, ct).ConfigureAwait(false);
        Changed?.Invoke();
        return entry.DriverId;
    }

    private async Task<IReadOnlyList<DiscoveredScanner>> TryDiscoverAsync(CancellationToken ct)
    {
        try { return await DiscoverAllAsync(ct).ConfigureAwait(false); }
        catch { return []; }
    }

    private Task<IReadOnlyList<DiscoveredScanner>> DiscoverAllAsync(CancellationToken ct) =>
        _engine.ListDevicesAsync(ct);

    private async Task RecoverFromAsync(
        ScannerRegistryEntry entry, IReadOnlyList<DiscoveredScanner> found, CancellationToken ct)
    {
        var address = EntryAddress(entry);
        if (address is null) return;

        var taken = _registry.Entries.Select(e => e.DriverId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in found)
        {
            if (taken.Contains(candidate.DriverId)) continue;
            if (!string.Equals(ExtractAddress(candidate), address, StringComparison.OrdinalIgnoreCase)) continue;

            bool reachable;
            try { reachable = await _engine.IsReachableAsync(candidate.DriverId, ct).ConfigureAwait(false); }
            catch { reachable = false; }
            if (!reachable) continue;

            var wasDefault = _registry.DefaultDriverId == entry.DriverId;
            var oldId = entry.DriverId;
            entry.DriverId = candidate.DriverId;
            entry.Reachability = ScannerReachability.Ready;
            entry.LastSeenReadyAt = DateTimeOffset.UtcNow;
            if (wasDefault) _registry.DefaultDriverId = candidate.DriverId;
            await _store.SaveAsync(_registry, ct).ConfigureAwait(false);
            ScannerRepointed?.Invoke(oldId, candidate.DriverId);
            return;
        }
    }

    public async Task<ScannerReachability> CheckReachabilityAsync(
        ScannerRegistryEntry entry, CancellationToken ct = default)
    {
        try
        {
            var reachable = await _engine.IsReachableAsync(entry.DriverId, ct).ConfigureAwait(false);
            return reachable ? ScannerReachability.Ready : ScannerReachability.NotReady;
        }
        catch
        {
            return ScannerReachability.NotReady;
        }
    }

    public Task<IReadOnlyList<DiscoveredScanner>> DiscoverAvailableAsync(CancellationToken ct = default) =>
        DiscoverAllAsync(ct);
    public Task<ScannerRegistryEntry> AddFromDiscoveryAsync(DiscoveredScanner discovered, CancellationToken ct = default)
    {
        var entry = new ScannerRegistryEntry
        {
            DriverId = discovered.DriverId,
            DisplayName = $"{discovered.Vendor} {discovered.Model}".Trim(),
            Vendor = discovered.Vendor,
            Model = discovered.Model,
            AddMethod = ScannerAddMethod.AutoDiscovered,
        };
        return AddEntryAsync(entry, ct);
    }

    private static string? ExtractAddress(DiscoveredScanner s) =>
        ScannerAddressExtractor.Extract(s.DriverId, s.Model, s.Vendor);

    private static string? EntryAddress(ScannerRegistryEntry e) =>
        e.ResolvedAddress ?? ScannerAddressExtractor.Extract(e.ManualAddress, e.DriverId, e.Model, e.Vendor);

    private static string? EntryDeviceId(ScannerRegistryEntry e) =>
        e.DeviceId ?? ScannerAddressExtractor.ExtractDeviceId(e.ManualAddress, e.DriverId, e.Model, e.Vendor);

    private static bool SameDevice(ScannerRegistryEntry a, ScannerRegistryEntry b)
    {
        if (a.DriverId == b.DriverId) return true;

        var idA = EntryDeviceId(a);
        var idB = EntryDeviceId(b);
        if (idA is not null && idB is not null)
            return string.Equals(idA, idB, StringComparison.OrdinalIgnoreCase);

        var addrA = EntryAddress(a);
        var addrB = EntryAddress(b);
        return addrA is not null && addrB is not null &&
               string.Equals(addrA, addrB, StringComparison.OrdinalIgnoreCase);
    }

    public Task<ScannerRegistryEntry> AddManualAsync(
        string driverId, string displayName, string? address, CancellationToken ct = default,
        bool requireReachable = false)
    {
        if (string.IsNullOrWhiteSpace(driverId))
            throw new ArgumentException("A driver/connection identifier is required.", nameof(driverId));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("A display name is required.", nameof(displayName));

        var entry = new ScannerRegistryEntry
        {
            DriverId = driverId,
            DisplayName = displayName,
            AddMethod = ScannerAddMethod.ManuallyEntered,
            ManualAddress = address,
        };
        return AddManualEntryAsync(entry, ct, requireReachable);
    }

    private async Task<ScannerRegistryEntry> AddManualEntryAsync(
        ScannerRegistryEntry entry, CancellationToken ct, bool requireReachable)
    {
        var host = HostOf(entry.ManualAddress);
        if (host is not null && ScannerAddressExtractor.Extract(host) is null)
        {
            try { entry.ResolvedAddress = await _engine.ResolveAddressAsync(host, ct).ConfigureAwait(false); }
            catch {  }
        }
        return await AddEntryAsync(entry, ct, requireReachable).ConfigureAwait(false);
    }

    private static string? HostOf(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var text = address.Contains("://") ? address : $"https://{address}";
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    private async Task<ScannerRegistryEntry> AddEntryAsync(ScannerRegistryEntry entry, CancellationToken ct, bool requireReachable = false)
    {
        entry.DeviceId ??= ScannerAddressExtractor.ExtractDeviceId(entry.ManualAddress, entry.DriverId, entry.Model, entry.Vendor);
        entry.Reachability = await CheckReachabilityAsync(entry, ct).ConfigureAwait(false);
        if (requireReachable && entry.Reachability != ScannerReachability.Ready)
            throw new ScannerUnreachableException(entry.DisplayName);

        var existing = _registry.Entries.FirstOrDefault(e => SameDevice(e, entry));
        if (existing is not null)
        {
            if (_registry.DefaultDriverId == existing.DriverId)
                _registry.DefaultDriverId = entry.DriverId;

            var oldId = existing.DriverId;
            existing.DriverId = entry.DriverId;
            if (!string.IsNullOrEmpty(entry.Vendor)) existing.Vendor = entry.Vendor;
            if (!string.IsNullOrEmpty(entry.Model)) existing.Model = entry.Model;
            existing.AddMethod = entry.AddMethod;
            existing.ManualAddress = entry.ManualAddress;
            existing.ResolvedAddress = entry.ResolvedAddress ?? existing.ResolvedAddress;
            existing.DeviceId = entry.DeviceId ?? existing.DeviceId;
            existing.Reachability = entry.Reachability;
            if (entry.Reachability == ScannerReachability.Ready)
                existing.LastSeenReadyAt = DateTimeOffset.UtcNow;

            await _store.SaveAsync(_registry, ct).ConfigureAwait(false);
            if (oldId != existing.DriverId) ScannerRepointed?.Invoke(oldId, existing.DriverId);
            Changed?.Invoke();
            return existing;
        }

        bool wasEmpty = _registry.Entries.Count == 0;
        _registry.Entries.Add(entry);
        if (wasEmpty)
        {
            _registry.DefaultDriverId = entry.DriverId;
        }

        await _store.SaveAsync(_registry, ct).ConfigureAwait(false);
        Changed?.Invoke();
        return entry;
    }

    public async Task<string?> EnsureDefaultAsync(CancellationToken ct = default)
    {
        if (_registry.DefaultDriverId is { } current && _registry.Entries.Any(e => e.DriverId == current))
            return current;

        foreach (var entry in _registry.Entries.ToList())
        {
            entry.Reachability = await CheckReachabilityAsync(entry, ct).ConfigureAwait(false);
            if (entry.Reachability != ScannerReachability.Ready) continue;

            entry.LastSeenReadyAt = DateTimeOffset.UtcNow;
            _registry.DefaultDriverId = entry.DriverId;
            await _store.SaveAsync(_registry, ct).ConfigureAwait(false);
            Changed?.Invoke();
            return entry.DriverId;
        }
        Changed?.Invoke();
        return null;
    }

    public async Task SetDefaultAsync(string driverId, CancellationToken ct = default)
    {
        if (!_registry.Entries.Any(e => e.DriverId == driverId))
            throw new InvalidOperationException($"'{driverId}' is not a registered scanner.");

        _registry.DefaultDriverId = driverId;
        await _store.SaveAsync(_registry, ct).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task RemoveAsync(string driverId, CancellationToken ct = default)
    {
        bool existed = _registry.Entries.RemoveAll(e => e.DriverId == driverId) > 0;

        if (_registry.DefaultDriverId == driverId)
        {
            _registry.DefaultDriverId =
                _registry.Entries.FirstOrDefault(e => e.Reachability == ScannerReachability.Ready)?.DriverId
                ?? (_registry.Entries.Count == 1 ? _registry.Entries[0].DriverId : null);
        }

        await _store.SaveAsync(_registry, ct).ConfigureAwait(false);
        if (existed) ScannerRemoved?.Invoke(driverId);
        Changed?.Invoke();
    }
}

public sealed class ScannerUnreachableException : Exception
{
    public ScannerUnreachableException(string displayName)
        : base($"Couldn't reach '{displayName}'.") { }
}
