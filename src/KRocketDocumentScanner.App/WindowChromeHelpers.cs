using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using KRocketDocumentScanner.App.Theming;

namespace KRocketDocumentScanner.App;

public static class WindowChromeHelpers
{
    public static void ToggleMaximize(this Window window) =>
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

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
