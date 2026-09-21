using System.Text.RegularExpressions;

namespace KRocketDocumentScanner.Core.Models;

/// <summary>
/// Pulls a dotted IPv4 address out of wherever a driver embedded it — a SANE device id, model,
/// vendor string, or a manually-entered address. Shared by discovery grouping
/// (AddScannerViewModel) and registry dedup (ScannerRegistryManager), which both need to
/// recognize "same physical device" across different driver ids — e.g. a scanner's AirScan id
/// and its eSCL id are unrelated strings, but both embed the same IP.
/// </summary>
public static class ScannerAddressExtractor
{
    public static string? Extract(params string?[] candidates)
    {
        foreach (var text in candidates)
        {
            if (text is null) continue;
            var match = Regex.Match(text, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");
            if (match.Success) return match.Value;
        }
        return null;
    }

    /// <summary>Pulls a UUID (the eSCL device id) out of the same kinds of text, or null.</summary>
    public static string? ExtractDeviceId(params string?[] candidates)
    {
        foreach (var text in candidates)
        {
            if (text is null) continue;
            var match = Regex.Match(text,
                @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b");
            if (match.Success) return match.Value.ToLowerInvariant();
        }
        return null;
    }
}
