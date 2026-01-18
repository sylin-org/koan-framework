using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.AI;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Koan.AI.Pipeline;
using Xunit;

namespace Koan.Tests.AI.Unit.Specs.Routing;

public sealed class AiRoutingEngineSpec
{
    [Fact]
    public void ResolveChat_without_hints_selects_highest_priority_source()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new TestAdapter("alpha"));
        adapters.Add(new TestAdapter("beta"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "alpha",
            provider: "alpha",
            priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "alpha-chat" }
            }));
        sources.RegisterSource(CreateSource(
            name: "beta",
            provider: "beta",
            priority: 90,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "beta-chat" }
            }));

        var engine = new AiRoutingEngine(adapters, sources);

        var resolution = engine.ResolveChat(new AiChatRequest
        {
            Messages = new List<AiMessage> { new("user", "hi") }
        });

        resolution.Source.Name.Should().Be("beta");
        resolution.Adapter.Id.Should().Be("beta");
        resolution.Member.Name.Should().Be("beta::primary");
        resolution.EffectiveModel.Should().Be("beta-chat");
    }

    [Fact]
    public void ResolveChat_with_member_hint_pins_member_and_model()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new TestAdapter("alpha"));

        var memberCapabilities = new Dictionary<string, AiCapabilityConfig>
        {
            ["Chat"] = new() { Model = "alpha-special" }
        };

        var members = new[]
        {
            CreateMember("alpha", "primary", order: 0, MemberHealthState.Healthy),
            CreateMember("alpha", "secondary", order: 1, MemberHealthState.Healthy, memberCapabilities)
        };

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "alpha",
            provider: "alpha",
            priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "alpha-default" }
            },
            members: members));

        var engine = new AiRoutingEngine(adapters, sources);

        var resolution = engine.ResolveChat(new AiChatRequest
        {
            Messages = new List<AiMessage> { new("user", "hey") },
            Route = new AiRouteHints { AdapterId = "alpha::secondary" }
        });

        resolution.Member.Name.Should().Be("alpha::secondary");
        resolution.EffectiveModel.Should().Be("alpha-special");
    }

    [Fact]
    public void ResolveChat_skips_unhealthy_member_when_first_in_order()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new TestAdapter("alpha"));

        var members = new[]
        {
            CreateMember("alpha", "down", order: 0, MemberHealthState.Unhealthy),
            CreateMember("alpha", "healthy", order: 1, MemberHealthState.Healthy)
        };

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "alpha",
            provider: "alpha",
            priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "alpha-chat" }
            },
            members: members));

        var engine = new AiRoutingEngine(adapters, sources);

        var resolution = engine.ResolveChat(new AiChatRequest
        {
            Messages = new List<AiMessage> { new("user", "hi") }
        });

        resolution.Member.Name.Should().Be("alpha::healthy");
    }

    [Fact]
    public void ResolveEmbeddings_with_model_and_no_capability_falls_back_to_first_source()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new TestAdapter("alpha"));
        adapters.Add(new TestAdapter("beta"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "beta",
            provider: "beta",
            priority: 90,
            capabilities: new Dictionary<string, AiCapabilityConfig>()));
        sources.RegisterSource(CreateSource(
            name: "alpha",
            provider: "alpha",
            priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>()));

        var engine = new AiRoutingEngine(adapters, sources);

        var resolution = engine.ResolveEmbeddings(new AiEmbeddingsRequest
        {
            Model = "text-embedding-x",
            Input = new List<string> { "hello" }
        });

        resolution.Source.Name.Should().Be("beta");
        resolution.EffectiveModel.Should().Be("text-embedding-x");
    }

    [Fact]
    public void ResolveChat_with_unknown_source_hint_throws_informative_error()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new TestAdapter("alpha"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "alpha",
            provider: "alpha",
            priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "alpha-chat" }
            }));

        var engine = new AiRoutingEngine(adapters, sources);

        var act = () => engine.ResolveChat(new AiChatRequest
        {
            Messages = new List<AiMessage> { new("user", "hi") },
            Route = new AiRouteHints { AdapterId = "missing" }
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Source 'missing' not found. Available sources: alpha");
    }

    [Fact]
    public void ResolveChat_without_registered_adapter_throws()
    {
        var adapters = new InMemoryAdapterRegistry();

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "alpha",
            provider: "alpha",
            priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "alpha-chat" }
            }));

        var engine = new AiRoutingEngine(adapters, sources);

        var act = () => engine.ResolveChat(new AiChatRequest
        {
            Messages = new List<AiMessage> { new("user", "hi") }
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("No adapter found for provider 'alpha'. Available adapters: ");
    }

    [Fact]
    public void ResolveChat_with_missing_member_hint_surfaces_available_members()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new TestAdapter("alpha"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "alpha",
            provider: "alpha",
            priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "alpha-chat" }
            }));

        var engine = new AiRoutingEngine(adapters, sources);

        var act = () => engine.ResolveChat(new AiChatRequest
        {
            Messages = new List<AiMessage> { new("user", "hi") },
            Route = new AiRouteHints { AdapterId = "alpha::missing" }
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Member 'alpha::missing' not found in source 'alpha'. Available members: alpha::primary");
    }

    private static AiSourceDefinition CreateSource(
        string name,
        string provider,
        int priority,
        IReadOnlyDictionary<string, AiCapabilityConfig>? capabilities,
        IEnumerable<AiMemberDefinition>? members = null)
    {
        var capabilityMap = capabilities ?? new Dictionary<string, AiCapabilityConfig>();
        var memberList = members?.ToList() ?? new List<AiMemberDefinition>
        {
            CreateMember(name, "primary", order: 0, MemberHealthState.Healthy, capabilityMap)
        };

        return new AiSourceDefinition
        {
            Name = name,
            Provider = provider,
            Priority = priority,
            Members = memberList,
            Capabilities = capabilityMap
        };
    }

    private static AiMemberDefinition CreateMember(
        string source,
        string memberKey,
        int order,
        MemberHealthState health,
        IReadOnlyDictionary<string, AiCapabilityConfig>? capabilities = null)
        => new()
        {
            Name = $"{source}::{memberKey}",
            ConnectionString = $"test://{source}/{memberKey}",
            Order = order,
            HealthState = health,
            Capabilities = capabilities
        };

    private sealed class FakeSourceRegistry : IAiSourceRegistry
    {
        private readonly Dictionary<string, AiSourceDefinition> _sources = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterSource(AiSourceDefinition source)
        {
            _sources[source.Name] = source;
        }

        public AiSourceDefinition? GetSource(string name)
            => _sources.TryGetValue(name, out var source) ? source : null;

        public bool TryGetSource(string name, out AiSourceDefinition? source)
        {
            source = GetSource(name);
            return source is not null;
        }

        public IReadOnlyCollection<string> GetSourceNames()
            => _sources.Keys.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray();

        public IReadOnlyCollection<AiSourceDefinition> GetAllSources()
            => _sources.Values
                .OrderByDescending(static s => s.Priority)
                .ThenBy(static s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        public bool HasSource(string name)
            => _sources.ContainsKey(name);

        public IReadOnlyCollection<AiSourceDefinition> GetSourcesWithCapability(string capabilityName)
            => _sources.Values
                .Where(source => HasCapability(source, capabilityName))
                .OrderByDescending(static s => s.Priority)
                .ThenBy(static s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        private static bool HasCapability(AiSourceDefinition source, string capabilityName)
        {
            if (source.Capabilities.ContainsKey(capabilityName))
            {
                return true;
            }

            return source.Members.Any(member => member.Capabilities?.ContainsKey(capabilityName) == true);
        }
    }

    private sealed class TestAdapter : IAiAdapter
    {
        private readonly string _id;

        public TestAdapter(string id)
        {
            _id = id;
        }

        public string Id => _id;
        public string Name => _id;
        public string Type => _id;

        public bool CanServe(AiChatRequest request) => true;

        public Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiChatResponse { AdapterId = _id });

        public async IAsyncEnumerable<AiChatChunk> StreamAsync(AiChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }

        public Task<AiEmbeddingsResponse> EmbedAsync(AiEmbeddingsRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiEmbeddingsResponse { Model = request.Model ?? _id });

        public Task<IReadOnlyList<AiModelDescriptor>> ListModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AiModelDescriptor>>(Array.Empty<AiModelDescriptor>());

        public Task<AiCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
            => Task.FromResult(new AiCapabilities
            {
                AdapterId = _id,
                AdapterType = _id,
                SupportsChat = true,
                SupportsStreaming = true,
                SupportsEmbeddings = true
            });
    }
}
