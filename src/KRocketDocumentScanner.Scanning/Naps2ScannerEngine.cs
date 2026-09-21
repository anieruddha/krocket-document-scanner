using KRocketDocumentScanner.Core.Abstractions;
using KRocketDocumentScanner.Core.Concurrency;
using KRocketDocumentScanner.Core.Models;
using KRocketDocumentScanner.Core.Registry;
using KRocketDocumentScanner.Core.Settings;
using KRocketDocumentScanner.Imaging;
using NAPS2.Images.ImageSharp;
using NAPS2.Scan;
using NAPS2.Scan.Exceptions;
using CoreScanOptions = KRocketDocumentScanner.Core.Models.ScanOptions;

namespace KRocketDocumentScanner.Scanning;

public sealed class Naps2ScannerEngine : IScannerEngine, IDisposable
{
    private readonly ScanningContext _scanningContext;
    private readonly ScanController _controller;
    private readonly SerialExecutor _executor;
    private readonly INetworkActivityLog _networkActivityLog;

    public Naps2ScannerEngine(INetworkActivityLog? networkActivityLog = null)
    {
        _scanningContext = new ScanningContext(new ImageSharpImageContext());
        _controller = new ScanController(_scanningContext);
        _executor = new SerialExecutor("naps2-scan-worker");
        _networkActivityLog = networkActivityLog ?? new FileNetworkActivityLog();
    }

    public TimeSpan NetworkCallTimeout { get; set; } = SettingsDefaults.NetworkCallTimeout;

    private async Task<T> WithTimeoutAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(NetworkCallTimeout);
        try
        {
            return await call(cts.Token).WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"no answer within {NetworkCallTimeout.TotalSeconds:0} seconds");
        }
    }

    public Task<IReadOnlyList<DiscoveredScanner>> ListDevicesAsync(CancellationToken ct = default)
    {
        return _executor.RunAsync<IReadOnlyList<DiscoveredScanner>>(async innerCt =>
        {
            var devices = new List<ScanDevice>();
            Exception? saneError = null;

            try
            {
                var options = new NAPS2.Scan.ScanOptions { Driver = Driver.Sane };
                devices.AddRange(await WithTimeoutAsync(_ => _controller.GetDeviceList(options), innerCt).ConfigureAwait(false));
                await LogNetworkActivityAsync(
                    "local SANE bus + any network scanners configured in the host's SANE setup",
                    "Scanner device discovery", "SANE (local)", success: true,
                    detail: $"found {devices.Count} device(s)", innerCt).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                saneError = ex;
                await LogNetworkActivityAsync(
                    "local SANE bus + any network scanners configured in the host's SANE setup",
                    "Scanner device discovery", "SANE (local)", success: false,
                    detail: ex.Message, innerCt).ConfigureAwait(false);
            }

            try
            {
                var options = new NAPS2.Scan.ScanOptions { Driver = Driver.Escl };
                var escl = await WithTimeoutAsync(_ => _controller.GetDeviceList(options), innerCt).ConfigureAwait(false);
                devices.AddRange(escl);
                await LogNetworkActivityAsync(
                    "local network (mDNS multicast query)",
                    "Scanner device discovery", "ESCL (mDNS, local network only)", success: true,
                    detail: $"found {escl.Count} device(s)", innerCt).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await LogNetworkActivityAsync(
                    "local network (mDNS multicast query)",
                    "Scanner device discovery", "ESCL (mDNS, local network only)", success: false,
                    detail: ex.Message, innerCt).ConfigureAwait(false);
            }

            if (devices.Count == 0 && saneError is not null) throw saneError;
            return devices.Select(ToDiscoveredScanner).ToList();
        }, ct);
    }

    public Task<string?> ResolveAddressAsync(string host, CancellationToken ct = default)
    {
        return _executor.RunAsync<string?>(async innerCt =>
        {
            try
            {
                var addresses = await WithTimeoutAsync(t => System.Net.Dns.GetHostAddressesAsync(host, t), innerCt).ConfigureAwait(false);
                var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                await LogNetworkActivityAsync(
                    host, "Look up scanner host name", "DNS / mDNS (local resolver)", success: ipv4 is not null,
                    detail: ipv4?.ToString() ?? "no IPv4 address", innerCt).ConfigureAwait(false);
                return ipv4?.ToString();
            }
            catch (Exception ex)
            {
                await LogNetworkActivityAsync(
                    host, "Look up scanner host name", "DNS / mDNS (local resolver)", success: false,
                    detail: ex.Message, innerCt).ConfigureAwait(false);
                return null;
            }
        }, ct);
    }

    public Task<bool> IsReachableAsync(string driverId, CancellationToken ct = default)
    {
        return _executor.RunAsync(async innerCt =>
        {
            ScanDevice device;
            try
            {
                device = Naps2DeviceIdCodec.Decode(driverId);
            }
            catch (FormatException)
            {
                return false;
            }

            try
            {
                var caps = await WithTimeoutAsync(t => _controller.GetCaps(device, t), innerCt).ConfigureAwait(false);

                bool answered = caps.MetadataCaps is not null || caps.PaperSourceCaps is not null ||
                                caps.FlatbedCaps is not null || caps.FeederCaps is not null ||
                                caps.DuplexCaps is not null;
                await LogNetworkActivityAsync(
                    DescribeDestination(device), "Scanner reachability check", DescribeProtocol(device),
                    success: answered, detail: answered ? null : "no scanner answered at this address", innerCt).ConfigureAwait(false);
                return answered;
            }
            catch (Exception ex) when (ex is DeviceException or TimeoutException)
            {
                await LogNetworkActivityAsync(
                    DescribeDestination(device), "Scanner reachability check", DescribeProtocol(device),
                    success: false, detail: ex.Message, innerCt).ConfigureAwait(false);
                return false;
            }
        }, ct);
    }

    public Task<ScannerCapabilities> GetCapabilitiesAsync(string driverId, CancellationToken ct = default)
    {
        return _executor.RunAsync(async innerCt =>
        {
            var device = Naps2DeviceIdCodec.Decode(driverId);
            try
            {
                var caps = await WithTimeoutAsync(t => _controller.GetCaps(device, t), innerCt).ConfigureAwait(false);
                await LogNetworkActivityAsync(
                    DescribeDestination(device), "Scanner capability query", DescribeProtocol(device),
                    success: true, detail: null, innerCt).ConfigureAwait(false);

                var sources = caps.PaperSourceCaps;
                return new ScannerCapabilities(
                    SupportsFlatbed: sources?.SupportsFlatbed ?? true,
                    SupportsFeeder: sources?.SupportsFeeder ?? false,
                    SupportsDuplex: sources?.SupportsDuplex ?? false);
            }
            catch (Exception ex) when (ex is DeviceException or TimeoutException)
            {
                await LogNetworkActivityAsync(
                    DescribeDestination(device), "Scanner capability query", DescribeProtocol(device),
                    success: false, detail: ex.Message, innerCt).ConfigureAwait(false);
                throw;
            }
        }, ct);
    }

    public Task<CapturedPage> PreviewAsync(string driverId, CoreScanOptions options, CancellationToken ct = default)
    {
        var previewOptions = options with { ResolutionDpi = 100 };
        return ScanSingleAsync(driverId, previewOptions, ct);
    }

    public Task<CapturedPage> ScanSingleAsync(string driverId, CoreScanOptions options, CancellationToken ct = default)
    {
        return _executor.RunAsync(async innerCt =>
        {
            var device = Naps2DeviceIdCodec.Decode(driverId);
            var naps2Options = ToNaps2Options(device, options);

            try
            {
                await foreach (var image in _controller.Scan(naps2Options, innerCt).ConfigureAwait(false))
                {
                    using (image)
                    {
                        var page = await ToCapturedPageAsync(image, options, innerCt).ConfigureAwait(false);
                        await LogNetworkActivityAsync(
                            DescribeDestination(device), "Scan operation", DescribeProtocol(device),
                            success: true, detail: null, innerCt).ConfigureAwait(false);
                        return page;
                    }
                }
            }
            catch (Exception ex)
            {
                await LogNetworkActivityAsync(
                    DescribeDestination(device), "Scan operation", DescribeProtocol(device),
                    success: false, detail: ex.Message, innerCt).ConfigureAwait(false);
                throw;
            }

            var noDataMessage = "The scanner did not return any image data. Check that a page is loaded (flatbed) " +
                                 "or the feeder isn't empty (ADF), then try again.";
            await LogNetworkActivityAsync(
                DescribeDestination(device), "Scan operation", DescribeProtocol(device),
                success: false, detail: noDataMessage, innerCt).ConfigureAwait(false);
            throw new InvalidOperationException(noDataMessage);
        }, ct);
    }

    public async IAsyncEnumerable<CapturedPage> ScanBatchAsync(
        string driverId, CoreScanOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var page in _executor.RunStreamAsync(innerCt => ScanBatchCoreAsync(driverId, options, innerCt), ct)
                           .ConfigureAwait(false))
        {
            yield return page;
        }
    }

    private async IAsyncEnumerable<CapturedPage> ScanBatchCoreAsync(
        string driverId, CoreScanOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var device = Naps2DeviceIdCodec.Decode(driverId);
        var naps2Options = ToNaps2Options(device, options);
        int pageCount = 0;

        var enumerator = _controller.Scan(naps2Options, ct).GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await LogNetworkActivityAsync(
                        DescribeDestination(device), "Batch scan operation", DescribeProtocol(device),
                        success: false, detail: $"{ex.Message} (after {pageCount} page(s))", ct).ConfigureAwait(false);
                    throw;
                }
                if (!hasNext) break;

                using (enumerator.Current)
                {
                    pageCount++;
                    yield return await ToCapturedPageAsync(enumerator.Current, options, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        await LogNetworkActivityAsync(
            DescribeDestination(device), "Batch scan operation", DescribeProtocol(device),
            success: true, detail: $"{pageCount} page(s)", ct).ConfigureAwait(false);
    }

    private Task LogNetworkActivityAsync(
        string destination, string purpose, string protocol, bool success, string? detail, CancellationToken ct)
    {
        var entry = new NetworkActivityEntry(DateTimeOffset.UtcNow, destination, purpose, protocol, success, detail);
        return _networkActivityLog.LogAsync(entry, ct);
    }

    private static string DescribeDestination(ScanDevice device) =>
        device.ConnectionUri ?? "local USB/SANE device (no network address)";

    private static string DescribeProtocol(ScanDevice device) =>
        device.Driver == Driver.Escl ? "ESCL (HTTP/HTTPS, local network device only)" : "SANE (local)";

    public static string BuildManualNetworkScannerDriverId(string address, string displayName) =>
        Naps2DeviceIdCodec.EncodeManualEscl(address, displayName);

    private static DiscoveredScanner ToDiscoveredScanner(ScanDevice device)
    {
        var name = device.Name;
        var spaceIndex = name.IndexOf(' ');
        var (vendor, model) = spaceIndex > 0
            ? (name[..spaceIndex], name[(spaceIndex + 1)..])
            : ("", name);

        return new DiscoveredScanner(Naps2DeviceIdCodec.Encode(device), vendor, model);
    }

    private static NAPS2.Scan.ScanOptions ToNaps2Options(ScanDevice device, CoreScanOptions options)
    {
        return new NAPS2.Scan.ScanOptions
        {
            Driver = device.Driver,
            Device = device,
            Dpi = options.ResolutionDpi,
            BitDepth = options.ColorMode switch
            {
                ColorMode.Color => NAPS2.Images.BitDepth.Color,
                ColorMode.Grayscale => NAPS2.Images.BitDepth.Grayscale,
                ColorMode.BlackAndWhiteText => NAPS2.Images.BitDepth.BlackAndWhite,
                ColorMode.BlackAndWhiteClean => NAPS2.Images.BitDepth.Grayscale,
                _ => throw new ArgumentOutOfRangeException(nameof(options), $"Unhandled color mode: {options.ColorMode}"),
            },
            PaperSource = options.Source switch
            {
                ScanSource.Flatbed => PaperSource.Flatbed,
                ScanSource.AutomaticDocumentFeeder => PaperSource.Feeder,
                ScanSource.AutomaticDocumentFeederDuplex => PaperSource.Duplex,
                _ => throw new ArgumentOutOfRangeException(nameof(options), $"Unhandled scan source: {options.Source}"),
            },
            AutoDeskew = options.AutoDeskew,
            ExcludeBlankPages = options.DropBlankPages,
        };
    }

    private async Task<CapturedPage> ToCapturedPageAsync(NAPS2.Images.ProcessedImage image, CoreScanOptions options, CancellationToken ct)
    {
        using var memoryImage = _scanningContext.ImageContext.Render(image);
        using var stream = new MemoryStream();
        memoryImage.Save(stream, NAPS2.Images.ImageFileFormat.Png);
        stream.Position = 0;

        var page = CapturedPageOps.WithDpi(ImageConversion.DecodeToCapturedPage(stream), options.ResolutionDpi);

        if (options.Area is { } area)
        {
            const double mmPerInch = 25.4;
            double pxPerMm = options.ResolutionDpi / mmPerInch;
            var (left, top, right, bottom) = (
                (int)Math.Round(area.LeftMm * pxPerMm),
                (int)Math.Round(area.TopMm * pxPerMm),
                Math.Min(page.WidthPx, (int)Math.Round(area.RightMm * pxPerMm)),
                Math.Min(page.HeightPx, (int)Math.Round(area.BottomMm * pxPerMm)));

            if (right > left && bottom > top)
            {
                page = CapturedPageOps.Crop(page, left, top, right, bottom);
            }
        }

        if (options.ColorMode == ColorMode.BlackAndWhiteClean)
            page = CapturedPageOps.ToBlackAndWhite(page);

        return page;
    }

    public void Dispose()
    {
        _executor.Dispose();
        _scanningContext.Dispose();
    }
}
