using KRocketDocumentScanner.App.ViewModels;
using KRocketDocumentScanner.App.ViewModels.Localization;
using KRocketDocumentScanner.Core.Models;
using KRocketDocumentScanner.Core.Registry;
using KRocketDocumentScanner.Tests.Support;
using Xunit;

namespace KRocketDocumentScanner.Tests.Functional;

[Trait("Category", "Functional")]
public class ScanViewModelTests
{
    private const string One = "airscan:ip=192.0.2.10";
    private const string OneEscl = "escl:https://192.0.2.10:443";
    private const string Two = "usb:two";

    private static ScanViewModel NewVm(KRocketDocumentScanner.Core.Abstractions.IScannerEngine engine, ScannerRegistryManager registry, string? driverId, Action? close = null) =>
        new(engine, registry, driverId, _ => Task.FromResult(true), () => Task.CompletedTask, close ?? (() => { }));

    private static async Task<(ScanViewModel Vm, ScannerRegistryManager Registry, FakeEngine Engine)> ReadyAsync(
        string[]? reachable = null, params ScannerRegistryEntry[] saved)
    {
        saved = saved.Length > 0 ? saved : new[] { Make.Entry(One, "First"), Make.Entry(Two, "Second") };
        var (registry, engine, _) = await Make.RegistryAsync(reachable ?? new[] { One, Two, OneEscl }, saved);
        var vm = NewVm(engine, registry, registry.DefaultDriverId);
        await vm.InitializeAsync();
        return (vm, registry, engine);
    }

    [Fact]
    public async Task Preview_and_scan_are_locked_until_the_scanner_check_passes()
    {
        var (registry, engine, _) = await Make.RegistryAsync(new[] { One }, Make.Entry(One));
        engine.ReachableGate = new TaskCompletionSource<bool>();
        var vm = NewVm(engine, registry, One);

        Assert.Equal(ScanScreenState.Checking, vm.State);
        Assert.False(vm.PreviewCommand.CanExecute(null));
        Assert.False(vm.ScanCommand.CanExecute(null));

        var check = vm.InitializeAsync();
        await Make.EventuallyAsync(() => engine.ReachabilityChecks.Count > 0);
        Assert.True(vm.IsWorking);
        Assert.Equal(Strings.CheckingScannerStatus, vm.StatusMessage);
        Assert.False(vm.PreviewCommand.CanExecute(null));
        Assert.False(vm.RefreshScannersCommand.CanExecute(null));

        engine.ReachableGate.SetResult(true);
        await check;
        Assert.Equal(ScanScreenState.Ready, vm.State);
        Assert.False(vm.IsWorking);
        Assert.Null(vm.StatusMessage);
        Assert.True(vm.PreviewCommand.CanExecute(null));
        Assert.True(vm.ScanCommand.CanExecute(null));
        Assert.True(vm.IsReady);
    }

    [Fact]
    public async Task An_unreachable_scanner_leaves_preview_and_scan_disabled()
    {
        var (registry, engine, _) = await Make.RegistryAsync(null, Make.Entry(One));
        var vm = NewVm(engine, registry, One);
        await vm.InitializeAsync();
        Assert.Equal(ScanScreenState.ScannerUnavailable, vm.State);
        Assert.True(vm.ShowUnavailable);
        Assert.False(vm.IsReady);
        Assert.False(vm.PreviewCommand.CanExecute(null));
        Assert.False(vm.ScanCommand.CanExecute(null));
    }

    [Fact]
    public async Task With_no_scanner_selected_the_first_online_saved_scanner_is_used()
    {
        var (registry, engine, store) = await Make.RegistryAsync(new[] { Two }, Make.Entry(One), Make.Entry(Two, "Second"));
        store.Saved.DefaultDriverId = null;
        await registry.LoadStoredAsync();
        var vm = NewVm(engine, registry, null);
        await vm.InitializeAsync();
        Assert.Equal(ScanScreenState.Ready, vm.State);
        Assert.Equal("Second", vm.ScannerName);
        Assert.Equal(Two, registry.DefaultDriverId);
    }

    [Fact]
    public async Task With_nothing_registered_the_screen_says_so()
    {
        var (registry, engine, _) = await Make.RegistryAsync();
        var vm = NewVm(engine, registry, null);
        await vm.InitializeAsync();
        Assert.True(vm.NoScannersRegistered);
        Assert.True(vm.ShowUnavailable);
    }

    [Fact]
    public async Task A_check_that_throws_shows_unavailable_instead_of_crashing()
    {
        var (registry, engine, _) = await Make.RegistryAsync(new[] { One }, Make.Entry(One));
        var vm = NewVm(new ThrowingEngine(), registry, One);
        await vm.InitializeAsync();
        Assert.Equal(ScanScreenState.ScannerUnavailable, vm.State);
    }

    private sealed class ThrowingEngine : KRocketDocumentScanner.Core.Abstractions.IScannerEngine
    {
        public Task<IReadOnlyList<DiscoveredScanner>> ListDevicesAsync(CancellationToken ct = default) => throw new InvalidOperationException();
        public Task<bool> IsReachableAsync(string driverId, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task<ScannerCapabilities> GetCapabilitiesAsync(string driverId, CancellationToken ct = default) => throw new InvalidOperationException();
        public Task<CapturedPage> PreviewAsync(string driverId, ScanOptions options, CancellationToken ct = default) => throw new InvalidOperationException();
        public Task<CapturedPage> ScanSingleAsync(string driverId, ScanOptions options, CancellationToken ct = default) => throw new InvalidOperationException();
        public IAsyncEnumerable<CapturedPage> ScanBatchAsync(string driverId, ScanOptions options, CancellationToken ct = default) => throw new InvalidOperationException();
    }

    [Fact]
    public async Task Preview_then_scan_adds_a_page_and_clears_the_preview()
    {
        var (vm, _, engine) = await ReadyAsync();
        await vm.PreviewCommand.ExecuteAsync();
        Assert.True(vm.HasPreview);
        Assert.Equal(ScanScreenState.Ready, vm.State);

        await vm.ScanCommand.ExecuteAsync();
        Assert.Equal(1, vm.PageCount);
        Assert.False(vm.HasPreview);
        Assert.Equal(new[] { "preview", "scan" }, engine.Calls.Where(c => c is "preview" or "scan"));
    }

    [Fact]
    public async Task While_previewing_the_window_is_busy_and_returns_to_ready()
    {
        var (vm, _, engine) = await ReadyAsync();
        engine.DelayMs = 150;
        var run = vm.PreviewCommand.ExecuteAsync();
        await Make.EventuallyAsync(() => vm.State == ScanScreenState.Previewing);
        Assert.True(vm.IsBusy);
        Assert.True(vm.IsWorking);
        Assert.False(vm.RefreshScannersCommand.CanExecute(null));
        await run;
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task A_full_image_selection_is_treated_as_no_crop()
    {
        var (vm, _, engine) = await ReadyAsync();
        vm.PagePresetIndex = ScanViewModel.PagePresetChoices.Count - 1;
        await vm.PreviewCommand.ExecuteAsync();
        vm.SetSelectedArea(0, 0, 1, 1);
        await vm.ScanCommand.ExecuteAsync();
        Assert.Null(engine.LastScanOptions?.Area);
    }

    [Fact]
    public async Task A_partial_selection_is_sent_as_a_crop_area()
    {
        var (vm, _, engine) = await ReadyAsync();
        vm.PagePresetIndex = ScanViewModel.PagePresetChoices.Count - 1;
        await vm.PreviewCommand.ExecuteAsync();
        vm.SetSelectedArea(0.1, 0.1, 0.9, 0.9);
        await vm.ScanCommand.ExecuteAsync();
        Assert.NotNull(engine.LastScanOptions?.Area);
    }

    [Fact]
    public void Custom_is_the_last_preset_and_the_default_choice()
    {
        Assert.Equal(new[] { "A4", "Letter", "Legal", "A5", "Custom" }, ScanViewModel.PagePresetChoices);
        Assert.Equal(ScanViewModel.CustomPresetName, ScanViewModel.PagePresetChoices[^1]);
    }

    [Fact]
    public async Task Picking_a_named_preset_is_not_custom_and_custom_is()
    {
        var (vm, _, _) = await ReadyAsync();
        Assert.True(vm.IsCustomPreset);
        vm.PagePresetIndex = 0;
        Assert.False(vm.IsCustomPreset);
        vm.PagePresetIndex = ScanViewModel.PagePresetChoices.Count - 1;
        Assert.True(vm.IsCustomPreset);
    }

    [Fact]
    public async Task Picking_another_scanner_resets_the_preview_but_keeps_scanned_pages()
    {
        var (vm, _, _) = await ReadyAsync();
        await vm.PreviewCommand.ExecuteAsync();
        await vm.ScanCommand.ExecuteAsync();
        await vm.PreviewCommand.ExecuteAsync();
        Assert.True(vm.HasPreview);

        vm.ScannerIndex = 1;
        Assert.False(vm.HasPreview);
        Assert.Equal(1, vm.PageCount);
        Assert.Equal("Second", vm.ScannerName);
        await Make.EventuallyAsync(() => vm.State == ScanScreenState.Ready);
    }

    [Fact]
    public async Task Re_selecting_the_same_scanner_changes_nothing()
    {
        var (vm, _, _) = await ReadyAsync();
        await vm.PreviewCommand.ExecuteAsync();
        vm.ScannerIndex = 0;
        Assert.True(vm.HasPreview);
    }

    [Fact]
    public async Task Removing_the_scanner_in_use_switches_to_the_default_and_resets_the_preview()
    {
        var (vm, registry, _) = await ReadyAsync();
        await vm.PreviewCommand.ExecuteAsync();
        Assert.True(vm.HasPreview);

        await registry.RemoveAsync(One);
        await Make.EventuallyAsync(() => vm.ScannerName == "Second");
        Assert.False(vm.HasPreview);
        await Make.EventuallyAsync(() => vm.State == ScanScreenState.Ready);
    }

    [Fact]
    public async Task Removing_another_scanner_leaves_this_window_alone()
    {
        var (vm, registry, _) = await ReadyAsync();
        await vm.PreviewCommand.ExecuteAsync();
        await registry.RemoveAsync(Two);
        await Make.EventuallyAsync(() => vm.ScannerChoices.Count == 1);
        Assert.True(vm.HasPreview);
        Assert.Equal("First", vm.ScannerName);
    }

    [Fact]
    public async Task Removing_and_re_adding_the_scanner_in_use_counts_as_a_change()
    {
        var (vm, registry, _) = await ReadyAsync();
        await vm.PreviewCommand.ExecuteAsync();
        await registry.RemoveAsync(One);
        await registry.AddManualAsync(One, "First again", null);
        await Make.EventuallyAsync(() => !vm.HasPreview);
        Assert.Equal("Second", vm.ScannerName);
    }

    [Fact]
    public async Task The_same_saved_scanner_getting_another_connection_is_followed_without_a_reset()
    {
        var (vm, registry, engine) = await ReadyAsync();
        await vm.PreviewCommand.ExecuteAsync();
        engine.Reachable.Add(OneEscl);
        await registry.AddFromDiscoveryAsync(new DiscoveredScanner(OneEscl, "E", "M (escl:https://192.0.2.10:443)"));
        Assert.Equal(2, registry.Entries.Count);

        await Make.EventuallyAsync(() => registry.Entries[0].DriverId == OneEscl);
        await Task.Delay(100);
        Assert.True(vm.HasPreview);
        Assert.Equal("First", vm.ScannerName);
        await vm.ScanCommand.ExecuteAsync();
        Assert.Equal(OneEscl, engine.LastDriverId);
    }

    [Fact]
    public async Task A_window_that_has_been_detached_ignores_registry_changes()
    {
        var (vm, registry, _) = await ReadyAsync();
        await vm.PreviewCommand.ExecuteAsync();
        vm.Detach();
        await registry.RemoveAsync(One);
        await Task.Delay(150);
        Assert.True(vm.HasPreview);
        Assert.Equal("First", vm.ScannerName);
    }

    [Fact]
    public async Task Dropdown_dots_follow_the_registry_and_the_windows_own_check()
    {
        var (registry, engine, _) = await Make.RegistryAsync(new[] { One }, Make.Entry(One, "First"), Make.Entry(Two, "Second"));
        var vm = NewVm(engine, registry, One);
        Assert.All(vm.ScannerChoices, c => Assert.False(c.IsReady));

        await registry.LoadAndRefreshAsync();
        await Make.EventuallyAsync(() => vm.ScannerChoices[0].IsReady);
        Assert.False(vm.ScannerChoices[1].IsReady);

        engine.Reachable.Clear();
        await vm.InitializeAsync();
        Assert.False(vm.ScannerChoices[0].IsReady);
        Assert.False(vm.IsReady);
    }

    [Fact]
    public async Task A_saved_ready_state_never_shows_a_green_dot_at_startup()
    {
        var saved = Make.Entry(One, "First");
        saved.Reachability = ScannerReachability.Ready;
        var (registry, engine, _) = await Make.RegistryAsync(null, saved);
        var vm = NewVm(engine, registry, One);
        Assert.False(vm.ScannerChoices[0].IsReady);
        Assert.False(vm.IsReady);
    }

    [Fact]
    public async Task Refresh_rechecks_scanners_and_keeps_the_current_one_and_its_preview()
    {
        var (vm, _, engine) = await ReadyAsync();
        await vm.PreviewCommand.ExecuteAsync();
        await vm.RefreshScannersCommand.ExecuteAsync();
        Assert.Equal(ScanScreenState.Ready, vm.State);
        Assert.True(vm.HasPreview);
        Assert.Equal("First", vm.ScannerName);
    }

    [Fact]
    public async Task Refresh_finds_a_scanner_that_was_switched_on_after_the_window_opened()
    {
        var (registry, engine, _) = await Make.RegistryAsync(null, Make.Entry(One, "First"));
        var vm = NewVm(engine, registry, One);
        await vm.InitializeAsync();
        Assert.Equal(ScanScreenState.ScannerUnavailable, vm.State);

        engine.Reachable.Add(One);
        await vm.RefreshScannersCommand.ExecuteAsync();
        Assert.Equal(ScanScreenState.Ready, vm.State);
        Assert.True(vm.PreviewCommand.CanExecute(null));
    }

    [Fact]
    public async Task Refresh_recovers_a_scanner_whose_saved_connection_no_longer_answers()
    {
        var (registry, engine, _) = await Make.RegistryAsync(new[] { OneEscl }, Make.Entry(One, "First"));
        engine.Discoverable.Add(new DiscoveredScanner(OneEscl, "E", "M (escl:https://192.0.2.10:443)"));
        var vm = NewVm(engine, registry, One);
        await vm.InitializeAsync();
        Assert.Equal(ScanScreenState.Ready, vm.State);
        Assert.Equal(OneEscl, registry.Entries[0].DriverId);
    }

    [Fact]
    public async Task Switching_scanner_never_touches_pages_already_scanned()
    {
        var (vm, _, _) = await ReadyAsync();
        await vm.PreviewCommand.ExecuteAsync();
        await vm.ScanCommand.ExecuteAsync();
        await vm.PreviewCommand.ExecuteAsync();
        await vm.ScanCommand.ExecuteAsync();
        vm.ScannerIndex = 1;
        Assert.Equal(2, vm.Pages.Count);
    }
}
