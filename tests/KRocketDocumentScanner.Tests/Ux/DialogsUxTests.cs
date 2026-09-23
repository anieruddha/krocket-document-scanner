using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using KRocketDocumentScanner.App;
using KRocketDocumentScanner.App.ViewModels;
using KRocketDocumentScanner.App.ViewModels.Localization;
using KRocketDocumentScanner.Tests.Support;
using Xunit;

namespace KRocketDocumentScanner.Tests.Ux;

[Trait("Category", "UX")]
public class DialogsUxTests
{
    private static ConfirmCloseWindow Show(int pages = 0, bool busy = false, bool preview = false)
    {
        var w = new ConfirmCloseWindow(pages, busy, preview);
        w.Show();
        UxHost.Flush();
        return w;
    }

    [AvaloniaFact]
    public void Close_confirmation_has_close_anyway_on_the_left_and_cancel_on_the_right_in_the_primary_colour()
    {
        var w = Show(pages: 2);
        var close = UxHost.ButtonWith(w, Strings.DiscardAndCloseButton)!;
        var cancel = UxHost.ButtonWith(w, Strings.CancelButton)!;
        Assert.Equal("Close Anyway", Strings.DiscardAndCloseButton);
        Assert.Equal(0, Grid.GetColumn(close));
        Assert.Equal(2, Grid.GetColumn(cancel));
        Assert.Contains("accent", cancel.Classes);
        Assert.DoesNotContain("accent", close.Classes);
        w.Close();
    }

    [AvaloniaFact]
    public void Close_confirmation_has_exactly_two_action_buttons()
    {
        var w = Show(pages: 1);
        var texts = UxHost.All<Button>(w).Select(UxHost.TextOf).Where(t => t is "Close Anyway" or "Cancel").ToList();
        Assert.Equal(new[] { "Close Anyway", "Cancel" }, texts);
        w.Close();
    }

    [AvaloniaFact]
    public void The_message_matches_what_would_be_lost()
    {
        var pages = Show(pages: 3);
        Assert.Contains(string.Format(Strings.UnsavedPagesMessage, Strings.Pages(3)), UxHost.Texts(pages));
        pages.Close();

        var preview = Show(preview: true);
        Assert.Contains(Strings.PreviewNotScannedMessage, UxHost.Texts(preview));
        Assert.Equal(Strings.PreviewNotScannedTitle, preview.Title);
        preview.Close();

        var busy = Show(busy: true);
        Assert.Contains(Strings.ScannerBusyMessage, UxHost.Texts(busy));
        Assert.Equal(Strings.ScannerBusyTitle, busy.Title);
        busy.Close();

        var both = Show(pages: 2, busy: true);
        Assert.Contains(string.Format(Strings.ScannerBusyWithPagesMessage, Strings.Pages(2)), UxHost.Texts(both));
        both.Close();
    }

    [AvaloniaFact]
    public void Only_the_close_anyway_button_confirms()
    {
        var confirm = Show(pages: 1);
        Assert.False(confirm.Confirmed);
        UxHost.ButtonWith(confirm, Strings.DiscardAndCloseButton)!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        UxHost.Flush();
        Assert.True(confirm.Confirmed);

        var cancel = Show(pages: 1);
        UxHost.ButtonWith(cancel, Strings.CancelButton)!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        UxHost.Flush();
        Assert.False(cancel.Confirmed);
    }

    [AvaloniaFact]
    public void The_message_is_short_enough_to_be_read()
    {
        foreach (var text in new[]
        {
            string.Format(Strings.UnsavedPagesMessage, Strings.Pages(2)), Strings.PreviewNotScannedMessage,
            Strings.ScannerBusyMessage, string.Format(Strings.ScannerBusyWithPagesMessage, Strings.Pages(2)),
        })
            Assert.True(text.Split(' ').Length <= 25, text);
    }

    [AvaloniaFact]
    public async Task The_unavailable_popup_has_a_single_manage_scanners_button()
    {
        var (registry, engine, _) = await Make.RegistryAsync(null, Make.Entry("usb:1"));
        var vm = new ScanViewModel(engine, registry, "usb:1", _ => Task.FromResult(false), () => Task.CompletedTask, () => { });
        await vm.InitializeAsync();
        var w = new ScannerUnavailableWindow(vm);
        w.Show();
        UxHost.Flush();

        var buttons = UxHost.All<Button>(w).Select(UxHost.TextOf).Where(t => t == Strings.ManageScannerButton).ToList();
        Assert.Single(buttons);
        Assert.Contains(vm.UnavailableMessage, UxHost.Texts(w));
        w.Close();
    }
}
