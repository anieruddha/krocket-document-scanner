using System.Globalization;
using KRocketDocumentScanner.Core.Registry;
using KRocketDocumentScanner.Core.Settings;
using Tomlyn;
using Tomlyn.Model;

namespace KRocketDocumentScanner.App;

public sealed class AppSettings
{
    private const string NetworkTimeoutKey = "network_timeout_seconds";
    private const string ReduceFileSizeKey = "reduce_file_size";

    public TimeSpan NetworkTimeout { get; private init; } = SettingsDefaults.NetworkCallTimeout;
    public bool ReduceFileSize { get; private init; } = SettingsDefaults.ReduceFileSize;

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

            var reduceFileSize = SettingsDefaults.ReduceFileSize;
            if (table.TryGetValue(ReduceFileSizeKey, out var rawReduce))
            {
                if (rawReduce is not bool parsedReduce) return false;
                reduceFileSize = parsedReduce;
            }

            settings = new AppSettings { NetworkTimeout = TimeSpan.FromSeconds(seconds), ReduceFileSize = reduceFileSize };
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
            $"{NetworkTimeoutKey} = {settings.NetworkTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)}\n" +
            "# Start with \"Reduce file size\" turned on when saving scans as PDF or JPEG.\n" +
            $"{ReduceFileSizeKey} = {(settings.ReduceFileSize ? "true" : "false")}\n";
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }
}
