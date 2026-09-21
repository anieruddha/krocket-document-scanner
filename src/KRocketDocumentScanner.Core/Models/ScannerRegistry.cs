namespace KRocketDocumentScanner.Core.Models;

/// <summary>
/// The full persisted scanner registry: every known scanner, plus which one (if any) is the
/// default. Kept as one aggregate — rather than a separate "IsDefault" flag on each
/// <see cref="ScannerRegistryEntry"/> — specifically so "there is at most one default" is true
/// by construction, not something that has to be validated every time it changes.
/// </summary>
public sealed class ScannerRegistry
{
    public string? DefaultDriverId { get; set; }
    public List<ScannerRegistryEntry> Entries { get; set; } = new();
}
