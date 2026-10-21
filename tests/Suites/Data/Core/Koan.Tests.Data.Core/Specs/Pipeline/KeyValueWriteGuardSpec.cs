using System.Collections.Concurrent;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.KeyValue;

namespace Koan.Tests.Data.Core.Specs.Pipeline;

/// <summary>
/// NK-095: the key-value family's cross-scope write guard reads the stored record before it writes. Two scopes upserting
/// one new id must not both pass that read: exactly one write lands and the other is the cross-scope rejection.
/// </summary>
public sealed class KeyValueWriteGuardSpec
{
    private const string Field = "__owner";

    public sealed class Doc : IEntity<string>
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
    }

    /// <summary>A store whose reads wait until the spec releases them, so concurrent guards can be lined up.</summary>
    private sealed class GatedStore : KeyValueStore<Doc, string>
    {
        private readonly ConcurrentDictionary<string, KvRecord<Doc>> _rows = new();
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _waiting;

        public int Waiting => Volatile.Read(ref _waiting);
        public void Release() => _release.TrySetResult();
        public string? Owner(string id)
            => _rows.TryGetValue(id, out var row) && row.Managed?.TryGetValue(Field, out var value) == true ? value as string : null;

        protected override async Task<KvRecord<Doc>?> ReadAsync(string id, CancellationToken ct)
        {
            Interlocked.Increment(ref _waiting);
            await _release.Task.WaitAsync(ct);
            return _rows.TryGetValue(id, out var row) ? row : null;
        }

        protected override Task<IReadOnlyList<KvRecord<Doc>>> ScanAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<KvRecord<Doc>>>(_rows.Values.ToArray());

        protected override Task<IReadOnlyList<KvRecord<Doc>>> ScanBoundedAsync(int maxCandidates, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<KvRecord<Doc>>>(_rows.Values.Take(maxCandidates).ToArray());

        protected override Task WriteAsync(string id, KvRecord<Doc> record, CancellationToken ct)
        {
            _rows[id] = record;
            return Task.CompletedTask;
        }

        protected override Task<bool> RemoveAsync(string id, CancellationToken ct) => Task.FromResult(_rows.TryRemove(id, out _));

        protected override Task<int> ClearAsync(CancellationToken ct)
        {
            var count = _rows.Count;
            _rows.Clear();
            return Task.FromResult(count);
        }

        protected override void DescribeBackend(ICapabilities caps) { }
    }

    [Fact(DisplayName = "KeyValue: two scopes upserting one new id cannot both pass the cross-scope guard (NK-095)")]
    public async Task Concurrent_cross_scope_upserts_of_one_new_id_admit_exactly_one()
    {
        var store = new GatedStore();
        var a = Upsert(store, "A");
        var b = Upsert(store, "B");

        // Without the guard and its write held together, both reads would be waiting here at once.
        var deadline = DateTime.UtcNow.AddMilliseconds(500);
        while (store.Waiting < 2 && DateTime.UtcNow < deadline) await Task.Delay(10);
        store.Release();

        var outcomes = await Task.WhenAll(Settle(a), Settle(b));
        outcomes.Count(error => error is null).Should().Be(1, "exactly one scope may own the new id");
        outcomes.Single(error => error is not null).Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("cross-scope write");
        store.Owner("x").Should().Be(outcomes[0] is null ? "A" : "B", "the row belongs to the write that passed its guard");
    }

    private static Task Upsert(GatedStore store, string owner) => Task.Run(async () =>
    {
        using (ManagedFieldWriteScope.Enter(new Dictionary<string, object?>(StringComparer.Ordinal) { [Field] = owner }))
            await store.Upsert(new Doc { Id = "x", Title = owner });
    });

    private static async Task<Exception?> Settle(Task write)
    {
        try { await write; return null; }
        catch (Exception error) { return error; }
    }
}
