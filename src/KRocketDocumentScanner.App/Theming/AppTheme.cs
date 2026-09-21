using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace KRocketDocumentScanner.App.Theming;

/// <summary>
/// Loads a theme's colors from an external JSON file (not compiled in) and applies them,
/// alongside a fixed set of built-in sizes (see ThemeSizes), to Application.Current.Resources
/// as DynamicResource-bindable brushes/values, so XAML across the app reads
/// {DynamicResource ThemeXxx} instead of hardcoded colors/sizes. Reskinning the app's colors
/// is editing theme.light.json / theme.dark.json and relaunching — no rebuild needed. Sizes
/// are not reskinnable this way — changing them is a code change (ThemeSizes' defaults).
///
/// Picks light or dark based on the OS's current setting, and reacts live if it changes while
/// running (via IPlatformSettings.ColorValuesChanged) — not just at startup — UNLESS the
/// --theme CLI arg (see AppOptions.ThemeArgValue) explicitly overrides it with "dark",
/// "light", or an inline theme JSON string, in which case that fixed choice sticks for the
/// whole run; see LoadAndApply. That live-update
/// path matters for more than "the user flips their OS theme while the app is open": on Linux,
/// IPlatformSettings.GetColorValues() is backed by an async D-Bus round-trip to the
/// org.freedesktop.portal.Settings portal (confirmed by reading Avalonia.FreeDesktop's
/// DBusPlatformSettings source), so the FIRST read of it — the one LoadAndApply() does at
/// startup — can easily happen before that round-trip has completed, silently returning a
/// default (light) regardless of the OS's real setting. ColorValuesChanged is what eventually
/// delivers the real value once the D-Bus response lands, typically a moment after startup —
/// without subscribing to it, that correct value never arrives and the app is stuck on
/// whatever the early, likely-wrong guess was. Subscribing here fixes both the startup race
/// and genuine runtime theme switches with the same one mechanism.
/// </summary>
public static class AppTheme
{
    public static ThemeConfig Current { get; private set; } = new();

    /// <summary>Fixed, built-in sizes — not read from theme.light.json/theme.dark.json (only
    /// colors are user-reskinnable; see ThemeConfig).</summary>
    public static ThemeSizes Sizes { get; } = new();

    /// <summary>Raised every time theme resources are (re)applied — not just on the initial
    /// load. Anything that resolves a Theme brush itself in C# (rather than via a XAML
    /// {DynamicResource}, which re-resolves live automatically) needs to subscribe to this
    /// and re-resolve, or it'll stay stuck on whatever was resolved the first time —
    /// including the startup race value described above. See
    /// WindowChromeHelpers.AttachActiveStateDimming for the concrete case that bit us.</summary>
    public static event Action? Applied;

    /// <summary>--theme dark/light forces that variant (file-based, same as auto-detection
    /// would load) and does NOT subscribe to OS theme changes — an explicit override should
    /// stick, not get silently replaced the moment the OS setting changes. Any other --theme
    /// value is treated as an inline theme JSON string (colors only) applied directly,
    /// bypassing the theme.light.json/theme.dark.json files entirely; the OS's current
    /// light/dark setting is still used for Avalonia's own base FluentTheme chrome (an
    /// orthogonal concern from these custom colors), same as when there's no override.
    /// With no --theme at all: auto-detect and react live, as before.</summary>
    public static void LoadAndApply()
    {
        var themeArg = AppOptions.ThemeArgValue;
        if (themeArg is not null)
        {
            if (TryParseVariantKeyword(themeArg, out var forced))
                LoadAndApplyForVariant(forced);
            else
                ApplyConfig(ParseConfigJsonStrict(themeArg), DetectOsThemeVariant());
            return;
        }

        LoadAndApplyForVariant(DetectOsThemeVariant());

        if (Application.Current?.PlatformSettings is { } settings)
            settings.ColorValuesChanged += (_, values) => LoadAndApplyForVariant(values.ThemeVariant);
    }

    private static bool TryParseVariantKeyword(string value, out PlatformThemeVariant variant)
    {
        if (string.Equals(value, "dark", StringComparison.OrdinalIgnoreCase)) { variant = PlatformThemeVariant.Dark; return true; }
        if (string.Equals(value, "light", StringComparison.OrdinalIgnoreCase)) { variant = PlatformThemeVariant.Light; return true; }
        variant = default;
        return false;
    }

    private static void LoadAndApplyForVariant(PlatformThemeVariant variant)
    {
        var fileName = variant == PlatformThemeVariant.Dark ? "theme.dark.json" : "theme.light.json";
        var path = Path.Combine(AppContext.BaseDirectory, "Theming", fileName);

        ThemeConfig config;
        try
        {
            config = ParseConfigJsonStrict(File.ReadAllText(path));
        }
        catch
        {
            // Missing/corrupt theme.light.json/theme.dark.json must never prevent the app
            // from starting (the user didn't type this JSON themselves, and can't fix a typo
            // mid-launch) — fall back to the built-in defaults on ThemeConfig/ThemeSizes.
            // Contrast with a bad --theme CLI string, which IS the user's own input and is
            // deliberately NOT forgiven this way — see LoadAndApply.
            config = new ThemeConfig();
        }

        ApplyConfig(config, variant);
    }

    /// <summary>Throws on invalid JSON rather than falling back to defaults — used for the
    /// --theme CLI argument specifically. An explicit, user-typed --theme value getting
    /// silently ignored on a typo would be far more confusing than a clear startup failure;
    /// contrast with theme.light.json/theme.dark.json (LoadAndApplyForVariant), which are
    /// forgiven since the user isn't the one typing that JSON at launch time.</summary>
    private static ThemeConfig ParseConfigJsonStrict(string json)
    {
        var config = JsonSerializer.Deserialize<ThemeConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        if (config is null)
            throw new JsonException("Theme JSON deserialized to null (was the input just \"null\" or empty?).");
        return config;
    }

    private static void ApplyConfig(ThemeConfig config, PlatformThemeVariant baseVariant)
    {
        Current = config;
        Apply(config);

        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = baseVariant == PlatformThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

        Applied?.Invoke();
    }

    private static PlatformThemeVariant DetectOsThemeVariant()
    {
        try
        {
            var settings = Application.Current?.PlatformSettings;
            var variant = settings?.GetColorValues().ThemeVariant;
            if (variant is not null) return variant.Value;
        }
        catch
        {
            // Detection failing (unusual platform, headless test runner, etc.) falls back to
            // Light below — never let theme detection itself crash startup. If this guess
            // turns out to be wrong because the real value just hadn't arrived yet (see the
            // class doc comment), ColorValuesChanged corrects it shortly after.
        }
        return PlatformThemeVariant.Light;
    }

    private static void Apply(ThemeConfig config)
    {
        if (Application.Current is null) return;
        var res = Application.Current.Resources;

        void SetBrush(string key, string hex) => res[key] = new SolidColorBrush(Color.Parse(hex));
        void SetDouble(string key, double value) => res[key] = value;
        void SetThickness(string key, double horizontal, double vertical) => res[key] = new Thickness(horizontal, vertical);
        void SetCornerRadius(string key, double value) => res[key] = new CornerRadius(value);

        var c = config;
        SetBrush("ThemeBackgroundBrush", c.Background);
        SetBrush("ThemeSurfaceBrush", c.Surface);
        SetBrush("ThemeSurfaceSubtleBrush", c.SurfaceSubtle);
        SetBrush("ThemeBorderBrush", c.Border);
        SetBrush("ThemeBorderStrongBrush", c.BorderStrong);
        SetBrush("ThemeTextPrimaryBrush", c.TextPrimary);
        SetBrush("ThemeTextSecondaryBrush", c.TextSecondary);
        SetBrush("ThemeTextMutedBrush", c.TextMuted);
        SetBrush("ThemeAccentBrush", c.Accent);
        SetBrush("ThemeAccentTextBrush", c.AccentText);
        SetBrush("ThemeDangerBrush", c.Danger);
        SetBrush("ThemeReadyBrush", c.Ready);
        SetBrush("ThemeNotReadyBrush", c.NotReady);
        SetBrush("ThemeCropDimBrush", c.CropDim);
        SetBrush("ThemePaperBrush", c.Paper);
        SetBrush("ThemeToolbarBrush", c.Toolbar);
        SetBrush("ThemeToolbarInactiveBrush", c.ToolbarInactive);

        var s = Sizes;
        SetDouble("ThemeSpacingXs", s.SpacingXs);
        SetDouble("ThemeSpacingSm", s.SpacingSm);
        SetDouble("ThemeSpacingMd", s.SpacingMd);
        SetDouble("ThemeSpacingLg", s.SpacingLg);
        SetDouble("ThemeSpacingXl", s.SpacingXl);
        SetDouble("ThemeSpacingXxl", s.SpacingXxl);
        SetCornerRadius("ThemeCornerRadiusSm", s.CornerRadiusSm);
        SetCornerRadius("ThemeCornerRadiusMd", s.CornerRadiusMd);
        SetDouble("ThemeStatusDotSize", s.StatusDotSize);
        SetDouble("ThemeThumbnailHeight", s.ThumbnailHeight);
        SetDouble("ThemeToolbarButtonSize", s.ToolbarButtonSize);
        SetThickness("ThemeToolbarPadding", s.SpacingMd, s.SpacingSm);
    }
}
