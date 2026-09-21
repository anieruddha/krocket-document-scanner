using KRocketDocumentScanner.App.ViewModels;
using KRocketDocumentScanner.App.ViewModels.Localization;
using KRocketDocumentScanner.Core.Models;
using KRocketDocumentScanner.Tests.Support;
using Xunit;

namespace KRocketDocumentScanner.Tests.Functional;

[Trait("Category", "Functional")]
public class AddAndManageViewModelTests
{
    private static string ManualId(string address, string name) => $"manual:{address}";

    private static async Task<(AddScannerViewModel Vm, KRocketDocumentScanner.Core.Registry.ScannerRegistryManager Registry, FakeEngine Engine, Counter Closed)>
        NewAddAsync(params string[] reachable)
    {
        var (registry, engine, _) = await Make.RegistryAsync(reachable);
        var closed = new Counter();
        return (new AddScannerViewModel(registry, ManualId, () => closed.Count++), registry, engine, closed);
    }

    private sealed class Counter { public int Count; }

    [Fact]
    public void AirScan_and_eSCL_reports_of_one_device_become_one_card_with_two_connections()
    {
        var groups = AddScannerViewModel.BuildGroups(new[]
        {
            new DiscoveredScanner("airscan:e0:Model X", "EPSON", "Model X (airscan:ip=192.0.2.10)"),
            new DiscoveredScanner("escl:https://192.0.2.10:443", "Epson", "Model X (escl:https://192.0.2.10:443)"),
        }, ManualId);

        var card = Assert.Single(groups);
        Assert.Equal(2, card.Connections.Count);
        Assert.Equal(Strings.AirScanLabel, card.Connections[0].Label);
        Assert.Equal(Strings.EsclLabel, card.Connections[1].Label);
        Assert.True(card.IsChecked);
    }

    [Fact]
    public void An_AirScan_only_report_gets_a_synthesised_eSCL_connection()
    {
        var groups = AddScannerViewModel.BuildGroups(new[]
        {
            new DiscoveredScanner("airscan:e0:Model X", "EPSON", "Model X (airscan:ip=192.0.2.10)"),
        }, ManualId);
        var card = Assert.Single(groups);
        Assert.Equal(2, card.Connections.Count);
        Assert.Equal("manual:192.0.2.10", card.Connections[1].Scanner.DriverId);
    }

    [Fact]
    public void Different_devices_and_addressless_devices_get_their_own_cards()
    {
        var groups = AddScannerViewModel.BuildGroups(new[]
        {
            new DiscoveredScanner("escl:https://192.0.2.10", "A", "One"),
            new DiscoveredScanner("escl:https://192.0.2.20", "B", "Two"),
            new DiscoveredScanner("usb:1", "C", "Three"),
            new DiscoveredScanner("usb:2", "D", "Four"),
        }, ManualId);
        Assert.Equal(4, groups.Count);
    }

    [Fact]
    public void Duplicate_reports_are_collapsed_and_an_empty_result_gives_no_cards()
    {
        var dup = new DiscoveredScanner("usb:1", "C", "Three");
        Assert.Single(AddScannerViewModel.BuildGroups(new[] { dup, dup }, ManualId));
        Assert.Empty(AddScannerViewModel.BuildGroups(Array.Empty<DiscoveredScanner>(), ManualId));
    }

    [Fact]
    public async Task Discover_lists_results_and_reports_failures_without_throwing()
    {
        var (vm, _, engine, _) = await NewAddAsync();
        engine.Discoverable.Add(new DiscoveredScanner("usb:1", "C", "Three"));
        await vm.DiscoverCommand.ExecuteAsync();
        Assert.Single(vm.Found);
        Assert.False(vm.IsSearching);

        engine.DiscoveryThrows = true;
        await vm.DiscoverCommand.ExecuteAsync();
        Assert.False(vm.IsSearching);
        Assert.True(vm.HasStatusMessage);
    }

    [Fact]
    public async Task Adding_a_checked_card_saves_it_and_closes_the_window()
    {
        var (vm, registry, engine, closed) = await NewAddAsync("usb:1");
        engine.Discoverable.Add(new DiscoveredScanner("usb:1", "C", "Three"));
        await vm.DiscoverCommand.ExecuteAsync();
        await vm.AddSelectedCommand.ExecuteAsync();
        Assert.Single(registry.Entries);
        Assert.Equal(1, closed.Count);
    }

    [Fact]
    public async Task Adding_an_already_registered_scanner_from_discovery_does_not_duplicate_it()
    {
        var (vm, registry, engine, _) = await NewAddAsync("usb:1");
        await registry.AddManualAsync("usb:1", "Mine", null);
        engine.Discoverable.Add(new DiscoveredScanner("usb:1", "C", "Three"));
        await vm.DiscoverCommand.ExecuteAsync();
        Assert.Single(vm.Found);
        await vm.AddSelectedCommand.ExecuteAsync();
        Assert.Single(registry.Entries);
        Assert.Equal("Mine", registry.Entries[0].DisplayName);
    }

    [Fact]
    public async Task Manual_add_is_enabled_by_an_address_alone()
    {
        var (vm, _, _, _) = await NewAddAsync();
        Assert.False(vm.CanAddManual);
        vm.ManualAddress = "192.0.2.30";
        Assert.True(vm.CanAddManual);
        vm.ManualAddress = "   ";
        Assert.False(vm.CanAddManual);
    }

    [Fact]
    public async Task A_bare_address_tries_https_then_http_and_saves_the_one_that_answers()
    {
        var (vm, registry, engine, closed) = await NewAddAsync("manual:http://192.0.2.30");
        vm.ManualAddress = "192.0.2.30";
        await vm.AddManualCommand.ExecuteAsync();

        Assert.Equal(new[] { "manual:https://192.0.2.30", "manual:http://192.0.2.30" }, engine.ReachabilityChecks);
        var entry = Assert.Single(registry.Entries);
        Assert.Equal("http://192.0.2.30", entry.ManualAddress);
        Assert.Equal("192.0.2.30", entry.DisplayName);
        Assert.Equal(1, closed.Count);
        Assert.Null(vm.ManualStatusMessage);
    }

    [Fact]
    public async Task The_scanner_is_named_after_the_typed_address()
    {
        var (vm, registry, _, _) = await NewAddAsync("manual:https://192.0.2.31");
        vm.ManualAddress = "192.0.2.31";
        await vm.AddManualCommand.ExecuteAsync();
        Assert.Equal("192.0.2.31", registry.Entries[0].DisplayName);
    }

    [Fact]
    public async Task An_address_with_a_scheme_is_tried_only_as_typed()
    {
        var (vm, registry, engine, _) = await NewAddAsync();
        vm.ManualAddress = "http://192.0.2.32";
        await vm.AddManualCommand.ExecuteAsync();
        Assert.Equal(new[] { "manual:http://192.0.2.32" }, engine.ReachabilityChecks);
        Assert.Empty(registry.Entries);
        Assert.True(vm.HasManualStatusMessage);
    }

    [Fact]
    public async Task An_invalid_address_is_refused_before_any_network_check_and_names_the_address()
    {
        var (vm, registry, engine, closed) = await NewAddAsync();
        vm.ManualAddress = "192.0.2.999";
        await vm.AddManualCommand.ExecuteAsync();
        Assert.Empty(engine.ReachabilityChecks);
        Assert.Empty(registry.Entries);
        Assert.Contains("192.0.2.999", vm.ManualStatusMessage);
        Assert.Equal(0, closed.Count);
    }

    [Fact]
    public async Task An_unreachable_address_shows_a_message_with_the_address_and_keeps_the_window_open()
    {
        var (vm, registry, _, closed) = await NewAddAsync();
        vm.ManualAddress = "192.0.2.33";
        await vm.AddManualCommand.ExecuteAsync();
        Assert.Contains("192.0.2.33", vm.ManualStatusMessage);
        Assert.Empty(registry.Entries);
        Assert.Equal(0, closed.Count);
        Assert.False(vm.IsAddingManual);
    }

    [Fact]
    public async Task While_the_address_is_being_checked_add_is_disabled()
    {
        var (vm, registry, engine, _) = await NewAddAsync();
        engine.ReachableGate = new TaskCompletionSource<bool>();
        engine.Reachable.Add("manual:https://192.0.2.34");
        vm.ManualAddress = "192.0.2.34";

        var running = vm.AddManualCommand.ExecuteAsync();
        await Make.EventuallyAsync(() => vm.IsAddingManual);
        Assert.False(vm.CanAddManual);

        engine.ReachableGate.SetResult(true);
        await running;
        Assert.False(vm.IsAddingManual);
        Assert.Single(registry.Entries);
    }

    [Fact]
    public async Task Adding_the_same_address_twice_updates_instead_of_failing()
    {
        var (vm, registry, _, _) = await NewAddAsync("manual:https://192.0.2.35");
        vm.ManualAddress = "192.0.2.35";
        await vm.AddManualCommand.ExecuteAsync();
        await vm.AddManualCommand.ExecuteAsync();
        Assert.Single(registry.Entries);
        Assert.Null(vm.ManualStatusMessage);
    }

    [Fact]
    public async Task Manage_lists_scanners_marks_the_default_and_only_lets_you_choose_with_several()
    {
        var (registry, _, _) = await Make.RegistryAsync(new[] { "usb:1", "usb:2" }, Make.Entry("usb:1", "One"));
        var vm = new ManageScannersViewModel(registry, () => Task.CompletedTask);
        Assert.Single(vm.Scanners);
        Assert.True(vm.Scanners[0].IsDefault);
        Assert.False(vm.CanChooseDefault);
        Assert.False(vm.IsEmpty);

        await registry.AddManualAsync("usb:2", "Two", null);
        var vm2 = new ManageScannersViewModel(registry, () => Task.CompletedTask);
        Assert.Equal(2, vm2.Scanners.Count);
        Assert.True(vm2.CanChooseDefault);
    }

    [Fact]
    public async Task Manage_can_change_the_default_and_remove_a_scanner()
    {
        var (registry, _, _) = await Make.RegistryAsync(new[] { "usb:1", "usb:2" });
        await registry.AddManualAsync("usb:1", "One", null);
        await registry.AddManualAsync("usb:2", "Two", null);
        var vm = new ManageScannersViewModel(registry, () => Task.CompletedTask);

        await vm.SetDefaultAsync("usb:2");
        Assert.Equal("usb:2", registry.DefaultDriverId);
        await vm.RemoveAsync("usb:2");
        Assert.Single(registry.Entries);
        Assert.Equal("usb:1", registry.DefaultDriverId);
    }

    [Fact]
    public async Task Manage_refresh_rechecks_every_scanner_and_reports_failures_softly()
    {
        var (registry, engine, _) = await Make.RegistryAsync(new[] { "usb:1" }, Make.Entry("usb:1"));
        var vm = new ManageScannersViewModel(registry, () => Task.CompletedTask);
        await vm.RefreshCommand.ExecuteAsync();
        Assert.True(vm.Scanners[0].IsReady);

        engine.Reachable.Clear();
        await vm.RefreshCommand.ExecuteAsync();
        Assert.False(vm.Scanners[0].IsReady);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task An_empty_registry_shows_the_empty_state()
    {
        var (registry, _, _) = await Make.RegistryAsync();
        var vm = new ManageScannersViewModel(registry, () => Task.CompletedTask);
        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasScanners);
    }
}
