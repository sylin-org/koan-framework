using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Koan.Cache;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Entity;
using Koan.Cache.Identity;
using Koan.Core.Context;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Pipeline;

namespace Koan.Tests.Cache.Topology.Specs;

public sealed class EntityCacheEvictionSpec
{
    [Fact]
    public async Task Custom_key_template_and_captured_partition_are_shared_by_entry_eviction()
    {
        var writer = new RecordingWriter();
        var coordinator = Coordinator(
            writer,
            Policy("custom:{TypeName}:{Partition}:{Id}"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var note = new Note { Id = "n1" };

        Task<EntityCacheEviction> pending;
        using (EntityContext.Partition("captured"))
        {
            pending = coordinator.Evict<Note, string>(Delayed(note, release.Task), default);
        }

        using (EntityContext.Partition("changed"))
        {
            release.SetResult();
            var eviction = await pending;
            eviction.Absent.Should().Be(1);
            eviction.SourceCompleted.Should().BeTrue();
        }

        writer.Keys.Should().Equal(new CacheKey("custom:Note:captured:n1"));
        writer.Partitions.Should().Equal("captured");
        EntityContext.Current.Should().BeNull();
    }

    [Fact]
    public async Task Finite_eviction_is_sequential_and_does_not_read_ahead()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var yielded = 0;
        var writer = new RecordingWriter(async (call, _, _) =>
        {
            if (call == 1)
            {
                firstEntered.SetResult();
                await releaseFirst.Task;
            }

            return true;
        });
        var coordinator = Coordinator(writer, Policy(CacheableAttribute.DefaultKeyTemplate));

        var pending = coordinator.Evict<Note, string>(CountedSource(), default);
        await firstEntered.Task;

        yielded.Should().Be(1, "the terminal must not request a second source item while the first removal is pending");
        writer.Calls.Should().Be(1);

        releaseFirst.SetResult();
        var eviction = await pending;

        eviction.Enumerated.Should().Be(3);
        eviction.Removed.Should().Be(3);
        eviction.Confirmed.Should().Be(3);
        eviction.SourceCompleted.Should().BeTrue();
        writer.MaxConcurrent.Should().Be(1);

        async IAsyncEnumerable<Entity<Note, string>> CountedSource()
        {
            foreach (var id in new[] { "one", "two", "three" })
            {
                yielded++;
                yield return new Note { Id = id };
                await Task.Yield();
            }
        }
    }

    [Fact]
    public async Task Default_identifier_is_reported_as_a_skip_without_cache_io()
    {
        var writer = new RecordingWriter();
        var coordinator = Coordinator(writer, Policy(CacheableAttribute.DefaultKeyTemplate, typeof(IntNote)));

        var eviction = await coordinator.Evict<IntNote, int>(IntItems(new IntNote()), default);

        eviction.Enumerated.Should().Be(1);
        eviction.Skipped.Should().Be(1);
        eviction.Confirmed.Should().Be(0);
        eviction.SourceCompleted.Should().BeTrue();
        writer.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Removal_failure_carries_only_the_confirmed_prefix()
    {
        var writer = new RecordingWriter((call, _, _) =>
            call == 2
                ? ValueTask.FromException<bool>(new IOException("store unavailable"))
                : ValueTask.FromResult(true));
        var coordinator = Coordinator(writer, Policy(CacheableAttribute.DefaultKeyTemplate));

        var action = () => coordinator.Evict<Note, string>(Items(
            new Note { Id = "one" },
            new Note { Id = "two" },
            new Note { Id = "three" }), default);

        var error = (await action.Should().ThrowAsync<EntityCacheEvictionException>()).Which;
        error.Failure.Should().Be(EntityCacheEvictionException.FailureKind.EvictionFailed);
        error.Eviction.Enumerated.Should().Be(2);
        error.Eviction.Removed.Should().Be(1);
        error.Eviction.Failed.Should().Be(1);
        error.Eviction.SourceCompleted.Should().BeFalse();
        writer.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Source_failure_is_distinct_and_preserves_completed_removals()
    {
        var writer = new RecordingWriter();
        var coordinator = Coordinator(writer, Policy(CacheableAttribute.DefaultKeyTemplate));

        var action = () => coordinator.Evict<Note, string>(FailingSource(), default);

        var error = (await action.Should().ThrowAsync<EntityCacheEvictionException>()).Which;
        error.Failure.Should().Be(EntityCacheEvictionException.FailureKind.SourceFailed);
        error.Eviction.Enumerated.Should().Be(1);
        error.Eviction.Absent.Should().Be(1);
        error.Eviction.Failed.Should().Be(0);
        error.Eviction.SourceCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Cancellation_carries_the_confirmed_prefix()
    {
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new RecordingWriter(async (call, _, ct) =>
        {
            if (call == 1)
            {
                return true;
            }

            secondEntered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return true;
        });
        var coordinator = Coordinator(writer, Policy(CacheableAttribute.DefaultKeyTemplate));
        using var cancellation = new CancellationTokenSource();

        var pending = coordinator.Evict<Note, string>(Items(
            new Note { Id = "one" },
            new Note { Id = "two" },
            new Note { Id = "three" }), cancellation.Token);
        await secondEntered.Task;
        cancellation.Cancel();

        var action = async () => await pending;
        var error = (await action.Should().ThrowAsync<EntityCacheEvictionCanceledException>()).Which;
        error.Eviction.Enumerated.Should().Be(2);
        error.Eviction.Removed.Should().Be(1);
        error.Eviction.Failed.Should().Be(1);
        error.Eviction.SourceCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Missing_policy_fails_before_the_source_is_enumerated()
    {
        var enumerated = false;
        var coordinator = Coordinator(new RecordingWriter());

        var action = () => coordinator.Evict<Note, string>(Source(), default);

        var error = (await action.Should().ThrowAsync<InvalidOperationException>()).Which;
        error.Message.Should().Contain("[Cacheable]");
        enumerated.Should().BeFalse();

        async IAsyncEnumerable<Entity<Note, string>> Source()
        {
            enumerated = true;
            yield return new Note { Id = "one" };
            await Task.CompletedTask;
        }
    }

    private static EntityCacheEvictionCoordinator Coordinator(
        ICacheWriter writer,
        params CachePolicyDescriptor[] policies)
    {
        var plan = new EntityCachePlan(
            new StubPolicyRegistry(policies),
            Array.Empty<IReadFilterContributor>());
        return new EntityCacheEvictionCoordinator(
            new IdentityWriter(writer),
            plan,
            new KoanContextCarrierRegistry(Array.Empty<IKoanContextCarrier>()));
    }

    private sealed class IdentityWriter(ICacheWriter inner) : ICacheIdentityWriter
    {
        public ValueTask<bool> Remove(CacheKey key, Type? subject, CancellationToken ct)
            => inner.Remove(key, ct);
    }

    private static CachePolicyDescriptor Policy(string template, Type? entityType = null)
        => new(
            CacheScope.Entity,
            template,
            CacheStrategy.GetOrSet,
            CacheConsistencyMode.Strict,
            CacheTier.Layered,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1),
            null,
            null,
            [(entityType ?? typeof(Note)).Name],
            null,
            null,
            null,
            null,
            true,
            new Dictionary<string, string>(),
            null,
            entityType ?? typeof(Note));

    private static async IAsyncEnumerable<Entity<Note, string>> Items(params Note[] notes)
    {
        foreach (var note in notes)
        {
            yield return note;
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<Entity<IntNote, int>> IntItems(params IntNote[] notes)
    {
        foreach (var note in notes)
        {
            yield return note;
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<Entity<Note, string>> Delayed(
        Note note,
        Task release,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await release.WaitAsync(ct);
        yield return note;
    }

    private static async IAsyncEnumerable<Entity<Note, string>> FailingSource()
    {
        yield return new Note { Id = "one" };
        await Task.Yield();
        throw new IOException("source failed");
    }

    private sealed class Note : Entity<Note>;

    private sealed class IntNote : Entity<IntNote, int>;

    private sealed class StubPolicyRegistry(params CachePolicyDescriptor[] policies) : ICachePolicyRegistry
    {
        public IReadOnlyList<CachePolicyDescriptor> GetPoliciesFor(Type type)
            => policies.Where(policy => policy.DeclaringType == type).ToArray();

        public IReadOnlyList<CachePolicyDescriptor> GetPoliciesFor(System.Reflection.MemberInfo member) => [];

        public IReadOnlyList<CachePolicyDescriptor> GetAllPolicies() => policies;

        public bool TryGetPolicy(Type type, [NotNullWhen(true)] out CachePolicyDescriptor? descriptor)
        {
            descriptor = GetPoliciesFor(type).FirstOrDefault();
            return descriptor is not null;
        }

        public bool TryGetPolicy(
            System.Reflection.MemberInfo member,
            [NotNullWhen(true)] out CachePolicyDescriptor? descriptor)
        {
            descriptor = null;
            return false;
        }
    }

    private sealed class RecordingWriter(
        Func<int, CacheKey, CancellationToken, ValueTask<bool>>? remove = null) : ICacheWriter
    {
        private int _concurrent;

        public List<CacheKey> Keys { get; } = [];

        public List<string?> Partitions { get; } = [];

        public int Calls { get; private set; }

        public int MaxConcurrent { get; private set; }

        public ValueTask SetAsync<T>(CacheKey key, T value, CacheEntryOptions options, CancellationToken ct)
            => throw new NotSupportedException();

        public async ValueTask<bool> Remove(CacheKey key, CancellationToken ct)
        {
            Calls++;
            var call = Calls;
            Keys.Add(key);
            Partitions.Add(EntityContext.Current?.Partition);
            var concurrent = Interlocked.Increment(ref _concurrent);
            MaxConcurrent = Math.Max(MaxConcurrent, concurrent);
            try
            {
                return remove is null
                    ? false
                    : await remove(call, key, ct);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }

        public ValueTask Touch(CacheKey key, CacheEntryOptions options, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
