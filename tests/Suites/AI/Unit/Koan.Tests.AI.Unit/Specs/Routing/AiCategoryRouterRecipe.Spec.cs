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
using Koan.AI.Contracts;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Tests.AI.Unit.Specs.Routing;

/// <summary>
/// Tests for recipe integration in the AiCategoryRouter resolution chain (AI-0032).
/// The recipe provider sits at level 3: scope (2) > recipe (3) > advisor (4) > config (5).
/// </summary>
[Trait("ADR", "AI-0032")]
[Trait("Category", "Unit")]
public sealed class AiCategoryRouterRecipeSpec
{
    [Fact]
    public void Recipe_overrides_source_default_model()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new FakeChatAdapter("provider"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "provider",
            provider: "provider",
            priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "default-model" }
            }));

        var recipe = new FakeRecipeProvider("fast", new Dictionary<string, string>
        {
            ["Chat"] = "recipe-model"
        });

        var router = CreateRouter(adapters, sources, recipe: recipe);

        var resolution = router.Resolve("Chat");

        resolution.EffectiveModel.Should().Be("recipe-model",
            "recipe binding should override the source default model");
    }

    [Fact]
    public void Recipe_defers_to_scope_when_scope_is_active()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new FakeChatAdapter("provider"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "provider",
            provider: "provider",
            priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "default-model" }
            }));

        var recipe = new FakeRecipeProvider("fast", new Dictionary<string, string>
        {
            ["Chat"] = "recipe-model"
        });

        var router = CreateRouter(adapters, sources, recipe: recipe);

        using (new AiCategoryScope(chatModel: "scope-model"))
        {
            var resolution = router.Resolve("Chat");

            resolution.EffectiveModel.Should().Be("scope-model",
                "scope override (level 2) should take priority over recipe (level 3)");
        }
    }

    [Fact]
    public void Recipe_fallthrough_when_no_binding_for_category()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new FakeChatEmbedAdapter("provider"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "provider",
            provider: "provider",
            priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "chat-default" },
                ["Embedding"] = new() { Model = "embed-default" }
            }));

        // Recipe only binds Chat — Embed has no opinion
        var recipe = new FakeRecipeProvider("chat-only", new Dictionary<string, string>
        {
            ["Chat"] = "recipe-chat"
        });

        var advisor = new FakeModelAdvisor(new Dictionary<string, string>
        {
            ["Embed"] = "advisor-embed"
        });

        var router = CreateRouter(adapters, sources, recipe: recipe, advisor: advisor);

        var chatResolution = router.Resolve("Chat");
        var embedResolution = router.Resolve("Embed");

        chatResolution.EffectiveModel.Should().Be("recipe-chat",
            "Chat should use recipe binding");
        embedResolution.EffectiveModel.Should().Be("advisor-embed",
            "Embed should fall through recipe to advisor (no recipe binding for Embed)");
    }

    [Fact]
    public void Null_recipe_provider_is_handled_gracefully()
    {
        var adapters = new InMemoryAdapterRegistry();
        adapters.Add(new FakeChatAdapter("provider"));

        var sources = new FakeSourceRegistry();
        sources.RegisterSource(CreateSource(
            name: "provider",
            provider: "provider",
            priority: 50,
            capabilities: new Dictionary<string, AiCapabilityConfig>
            {
                ["Chat"] = new() { Model = "source-default" }
            }));

        // Explicitly pass null recipe and null advisor
        var router = CreateRouter(adapters, sources, recipe: null, advisor: null);

        var act = () => router.Resolve("Chat");

        act.Should().NotThrow("null recipe provider should be handled gracefully");

        var resolution = router.Resolve("Chat");
        resolution.EffectiveModel.Should().Be("source-default",
            "should fall through to source default when no recipe or advisor is present");
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static AiCategoryRouter CreateRouter(
        InMemoryAdapterRegistry adapters,
        FakeSourceRegistry sources,
        AiOptions? options = null,
        IAiRecipeProvider? recipe = null,
        IAiModelAdvisor? advisor = null)
        => new(adapters, sources, Options.Create(options ?? new AiOptions()), recipe, advisor);

    private static AiSourceDefinition CreateSource(
        string name,
        string provider,
        int priority,
        IReadOnlyDictionary<string, AiCapabilityConfig>? capabilities)
    {
        var capabilityMap = capabilities ?? new Dictionary<string, AiCapabilityConfig>();
        var memberList = new List<AiMemberDefinition>
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
    // Fakes
    // ========================================================================

    private sealed class FakeRecipeProvider(
        string? recipeName,
        IReadOnlyDictionary<string, string> bindings) : IAiRecipeProvider
    {
        private readonly Dictionary<string, string> _bindings =
            new(bindings, StringComparer.OrdinalIgnoreCase);

        public string? ActiveRecipeName => recipeName;

        public string? GetModel(string category)
            => _bindings.TryGetValue(category, out var model) ? model : null;
    }

    private sealed class FakeModelAdvisor(
        IReadOnlyDictionary<string, string> recommendations) : IAiModelAdvisor
    {
        private readonly Dictionary<string, string> _recommendations =
            new(recommendations, StringComparer.OrdinalIgnoreCase);

        public string? GetRecommendedModel(string category)
            => _recommendations.TryGetValue(category, out var model) ? model : null;
    }

    private sealed class FakeChatAdapter(string id) : IChatAdapter
    {
        public string Id => id;
        public string Name => id;
        public string Type => id;
        public bool CanServe(AiChatRequest request) => true;
        public Task<AiChatResponse> Chat(AiChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiChatResponse { AdapterId = id });
        public async IAsyncEnumerable<AiChatChunk> Stream(AiChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        { await Task.CompletedTask; yield break; }
        public Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AiModelDescriptor>>([]);
    }

    private sealed class FakeChatEmbedAdapter(string id) : IChatAdapter, IEmbedAdapter
    {
        public string Id => id;
        public string Name => id;
        public string Type => id;
        public bool CanServe(AiChatRequest request) => true;
        public Task<AiChatResponse> Chat(AiChatRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiChatResponse { AdapterId = id });
        public async IAsyncEnumerable<AiChatChunk> Stream(AiChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        { await Task.CompletedTask; yield break; }
        public Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiEmbeddingsResponse { Model = request.Model ?? id });
        public Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AiModelDescriptor>>([]);
    }

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
