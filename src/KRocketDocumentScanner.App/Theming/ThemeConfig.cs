namespace KRocketDocumentScanner.App.Theming;

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

    public string CropDim { get; set; } = "#73000000";

    public string Paper { get; set; } = "#FFFFFFFF";
    public string PageShadow { get; set; } = "#40000000";

    public string Toolbar { get; set; } = "#FFE8EFFC";
    public string ToolbarInactive { get; set; } = "#FFE4E4E7";
}

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
