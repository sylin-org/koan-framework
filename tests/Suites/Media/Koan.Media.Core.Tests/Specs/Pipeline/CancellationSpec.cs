using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Tests.Support;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

/// <summary>
/// Cancellation honoured at every stage where work can stall —
/// decode, encode, and overlay resolution. Real production sees
/// client-cancelled requests constantly; a pipeline that ignores its
/// token can pin threads on disconnected clients.
/// </summary>
public sealed class CancellationSpec
{
    [Fact]
    public async Task Cancelled_before_terminal_call_propagates_OCE()
    {
        await using var src = Fixtures.WideJpeg();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await src.AsMedia().Resize(100, 100).ToBytesAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Cancellation_during_decode_propagates_OCE()
    {
        // A SlowReadStream stalls on Read; cancelling mid-read should
        // surface as OperationCanceledException, not as a hung Task or
        // a generic IOException.
        var inner = Fixtures.WideJpeg(width: 1200, height: 800);
        using var ms = new MemoryStream();
        await inner.CopyToAsync(ms);
        ms.Position = 0;
        var slow = new SlowReadStream(ms, delayPerReadMs: 50);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(20));

        var act = async () => await slow.AsMedia().Resize(100, 100).ToBytesAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Cancellation_during_materialize_branches_propagates_OCE()
    {
        await using var src = Fixtures.WideJpeg(width: 800, height: 600);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await src.AsMedia().MaterializeAsync(b => b
            .Add("small", v => v.ResizeFit(100, 100))
            .Add("medium", v => v.ResizeFit(400, 400))
        , cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Cancellation_during_overlay_resolution_propagates_OCE()
    {
        // Resolver stalls; pipeline should observe the cancel and bail.
        await using var host = MakeSolidPng(200, 200, new Rgba32(0, 0, 0, 255));
        var stallingResolver = new StallingOverlayResolver(delayMs: 200);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(30));

        var act = async () => await host.AsMedia(overlayResolver: stallingResolver)
            .Overlay("logo")
            .EncodeAs("png")
            .ToBytesAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Default_CancellationToken_does_not_cancel()
    {
        // Regression guard: passing default(CancellationToken) must NOT
        // behave as already-cancelled.
        await using var src = Fixtures.WideJpeg();
        var output = await src.AsMedia().Resize(100, 100).ToBytesAsync(default);
        output.Bytes.Length.Should().BeGreaterThan(0);
    }

    // ----- test helpers -----

    private static MemoryStream MakeSolidPng(int width, int height, Rgba32 color)
    {
        using var img = new Image<Rgba32>(width, height);
        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++) row[x] = color;
            }
        });
        var ms = new MemoryStream();
        img.SaveAsPng(ms);
        ms.Position = 0;
        return ms;
    }

    /// <summary>Wraps a stream and inserts a delay on every Read so cancellation has a window to fire.</summary>
    private sealed class SlowReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly int _delayMs;

        public SlowReadStream(Stream inner, int delayPerReadMs)
        {
            _inner = inner;
            _delayMs = delayPerReadMs;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            Thread.Sleep(_delayMs);
            return _inner.Read(buffer, offset, count);
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            await Task.Delay(_delayMs, ct);
            return await _inner.ReadAsync(buffer.AsMemory(offset, count), ct);
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(_delayMs, ct);
            return await _inner.ReadAsync(buffer, ct);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class StallingOverlayResolver : IOverlayResolver
    {
        private readonly int _delayMs;
        public StallingOverlayResolver(int delayMs) => _delayMs = delayMs;

        public async Task<Stream?> OpenAsync(MediaOverlaySource source, int depth, CancellationToken ct)
        {
            await Task.Delay(_delayMs, ct);
            return new MemoryStream(Array.Empty<byte>());
        }
    }
}
