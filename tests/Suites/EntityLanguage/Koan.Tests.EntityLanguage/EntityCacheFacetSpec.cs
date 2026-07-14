using Koan.Cache.Abstractions;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace Koan.Tests.EntityLanguage;

public sealed class EntityCacheFacetSpec
{
    [Fact]
    public void Explain_projects_materialized_policy_without_cache_io()
    {
        var registry = new StubPolicyRegistry(Policy(TimeSpan.FromMinutes(5)));
        using var provider = Services(registry).BuildServiceProvider();
        using var scope = AppHost.PushScope(provider);

        var explanation = Todo.Cache.Explain();

        explanation.Capability.Should().Be(CacheConstants.Capabilities.Entity);
        explanation.EntityType.Should().Be(typeof(Todo));
        explanation.IsConfigured.Should().BeTrue();
        explanation.Policies.Should().ContainSingle()
            .Which.AbsoluteTtl.Should().Be(TimeSpan.FromMinutes(5));
        explanation.Summary().Should().Contain(nameof(Todo));
    }

    [Fact]
    public void Explain_resolves_the_current_host_on_every_call()
    {
        using var first = Services(new StubPolicyRegistry(Policy(TimeSpan.FromSeconds(10)))).BuildServiceProvider();
        using var second = Services(new StubPolicyRegistry(Policy(TimeSpan.FromSeconds(20)))).BuildServiceProvider();

        EntityCacheExplanation firstExplanation;
        using (AppHost.PushScope(first))
        {
            firstExplanation = Todo.Cache.Explain();
        }

        using (AppHost.PushScope(second))
        {
            Todo.Cache.Explain().Policies.Single().AbsoluteTtl.Should().Be(TimeSpan.FromSeconds(20));
        }

        firstExplanation.Policies.Single().AbsoluteTtl.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Explain_fails_with_the_typed_host_context_error_when_cache_services_are_missing()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var scope = AppHost.PushScope(provider);

        var action = () => Todo.Cache.Explain();

        var error = action.Should().Throw<KoanHostContextException>().Which;
        error.Failure.Should().Be(KoanHostContextException.FailureKind.MissingService);
        error.RequiredService.Should().Be(typeof(ICachePolicyRegistry));
        error.Operation.Should().Be("entity cache policy explanation");
    }

    [Fact]
    public async Task Existing_type_scoped_operations_forward_through_the_module_owned_facet()
    {
        var registry = new StubPolicyRegistry(Policy(TimeSpan.FromMinutes(5)));
        var client = new StubCacheClient();
        using var provider = Services(registry).AddSingleton<ICacheClient>(client).BuildServiceProvider();
        using var scope = AppHost.PushScope(provider);

        (await Todo.Cache.Count()).Should().Be(2);
        (await Todo.Cache.Any()).Should().BeTrue();
        (await Todo.Cache.Flush()).Should().Be(2);

        client.CountedTags.Should().Equal(nameof(Todo));
        client.FlushedTags.Should().Equal(nameof(Todo));
    }

    private static IServiceCollection Services(ICachePolicyRegistry registry)
        => new ServiceCollection().AddSingleton(registry);

    private static CachePolicyDescriptor Policy(TimeSpan ttl)
        => new(
            CacheScope.Entity,
            CacheableAttribute.DefaultKeyTemplate,
            CacheStrategy.GetOrSet,
            CacheConsistencyMode.Strict,
            CacheTier.Layered,
            ttl,
            TimeSpan.FromSeconds(30),
            null,
            null,
            [nameof(Todo)],
            null,
            null,
            null,
            null,
            true,
            new Dictionary<string, string>(),
            null,
            typeof(Todo));

    private sealed class Todo : Entity<Todo>
    {
    }

    private sealed class StubPolicyRegistry(params CachePolicyDescriptor[] policies) : ICachePolicyRegistry
    {
        public IReadOnlyList<CachePolicyDescriptor> GetPoliciesFor(Type type)
            => type == typeof(Todo) ? policies : [];

        public IReadOnlyList<CachePolicyDescriptor> GetPoliciesFor(System.Reflection.MemberInfo member) => [];
        public IReadOnlyList<CachePolicyDescriptor> GetAllPolicies() => policies;

        public bool TryGetPolicy(Type type, [NotNullWhen(true)] out CachePolicyDescriptor? descriptor)
        {
            descriptor = GetPoliciesFor(type).FirstOrDefault();
            return descriptor is not null;
        }

        public bool TryGetPolicy(System.Reflection.MemberInfo member, [NotNullWhen(true)] out CachePolicyDescriptor? descriptor)
        {
            descriptor = null;
            return false;
        }
    }

    private sealed class StubCacheClient : ICacheClient
    {
        public IReadOnlyCollection<string> CountedTags { get; private set; } = [];
        public IReadOnlyCollection<string> FlushedTags { get; private set; } = [];

        public ValueTask<long> FlushTags(IReadOnlyCollection<string> tags, CancellationToken ct)
        {
            FlushedTags = tags;
            return ValueTask.FromResult(2L);
        }

        public ValueTask<long> CountTags(IReadOnlyCollection<string> tags, CancellationToken ct)
        {
            CountedTags = tags;
            return ValueTask.FromResult(2L);
        }

        public ICacheEntryBuilder<T> CreateEntry<T>(CacheKey key) => throw new NotSupportedException();
        public CacheScopeHandle BeginScope(string scopeId, string? region = null) => throw new NotSupportedException();
        public ValueTask<CacheFetchResult> Get(CacheKey key, CacheEntryOptions options, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<T?> GetAsync<T>(CacheKey key, CacheEntryOptions options, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<T?> GetOrAddAsync<T>(CacheKey key, Func<CancellationToken, ValueTask<T?>> valueFactory, CacheEntryOptions options, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<bool> Exists(CacheKey key, CacheEntryOptions options, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask SetAsync<T>(CacheKey key, T value, CacheEntryOptions options, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<bool> Remove(CacheKey key, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask Touch(CacheKey key, CacheEntryOptions options, CancellationToken ct) => throw new NotSupportedException();
    }
}
