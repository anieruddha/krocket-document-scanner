using System.Text.RegularExpressions;

namespace KRocketDocumentScanner.Core.Models;

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
