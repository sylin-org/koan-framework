using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.AI;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.AI.Tests;

public sealed class DefaultAiRouterTests
{
    [Fact]
    public async Task Picks_highest_priority_adapter_by_default()
    {
        var registry = new InMemoryAdapterRegistry();
        registry.Add(new LowPriorityAdapter());
        registry.Add(new HighPriorityAdapter());

        var router = CreateRouter(registry, "wrw");
        var request = CreateRequest();

        var response = await router.PromptAsync(request, CancellationToken.None);

        response.AdapterId.Should().Be("high");
    }

    [Fact]
    public async Task Honors_round_robin_policy_hint()
    {
        var registry = new InMemoryAdapterRegistry();
        var first = new HighPriorityAdapter();
        var second = new AnotherHighPriorityAdapter();
        registry.Add(first);
        registry.Add(second);

        var router = CreateRouter(registry, "priority");
        var request = CreateRequest() with
        {
            Route = new AiRouteHints { Policy = "round-robin" }
        };

        var a = await router.PromptAsync(request, CancellationToken.None);
        var b = await router.PromptAsync(request, CancellationToken.None);

        new[] { a.AdapterId, b.AdapterId }
            .Should()
            .Contain(new[] { first.Id, second.Id });
        a.AdapterId.Should().NotBe(b.AdapterId);
    }

    [Fact]
    public async Task Weighted_round_robin_respects_descriptor_weights()
    {
        var registry = new InMemoryAdapterRegistry();
        registry.Add(new WeightedHeavyAdapter());
        registry.Add(new WeightedLightAdapter());

        var router = CreateRouter(registry, "wrw");
        var request = CreateRequest();

        var picks = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            var response = await router.PromptAsync(request, CancellationToken.None);
            picks.Add(response.AdapterId!);
        }

        picks.Count(id => id == "weighted-heavy").Should().BeGreaterThan(picks.Count(id => id == "weighted-light"));
    }

    [Fact]
    public async Task Skips_adapters_that_cannot_serve()
    {
        var registry = new InMemoryAdapterRegistry();
        registry.Add(new RefusingAdapter());
        registry.Add(new HighPriorityAdapter());

        var router = CreateRouter(registry, "wrw");
        var request = CreateRequest();

        var response = await router.PromptAsync(request, CancellationToken.None);
        response.AdapterId.Should().Be("high");
    }

    [Fact]
    public async Task Embedding_model_request_prefers_adapter_with_model()
    {
        var registry = new InMemoryAdapterRegistry();
        registry.Add(new EmbeddingsAdapter("alpha", new[] { "text-embedding-a" }));
        registry.Add(new EmbeddingsAdapter("beta", new[] { "text-embedding-b" }));

        var router = CreateRouter(registry, "wrw");
        var request = new AiEmbeddingsRequest
        {
            Model = "text-embedding-b",
            Input = new List<string> { "hello" }
        };

        var response = await router.EmbedAsync(request, CancellationToken.None);
        response.Model.Should().Be("beta");
    }

    private static AiChatRequest CreateRequest()
        => new()
        {
            Messages = new List<AiMessage> { new("user", "hi") }
        };

    private static DefaultAiRouter CreateRouter(IAiAdapterRegistry registry, string defaultPolicy)
    {
        var options = new StaticOptionsMonitor(new AiOptions { DefaultPolicy = defaultPolicy });
        return new DefaultAiRouter(registry, options, NullLogger<DefaultAiRouter>.Instance);
    }

    [AiAdapterDescriptor(priority: 1)]
    private sealed class LowPriorityAdapter : TestAdapterBase
    {
        public LowPriorityAdapter() : base("low") { }
    }

    [AiAdapterDescriptor(priority: 10, Weight = 2)]
    private sealed class HighPriorityAdapter : TestAdapterBase
    {
        public HighPriorityAdapter() : base("high") { }
    }

    [AiAdapterDescriptor(priority: 10, Weight = 2)]
    private sealed class AnotherHighPriorityAdapter : TestAdapterBase
    {
        public AnotherHighPriorityAdapter() : base("another-high") { }
    }

    [AiAdapterDescriptor(priority: 5)]
    private sealed class RefusingAdapter : TestAdapterBase
    {
        public RefusingAdapter() : base("refuse")
        {
            CanServeFunc = _ => false;
        }
    }

    private sealed class EmbeddingsAdapter : TestAdapterBase
    {
        private readonly IReadOnlyList<AiModelDescriptor> _models;

        public EmbeddingsAdapter(string id, IEnumerable<string> models) : base(id)
        {
            _models = models.Select(name => new AiModelDescriptor
            {
                Name = name,
                AdapterId = id,
                AdapterType = "test"
            }).ToList();
        }

        public override Task<IReadOnlyList<AiModelDescriptor>> ListModelsAsync(CancellationToken ct = default)
            => Task.FromResult(_models);
    }

    [AiAdapterDescriptor(priority: 5, Weight = 3)]
    private sealed class WeightedHeavyAdapter : TestAdapterBase
    {
        public WeightedHeavyAdapter() : base("weighted-heavy") { }
    }

    [AiAdapterDescriptor(priority: 5, Weight = 1)]
    private sealed class WeightedLightAdapter : TestAdapterBase
    {
        public WeightedLightAdapter() : base("weighted-light") { }
    }

    private abstract class TestAdapterBase : IAiAdapter
    {
        protected TestAdapterBase(string id)
        {
            Id = id;
            Name = id;
        }

        public string Id { get; }
        public string Name { get; }
        public string Type => "test";
        public Func<AiChatRequest, bool>? CanServeFunc { get; set; }
        public virtual bool CanServe(AiChatRequest request)
            => CanServeFunc?.Invoke(request) ?? true;

        public Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiChatResponse { Text = Id });

        public IAsyncEnumerable<AiChatChunk> StreamAsync(AiChatRequest request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<AiChatChunk>();

        public Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiEmbeddingsResponse { Model = Id });

        public virtual Task<IReadOnlyList<AiModelDescriptor>> ListModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AiModelDescriptor>>(new List<AiModelDescriptor>());

        public Task<AiCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
            => Task.FromResult(new AiCapabilities
            {
                AdapterId = Id,
                AdapterType = Type,
                SupportsChat = true,
                SupportsStreaming = true,
                SupportsEmbeddings = true
            });
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<AiOptions>
    {
        private readonly AiOptions _value;

        public StaticOptionsMonitor(AiOptions value) => _value = value;

        public AiOptions CurrentValue => _value;

        public AiOptions Get(string? name) => _value;

        public IDisposable OnChange(Action<AiOptions, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
