namespace KRocketDocumentScanner.Core.Models;

/// <summary>
/// One record of network activity the app performed. This exists so every network call the
/// app ever makes is auditable after the fact — not just documented in comments, but actually
/// logged, in a plain human-readable local file, with nothing hidden.
/// </summary>
public sealed record NetworkActivityEntry(
    DateTimeOffset Timestamp,
    /// <summary>Where the activity went: a device's local network address, "local USB/SANE bus"
    /// for non-network device communication, or similar. Never a vendor/telemetry endpoint —
    /// there are none in this app.</summary>
    string Destination,
    /// <summary>What the activity was for, in plain language (e.g. "Scanner device discovery").</summary>
    string Purpose,
    /// <summary>The protocol/mechanism involved (e.g. "SANE (local)", "ESCL (HTTP/HTTPS)").</summary>
    string Protocol,
    bool Success,
    string? Detail = null);
