namespace KRocketDocumentScanner.Core.Models;

public sealed record NetworkActivityEntry(
    DateTimeOffset Timestamp,
    string Destination,
    string Purpose,
    string Protocol,
    bool Success,
    string? Detail = null);
