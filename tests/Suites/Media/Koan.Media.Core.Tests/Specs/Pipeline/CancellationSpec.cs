using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Tests.Support;

namespace Koan.Media.Core.Tests.Specs.Pipeline;

/// <summary>
/// Cancellation honoured wherever the pipeline holds the thread between
/// units of work: reading the source, between steps and frames, resolving
/// overlays, and writing the output. No decode or encode call takes a token,
/// so a single native call runs to its end. Real production sees
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
    public async Task Cancellation_while_reading_the_source_propagates_OCE()
    {
        // A read that stalls, cancelled while it stalls, should surface
        // as OperationCanceledException, not as a hung Task or a generic
        // IOException.
        var inner = Fixtures.WideJpeg(width: 1200, height: 800);
        using var ms = new MemoryStream();
        await inner.CopyToAsync(ms);
        ms.Position = 0;
        using var cts = new CancellationTokenSource();
        var slow = new SlowReadStream(ms, cts, stallMs: 5000);

        var act = async () => await slow.AsMedia().Resize(100, 100).ToBytesAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Cancellation_is_honoured_between_frames()
    {
        // Overlay resolution hands control back mid-render; the frame loop after it must observe the token.
        await using var host = Fixtures.AnimatedWebp(frames: 4, width: 40, height: 40);
        var overlay = await Fixtures.Snapshot(Fixtures.SolidPng(8, 8, Rgba.Red));
        using var cts = new CancellationTokenSource();
        var resolver = new CancellingOverlayResolver(cts, overlay);

        var act = async () => await host.AsMedia(overlayResolver: resolver).Overlay("logo").ToBytesAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Cancellation_while_writing_the_output_propagates_OCE()
    {
        await using var src = Fixtures.WideJpeg(width: 64, height: 64);
        using var cts = new CancellationTokenSource();
        await using var sink = new CancellingStream(cts);

        var act = async () => await src.AsMedia().EncodeAs("png").WriteToAsync(sink, cts.Token);
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
        await using var host = Fixtures.SolidPng(200, 200, new Rgba(0, 0, 0, 255));
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


    /// <summary>Wraps a stream whose reads stall, and cancels the render as a read begins.</summary>
    private sealed class SlowReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly CancellationTokenSource _cts;
        private readonly int _stallMs;

        public SlowReadStream(Stream inner, CancellationTokenSource cts, int stallMs)
        {
            _inner = inner;
            _cts = cts;
            _stallMs = stallMs;
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
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("The source must be read asynchronously, so it can be cancelled.");
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            _cts.Cancel();
            await Task.Delay(_stallMs, ct);
            return await _inner.ReadAsync(buffer, ct);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class CancellingOverlayResolver(CancellationTokenSource cts, byte[] overlay) : IOverlayResolver
    {
        public Task<Stream?> OpenAsync(MediaOverlaySource source, int depth, CancellationToken ct)
        {
            cts.Cancel();
            return Task.FromResult<Stream?>(new MemoryStream(overlay));
        }
    }

    /// <summary>A destination that cancels the render on its first write.</summary>
    private sealed class CancellingStream(CancellationTokenSource cts) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cts.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return base.WriteAsync(buffer, cancellationToken);
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
