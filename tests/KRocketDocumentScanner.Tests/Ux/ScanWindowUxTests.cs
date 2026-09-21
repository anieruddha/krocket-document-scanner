using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using KRocketDocumentScanner.App;
using KRocketDocumentScanner.App.ViewModels;
using KRocketDocumentScanner.App.ViewModels.Localization;
using KRocketDocumentScanner.Tests.Support;
using Xunit;

namespace KRocketDocumentScanner.Tests.Ux;

[Trait("Category", "UX")]
public class ScanWindowUxTests
{
    private const string One = "escl:https://192.0.2.10:443";
    private const string Two = "usb:two";

    private static async Task<(ScanWindow Window, ScanViewModel Vm, FakeEngine Engine, KRocketDocumentScanner.Core.Registry.ScannerRegistryManager Registry)> OpenAsync(
        bool oneReachable = true, TaskCompletionSource<bool>? gate = null)
    {
        var (registry, engine, _) = await Make.RegistryAsync(oneReachable ? new[] { One, Two } : null,
            Make.Entry(One, "First"), Make.Entry(Two, "Second"));
        engine.ReachableGate = gate;
        UxHost.UseRegistry(registry);
        var window = new ScanWindow(One);
        var vm = UxHost.InjectViewModel(window, engine, registry, One);
        window.Show();
        UxHost.Flush();
        return (window, vm, engine, registry);
    }

    private static Button Preview(ScanWindow w) => UxHost.ButtonWith(w, Strings.PreviewButton)!;
    private static Button Scan(ScanWindow w) => UxHost.ButtonWith(w, Strings.ScanActionButton)!;

    [AvaloniaFact]
    public async Task Preview_and_scan_are_disabled_while_the_scanner_is_being_checked_then_enabled()
    {
        var gate = new TaskCompletionSource<bool>();
        var (window, vm, _, _) = await OpenAsync(gate: gate);
        await UxHost.PumpAsync(() => vm.State == ScanScreenState.Checking && vm.StatusMessage is not null);

        Assert.NotNull(Preview(window));
        Assert.False(Preview(window).IsEffectivelyEnabled);
        Assert.False(Scan(window).IsEffectivelyEnabled);

        gate.SetResult(true);
        await UxHost.PumpAsync(() => vm.State == ScanScreenState.Ready);
        Assert.True(Preview(window).IsEffectivelyEnabled);
        Assert.True(Scan(window).IsEffectivelyEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Preview_and_scan_stay_disabled_when_the_scanner_is_unreachable()
    {
        var (window, vm, _, _) = await OpenAsync(oneReachable: false);
        await UxHost.PumpAsync(() => vm.State == ScanScreenState.ScannerUnavailable);
        Assert.False(Preview(window).IsEffectivelyEnabled);
        Assert.False(Scan(window).IsEffectivelyEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public async Task An_activity_bar_shows_only_while_the_app_waits_on_the_scanner()
    {
        var gate = new TaskCompletionSource<bool>();
        var (window, vm, _, _) = await OpenAsync(gate: gate);
        await UxHost.PumpAsync(() => vm.IsWorking);
        var bar = UxHost.All<ProgressBar>(window).First(p => p.IsIndeterminate && p.Height <= 4);
        Assert.True(bar.IsEffectivelyVisible);

        gate.SetResult(true);
        await UxHost.PumpAsync(() => !vm.IsWorking);
        Assert.False(bar.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_status_line_says_checking_scanner_during_the_check()
    {
        var gate = new TaskCompletionSource<bool>();
        var (window, vm, _, _) = await OpenAsync(gate: gate);
        await UxHost.PumpAsync(() => vm.StatusMessage == Strings.CheckingScannerStatus);
        Assert.Contains(Strings.CheckingScannerStatus, UxHost.Texts(window));
        gate.SetResult(true);
        await UxHost.PumpAsync(() => vm.State == ScanScreenState.Ready);
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_scanner_list_has_a_refresh_button_beside_it_with_a_tooltip()
    {
        var (window, vm, _, _) = await OpenAsync();
        await UxHost.PumpAsync(() => vm.State == ScanScreenState.Ready);
        var refresh = UxHost.All<Button>(window).First(b => b.Content as string == "⟳");
        Assert.True(refresh.IsEffectivelyEnabled);
        Assert.Equal(Strings.RefreshStatusButton, Avalonia.Controls.ToolTip.GetTip(refresh) as string);
        var combo = UxHost.All<ComboBox>(window).First(c => c.ItemsSource is System.Collections.IEnumerable e && e.Cast<object>().Count() == 2);
        Assert.Equal(2, combo.ItemCount);
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_refresh_button_is_disabled_while_work_is_running()
    {
        var gate = new TaskCompletionSource<bool>();
        var (window, vm, _, _) = await OpenAsync(gate: gate);
        await UxHost.PumpAsync(() => vm.IsWorking);
        var refresh = UxHost.All<Button>(window).First(b => b.Content as string == "⟳");
        Assert.False(refresh.IsEffectivelyEnabled);
        gate.SetResult(true);
        await UxHost.PumpAsync(() => vm.State == ScanScreenState.Ready);
        Assert.True(refresh.IsEffectivelyEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_preset_list_ends_with_custom()
    {
        var (window, vm, _, _) = await OpenAsync();
        await UxHost.PumpAsync(() => vm.State == ScanScreenState.Ready);
        var combo = UxHost.All<ComboBox>(window).First(c => c.ItemCount == ScanViewModel.PagePresetChoices.Count);
        Assert.Equal("Custom", combo.Items.Cast<object>().Last());
        window.Close();
    }

    [AvaloniaFact]
    public async Task Switching_scanner_in_the_dropdown_clears_the_preview_but_not_the_pages()
    {
        var (window, vm, _, _) = await OpenAsync();
        await UxHost.PumpAsync(() => vm.State == ScanScreenState.Ready);
        await vm.PreviewCommand.ExecuteAsync();
        await vm.ScanCommand.ExecuteAsync();
        await vm.PreviewCommand.ExecuteAsync();
        UxHost.Flush();
        Assert.True(vm.HasPreview);

        var combo = UxHost.All<ComboBox>(window).First(c => c.ItemCount == 2);
        combo.SelectedIndex = 1;
        await UxHost.PumpAsync(() => !vm.HasPreview);
        Assert.Equal(1, vm.PageCount);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_window_with_nothing_to_lose_closes_straight_away()
    {
        var (window, vm, _, _) = await OpenAsync();
        await UxHost.PumpAsync(() => vm.State == ScanScreenState.Ready);
        window.Close();
        UxHost.Flush();
        Assert.False(window.IsVisible);
    }
}
