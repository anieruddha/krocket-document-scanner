using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using KRocketDocumentScanner.App;
using KRocketDocumentScanner.App.ViewModels;
using KRocketDocumentScanner.App.ViewModels.Localization;
using KRocketDocumentScanner.Tests.Support;
using Xunit;

namespace KRocketDocumentScanner.Tests.Ux;

[Trait("Category", "UX")]
public class ManageAndAddWindowsUxTests
{
    private static async Task<(ManageScannersWindow W, ManageScannersViewModel Vm, FakeEngine Engine)> ManageAsync(params string[] ids)
    {
        var (registry, engine, _) = await Make.RegistryAsync(ids, ids.Select(i => Make.Entry(i, "Scanner " + i)).ToArray());
        UxHost.UseRegistry(registry);
        var w = new ManageScannersWindow();
        w.Show();
        UxHost.Flush();
        return (w, (ManageScannersViewModel)w.DataContext!, engine);
    }

    [AvaloniaFact]
    public async Task The_footer_has_refresh_on_the_left_and_add_scanner_on_the_right_in_the_primary_colour()
    {
        var (w, _, _) = await ManageAsync("usb:1", "usb:2");
        var refresh = UxHost.ButtonWith(w, Strings.RefreshStatusButton)!;
        var add = UxHost.ButtonWith(w, Strings.AddScannerButton)!;
        Assert.Equal(0, Grid.GetColumn(refresh));
        Assert.Equal(2, Grid.GetColumn(add));
        Assert.Contains("accent", add.Classes);
        Assert.True(refresh.IsEffectivelyVisible && add.IsEffectivelyVisible);
        w.Close();
    }

    [AvaloniaFact]
    public async Task With_no_scanners_the_footer_is_hidden_and_a_single_add_button_is_offered()
    {
        var (w, vm, _) = await ManageAsync();
        Assert.True(vm.IsEmpty);
        var footerAdd = UxHost.ButtonWith(w, Strings.AddScannerButton);
        Assert.True(footerAdd is null || !footerAdd.IsEffectivelyVisible);
        Assert.True(UxHost.ButtonWith(w, Strings.AddScannerEmptyStateButton)!.IsEffectivelyVisible);
        w.Close();
    }

    [AvaloniaFact]
    public async Task An_activity_bar_shows_while_refreshing()
    {
        var (w, vm, engine) = await ManageAsync("usb:1");
        engine.ReachableGate = new TaskCompletionSource<bool>();
        var bar = UxHost.All<ProgressBar>(w).First(p => p.IsIndeterminate && p.Height <= 4);
        Assert.False(bar.IsEffectivelyVisible);

        var run = vm.RefreshCommand.ExecuteAsync();
        await UxHost.PumpAsync(() => vm.IsBusy);
        Assert.True(bar.IsEffectivelyVisible);

        engine.ReachableGate.SetResult(true);
        await run;
        await UxHost.PumpAsync(() => !vm.IsBusy);
        Assert.False(bar.IsEffectivelyVisible);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Each_saved_scanner_is_listed_by_name()
    {
        var (w, _, _) = await ManageAsync("usb:1", "usb:2");
        var texts = UxHost.Texts(w).ToList();
        Assert.Contains("Scanner usb:1", texts);
        Assert.Contains("Scanner usb:2", texts);
        w.Close();
    }

    private static async Task<(AddScannerWindow W, AddScannerViewModel Vm, FakeEngine Engine)> AddAsync()
    {
        var (registry, engine, _) = await Make.RegistryAsync();
        UxHost.UseRegistry(registry);
        var w = new AddScannerWindow();
        w.Show();
        UxHost.Flush();
        return (w, (AddScannerViewModel)w.DataContext!, engine);
    }

    private static void OpenManualTab(AddScannerWindow w)
    {
        var tabs = UxHost.All<TabControl>(w).First();
        tabs.SelectedIndex = 1;
        UxHost.Flush();
    }

    [AvaloniaFact]
    public async Task Discovery_starts_by_itself_and_shows_a_searching_indicator_while_it_runs()
    {
        var (registry, engine, _) = await Make.RegistryAsync();
        engine.DelayMs = 300;
        UxHost.UseRegistry(registry);
        var w = new AddScannerWindow();
        w.Show();
        var vm = (AddScannerViewModel)w.DataContext!;
        await UxHost.PumpAsync(() => vm.IsSearching);
        Assert.Contains(Strings.SearchingText, UxHost.Texts(w));
        await UxHost.PumpAsync(() => !vm.IsSearching);
        w.Close();
    }

    [AvaloniaFact]
    public async Task The_manual_tab_has_only_an_address_field_and_no_help_line_or_name_field()
    {
        var (w, _, _) = await AddAsync();
        OpenManualTab(w);
        var boxes = UxHost.All<TextBox>(w).Where(b => b.IsEffectivelyVisible).ToList();
        Assert.Single(boxes);
        Assert.Equal(Strings.AddressWatermark, boxes[0].PlaceholderText);
        Assert.DoesNotContain("Add a network scanner by its address", UxHost.Texts(w));
        Assert.DoesNotContain(UxHost.Texts(w), t => t.Contains("Scanner Name"));
        w.Close();
    }

    [AvaloniaFact]
    public async Task Add_scanner_is_enabled_as_soon_as_an_address_is_typed()
    {
        var (w, vm, _) = await AddAsync();
        OpenManualTab(w);
        var add = UxHost.All<Button>(w).Where(b => UxHost.TextOf(b) == Strings.AddScannerActionButton && b.IsEffectivelyVisible).First();
        Assert.False(add.IsEffectivelyEnabled);
        UxHost.All<TextBox>(w).First(b => b.IsEffectivelyVisible).Text = "192.0.2.10";
        UxHost.Flush();
        Assert.True(add.IsEffectivelyEnabled);
        Assert.True(vm.CanAddManual);
        w.Close();
    }

    [AvaloniaFact]
    public async Task A_rejected_address_shows_a_message_that_names_it()
    {
        var (w, vm, _) = await AddAsync();
        OpenManualTab(w);
        vm.ManualAddress = "192.0.2.999";
        await vm.AddManualCommand.ExecuteAsync();
        UxHost.Flush();
        Assert.Contains(UxHost.Texts(w), t => t.Contains("192.0.2.999"));
        w.Close();
    }

    [AvaloniaFact]
    public async Task While_an_address_is_checked_a_progress_bar_and_checking_text_show()
    {
        var (w, vm, engine) = await AddAsync();
        OpenManualTab(w);
        engine.ReachableGate = new TaskCompletionSource<bool>();
        vm.ManualAddress = "192.0.2.11";
        var run = vm.AddManualCommand.ExecuteAsync();
        await UxHost.PumpAsync(() => vm.IsAddingManual);
        Assert.Contains(Strings.CheckingScannerStatus, UxHost.Texts(w));
        engine.ReachableGate.SetResult(false);
        await run;
        await UxHost.PumpAsync(() => !vm.IsAddingManual);
        w.Close();
    }
}
