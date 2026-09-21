using System.Text.Json;
using KRocketDocumentScanner.Core.Abstractions;
using KRocketDocumentScanner.Core.Models;

namespace KRocketDocumentScanner.Core.Registry;

public sealed class FileNetworkActivityLog : INetworkActivityLog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly string _filePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FileNetworkActivityLog(string? filePathOverride = null)
    {
        _filePath = filePathOverride ?? DefaultFilePath();
    }

    public static string DefaultFilePath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var baseDir = !string.IsNullOrWhiteSpace(configHome)
            ? configHome
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(baseDir, "krocketdocumentscanner", "network-activity.log");
    }

    public async Task LogAsync(NetworkActivityEntry entry, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);

        var line = JsonSerializer.Serialize(entry, JsonOptions);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(_filePath, line + Environment.NewLine, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<NetworkActivityEntry>> ReadAllAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return Array.Empty<NetworkActivityEntry>();

        var lines = await File.ReadAllLinesAsync(_filePath, ct).ConfigureAwait(false);
        var entries = new List<NetworkActivityEntry>(lines.Length);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = JsonSerializer.Deserialize<NetworkActivityEntry>(line, JsonOptions);
            if (entry is not null) entries.Add(entry);
        }
        return entries;
    }
}
