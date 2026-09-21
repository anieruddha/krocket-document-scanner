using KRocketDocumentScanner.Core.Abstractions;
using KRocketDocumentScanner.Core.Models;

namespace KRocketDocumentScanner.Core.Registry;

/// <summary>
/// Orchestrates the scanner registry: loading it at startup, checking reachability of every
/// known scanner, the two "Add Scanner" paths (auto-discover, manual entry), and which
/// scanner (if any) is the default.
///
/// Default-scanner rules (all deliberate UX decisions, not incidental):
///  - The very first scanner ever added becomes the default automatically — nothing to
///    choose between yet, so no prompt.
///  - Adding a second (or later) scanner never changes an existing default on its own;
///    changing it is always an explicit action via <see cref="SetDefaultAsync"/>.
///  - Whenever the app has to pick a default itself, it picks the first scanner that is online
///    (see <see cref="EnsureDefaultAsync"/>). Removing the current default promotes the first
///    online one that remains; if none is online, a lone remaining scanner is promoted, and
///    otherwise the default is cleared until one is online.
///
/// Depends only on <see cref="IScannerEngine"/> and <see cref="IScannerRegistryStore"/>, so
/// it's fully testable against fakes without touching NAPS2.Sdk or the filesystem.
/// </summary>
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

    /// <summary>
    /// Raised after any state change (load, add, remove, default changed). In the multi-window
    /// model, one ScannerRegistryManager instance is shared by every window in the process —
    /// this is how a window whose Manage Scanners screen changed the default notifies every
    /// other open window to refresh its own "Scan from &lt;name&gt;" display.
    /// </summary>
    public event Action? Changed;

    /// <summary>A saved scanner was removed (argument: its driver id at that moment). Every open
    /// scan window that was using it switches to the default scanner.</summary>
    public event Action<string>? ScannerRemoved;

    /// <summary>A saved scanner stays the same entry but now uses another connection
    /// (arguments: old and new driver id) — by recovery, or by adding the same scanner again.
    /// Windows using it just follow to the new id.</summary>
    public event Action<string, string>? ScannerRepointed;

    public IReadOnlyList<ScannerRegistryEntry> Entries => _registry.Entries;
    public string? DefaultDriverId => _registry.DefaultDriverId;

    /// <summary>The default scanner's entry, or null if none is set.</summary>
    public ScannerRegistryEntry? DefaultEntry =>
        _registry.DefaultDriverId is null
            ? null
            : _registry.Entries.FirstOrDefault(e => e.DriverId == _registry.DefaultDriverId);

    /// <summary>
    /// Loads the persisted registry only — no reachability checks, so it is quick enough to
    /// wait for at startup, before the first window is created.
    /// </summary>
    public async Task LoadStoredAsync(CancellationToken ct = default)
    {
        _registry = await _store.LoadAsync(ct).ConfigureAwait(false);
        ForgetSavedReachability();
        Changed?.Invoke();
    }

    // Reachability is saved with the registry, but a saved "Ready" says nothing about right
    // now — a scanner switched off since would still show a green dot until checked.
    private void ForgetSavedReachability()
    {
        foreach (var e in _registry.Entries) e.Reachability = ScannerReachability.Unknown;
    }

    /// <summary>
    /// Loads the persisted registry and checks reachability of every entry. Call once at
    /// app startup. Reachability failures for individual devices don't throw — they just
    /// leave that entry marked NotReady, so one offline scanner never blocks startup.
    /// </summary>
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

        // A scanner switched on after the app started can't be reached through its saved SANE
        // id (SANE's discovery is stale), but a fresh discovery can find it under another id.
        // One discovery pass covers every not-ready entry.
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

    /// <summary>
    /// For a saved scanner that is currently unreachable: runs a fresh discovery and, if the
    /// same physical device (same address) is now reachable under a different driver id,
    /// re-points the entry at it. Returns the driver id to use (the new one, or the
    /// original if nothing better was found). Never throws.
    /// </summary>
    public async Task<string> TryRecoverAsync(string driverId, CancellationToken ct = default)
    {
        var entry = _registry.Entries.FirstOrDefault(e => e.DriverId == driverId);
        if (entry is null) return driverId;
        var found = await TryDiscoverAsync(ct).ConfigureAwait(false);
        await RecoverFromAsync(entry, found, ct).ConfigureAwait(false);
        Changed?.Invoke();
        return entry.DriverId;
    }

    /// <summary>The single place the manager runs device discovery (the engine merges every
    /// protocol behind it). Add Scanner lets failures surface; recovery ignores them.</summary>
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
            // Any failure talking to the engine means "can't confirm it's ready" — never let
            // a reachability check throw and take down the registry load.
            return ScannerReachability.NotReady;
        }
    }

    /// <summary>
    /// Everything a fresh discovery finds, including scanners already registered. Adding one that
    /// is already registered doesn't create a second entry — <see cref="AddEntryAsync"/> updates
    /// the existing one (see <see cref="SameDevice"/> for how "same scanner" is decided).
    /// </summary>
    public Task<IReadOnlyList<DiscoveredScanner>> DiscoverAvailableAsync(CancellationToken ct = default) =>
        DiscoverAllAsync(ct);
    /// <summary>Adds a scanner that was found via <see cref="DiscoverAvailableAsync"/>.</summary>
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

    /// <summary>Same physical scanner? The scanner's own device id decides when both sides have
    /// one; otherwise the same network address (a host name is compared by the address it
    /// resolved to); a scanner with neither (USB) only matches an identical driver id.</summary>
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

    /// <summary>Manual-entry path of "Add Scanner" — for devices that won't auto-discover.</summary>
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

    // A host name (printer.local) is resolved once so the same scanner can be recognised when
    // it is later found by IP address; a lookup that fails just leaves it unresolved.
    private async Task<ScannerRegistryEntry> AddManualEntryAsync(
        ScannerRegistryEntry entry, CancellationToken ct, bool requireReachable)
    {
        var host = HostOf(entry.ManualAddress);
        if (host is not null && ScannerAddressExtractor.Extract(host) is null)
        {
            try { entry.ResolvedAddress = await _engine.ResolveAddressAsync(host, ct).ConfigureAwait(false); }
            catch { /* unresolved is fine */ }
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
        // Nothing has been changed yet, so refusing here leaves the registry exactly as it was.
        if (requireReachable && entry.Reachability != ScannerReachability.Ready)
            throw new ScannerUnreachableException(entry.DisplayName);

        // The same physical scanner added again — possibly over a different connection (AirScan
        // before, eSCL now) or at a new address — updates the saved entry in place, silently,
        // instead of creating a second one. The user's own name for it and its default status
        // are kept; only how to reach it changes.
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
            // The very first scanner ever added becomes the default; every other addition
            // leaves the existing default alone.
            _registry.DefaultDriverId = entry.DriverId;
        }

        await _store.SaveAsync(_registry, ct).ConfigureAwait(false);
        Changed?.Invoke();
        return entry;
    }

    /// <summary>
    /// Makes sure there is a default: if there already is one it is returned untouched; otherwise
    /// the saved scanners are checked in order and the first one that is online becomes the
    /// default (and is saved). Returns null when none is online.
    /// </summary>
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

    /// <summary>Explicitly changes the default scanner — the only way the default changes once more than one scanner is registered.</summary>
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
            // The default was just removed — decide what, if anything, becomes the new
            // default. See the class-level doc comment for why these two cases differ.
            _registry.DefaultDriverId =
                _registry.Entries.FirstOrDefault(e => e.Reachability == ScannerReachability.Ready)?.DriverId
                ?? (_registry.Entries.Count == 1 ? _registry.Entries[0].DriverId : null);
        }

        await _store.SaveAsync(_registry, ct).ConfigureAwait(false);
        if (existed) ScannerRemoved?.Invoke(driverId);
        Changed?.Invoke();
    }
}

/// <summary>Thrown when a scanner that must be reachable to be added (a manual entry) wasn't.</summary>
public sealed class ScannerUnreachableException : Exception
{
    public ScannerUnreachableException(string displayName)
        : base($"Couldn't reach '{displayName}'.") { }
}
