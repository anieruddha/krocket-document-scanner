using Avalonia.Controls;

namespace KRocketDocumentScanner.App;

/// <summary>
/// Process-wide startup options parsed from command-line args once, before any window is
/// created — same "load once, read via x:Static everywhere" pattern as
/// <see cref="Theming.AppTheme"/> and <see cref="ViewModels.Localization.Strings"/>.
///
/// Default is the OS's own title bar on every window.
/// Pass <c>--no-system-titlebar</c> to get the custom-drawn, Nautilus-style header (no native
/// title bar) instead.
/// Pass <c>--lang fr</c> (a language code) to use that language's strings file if it exists;
/// otherwise the operating system's language is used.
/// Pass <c>--manage</c> to open only the scanner registry (Manage Scanners), with no scan window.
/// </summary>
public static class AppOptions
{
    private const string NoSystemTitleBarArg = "--no-system-titlebar";
    private const string ThemeArg = "--theme";
    private const string LangArg = "--lang";
    private const string ManageArg = "--manage";

    public static bool UseSystemTitleBar { get; private set; } = true;

    /// <summary>True when <c>--manage</c> was passed: the app opens Manage Scanners as its only
    /// window and closes when that window closes.</summary>
    public static bool ManageOnly { get; private set; }

    /// <summary>Raw value passed to <c>--theme</c>, if any — "dark", "light", or an inline
    /// theme JSON string (colors only, same shape as theme.light.json/theme.dark.json).
    /// Null means no override; Theming.AppTheme falls back to auto-detecting the OS's
    /// current light/dark setting. See Theming.AppTheme.LoadAndApply for how this is used.</summary>
    public static string? ThemeArgValue { get; private set; }

    /// <summary>Raw value passed to <c>--lang</c>, if any (for example "fr"); null means none.</summary>
    public static string? LangArgValue { get; private set; }

    /// <summary>What every window's SystemDecorations should be set to. BorderOnly, not
    /// None: with zero native decoration at all, the window manager has nothing to hang a
    /// frame/shadow on, so two custom-chrome windows sitting next to each other become
    /// visually indistinguishable from one another — confirmed by screenshot. BorderOnly
    /// keeps the OS's native border/shadow (and, as a real side effect, native edge-drag
    /// resize) while still dropping the title bar itself so our own header still applies.
    /// Fixed-size dialogs (ManageScannersWindow/AddScannerWindow) opt back out of the
    /// resize side effect via their own CanResize="False" — that property is independent
    /// of SystemDecorations.</summary>
    public static Avalonia.Controls.WindowDecorations WindowDecorations =>
        UseSystemTitleBar ? Avalonia.Controls.WindowDecorations.Full : Avalonia.Controls.WindowDecorations.BorderOnly;

    /// <summary>Whether the custom header row (drag region + our own window buttons) should
    /// be shown — the inverse of UseSystemTitleBar, given its own name because that's what
    /// XAML visibility bindings actually want to ask.</summary>
    public static bool ShowCustomChrome => !UseSystemTitleBar;

    /// <summary>The hairline drawn around every custom-chrome window's content (see the root
    /// Border in each window's XAML). None with the system title bar, where the OS already
    /// draws its own frame.</summary>
    public static Avalonia.Thickness WindowBorderThickness => ShowCustomChrome ? new Avalonia.Thickness(1) : new Avalonia.Thickness(0);

    public static void ParseArgs(string[] args)
    {
        UseSystemTitleBar = !args.Any(a => string.Equals(a, NoSystemTitleBarArg, StringComparison.OrdinalIgnoreCase));

        ManageOnly = args.Any(a => string.Equals(a, ManageArg, StringComparison.OrdinalIgnoreCase));

        var themeIndex = Array.FindIndex(args, a => string.Equals(a, ThemeArg, StringComparison.OrdinalIgnoreCase));
        if (themeIndex >= 0 && themeIndex + 1 < args.Length)
            ThemeArgValue = args[themeIndex + 1];

        var langIndex = Array.FindIndex(args, a => string.Equals(a, LangArg, StringComparison.OrdinalIgnoreCase));
        if (langIndex >= 0 && langIndex + 1 < args.Length)
            LangArgValue = args[langIndex + 1];
    }
}
