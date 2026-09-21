namespace KRocketDocumentScanner.Core.Models;

public sealed class ScannerRegistry
{
    public string? DefaultDriverId { get; set; }
    public List<ScannerRegistryEntry> Entries { get; set; } = new();
}
