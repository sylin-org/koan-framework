using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Koan.Tests.AI.Unit.Specs.Router;

public sealed class DefaultAiRouterSpec
{
    private readonly ITestOutputHelper _output;

    public DefaultAiRouterSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public Task Picks_highest_priority_adapter_by_default()
        => TestPipeline.For<DefaultAiRouterSpec>(_output, nameof(Picks_highest_priority_adapter_by_default))
            .Assert(async _ =>
            {
                var registry = new InMemoryAdapterRegistry();
                registry.Add(new LowPriorityAdapter());
                registry.Add(new HighPriorityAdapter());

                var router = CreateRouter(registry, "wrw");
                var request = CreateRequest();

                var response = await router.PromptAsync(request, CancellationToken.None);

                response.AdapterId.Should().Be("high");
            })
            .RunAsync();

    [Fact]
    public Task Honors_round_robin_policy_hint()
        => TestPipeline.For<DefaultAiRouterSpec>(_output, nameof(Honors_round_robin_policy_hint))
            .Assert(async _ =>
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

                var ids = new[] { a.AdapterId, b.AdapterId };
                ids.Should().OnlyContain(id => id == first.Id || id == second.Id);

                TestAdapterBase used = a.AdapterId == first.Id ? first : second;
                used.LastRequest.Should().NotBeNull();
                used.LastRequest!.Route.Should().NotBeNull();
                used.LastRequest!.Route!.Policy.Should().Be("round-robin");
            })
            .RunAsync();

    [Fact]
    public Task Weighted_round_robin_respects_descriptor_weights()
        => TestPipeline.For<DefaultAiRouterSpec>(_output, nameof(Weighted_round_robin_respects_descriptor_weights))
            .Assert(async _ =>
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

                picks.Count(id => id == "weighted-heavy")
                    .Should()
                    .BeGreaterThan(picks.Count(id => id == "weighted-light"));
            })
            .RunAsync();

    [Fact]
    public Task Skips_adapters_that_cannot_serve()
        => TestPipeline.For<DefaultAiRouterSpec>(_output, nameof(Skips_adapters_that_cannot_serve))
            .Assert(async _ =>
            {
                var registry = new InMemoryAdapterRegistry();
                registry.Add(new RefusingAdapter());
                registry.Add(new HighPriorityAdapter());

                var router = CreateRouter(registry, "wrw");
                var request = CreateRequest();

                var response = await router.PromptAsync(request, CancellationToken.None);

                response.AdapterId.Should().Be("high");
            })
            .RunAsync();

    [Fact]
    public Task Embedding_model_request_prefers_adapter_with_model()
        => TestPipeline.For<DefaultAiRouterSpec>(_output, nameof(Embedding_model_request_prefers_adapter_with_model))
            .Assert(async _ =>
            {
                var registry = new InMemoryAdapterRegistry();
                var alpha = new EmbeddingsAdapter("alpha", new[] { "text-embedding-a" });
                var beta = new EmbeddingsAdapter("beta", new[] { "text-embedding-b" });
                registry.Add(alpha);
                registry.Add(beta);

                var router = CreateRouter(registry, "wrw", (adapter, index) =>
                {
                    var source = FakeSourceRegistry.CreateSource(adapter, "wrw", index);

                    if (adapter is EmbeddingsAdapter embeddings)
                    {
                        var caps = embeddings.SupportsModel("text-embedding-b")
                            ? new Dictionary<string, AiCapabilityConfig>
                            {
                                ["Chat"] = new() { Model = $"{adapter.Id}-chat" },
                                ["Embedding"] = new() { Model = "text-embedding-b" }
                            }
                            : new Dictionary<string, AiCapabilityConfig>
                            {
                                ["Chat"] = new() { Model = $"{adapter.Id}-chat" }
                            };

                        source = source with
                        {
                            Capabilities = caps,
                            Members = new List<AiMemberDefinition>
                            {
                                source.Members[0] with { Capabilities = caps }
                            }
                        };
                    }

                    return source;
                });
                var request = new AiEmbeddingsRequest
                {
                    Model = "text-embedding-b",
                    Input = new List<string> { "hello" }
                };

                var response = await router.EmbedAsync(request, CancellationToken.None);

                response.Model.Should().Be("beta");
            })
            .RunAsync();

    private static AiChatRequest CreateRequest()
        => new()
        {
            Messages = new List<AiMessage> { new("user", "hi") }
        };

    private static DefaultAiRouter CreateRouter(
        IAiAdapterRegistry registry,
        string defaultPolicy,
        Func<IAiAdapter, int, AiSourceDefinition>? sourceFactory = null)
    {
        var options = new StaticOptionsMonitor(new AiOptions { DefaultPolicy = defaultPolicy });
        var sourceRegistry = FakeSourceRegistry.FromAdapters(registry.All, defaultPolicy, sourceFactory);
        return new DefaultAiRouter(registry, sourceRegistry, options, NullLogger<DefaultAiRouter>.Instance);
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

        public bool SupportsModel(string model)
            => _models.Any(m => string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase));
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
        public AiChatRequest? LastRequest { get; private set; }

        public virtual bool CanServe(AiChatRequest request)
            => CanServeFunc?.Invoke(request) ?? true;

        public Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new AiChatResponse { Text = Id, AdapterId = Id });
        }

        public async IAsyncEnumerable<AiChatChunk> StreamAsync(AiChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            LastRequest = request;
            await Task.CompletedTask;
            yield break;
        }

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

    private sealed class FakeSourceRegistry : IAiSourceRegistry
    {
        private readonly Dictionary<string, AiSourceDefinition> _sources = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterSource(AiSourceDefinition source)
        {
            _sources[source.Name] = source;
        }

        public AiSourceDefinition? GetSource(string name)
        {
            _sources.TryGetValue(name, out var source);
            return source;
        }

        public bool TryGetSource(string name, out AiSourceDefinition? source)
        {
            source = GetSource(name);
            return source is not null;
        }

        public IReadOnlyCollection<string> GetSourceNames()
            => _sources.Keys.ToArray();

        public IReadOnlyCollection<AiSourceDefinition> GetAllSources()
            => _sources.Values
                .OrderByDescending(static s => s.Priority)
                .ToArray();

        public bool HasSource(string name) => _sources.ContainsKey(name);

        public IReadOnlyCollection<AiSourceDefinition> GetSourcesWithCapability(string capabilityName)
            => _sources.Values
                .Where(source => HasCapability(source, capabilityName))
                .OrderByDescending(static s => s.Priority)
                .ToArray();

        private static bool HasCapability(AiSourceDefinition source, string capabilityName)
        {
            if (source.Capabilities.TryGetValue(capabilityName, out _))
            {
                return true;
            }

            return source.Members.Any(m => m.Capabilities?.ContainsKey(capabilityName) == true);
        }

        public static FakeSourceRegistry FromAdapters(
            IReadOnlyList<IAiAdapter> adapters,
            string policy,
            Func<IAiAdapter, int, AiSourceDefinition>? factory)
        {
            var registry = new FakeSourceRegistry();

            for (var index = 0; index < adapters.Count; index++)
            {
                var adapter = adapters[index];
                var source = factory?.Invoke(adapter, index) ?? CreateSource(adapter, policy, index);
                registry.RegisterSource(source);
            }

            return registry;
        }

        public static AiSourceDefinition CreateSource(IAiAdapter adapter, string policy, int index)
        {
            var caps = new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = $"{adapter.Id}-chat" },
                ["Embedding"] = new() { Model = $"{adapter.Id}-embed" }
            };

            var member = new AiMemberDefinition
            {
                Name = $"{adapter.Id}::primary",
                ConnectionString = $"test://{adapter.Id}",
                Order = index,
                Weight = GetWeight(adapter),
                HealthState = MemberHealthState.Healthy,
                Capabilities = caps
            };

            return new AiSourceDefinition
            {
                Name = adapter.Id,
                Provider = adapter.Id,
                Priority = GetPriority(adapter, index),
                Policy = policy,
                Members = new List<AiMemberDefinition> { member },
                Capabilities = caps,
                Origin = "tests",
                IsAutoDiscovered = true
            };
        }

        private static int GetPriority(IAiAdapter adapter, int index)
        {
            var descriptor = adapter.GetType()
                .GetCustomAttributes(typeof(AiAdapterDescriptorAttribute), inherit: true)
                .OfType<AiAdapterDescriptorAttribute>()
                .FirstOrDefault();

            if (descriptor?.Priority is int priority && priority != 0)
            {
                return priority;
            }

            return 100 - index;
        }

        private static int GetWeight(IAiAdapter adapter)
        {
            var descriptor = adapter.GetType()
                .GetCustomAttributes(typeof(AiAdapterDescriptorAttribute), inherit: true)
                .OfType<AiAdapterDescriptorAttribute>()
                .FirstOrDefault();

            return Math.Max(1, descriptor?.Weight ?? 1);
        }
    }
}
