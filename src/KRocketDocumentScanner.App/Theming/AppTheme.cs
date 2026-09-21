using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace KRocketDocumentScanner.App.Theming;

public static class AppTheme
{
    public static ThemeConfig Current { get; private set; } = new();

    public static ThemeSizes Sizes { get; } = new();

    public static event Action? Applied;

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
            config = new ThemeConfig();
        }

        ApplyConfig(config, variant);
    }

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
