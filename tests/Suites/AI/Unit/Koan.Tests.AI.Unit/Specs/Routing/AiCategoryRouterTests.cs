using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.AI;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Options;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Koan.AI.Context;
using Koan.AI.Pipeline;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Tests.AI.Unit.Specs.Routing;

/// <summary>
/// Tests for AiCategoryRouter: category-aware routing, OCR Via delegation, and scope integration.
/// </summary>
[Trait("ADR", "AI-0021")]
[Trait("Category", "Unit")]
public sealed class AiCategoryRouterTests
{
    private static AiCategoryRouter CreateRouter(
        InMemoryAdapterRegistry adapters,
        FakeSourceRegistry sources,
        AiOptions? options = null)
        => new(adapters, sources, Options.Create(options ?? new AiOptions()));

    // ========================================================================
    // OCR Via Delegation
    // ========================================================================

    [Fact]
    public void Ocr_with_no_dedicated_adapter_delegates_via_Chat()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new ChatOnlyAdapter("ollama"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "ollama",
            provider: "ollama",
            priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "llama3" }
            }));

        var router = CreateRouter(adapters, sources);

        var resolution = router.Resolve("Ocr");

        resolution.Category.Should().Be("Chat", "OCR should delegate via Chat when no IOcrAdapter exists");
        resolution.Adapter.Should().BeAssignableTo<IChatAdapter>();
        resolution.Source.Name.Should().Be("ollama");
    }

    [Fact]
    public void Ocr_with_dedicated_adapter_resolves_directly()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new OcrOnlyAdapter("ocr-engine"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "ocr-engine",
            provider: "ocr-engine",
            priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Ocr"] = new() { Model = "tesseract-5" }
            }));

        var router = CreateRouter(adapters, sources);

        var resolution = router.Resolve("Ocr");

        resolution.Category.Should().Be("Ocr", "OCR should resolve directly when IOcrAdapter exists");
        resolution.Adapter.Should().BeAssignableTo<IOcrAdapter>();
    }

    [Fact]
    public void Ocr_via_delegation_uses_default_model_when_not_configured()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new ChatOnlyAdapter("provider"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "provider",
            provider: "provider",
            priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "default-chat" }
            }));

        var router = CreateRouter(adapters, sources);

        var resolution = router.Resolve("Ocr");

        // Via delegation with default Ocr model "glm-ocr" should override Chat's default
        resolution.EffectiveModel.Should().Be("glm-ocr",
            "OCR default model should be 'glm-ocr' per category definition");
    }

    // ========================================================================
    // Category Resolution
    // ========================================================================

    [Fact]
    public void Chat_resolves_to_IChatAdapter()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new ChatOnlyAdapter("chat-provider"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "chat-provider",
            provider: "chat-provider",
            priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "gpt-4" }
            }));

        var router = CreateRouter(adapters, sources);

        var resolution = router.Resolve("Chat");

        resolution.Category.Should().Be("Chat");
        resolution.Adapter.Should().BeAssignableTo<IChatAdapter>();
        resolution.EffectiveModel.Should().Be("gpt-4");
    }

    [Fact]
    public void Embed_resolves_to_IEmbedAdapter()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new EmbedOnlyAdapter("embed-provider"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "embed-provider",
            provider: "embed-provider",
            priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Embedding"] = new() { Model = "text-embedding-3" }
            }));

        var router = CreateRouter(adapters, sources);

        var resolution = router.Resolve("Embed");

        resolution.Category.Should().Be("Embed");
        resolution.Adapter.Should().BeAssignableTo<IEmbedAdapter>();
        resolution.EffectiveModel.Should().Be("text-embedding-3");
    }

    [Fact]
    public void Unknown_category_throws()
    {
        var adapters = new InMemoryAdapterRegistry();
        var sources = new FakeSourceRegistry();
        var router = CreateRouter(adapters, sources);

        var act = () => router.Resolve("Unknown");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Unknown AI category*");
    }

    // ========================================================================
    // Per-Category Config Override
    // ========================================================================

    [Fact]
    public void Category_options_source_overrides_default_resolution()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new ChatEmbedAdapter("alpha"));
        adapters.Add(new ChatEmbedAdapter("beta"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "alpha", provider: "alpha", priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "alpha-chat" },
                ["Embedding"] = new() { Model = "alpha-embed" }
            }));
        sources.RegisterSource(CreateSource(
            name: "beta", provider: "beta", priority: 90,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "beta-chat" },
                ["Embedding"] = new() { Model = "beta-embed" }
            }));

        // Config: Chat → alpha, Embed uses default (beta — highest priority)
        var options = new AiOptions
        {
            Chat = new() { Source = "alpha" }
        };

        var router = CreateRouter(adapters, sources, options);

        var chatResolution = router.Resolve("Chat");
        var embedResolution = router.Resolve("Embed");

        chatResolution.Source.Name.Should().Be("alpha", "Chat pinned to alpha via config");
        embedResolution.Source.Name.Should().Be("beta", "Embed uses highest priority (beta)");
    }

    // ========================================================================
    // Scope Integration
    // ========================================================================

    [Fact]
    public void Scope_overrides_source_per_category()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new ChatEmbedAdapter("alpha"));
        adapters.Add(new ChatEmbedAdapter("beta"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "alpha", provider: "alpha", priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "alpha-chat" },
                ["Embedding"] = new() { Model = "alpha-embed" }
            }));
        sources.RegisterSource(CreateSource(
            name: "beta", provider: "beta", priority: 90,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "beta-chat" },
                ["Embedding"] = new() { Model = "beta-embed" }
            }));

        var router = CreateRouter(adapters, sources);

        using (new AiCategoryScope(chatSource: "alpha", embedSource: "beta"))
        {
            var chatResolution = router.Resolve("Chat");
            var embedResolution = router.Resolve("Embed");

            chatResolution.Source.Name.Should().Be("alpha");
            embedResolution.Source.Name.Should().Be("beta");
        }
    }

    [Fact]
    public void Scope_all_applies_to_every_category()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new ChatEmbedAdapter("alpha"));
        adapters.Add(new ChatEmbedAdapter("beta"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "alpha", provider: "alpha", priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "alpha-chat" },
                ["Embedding"] = new() { Model = "alpha-embed" }
            }));
        sources.RegisterSource(CreateSource(
            name: "beta", provider: "beta", priority: 90,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "beta-chat" },
                ["Embedding"] = new() { Model = "beta-embed" }
            }));

        var router = CreateRouter(adapters, sources);

        using (new AiCategoryScope(all: "alpha"))
        {
            var chatResolution = router.Resolve("Chat");
            var embedResolution = router.Resolve("Embed");

            chatResolution.Source.Name.Should().Be("alpha", "all scope should apply to Chat");
            embedResolution.Source.Name.Should().Be("alpha", "all scope should apply to Embed");
        }
    }

    [Fact]
    public void Scope_disposes_cleanly_restoring_default_routing()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new ChatEmbedAdapter("alpha"));
        adapters.Add(new ChatEmbedAdapter("beta"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "alpha", provider: "alpha", priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "alpha-chat" }
            }));
        sources.RegisterSource(CreateSource(
            name: "beta", provider: "beta", priority: 90,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "beta-chat" }
            }));

        var router = CreateRouter(adapters, sources);

        using (new AiCategoryScope(chatSource: "alpha"))
        {
            router.Resolve("Chat").Source.Name.Should().Be("alpha");
        }

        // After dispose, reverts to default (beta = highest priority)
        router.Resolve("Chat").Source.Name.Should().Be("beta");
    }

    // ========================================================================
    // Helpers
    // ========================================================================

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
            new()
            {
                Name = $"{name}::primary",
                ConnectionString = $"test://{name}/primary",
                Order = 0,
                HealthState = MemberHealthState.Healthy,
                Capabilities = capabilityMap
            }
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

    // ========================================================================
    // Test Adapters
    // ========================================================================

    private sealed class ChatOnlyAdapter : IChatAdapter
    {
        public ChatOnlyAdapter(string id) => Id = id;
        public string Id { get; }
        public string Name => Id;
        public string Type => Id;
        public bool CanServe(AiChatRequest request) => true;
        public Task<AiChatResponse> Chat(AiChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiChatResponse { AdapterId = Id });
        public async IAsyncEnumerable<AiChatChunk> Stream(AiChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        { await Task.CompletedTask; yield break; }
        public Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AiModelDescriptor>>([]);
    }

    private sealed class EmbedOnlyAdapter : IEmbedAdapter
    {
        public EmbedOnlyAdapter(string id) => Id = id;
        public string Id { get; }
        public string Name => Id;
        public string Type => Id;
        public Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiEmbeddingsResponse { Model = request.Model ?? Id });
        public Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AiModelDescriptor>>([]);
    }

    private sealed class OcrOnlyAdapter : IOcrAdapter
    {
        public OcrOnlyAdapter(string id) => Id = id;
        public string Id { get; }
        public string Name => Id;
        public string Type => Id;
        public Task<OcrResponse> Recognize(OcrRequest request, CancellationToken ct = default)
            => Task.FromResult(new OcrResponse { Text = "recognized" });
        public Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AiModelDescriptor>>([]);
    }

    private sealed class ChatEmbedAdapter : IChatAdapter, IEmbedAdapter
    {
        public ChatEmbedAdapter(string id) => Id = id;
        public string Id { get; }
        public string Name => Id;
        public string Type => Id;
        public bool CanServe(AiChatRequest request) => true;
        public Task<AiChatResponse> Chat(AiChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiChatResponse { AdapterId = Id });
        public async IAsyncEnumerable<AiChatChunk> Stream(AiChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        { await Task.CompletedTask; yield break; }
        public Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiEmbeddingsResponse { Model = request.Model ?? Id });
        public Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AiModelDescriptor>>([]);
    }

    // ========================================================================
    // Fake Source Registry
    // ========================================================================

    internal sealed class FakeSourceRegistry : IAiSourceRegistry
    {
        private readonly Dictionary<string, AiSourceDefinition> _sources = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterSource(AiSourceDefinition source) => _sources[source.Name] = source;
        public AiSourceDefinition? GetSource(string name) => _sources.TryGetValue(name, out var s) ? s : null;
        public bool TryGetSource(string name, out AiSourceDefinition? source) { source = GetSource(name); return source is not null; }
        public IReadOnlyCollection<string> GetSourceNames() => _sources.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

        public IReadOnlyCollection<AiSourceDefinition> GetAllSources()
            => _sources.Values.OrderByDescending(s => s.Priority).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        public bool HasSource(string name) => _sources.ContainsKey(name);

        public IReadOnlyCollection<AiSourceDefinition> GetSourcesWithCapability(string capabilityName)
            => _sources.Values
                .Where(s => s.Capabilities.ContainsKey(capabilityName) ||
                           s.Members.Any(m => m.Capabilities?.ContainsKey(capabilityName) == true))
                .OrderByDescending(s => s.Priority)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }
}
