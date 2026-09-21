using System.Text.Json;
using System.Text.Json.Serialization;
using KRocketDocumentScanner.Core.Abstractions;
using KRocketDocumentScanner.Core.Models;

namespace KRocketDocumentScanner.Core.Registry;

public sealed class JsonScannerRegistryStore : IScannerRegistryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;

    public JsonScannerRegistryStore(string? filePathOverride = null)
    {
        _filePath = filePathOverride ?? DefaultFilePath();
    }

    public static string DefaultFilePath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var baseDir = !string.IsNullOrWhiteSpace(configHome)
            ? configHome
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(baseDir, "krocketdocumentscanner", "scanners.json");
    }

    public async Task<ScannerRegistry> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return new ScannerRegistry();

        await using var stream = File.OpenRead(_filePath);
        var registry = await JsonSerializer.DeserializeAsync<ScannerRegistry>(
            stream, JsonOptions, ct).ConfigureAwait(false);
        return registry ?? new ScannerRegistry();
    }

    public async Task SaveAsync(ScannerRegistry registry, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);

        var tempPath = Path.Combine(dir, $".scanners.json.{Guid.NewGuid():N}.tmp");
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, registry, JsonOptions, ct).ConfigureAwait(false);
        }
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
