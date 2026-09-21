namespace KRocketDocumentScanner.App.Theming;

/// <summary>
/// Every themeable color, as a hex string — the whole of what's actually read from
/// theme.light.json/theme.dark.json (sizes are fixed; see ThemeSizes). Plain strings (not
/// SolidColorBrush) specifically so this class has no Avalonia dependency and can be
/// deserialized directly from JSON with System.Text.Json — colors get parsed into brushes
/// only when applied (see AppTheme.Apply).
/// </summary>
public sealed class ThemeConfig
{
    public string Background { get; set; } = "#FFFFFFFF";
    public string Surface { get; set; } = "#FFFFFFFF";
    public string SurfaceSubtle { get; set; } = "#14000000";
    public string Border { get; set; } = "#22000000";
    public string BorderStrong { get; set; } = "#33000000";
    public string TextPrimary { get; set; } = "#FF1A1A1A";
    public string TextSecondary { get; set; } = "#B3000000";
    public string TextMuted { get; set; } = "#80000000";
    public string Accent { get; set; } = "#FF2E6BE6";
    public string AccentText { get; set; } = "#FFFFFFFF";
    public string Danger { get; set; } = "#FFC0392B";
    public string Ready { get; set; } = "#FF2EA043";
    public string NotReady { get; set; } = "#FF9A9A9A";

    /// <summary>Overlay dimming the part of the scan preview outside the crop rectangle.</summary>
    public string CropDim { get; set; } = "#73000000";

    /// <summary>Colour of a document page itself (scanned-page thumbnails) and the soft
    /// shadow around a page — a page is paper, whatever the app theme is.</summary>
    public string Paper { get; set; } = "#FFFFFFFF";
    public string PageShadow { get; set; } = "#40000000";

    // Deliberately its own pair of colors, not reused from Surface/SurfaceSubtle — that's
    // already used for ScanWindow's preview pane background, so reusing it for the
    // toolbar/header made a focused window's toolbar and its own preview pane render in the
    // identical color, and an unfocused window's toolbar blend into the preview pane too. A
    // light accent tint for the focused toolbar doubles as a focus cue; a plain,
    // slightly-more-saturated gray for unfocused reads as receded without matching any
    // content-area color.
    public string Toolbar { get; set; } = "#FFE8EFFC";
    public string ToolbarInactive { get; set; } = "#FFE4E4E7";
}

/// <summary>
/// Every size/spacing value the app uses. Kept as a flat set of named values (a spacing scale
/// plus a few one-off sizes) rather than per-control dimensions, so the same handful of
/// values are reused consistently across the app instead of each screen inventing its own.
/// Deliberately NOT user-reskinnable like <see cref="ThemeConfig"/>'s colors — these are
/// fixed, built-in constants (see AppTheme.Apply), not read from
/// theme.light.json/theme.dark.json. Layout/spacing changes are a code change, not a config
/// one; only colors are meant to be user-tweakable without a rebuild.
/// </summary>
public sealed class ThemeSizes
{
    public double SpacingXs { get; set; } = 4;
    public double SpacingSm { get; set; } = 8;
    public double SpacingMd { get; set; } = 12;
    public double SpacingLg { get; set; } = 16;
    public double SpacingXl { get; set; } = 20;
    public double SpacingXxl { get; set; } = 24;

    public double CornerRadiusSm { get; set; } = 6;
    public double CornerRadiusMd { get; set; } = 8;

    public double StatusDotSize { get; set; } = 9;
    public double ThumbnailHeight { get; set; } = 220;
    public double ToolbarButtonSize { get; set; } = 34;
}
