using System.Globalization;
using KRocketDocumentScanner.Core.Registry;
using KRocketDocumentScanner.Core.Settings;
using Tomlyn;
using Tomlyn.Model;

namespace KRocketDocumentScanner.App;

/// <summary>
/// The hand-editable settings file (~/.config/krocketdocumentscanner/settings.toml, next to scanners.json).
/// The defaults live here in code. At start-up <see cref="LoadOrCreate"/> reads the file; if
/// it is missing, or is not valid, it writes a new one holding the defaults (a file that was
/// there is first kept as settings.toml.bad). Read-only otherwise: nothing edits it while the
/// app runs, and a settings problem never stops start-up.
/// </summary>
public sealed class AppSettings
{
    private const string NetworkTimeoutKey = "network_timeout_seconds";

    /// <summary>Longest a single scanner network call may take.</summary>
    public TimeSpan NetworkTimeout { get; private init; } = SettingsDefaults.NetworkCallTimeout;

    public static string FilePath() =>
        Path.Combine(Path.GetDirectoryName(JsonScannerRegistryStore.DefaultFilePath())!, "settings.toml");

    public static AppSettings LoadOrCreate()
    {
        var defaults = new AppSettings();
        try
        {
            var path = FilePath();
            if (File.Exists(path))
            {
                if (TryRead(path, out var loaded)) return loaded;
                File.Move(path, path + ".bad", overwrite: true);
            }
            Write(path, defaults);
        }
        catch
        {
            // Unwritable folder, file in use, and so on: run on the defaults.
        }
        return defaults;
    }

    private static bool TryRead(string path, out AppSettings settings)
    {
        settings = new AppSettings();
        try
        {
            var table = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(path));
            if (table is null || !table.TryGetValue(NetworkTimeoutKey, out var raw)) return false;

            var seconds = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            if (!(seconds > 0)) return false;

            settings = new AppSettings { NetworkTimeout = TimeSpan.FromSeconds(seconds) };
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Write-to-temp-then-move, like the scanner list, so a crash can't leave half a file.
    private static void Write(string path, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text =
            "# Longest wait for one scanner network call, in seconds.\n" +
            $"{NetworkTimeoutKey} = {settings.NetworkTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)}\n";
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }
}
