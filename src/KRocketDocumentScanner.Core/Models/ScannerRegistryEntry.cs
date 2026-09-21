namespace KRocketDocumentScanner.Core.Models;

/// <summary>How a registry entry was originally added.</summary>
public enum ScannerAddMethod
{
    AutoDiscovered,
    ManuallyEntered,
}

/// <summary>Current reachability of a registered scanner, as of the last check.</summary>
public enum ScannerReachability
{
    /// <summary>Not checked yet this session.</summary>
    Unknown,
    Ready,
    NotReady,
}

/// <summary>
/// A scanner as reported live by the scanning engine (SANE device listing).
/// Transient — not persisted directly; see <see cref="ScannerRegistryEntry"/> for the
/// persisted form.
/// </summary>
public sealed record DiscoveredScanner(
    string DriverId,      // the engine/backend identifier, e.g. a SANE device name
    string Vendor,
    string Model);

/// <summary>
/// A scanner the user has added to their local registry. This is what gets persisted to
/// disk and re-checked for reachability on every app start.
/// </summary>
public sealed class ScannerRegistryEntry
{
    /// <summary>Stable identifier used to match this entry against a live discovery result.</summary>
    public required string DriverId { get; set; }

    private string _displayName = "";

    /// <summary>
    /// The scanner's display name. Strips any protocol prefix a driver may have embedded in
    /// the name (e.g. "Model X (escl:https://192.0.2.10:443)" -> "Model X") —
    /// that detail belongs in the connection/driver id, not in what the user reads.
    /// </summary>
    public required string DisplayName
    {
        get => _displayName;
        set => _displayName = StripProtocolSuffix(value);
    }

    /// <summary>
    /// Strips any parenthesised protocol/connection detail a driver embedded in a name (e.g.
    /// "Model X (escl:https://192.0.2.10:443)" -> "Model X", "Model X Series
    /// (airscan:ip=192.0.2.10)" -> "Model X Series"). Shared by <see cref="DisplayName"/>
    /// and by discovery-result display names — the same raw driver text feeds both.
    /// </summary>
    public static string StripProtocolSuffix(string? raw)
    {
        var valueToStore = raw?.Trim() ?? "";

        var protocols = new[]
        {
            "escl:",
            "ipp:",
            "airscan:",
            "https:",
            "http:"
        };

        foreach (var protocol in protocols)
        {
            var index = valueToStore.IndexOf(
                protocol,
                StringComparison.OrdinalIgnoreCase);

            if (index >= 0)
            {
                valueToStore = valueToStore[..index]
                    .TrimEnd(' ', '(');

                break;
            }
        }

        return valueToStore;
    }

    public string Vendor { get; set; } = "";
    public string Model { get; set; } = "";

    public ScannerAddMethod AddMethod { get; set; } = ScannerAddMethod.AutoDiscovered;

    /// <summary>For manually-added network scanners: host/IP the user entered. Null for USB/auto-discovered.</summary>
    public string? ManualAddress { get; set; }

    /// <summary>The scanner's own permanent id (its eSCL UUID) when one is known — the most
    /// reliable way to tell it's the same physical scanner even if its address or connection
    /// type changes. Null when the scanner never reported one.</summary>
    public string? DeviceId { get; set; }

    /// <summary>The IP address a manually-entered host name (e.g. printer.local) resolved to
    /// when it was added, so it can be matched against a scanner found by address.</summary>
    public string? ResolvedAddress { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenReadyAt { get; set; }

    /// <summary>Not persisted — populated at runtime after each reachability check.</summary>
    public ScannerReachability Reachability { get; set; } = ScannerReachability.Unknown;
}
