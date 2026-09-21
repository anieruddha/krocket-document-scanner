namespace KRocketDocumentScanner.Core.Settings;

/// <summary>
/// The built-in default for every setting. The settings file is created from these, and they
/// are used whenever the file gives no valid value.
/// </summary>
public static class SettingsDefaults
{
    /// <summary>Longest a single scanner network call may take.</summary>
    public static readonly TimeSpan NetworkCallTimeout = TimeSpan.FromSeconds(45);
}
