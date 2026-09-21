namespace KRocketDocumentScanner.Core.Models;

public enum ScannerAddMethod
{
    AutoDiscovered,
    ManuallyEntered,
}

public enum ScannerReachability
{
    Unknown,
    Ready,
    NotReady,
}

public sealed record DiscoveredScanner(
    string DriverId,
    string Vendor,
    string Model);

public sealed class ScannerRegistryEntry
{
    public required string DriverId { get; set; }

    private string _displayName = "";

    public required string DisplayName
    {
        get => _displayName;
        set => _displayName = StripProtocolSuffix(value);
    }

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

    public string? ManualAddress { get; set; }

    public string? DeviceId { get; set; }

    public string? ResolvedAddress { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenReadyAt { get; set; }

    public ScannerReachability Reachability { get; set; } = ScannerReachability.Unknown;
}
