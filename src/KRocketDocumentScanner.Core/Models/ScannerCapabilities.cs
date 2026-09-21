namespace KRocketDocumentScanner.Core.Models;

/// <summary>
/// Which paper sources a scanner's driver actually reports support for. Used so the UI can
/// hide/limit options that would otherwise fail at scan time on a given device — e.g. a
/// flatbed-only scanner has no feeder to pick, so there's nothing to choose between.
/// </summary>
public sealed record ScannerCapabilities(bool SupportsFlatbed, bool SupportsFeeder, bool SupportsDuplex);
