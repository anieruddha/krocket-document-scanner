using Avalonia.Controls;

namespace KRocketDocumentScanner.App;

public static class AppOptions
{
    private const string NoSystemTitleBarArg = "--no-system-titlebar";
    private const string ThemeArg = "--theme";
    private const string LangArg = "--lang";
    private const string ManageArg = "--manage";

    public static bool UseSystemTitleBar { get; private set; } = true;

    public static bool ManageOnly { get; private set; }

    public static string? ThemeArgValue { get; private set; }

    public static string? LangArgValue { get; private set; }

    public static Avalonia.Controls.WindowDecorations WindowDecorations =>
        UseSystemTitleBar ? Avalonia.Controls.WindowDecorations.Full : Avalonia.Controls.WindowDecorations.BorderOnly;

    public static bool ShowCustomChrome => !UseSystemTitleBar;

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
