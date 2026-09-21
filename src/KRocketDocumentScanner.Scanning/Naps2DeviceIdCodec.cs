using NAPS2.Scan;

namespace KRocketDocumentScanner.Scanning;

internal static class Naps2DeviceIdCodec
{
    private const string Separator = "|";

    public static string Encode(ScanDevice device)
    {
        return string.Join(Separator,
            ((int)device.Driver).ToString(),
            Uri.EscapeDataString(device.ID),
            Uri.EscapeDataString(device.Name),
            Uri.EscapeDataString(device.ConnectionUri ?? ""));
    }

    public static ScanDevice Decode(string driverId)
    {
        var parts = driverId.Split(Separator);
        if (parts.Length != 4 || !int.TryParse(parts[0], out var driverInt))
        {
            throw new FormatException(
                $"'{driverId}' is not a recognized scanner identifier for this app. " +
                "It may have been created by an incompatible version, or corrupted.");
        }

        var connectionUri = Uri.UnescapeDataString(parts[3]);
        return new ScanDevice(
            (Driver)driverInt,
            Uri.UnescapeDataString(parts[1]),
            Uri.UnescapeDataString(parts[2]),
            ConnectionUri: string.IsNullOrEmpty(connectionUri) ? null : connectionUri);
    }

    public static string EncodeManualEscl(string address, string displayName)
    {
        var uri = NormalizeToUri(address);
        var device = new ScanDevice(Driver.Escl, ID: uri, Name: displayName, ConnectionUri: uri);
        return Encode(device);
    }

    private static string NormalizeToUri(string address)
    {
        var uri = Uri.TryCreate(address, UriKind.Absolute, out var parsed) &&
                  (parsed.Scheme == "http" || parsed.Scheme == "https")
            ? parsed
            : new Uri($"https://{address}");

        if (uri.AbsolutePath is "" or "/")
            uri = new Uri(uri, "eSCL");

        return uri.ToString();
    }
}
