using System.Globalization;
using KRocketDocumentScanner.Core.Registry;
using KRocketDocumentScanner.Core.Settings;
using Tomlyn;
using Tomlyn.Model;

namespace KRocketDocumentScanner.App;

public sealed class AppSettings
{
    private const string NetworkTimeoutKey = "network_timeout_seconds";

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
