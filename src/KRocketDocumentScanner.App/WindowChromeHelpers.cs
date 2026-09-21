using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using KRocketDocumentScanner.App.Theming;

namespace KRocketDocumentScanner.App;

/// <summary>
/// Shared behavior for the custom Nautilus-style header every window now draws instead of a
/// native title bar (see <see cref="AppOptions"/>): dragging empty header space moves the
/// window, and — for windows that support it — double-clicking toggles maximize/restore.
/// Deliberately small and shared rather than copy-pasted per window, since the logic itself
/// (not the surrounding XAML, which differs per window) is identical everywhere it's used.
/// </summary>
public static class WindowChromeHelpers
{
    public static void ToggleMaximize(this Window window) =>
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>Wire this to a header container's PointerPressed. Buttons placed on top of
    /// that header (minimize/maximize/close, or anything else) consume their own press and
    /// never reach this handler, so it only fires for genuinely empty header space.
    ///
    /// The header itself must have a real Background (even Transparent) for this to ever
    /// fire on empty space in the first place — Avalonia (like WPF) doesn't hit-test a
    /// Border with no Background at all; clicks on the gaps between its children just pass
    /// through instead of registering here.</summary>
    public static void OnHeaderPointerPressed(this Window window, PointerPressedEventArgs e, bool supportsMaximizeToggle)
    {
        if (!e.GetCurrentPoint(window).Properties.IsLeftButtonPressed) return;

        if (supportsMaximizeToggle && e.ClickCount == 2)
        {
            window.ToggleMaximize();
            return;
        }

        window.BeginMoveDrag(e);
    }

    /// <summary>
    /// Swaps <paramref name="header"/>'s background between the theme's dedicated toolbar
    /// color (focused) and its inactive variant (unfocused) — see ThemeConfig.Toolbar
    /// / ToolbarInactive — the visual differentiation between two otherwise-identical
    /// custom-chrome windows. Two things this deliberately does NOT reuse: Opacity (a first
    /// attempt at this dimmed the header's Opacity instead; against this app's light theme,
    /// where the header only ever contains a few small icon glyphs on an already-near-white
    /// background, that difference was too subtle to actually notice), and Surface/
    /// SurfaceSubtle (a second attempt used those, but they're already the background color
    /// of ScanWindow's preview pane — so the toolbar ended up the same color as nearby
    /// content instead of standing apart from it, which was the whole point). The toolbar
    /// gets its own color pair for exactly this reason.
    /// Sets the correct initial state immediately (a window doesn't necessarily start
    /// active), then reacts to Activated/Deactivated from then on. Falls back to
    /// Transparent if the theme resources aren't found, so the header never ends up with a
    /// null Background — that would silently break the drag-to-move hit-testing this same
    /// header depends on (see OnHeaderPointerPressed).
    ///
    /// Also re-resolves on every AppTheme.Applied, not just on focus change — deliberately
    /// NOT a one-time FindResource snapshot. AppTheme's OS dark/light detection on Linux
    /// resolves asynchronously (a D-Bus round-trip — see AppTheme's own doc comment), so the
    /// correct theme can land after this window already exists; without re-resolving here,
    /// this header would silently keep whatever (possibly wrong) brush it grabbed at
    /// construction time even after the rest of the app — which uses genuinely live
    /// {DynamicResource} bindings in XAML — updates correctly. Unsubscribes on Closed so a
    /// closed window's header/handler isn't kept alive for the rest of the process by this
    /// static, app-lifetime event.
    /// </summary>
    public static void AttachActiveStateDimming(this Window window, Border header)
    {
        void ApplyHeaderColor(bool active)
        {
            var key = active ? "ThemeToolbarBrush" : "ThemeToolbarInactiveBrush";
            header.Background = Application.Current?.FindResource(key) as IBrush ?? Brushes.Transparent;
        }

        ApplyHeaderColor(window.IsActive);
        window.Activated += (_, _) => ApplyHeaderColor(true);
        window.Deactivated += (_, _) => ApplyHeaderColor(false);

        void OnThemeApplied() => ApplyHeaderColor(window.IsActive);
        AppTheme.Applied += OnThemeApplied;
        window.Closed += (_, _) => AppTheme.Applied -= OnThemeApplied;
    }
}
