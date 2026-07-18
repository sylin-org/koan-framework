using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core.Capabilities;
using Koan.Storage.Abstractions;
using Koan.Storage.Abstractions.Capabilities;
using Koan.Storage.Replication;
using Xunit;

namespace Koan.Storage.Core.Tests.Specs;

/// <summary>
/// (X-f2-failure-coverage) Failure-path coverage for the F2-storage burn-down (44eea2b6) in
/// <see cref="ReplicatedStorageProvider"/>: a cache fault must DEGRADE to the durable store (the
/// authoritative source) and never surface to the caller. The F2 change narrowed the silent swallow to a
/// LOGGED catch but deliberately kept it BROAD ("any cache fault must fall through to durable"), so these
/// pin that a NON-IOException cache fault still degrades — guarding against a later over-narrowing of the
/// catch that would let a flaky cache crash reads.
/// </summary>
/// <remarks>
/// Resurrected this suite from a husk (it referenced the phantom <c>Koan.TestPipeline</c> package and was
/// excluded from the sln). <see cref="ReplicatedStorageProvider"/> is composition-based, so a tiny in-memory
/// <see cref="IStorageProvider"/> fake suffices — no storage backend. Each test uses a unique container so the
/// provider's on-disk manifest (`.Koan/storage-manifest/{container}.jsonl`) starts empty and never cross-pollutes.
/// </remarks>
public sealed class ReplicatedStorageProviderFailurePathSpec
{
    private static string FreshContainer() => "f2probe-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Exists_degrades_to_durable_when_cache_throws()
    {
        // A NON-IOException on purpose: the catch must be broad, so ANY cache fault degrades to durable.
        var cache = new FakeProvider("cache") { ExistsThrow = () => new InvalidOperationException("flaky cache") };
        var durable = new FakeProvider("durable") { ExistsResult = true };
        using var sut = new ReplicatedStorageProvider(cache, durable, FreshContainer());

        var exists = await sut.Exists("c", "missing-in-cache");

        exists.Should().BeTrue();                       // degraded to durable's answer, not thrown
        durable.ExistsCalls.Should().BeGreaterThan(0);  // durable was actually consulted
    }

    [Fact]
    public async Task Exists_returns_false_when_neither_cache_nor_durable_has_it()
    {
        var cache = new FakeProvider("cache") { ExistsResult = false };
        var durable = new FakeProvider("durable") { ExistsResult = false };
        using var sut = new ReplicatedStorageProvider(cache, durable, FreshContainer());

        (await sut.Exists("c", "nope")).Should().BeFalse();
    }

    [Fact]
    public async Task Exists_short_circuits_on_cache_hit_without_consulting_durable()
    {
        // Positive control: a healthy cache hit must NOT fall through to durable.
        var cache = new FakeProvider("cache") { ExistsResult = true };
        var durable = new FakeProvider("durable") { ExistsResult = false };
        using var sut = new ReplicatedStorageProvider(cache, durable, FreshContainer());

        var exists = await sut.Exists("c", "in-cache");

        exists.Should().BeTrue();
        durable.ExistsCalls.Should().Be(0);
    }

    /// <summary>Minimal in-memory <see cref="IStorageProvider"/>: configurable Exists (throw or result),
    /// benign defaults for everything else so the background sync loop never faults on a real call.</summary>
    private sealed class FakeProvider : IStorageProvider
    {
        public FakeProvider(string name) => Name = name;

        public string Name { get; }
        public StorageProviderPlacement Placement => Name == "cache"
            ? StorageProviderPlacement.Local
            : StorageProviderPlacement.Remote;

        public void Describe(ICapabilities caps)
            => caps.Add(StorageCaps.SequentialRead).Add(StorageCaps.Seek);

        public Func<Exception>? ExistsThrow { get; set; }
        public bool ExistsResult { get; set; }
        public int ExistsCalls;

        public Task<bool> Exists(string container, string key, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ExistsCalls);
            if (ExistsThrow is not null) throw ExistsThrow();
            return Task.FromResult(ExistsResult);
        }

        public Task Write(string container, string key, Stream content, string? contentType, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream> OpenRead(string container, string key, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream());
        public Task<(Stream Stream, long? Length)> OpenReadRange(string container, string key, long? from, long? to, CancellationToken ct = default) => Task.FromResult<(Stream, long?)>((new MemoryStream(), (long?)null));
        public Task<bool> Delete(string container, string key, CancellationToken ct = default) => Task.FromResult(true);
    }
}
