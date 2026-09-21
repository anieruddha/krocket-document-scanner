using System.Text.Json;
using KRocketDocumentScanner.Core.Abstractions;
using KRocketDocumentScanner.Core.Models;

namespace KRocketDocumentScanner.Core.Registry;

/// <summary>
/// Appends every network-activity record to a local, human-readable JSON-lines file
/// (~/.config/krocketdocumentscanner/network-activity.log, respecting $XDG_CONFIG_HOME) — one JSON object per
/// line, so it can be inspected with nothing more than a text editor or `cat`, or piped through
/// `jq` for filtering. This file records activity; it never transmits it anywhere.
/// </summary>
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

        // Serialize concurrent writers (multiple log calls could in principle race) with a
        // simple in-process lock — appends are small and infrequent, so this is never a
        // bottleneck, and it guarantees each line is written whole, never interleaved.
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
