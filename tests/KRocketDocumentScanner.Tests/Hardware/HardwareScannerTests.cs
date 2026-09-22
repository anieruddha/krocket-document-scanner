using KRocketDocumentScanner.App.ViewModels;
using KRocketDocumentScanner.Core.Abstractions;
using KRocketDocumentScanner.Core.Models;
using KRocketDocumentScanner.Core.Registry;
using KRocketDocumentScanner.Scanning;
using KRocketDocumentScanner.Tests.Support;
using NAPS2.Scan;
using Xunit;
using CoreScanOptions = KRocketDocumentScanner.Core.Models.ScanOptions;

namespace KRocketDocumentScanner.Tests.Hardware;

public sealed class HardwareFactAttribute : FactAttribute
{
    public HardwareFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("KROCKETDOCUMENTSCANNER_SKIP_HARDWARE") == "1")
            Skip = "KROCKETDOCUMENTSCANNER_SKIP_HARDWARE=1";
    }
}

internal sealed class QuietLog : INetworkActivityLog
{
    public Task LogAsync(NetworkActivityEntry entry, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<NetworkActivityEntry>> ReadAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NetworkActivityEntry>>(new List<NetworkActivityEntry>());
}

public sealed class HardwareFixture : IDisposable
{
    internal Naps2ScannerEngine Engine { get; } = new(new QuietLog());
    private IReadOnlyList<DiscoveredScanner>? _found;

    public async Task<IReadOnlyList<DiscoveredScanner>> FoundAsync() => _found ??= await Engine.ListDevicesAsync();

    public async Task<DiscoveredScanner> NetworkScannerAsync()
    {
        var found = await FoundAsync();
        var escl = found.FirstOrDefault(d => Naps2DeviceIdCodec.Decode(d.DriverId).Driver == Driver.Escl);
        Assert.True(escl is not null, "no network scanner was discovered - switch the scanner on and check it is on this network");
        return escl!;
    }

    public void Dispose() => Engine.Dispose();
}

[Trait("Category", "Hardware")]
[Collection("Hardware")]
public class HardwareScannerTests : IClassFixture<HardwareFixture>
{
    private readonly HardwareFixture _hw;
    public HardwareScannerTests(HardwareFixture hw) => _hw = hw;

    private static CoreScanOptions Small() => new()
    {
        ResolutionDpi = 75, ColorMode = ColorMode.Color, Source = ScanSource.Flatbed,
        Area = new ScanArea(0, 0, 50, 50), AutoDeskew = false, DropBlankPages = false,
    };

    [HardwareFact]
    public async Task A_switched_on_network_scanner_is_discovered()
    {
        var scanner = await _hw.NetworkScannerAsync();
        Assert.False(string.IsNullOrWhiteSpace(scanner.Model));
    }

    [HardwareFact]
    public async Task Discovery_can_be_repeated_and_still_finds_the_scanner()
    {
        await _hw.NetworkScannerAsync();
        var again = await _hw.Engine.ListDevicesAsync();
        Assert.Contains(again, d => Naps2DeviceIdCodec.Decode(d.DriverId).Driver == Driver.Escl);
    }

    [HardwareFact]
    public async Task The_discovered_scanner_answers_the_reachability_check()
    {
        var scanner = await _hw.NetworkScannerAsync();
        Assert.True(await _hw.Engine.IsReachableAsync(scanner.DriverId));
    }

    [HardwareFact]
    public async Task Made_up_addresses_are_not_reported_reachable()
    {
        var bogusHost = Naps2ScannerEngine.BuildManualNetworkScannerDriverId("no-such-device.local", "x");
        var unroutable = Naps2ScannerEngine.BuildManualNetworkScannerDriverId("192.0.2.1", "x");
        Assert.False(await _hw.Engine.IsReachableAsync(bogusHost), "a made-up .local name was reported reachable");
        Assert.False(await _hw.Engine.IsReachableAsync(unroutable), "an unroutable address was reported reachable");
    }

    [HardwareFact]
    public async Task The_scanner_reports_a_flatbed()
    {
        var scanner = await _hw.NetworkScannerAsync();
        var caps = await _hw.Engine.GetCapabilitiesAsync(scanner.DriverId);
        Assert.True(caps.SupportsFlatbed);
    }

    [HardwareFact]
    public async Task A_low_resolution_preview_returns_an_image()
    {
        var scanner = await _hw.NetworkScannerAsync();
        var page = await _hw.Engine.PreviewAsync(scanner.DriverId, Small());
        Assert.True(page.WidthPx > 0 && page.HeightPx > 0);
        Assert.Equal(page.WidthPx * page.HeightPx * page.BytesPerPixel, page.PixelData.Length);
    }

    [HardwareFact]
    public async Task A_small_area_scan_has_about_the_expected_size()
    {
        var scanner = await _hw.NetworkScannerAsync();
        var page = await _hw.Engine.ScanSingleAsync(scanner.DriverId, Small());
        const double expected = 50 / 25.4 * 75;
        Assert.InRange(page.WidthPx, expected * 0.8, expected * 1.2);
        Assert.InRange(page.HeightPx, expected * 0.8, expected * 1.2);
    }

    private static string HostOf(DiscoveredScanner scanner) =>
        new Uri(Naps2DeviceIdCodec.Decode(scanner.DriverId).ConnectionUri!).Host;

    [HardwareFact]
    public async Task The_discovered_connection_carries_an_address_the_app_can_match()
    {
        var scanner = await _hw.NetworkScannerAsync();
        var host = HostOf(scanner);
        Assert.True(ScannerAddressExtractor.Extract(host) is not null || ScannerAddressExtractor.ExtractDeviceId(scanner.DriverId) is not null,
            "the discovered connection has neither an IPv4 address nor a device id, so it cannot be matched to a saved scanner");
    }

    [HardwareFact]
    public async Task The_discovered_connection_address_can_be_used_to_add_the_scanner_manually()
    {
        var scanner = await _hw.NetworkScannerAsync();
        var host = HostOf(scanner);
        var registry = new ScannerRegistryManager(new MemoryStore(), _hw.Engine);
        await registry.LoadStoredAsync();
        var added = await Record.ExceptionAsync(() => registry.AddManualAsync(
            Naps2ScannerEngine.BuildManualNetworkScannerDriverId(host, "hardware-test"), "hardware-test", host, requireReachable: true));
        Assert.True(added is null, "adding the scanner by the address it was discovered at was refused as unreachable");
    }

    [HardwareFact]
    public async Task Adding_the_scanner_by_address_and_by_discovery_gives_one_saved_scanner()
    {
        var scanner = await _hw.NetworkScannerAsync();
        var host = HostOf(scanner);
        Assert.True(ScannerAddressExtractor.Extract(host) is not null,
            "cannot test the merge: the discovered address is not IPv4");

        var registry = new ScannerRegistryManager(new MemoryStore(), _hw.Engine);
        await registry.LoadStoredAsync();
        await registry.AddManualAsync(
            Naps2ScannerEngine.BuildManualNetworkScannerDriverId(host, "hardware-test"), "hardware-test", host, requireReachable: true);
        await registry.AddFromDiscoveryAsync(scanner);
        Assert.True(registry.Entries.Count == 1,
            "the same scanner added by address and by discovery became more than one saved scanner");
    }

    [HardwareFact]
    public async Task The_scan_window_flow_works_end_to_end_on_the_real_scanner()
    {
        var scanner = await _hw.NetworkScannerAsync();
        var registry = new ScannerRegistryManager(new MemoryStore(), _hw.Engine);
        await registry.LoadStoredAsync();
        await registry.AddFromDiscoveryAsync(scanner);

        var vm = new ScanViewModel(_hw.Engine, registry, registry.DefaultDriverId,
            _ => Task.FromResult(false), () => Task.CompletedTask);
        await vm.InitializeAsync();
        Assert.Equal(ScanScreenState.Ready, vm.State);

        await vm.PreviewCommand.ExecuteAsync();
        Assert.True(vm.HasPreview, "no preview came back");
        await vm.ScanCommand.ExecuteAsync();
        Assert.Equal(1, vm.PageCount);
    }

    [HardwareFact]
    public async Task A_saved_scanner_is_ready_after_a_refresh()
    {
        var scanner = await _hw.NetworkScannerAsync();
        var registry = new ScannerRegistryManager(new MemoryStore(), _hw.Engine);
        await registry.LoadStoredAsync();
        await registry.AddFromDiscoveryAsync(scanner);
        await registry.LoadAndRefreshAsync();
        Assert.Equal(ScannerReachability.Ready, registry.Entries[0].Reachability);
    }
}
