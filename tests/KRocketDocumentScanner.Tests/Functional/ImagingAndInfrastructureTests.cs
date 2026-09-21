using System.Collections.Concurrent;
using KRocketDocumentScanner.Core.Concurrency;
using KRocketDocumentScanner.Core.Models;
using KRocketDocumentScanner.Core.Registry;
using KRocketDocumentScanner.Tests.Support;
using Xunit;

namespace KRocketDocumentScanner.Tests.Functional;

[Trait("Category", "Functional")]
public class ImagingAndInfrastructureTests
{
    private static CapturedPage Gradient(int w, int h)
    {
        var data = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 3;
                data[i] = (byte)(x * 50); data[i + 1] = (byte)(y * 50); data[i + 2] = 200;
            }
        return new CapturedPage { WidthPx = w, HeightPx = h, Format = PixelFormat.Rgb24, PixelData = data };
    }

    [Fact]
    public void Crop_returns_the_requested_pixels()
    {
        var cropped = CapturedPageOps.Crop(Gradient(4, 4), 1, 1, 3, 3);
        Assert.Equal((2, 2), (cropped.WidthPx, cropped.HeightPx));
        Assert.Equal(new byte[] { 50, 50, 200 }, cropped.PixelData[..3]);
        Assert.Equal(new byte[] { 100, 100, 200 }, cropped.PixelData[^3..]);
    }

    [Fact]
    public void Crop_rejects_an_inverted_rectangle() =>
        Assert.ThrowsAny<Exception>(() => CapturedPageOps.Crop(Gradient(4, 4), 3, 3, 1, 1));

    [Fact]
    public void Normalised_selections_convert_to_pixels()
    {
        var r = CapturedPageOps.NormalizedToPixelRect(0.1, 0.2, 0.3, 0.6, 1000, 1000);
        Assert.Equal((100, 200, 300, 600), r);
    }

    [Fact]
    public void A_zero_area_selection_still_gives_a_non_empty_rectangle()
    {
        var (l, t, r, b) = CapturedPageOps.NormalizedToPixelRect(0.5, 0.5, 0.5, 0.5, 1000, 1000);
        Assert.True(r > l && b > t);
    }

    [Fact]
    public void Black_and_white_output_has_only_two_levels_and_keeps_dpi()
    {
        var noisy = new byte[100 * 100];
        for (int i = 0; i < noisy.Length; i++) noisy[i] = (byte)(i % 2 == 0 ? 200 + i % 40 : 30 + i % 20);
        var page = new CapturedPage { WidthPx = 100, HeightPx = 100, Format = PixelFormat.Grayscale8, PixelData = noisy, Dpi = 300 };
        var bw = CapturedPageOps.ToBlackAndWhite(page);
        Assert.All(bw.PixelData, v => Assert.True(v == 0 || v == 255));
        Assert.Equal(255, bw.PixelData[0]);
        Assert.Equal(0, bw.PixelData[1]);
        Assert.Equal(300, bw.Dpi);
    }

    private static CapturedPage Blank(int w, int h, int dpi) =>
        new() { WidthPx = w, HeightPx = h, Format = PixelFormat.Grayscale8, PixelData = new byte[w * h], Dpi = dpi };

    [Fact]
    public void A_full_letter_page_is_612_by_792_points()
    {
        var l = PdfPageLayout.Compute(Blank(2550, 3300, 300), null);
        Assert.Equal(612, l.PageWidth, 0.5);
        Assert.Equal(792, l.PageHeight, 0.5);
    }

    [Fact]
    public void A_crop_keeps_its_true_size_on_a_sheet_and_is_centred()
    {
        var letter = PaperDetector.KnownSizes.First(p => p.Name == "Letter");
        var page = Blank(1830, 2368, 300);
        var bare = PdfPageLayout.Compute(page, null);
        var sheet = PdfPageLayout.Compute(page, letter);
        Assert.Equal(612, sheet.PageWidth, 0.5);
        Assert.Equal(bare.DrawWidth, sheet.DrawWidth, 0.01);
        Assert.Equal((612 - sheet.DrawWidth) / 2, sheet.X, 0.01);
        Assert.Equal((792 - sheet.DrawHeight) / 2, sheet.Y, 0.01);
    }

    [Fact]
    public void Landscape_scans_get_a_landscape_sheet_and_oversized_ones_are_reduced()
    {
        var letter = PaperDetector.KnownSizes.First(p => p.Name == "Letter");
        var land = PdfPageLayout.Compute(Blank(3300, 2550, 300), letter);
        Assert.True(land.PageWidth > land.PageHeight);
        var big = PdfPageLayout.Compute(Blank(5100, 6600, 300), letter);
        Assert.True(big.DrawWidth <= 612.01f && big.DrawHeight <= 792.01f);
    }

    [Fact]
    public void Known_paper_sizes_are_a4_letter_legal_a5() =>
        Assert.Equal(new[] { "A4", "Letter", "Legal", "A5" }, PaperDetector.KnownSizes.Select(p => p.Name));

    [Fact]
    public async Task Concurrent_callers_are_all_run_on_one_worker_thread_without_overlap()
    {
        using var executor = new SerialExecutor("test-worker");
        var threads = new ConcurrentBag<int>();
        var spans = new ConcurrentQueue<(DateTime Start, DateTime End)>();
        var barrier = new Barrier(12);

        var tasks = Enumerable.Range(0, 12).Select(i => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            var result = await executor.RunAsync(async ct =>
            {
                var start = DateTime.UtcNow;
                threads.Add(Environment.CurrentManagedThreadId);
                await Task.Delay(5, ct);
                spans.Enqueue((start, DateTime.UtcNow));
                return i * 10;
            });
            Assert.Equal(i * 10, result);
        })).ToArray();
        await Task.WhenAll(tasks);

        Assert.Single(threads.Distinct());
        Assert.Equal(executor.WorkerManagedThreadId, threads.First());
        var ordered = spans.OrderBy(s => s.Start).ToList();
        for (int i = 1; i < ordered.Count; i++) Assert.True(ordered[i].Start >= ordered[i - 1].End);
    }

    [Fact]
    public async Task Streaming_yields_items_in_order_and_errors_propagate()
    {
        using var executor = new SerialExecutor("test-stream");
        async IAsyncEnumerable<int> Three([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            for (int i = 1; i <= 3; i++) { await Task.Delay(1, ct); yield return i; }
        }
        var got = new List<int>();
        await foreach (var i in executor.RunStreamAsync(Three)) got.Add(i);
        Assert.Equal(new[] { 1, 2, 3 }, got);

        async IAsyncEnumerable<int> Throws([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return 1; await Task.Delay(1, ct);
            throw new InvalidOperationException("boom");
        }
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in executor.RunStreamAsync(Throws)) { }
        });
    }

    [Fact]
    public async Task Errors_inside_the_executor_reach_the_caller_and_the_worker_keeps_going()
    {
        using var executor = new SerialExecutor("test-errors");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.RunAsync<int>(_ => throw new InvalidOperationException("x")));
        Assert.Equal(7, await executor.RunAsync(_ => Task.FromResult(7)));
    }

    [Fact]
    public async Task The_activity_log_persists_entries_and_survives_concurrent_writers()
    {
        var dir = Path.Combine(Path.GetTempPath(), "krocketdocumentscanner-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "log.jsonl");
            var log = new FileNetworkActivityLog(path);
            Assert.Empty(await log.ReadAllAsync());

            await log.LogAsync(new NetworkActivityEntry(DateTimeOffset.UtcNow, "dest", "purpose", "proto", true, "detail"));
            await log.LogAsync(new NetworkActivityEntry(DateTimeOffset.UtcNow, "dest2", "purpose2", "proto2", false, null));
            var first = await new FileNetworkActivityLog(path).ReadAllAsync();
            Assert.Equal(2, first.Count);
            Assert.Equal("detail", first[0].Detail);
            Assert.False(first[1].Success);

            await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
                log.LogAsync(new NetworkActivityEntry(DateTimeOffset.UtcNow, $"d{i}", "p", "x", true, null))));
            Assert.Equal(22, (await log.ReadAllAsync()).Count);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
