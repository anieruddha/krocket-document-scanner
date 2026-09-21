using NAPS2.Scan;

namespace KRocketDocumentScanner.Scanning;

/// <summary>
/// Our <see cref="KRocketDocumentScanner.Core.Abstractions.IScannerEngine"/> abstraction identifies devices
/// by a single opaque <c>driverId</c> string, but NAPS2's own <see cref="ScanDevice"/> needs
/// Driver + ID + Name (and, for ESCL, a ConnectionUri) to actually be usable for a scan call.
///
/// Rather than keeping an in-memory cache mapping driverId -> ScanDevice (which would go stale
/// across app restarts — the persisted <see cref="KRocketDocumentScanner.Core.Models.ScannerRegistryEntry"/>
/// registry is exactly the "survives a restart" data our IsReachableAsync/ScanSingleAsync/etc.
/// need to work against), every driverId this codec produces is fully self-describing: decoding
/// it never depends on having seen the device in this process before.
/// </summary>
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

    /// <summary>
    /// Builds a driverId for a manually-entered network scanner, bypassing SANE/mDNS discovery
    /// entirely by connecting directly to a user-supplied address via the ESCL driver (NAPS2's
    /// cross-platform network-scanner protocol support). This is the "manual entry" half of the
    /// Add Scanner flow, for devices that don't auto-discover.
    /// </summary>
    public static string EncodeManualEscl(string address, string displayName)
    {
        var uri = NormalizeToUri(address);
        var device = new ScanDevice(Driver.Escl, ID: uri, Name: displayName, ConnectionUri: uri);
        return Encode(device);
    }

    /// <summary>
    /// eSCL devices publish their API under a "resource root" path on the host — "eSCL" is the
    /// near-universal convention (the default per the Mopria eSCL spec; confirmed against a real
    /// scanner, whose root really is /eSCL). Without it, NAPS2.Escl's client requests
    /// "/ScannerCapabilities" at the bare host root instead of "/eSCL/ScannerCapabilities", gets
    /// a 404, and reports the device as simply "offline" — indistinguishable from an actual
    /// network failure, which is exactly the bug this method exists to avoid. Only appended when
    /// the caller didn't already specify a path of their own, so an address a user typed with a
    /// non-standard root (e.g. "192.0.2.10/some-other-root") is left alone.
    /// </summary>
    private static string NormalizeToUri(string address)
    {
        var uri = Uri.TryCreate(address, UriKind.Absolute, out var parsed) &&
                  (parsed.Scheme == "http" || parsed.Scheme == "https")
            ? parsed
            // Bare host/IP with no scheme — default to https, the more common ESCL configuration.
            : new Uri($"https://{address}");

        if (uri.AbsolutePath is "" or "/")
            uri = new Uri(uri, "eSCL");

        return uri.ToString();
    }
}
