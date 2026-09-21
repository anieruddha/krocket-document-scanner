using KRocketDocumentScanner.Core.Models;
using KRocketDocumentScanner.Core.Registry;
using KRocketDocumentScanner.Tests.Support;
using Xunit;

namespace KRocketDocumentScanner.Tests.Functional;

[Trait("Category", "Functional")]
public class RegistryManagerTests
{
    private const string AirScanA = "airscan:ip=192.0.2.10";
    private const string EsclA = "escl:https://192.0.2.10:443";
    private const string UsbOne = "usb:one";
    private const string UsbTwo = "usb:two";
    private const string UsbThree = "usb:three";

    [Fact]
    public async Task First_scanner_added_becomes_default()
    {
        var (registry, _, _) = await Make.RegistryAsync(new[] { UsbOne });
        await registry.AddManualAsync(UsbOne, "One", null);
        Assert.Equal(UsbOne, registry.DefaultDriverId);
    }

    [Fact]
    public async Task Adding_a_second_scanner_keeps_the_default()
    {
        var (registry, _, _) = await Make.RegistryAsync(new[] { UsbOne, UsbTwo });
        await registry.AddManualAsync(UsbOne, "One", null);
        await registry.AddManualAsync(UsbTwo, "Two", null);
        Assert.Equal(UsbOne, registry.DefaultDriverId);
        Assert.Equal(2, registry.Entries.Count);
    }

    [Fact]
    public async Task Removing_the_default_promotes_the_first_online_scanner()
    {
        var (registry, engine, _) = await Make.RegistryAsync(new[] { UsbOne, UsbTwo, UsbThree });
        await registry.AddManualAsync(UsbOne, "One", null);
        await registry.AddManualAsync(UsbTwo, "Two", null);
        await registry.AddManualAsync(UsbThree, "Three", null);
        engine.Reachable.Remove(UsbTwo);
        await registry.LoadAndRefreshAsync();

        await registry.RemoveAsync(UsbOne);
        Assert.Equal(UsbThree, registry.DefaultDriverId);
    }

    [Fact]
    public async Task Removing_the_default_with_nothing_online_and_one_left_promotes_it()
    {
        var (registry, engine, _) = await Make.RegistryAsync(new[] { UsbOne, UsbTwo });
        await registry.AddManualAsync(UsbOne, "One", null);
        await registry.AddManualAsync(UsbTwo, "Two", null);
        engine.Reachable.Clear();
        await registry.LoadAndRefreshAsync();

        await registry.RemoveAsync(UsbOne);
        Assert.Equal(UsbTwo, registry.DefaultDriverId);
    }

    [Fact]
    public async Task Removing_the_default_with_nothing_online_and_several_left_clears_it()
    {
        var (registry, engine, _) = await Make.RegistryAsync(new[] { UsbOne, UsbTwo, UsbThree });
        foreach (var (id, n) in new[] { (UsbOne, "1"), (UsbTwo, "2"), (UsbThree, "3") })
            await registry.AddManualAsync(id, n, null);
        engine.Reachable.Clear();
        await registry.LoadAndRefreshAsync();

        await registry.RemoveAsync(UsbOne);
        Assert.Null(registry.DefaultDriverId);
    }

    [Fact]
    public async Task Ensure_default_keeps_an_existing_default_and_otherwise_picks_first_online()
    {
        var (registry, engine, store) = await Make.RegistryAsync(new[] { UsbTwo }, Make.Entry(UsbOne), Make.Entry(UsbTwo));
        store.Saved.DefaultDriverId = null;
        await registry.LoadStoredAsync();

        Assert.Equal(UsbTwo, await registry.EnsureDefaultAsync());
        Assert.Equal(UsbTwo, registry.DefaultDriverId);

        engine.Reachable.Add(UsbOne);
        Assert.Equal(UsbTwo, await registry.EnsureDefaultAsync());
    }

    [Fact]
    public async Task Set_default_rejects_an_unknown_scanner()
    {
        var (registry, _, _) = await Make.RegistryAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.SetDefaultAsync("nope"));
    }

    [Fact]
    public async Task A_saved_ready_state_is_not_trusted_on_load()
    {
        var saved = Make.Entry(UsbOne);
        saved.Reachability = ScannerReachability.Ready;
        var (registry, _, _) = await Make.RegistryAsync(null, saved);
        Assert.All(registry.Entries, e => Assert.Equal(ScannerReachability.Unknown, e.Reachability));
    }

    [Fact]
    public async Task Refresh_marks_unreachable_scanners_not_ready_without_throwing()
    {
        var (registry, engine, _) = await Make.RegistryAsync(new[] { UsbOne }, Make.Entry(UsbOne), Make.Entry(UsbTwo));
        await registry.LoadAndRefreshAsync();
        Assert.Equal(ScannerReachability.Ready, registry.Entries.First(e => e.DriverId == UsbOne).Reachability);
        Assert.Equal(ScannerReachability.NotReady, registry.Entries.First(e => e.DriverId == UsbTwo).Reachability);
    }

    [Fact]
    public async Task Discovery_lists_registered_scanners_too()
    {
        var (registry, engine, _) = await Make.RegistryAsync(new[] { UsbOne }, Make.Entry(UsbOne));
        engine.Discoverable.Add(new DiscoveredScanner(UsbOne, "V", "One"));
        engine.Discoverable.Add(new DiscoveredScanner(UsbTwo, "V", "Two"));
        var found = await registry.DiscoverAvailableAsync();
        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task Discovery_errors_reach_the_caller()
    {
        var (registry, engine, _) = await Make.RegistryAsync();
        engine.DiscoveryThrows = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.DiscoverAvailableAsync());
    }

    [Fact]
    public async Task Adding_the_same_scanner_over_another_protocol_updates_the_entry()
    {
        var (registry, _, _) = await Make.RegistryAsync(new[] { AirScanA, EsclA });
        var first = await registry.AddFromDiscoveryAsync(new DiscoveredScanner(AirScanA, "EPSON", "Model X (airscan:ip=192.0.2.10)"));
        var repointed = new List<(string Old, string New)>();
        registry.ScannerRepointed += (o, n) => repointed.Add((o, n));

        var second = await registry.AddFromDiscoveryAsync(new DiscoveredScanner(EsclA, "Epson", "Model X (escl:https://192.0.2.10:443)"));

        Assert.Single(registry.Entries);
        Assert.Same(first, second);
        Assert.Equal(EsclA, registry.Entries[0].DriverId);
        Assert.Equal(EsclA, registry.DefaultDriverId);
        Assert.Equal(first.DisplayName, second.DisplayName);
        Assert.Single(repointed);
        Assert.Equal((AirScanA, EsclA), repointed[0]);
    }

    [Fact]
    public async Task Adding_the_identical_connection_again_does_not_duplicate_or_fail()
    {
        var (registry, _, _) = await Make.RegistryAsync(new[] { UsbOne });
        await registry.AddManualAsync(UsbOne, "One", null);
        await registry.AddManualAsync(UsbOne, "Again", null);
        Assert.Single(registry.Entries);
        Assert.Equal("One", registry.Entries[0].DisplayName);
    }

    [Fact]
    public async Task Same_device_id_at_a_different_address_is_still_one_scanner()
    {
        const string uuid = "11111111-2222-3333-4444-555555555555";
        var a = $"escl:https://192.0.2.50/{uuid}";
        var b = $"escl:https://192.0.2.99/{uuid}";
        var (registry, _, _) = await Make.RegistryAsync(new[] { a, b });
        await registry.AddManualAsync(a, "Scanner", null);
        await registry.AddManualAsync(b, "Scanner moved", null);
        Assert.Single(registry.Entries);
        Assert.Equal(uuid, registry.Entries[0].DeviceId);
        Assert.Equal(b, registry.Entries[0].DriverId);
    }

    [Fact]
    public async Task Different_addresses_without_a_device_id_are_two_scanners()
    {
        var (registry, _, _) = await Make.RegistryAsync(new[] { "escl:https://192.0.2.21", "escl:https://192.0.2.22" });
        await registry.AddManualAsync("escl:https://192.0.2.21", "A", null);
        await registry.AddManualAsync("escl:https://192.0.2.22", "B", null);
        Assert.Equal(2, registry.Entries.Count);
    }

    [Fact]
    public async Task A_manual_host_name_is_matched_by_the_address_it_resolves_to()
    {
        var manualId = "manual:https://printer.example.local";
        var (registry, engine, _) = await Make.RegistryAsync(new[] { manualId, AirScanA });
        engine.Resolvable["printer.example.local"] = "192.0.2.10";

        await registry.AddManualAsync(manualId, "printer.example.local", "printer.example.local", requireReachable: true);
        Assert.Equal("192.0.2.10", registry.Entries[0].ResolvedAddress);

        await registry.AddFromDiscoveryAsync(new DiscoveredScanner(AirScanA, "V", "M (airscan:ip=192.0.2.10)"));
        Assert.Single(registry.Entries);
        Assert.Equal(AirScanA, registry.Entries[0].DriverId);
    }

    [Fact]
    public async Task Manual_add_that_must_be_reachable_refuses_and_saves_nothing_when_unreachable()
    {
        var (registry, _, store) = await Make.RegistryAsync();
        await Assert.ThrowsAsync<ScannerUnreachableException>(
            () => registry.AddManualAsync("manual:x", "X", "192.0.2.77", requireReachable: true));
        Assert.Empty(registry.Entries);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Manual_add_requires_a_driver_id_and_a_name()
    {
        var (registry, _, _) = await Make.RegistryAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => registry.AddManualAsync("", "X", null));
        await Assert.ThrowsAsync<ArgumentException>(() => registry.AddManualAsync("x", " ", null));
    }

    [Fact]
    public async Task Refresh_recovers_a_broken_connection_through_a_working_sibling()
    {
        var (registry, engine, _) = await Make.RegistryAsync(new[] { AirScanA }, Make.Entry(AirScanA, "Scanner", null));
        engine.Discoverable.Add(new DiscoveredScanner(EsclA, "E", "M (escl:https://192.0.2.10:443)"));
        await registry.LoadAndRefreshAsync();
        Assert.Equal(ScannerReachability.Ready, registry.Entries[0].Reachability);

        engine.Reachable.Clear();
        engine.Reachable.Add(EsclA);
        var repointed = new List<(string, string)>();
        registry.ScannerRepointed += (o, n) => repointed.Add((o, n));

        await registry.LoadAndRefreshAsync();

        Assert.Single(registry.Entries);
        Assert.Equal(EsclA, registry.Entries[0].DriverId);
        Assert.Equal(ScannerReachability.Ready, registry.Entries[0].Reachability);
        Assert.Equal(EsclA, registry.DefaultDriverId);
        Assert.Contains((AirScanA, EsclA), repointed);
    }

    [Fact]
    public async Task Recovery_leaves_the_entry_alone_when_no_sibling_answers()
    {
        var (registry, engine, _) = await Make.RegistryAsync(null, Make.Entry(AirScanA));
        engine.Discoverable.Add(new DiscoveredScanner(EsclA, "E", "M (escl:https://192.0.2.10:443)"));
        await registry.LoadAndRefreshAsync();
        Assert.Equal(AirScanA, registry.Entries[0].DriverId);
        Assert.Equal(ScannerReachability.NotReady, registry.Entries[0].Reachability);
    }

    [Fact]
    public async Task Recovery_survives_a_failing_discovery()
    {
        var (registry, engine, _) = await Make.RegistryAsync(null, Make.Entry(AirScanA));
        engine.DiscoveryThrows = true;
        await registry.LoadAndRefreshAsync();
        Assert.Equal(ScannerReachability.NotReady, registry.Entries[0].Reachability);
    }

    [Fact]
    public async Task Removing_a_scanner_raises_ScannerRemoved_only_when_it_existed()
    {
        var (registry, _, _) = await Make.RegistryAsync(new[] { UsbOne });
        await registry.AddManualAsync(UsbOne, "One", null);
        var removed = new List<string>();
        registry.ScannerRemoved += removed.Add;

        await registry.RemoveAsync("does-not-exist");
        Assert.Empty(removed);
        await registry.RemoveAsync(UsbOne);
        Assert.Equal(new[] { UsbOne }, removed);
    }

    [Fact]
    public async Task The_registry_round_trips_through_the_json_file_and_forgets_reachability()
    {
        var dir = Path.Combine(Path.GetTempPath(), "krocketdocumentscanner-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "scanners.json");
            var engine = new FakeEngine();
            engine.Reachable.Add(UsbOne);
            var registry = new ScannerRegistryManager(new JsonScannerRegistryStore(path), engine);
            await registry.LoadStoredAsync();
            await registry.AddManualAsync(UsbOne, "One", "192.0.2.5");

            var reloaded = new ScannerRegistryManager(new JsonScannerRegistryStore(path), engine);
            await reloaded.LoadStoredAsync();
            Assert.Equal(UsbOne, reloaded.DefaultDriverId);
            Assert.Equal("192.0.2.5", reloaded.Entries[0].ManualAddress);
            Assert.Equal(ScannerReachability.Unknown, reloaded.Entries[0].Reachability);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task A_missing_registry_file_loads_as_empty()
    {
        var path = Path.Combine(Path.GetTempPath(), "krocketdocumentscanner-tests-" + Guid.NewGuid().ToString("N"), "none.json");
        var loaded = await new JsonScannerRegistryStore(path).LoadAsync();
        Assert.Empty(loaded.Entries);
        Assert.Null(loaded.DefaultDriverId);
    }
}
