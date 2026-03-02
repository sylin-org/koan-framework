using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.AI;
using Koan.AI.Contracts.Options;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Koan.AI.Connector.Ollama.Initialization;
using Koan.AI.Connector.Ollama.Options;
using Koan.Core.Adapters;
using Koan.ZenGarden.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Tests.AI.Unit.Specs.Adapters;

public sealed class OllamaAdapterContributorSpec
{
    [Fact]
    public async Task ContributeAsync_with_explicit_url_registers_expected_source()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Koan:Ai:Ollama:Urls:0"] = "http://localhost:6001",
            ["Koan:Ai:Ollama:DefaultModel"] = "phi3",
            ["Koan:Ai:Ollama:Policy"] = "RoundRobin"
        });

        var sourceRegistry = new RecordingSourceRegistry();
        var adapterRegistry = new InMemoryAdapterRegistry();
        using var provider = BuildServiceProvider(configuration, sourceRegistry, adapterRegistry);

        var contributor = new OllamaAdapterContributor();
        await contributor.ContributeAsync(provider, CancellationToken.None);

        sourceRegistry.RegisteredSources.Should().HaveCount(1);
        var source = sourceRegistry.RegisteredSources.Single();

        source.Name.Should().Be("ollama");
        source.Policy.Should().Be("RoundRobin");
        source.Origin.Should().Be("explicit-config");
        source.IsAutoDiscovered.Should().BeFalse();
        source.Members.Should().ContainSingle();

        var member = source.Members[0];
        member.Name.Should().Be("ollama::explicit-1");
        member.ConnectionString.Should().Be("http://localhost:6001");
        member.Order.Should().Be(0);
        member.Origin.Should().Be("config-urls");
        member.IsAutoDiscovered.Should().BeFalse();
        member.Capabilities.Should().ContainKey("Chat").WhoseValue.Model.Should().Be("phi3");

        adapterRegistry.All.Should().ContainSingle(adapter => adapter.Id == "ollama");
    }

    [Fact]
    public async Task ContributeAsync_with_zen_garden_connection_string_resolves_member()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Koan:Ai:Ollama:ConnectionString"] = "zen-garden://ollama?cap=llama3.2,nomic-embed-text",
            ["Koan:Ai:Ollama:DefaultModel"] = "llama3.2"
        });

        var sourceRegistry = new RecordingSourceRegistry();
        var adapterRegistry = new InMemoryAdapterRegistry();
        var zenGardenProvider = new StubZenGardenProvider(_ => new ZenGardenOfferingResolution
        {
            ToolFqid = "ollama",
            Offering = "ollama",
            Uris = new[] { "http://zen-ollama:11434" },
            Capabilities = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = new[] { "llama3.2", "nomic-embed-text" }
            }
        });

        using var provider = BuildServiceProvider(configuration, sourceRegistry, adapterRegistry, zenGardenProvider);

        var contributor = new OllamaAdapterContributor();
        await contributor.ContributeAsync(provider, CancellationToken.None);

        var source = sourceRegistry.RegisteredSources.Single();
        source.Origin.Should().Be("explicit-config");
        source.IsAutoDiscovered.Should().BeFalse();
        source.Members.Should().ContainSingle();

        var member = source.Members.Single();
        member.Name.Should().Be("ollama::connection");
        member.ConnectionString.Should().Be("http://zen-ollama:11434");
        member.Origin.Should().Be("config-connection-string");
        member.Capabilities.Should().ContainKey("Chat").WhoseValue.Model.Should().Be("llama3.2");
    }

    [Fact]
    public async Task ContributeAsync_with_unresolved_zen_garden_connection_uses_additional_url_fallback()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Koan:Ai:Ollama:ConnectionString"] = "zen-garden://ollama",
            ["Koan:Ai:Ollama:AdditionalUrls:0"] = "http://localhost:6003",
            ["Koan:Ai:Ollama:DefaultModel"] = "phi3"
        });

        var sourceRegistry = new RecordingSourceRegistry();
        var adapterRegistry = new InMemoryAdapterRegistry();
        var zenGardenProvider = new StubZenGardenProvider(_ => null);
        using var provider = BuildServiceProvider(configuration, sourceRegistry, adapterRegistry, zenGardenProvider);

        var contributor = new OllamaAdapterContributor();
        await contributor.ContributeAsync(provider, CancellationToken.None);

        var source = sourceRegistry.RegisteredSources.Single();
        source.Origin.Should().Be("auto-discovery");
        source.Members.Should().Contain(member =>
            member.ConnectionString == "http://localhost:6003" &&
            member.Origin == "config-additional-urls");
        adapterRegistry.All.Should().ContainSingle(adapter => adapter.Id == "ollama");
    }

    [Fact]
    public async Task ContributeAsync_with_missing_required_capabilities_passes_capability_intent_to_provider_and_continues()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Koan:Ai:Ollama:ConnectionString"] = "zen-garden://ollama?cap=llama3.2,nomic-embed-text"
        });

        var sourceRegistry = new RecordingSourceRegistry();
        var adapterRegistry = new InMemoryAdapterRegistry();
        var zenGardenProvider = new StubZenGardenProvider(_ => new ZenGardenOfferingResolution
        {
            ToolFqid = "ollama",
            Offering = "ollama",
            Uris = new[] { "http://zen-ollama:11434" },
            Capabilities = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = new[] { "llama3.2" }
            }
        });

        using var provider = BuildServiceProvider(configuration, sourceRegistry, adapterRegistry, zenGardenProvider);

        var contributor = new OllamaAdapterContributor();
        await contributor.ContributeAsync(provider, CancellationToken.None);

        sourceRegistry.RegisteredSources.Should().ContainSingle();
        zenGardenProvider.ResolveRequests.Should().ContainSingle();
        zenGardenProvider.ResolveRequests[0].Capabilities.Should().Contain(["llama3.2", "nomic-embed-text"]);
    }

    [Fact]
    public async Task ContributeAsync_bails_when_source_already_registered()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Koan:Ai:Ollama:Urls:0"] = "http://localhost:6005"
        });

        var sourceRegistry = new RecordingSourceRegistry();
        sourceRegistry.MarkExisting("ollama");
        var adapterRegistry = new InMemoryAdapterRegistry();
        using var provider = BuildServiceProvider(configuration, sourceRegistry, adapterRegistry);

        var contributor = new OllamaAdapterContributor();
        await contributor.ContributeAsync(provider, CancellationToken.None);

        sourceRegistry.RegisteredSources.Should().BeEmpty();
        adapterRegistry.All.Should().ContainSingle(adapter => adapter.Id == "ollama");
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ServiceProvider BuildServiceProvider(
        IConfiguration configuration,
        RecordingSourceRegistry sourceRegistry,
        IAiAdapterRegistry adapterRegistry,
        IZenGardenInitializationProvider? zenGardenProvider = null,
        AiOptions? aiOptions = null,
        OllamaOptions? ollamaOptions = null,
        AdaptersReadinessOptions? readiness = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton<IAiSourceRegistry>(sourceRegistry);
        services.AddSingleton<IAiAdapterRegistry>(adapterRegistry);
        services.AddSingleton<IOptions<AiOptions>>(Options.Create(aiOptions ?? new AiOptions
        {
            AutoDiscoveryEnabled = true,
            AllowDiscoveryInNonDev = true
        }));
        services.AddSingleton<IOptionsMonitor<OllamaOptions>>(new TestOptionsMonitor<OllamaOptions>(ollamaOptions ?? new OllamaOptions()));
        services.AddSingleton<IOptions<AdaptersReadinessOptions>>(Options.Create(readiness ?? new AdaptersReadinessOptions()));
        services.AddSingleton<ILogger<OllamaAdapterContributor>>(NullLogger<OllamaAdapterContributor>.Instance);

        if (zenGardenProvider is not null)
        {
            services.AddSingleton(zenGardenProvider);
        }

        return services.BuildServiceProvider();
    }

    private sealed class RecordingSourceRegistry : IAiSourceRegistry
    {
        private readonly Dictionary<string, AiSourceDefinition> _sources = new(StringComparer.OrdinalIgnoreCase);

        public List<AiSourceDefinition> RegisteredSources { get; } = new();

        public void RegisterSource(AiSourceDefinition source)
        {
            _sources[source.Name] = source;
            RegisteredSources.Add(source);
        }

        public AiSourceDefinition? GetSource(string name)
            => _sources.TryGetValue(name, out var source) ? source : null;

        public bool TryGetSource(string name, out AiSourceDefinition? source)
        {
            source = GetSource(name);
            return source is not null;
        }

        public IReadOnlyCollection<string> GetSourceNames()
            => _sources.Keys.ToArray();

        public IReadOnlyCollection<AiSourceDefinition> GetAllSources()
            => _sources.Values.ToArray();

        public bool HasSource(string name)
            => _sources.ContainsKey(name);

        public IReadOnlyCollection<AiSourceDefinition> GetSourcesWithCapability(string capabilityName)
            => _sources.Values
                .Where(source => source.Capabilities.ContainsKey(capabilityName) ||
                                  source.Members.Any(member => member.Capabilities?.ContainsKey(capabilityName) == true))
                .ToArray();

        public void MarkExisting(string name)
        {
            _sources[name] = new AiSourceDefinition
            {
                Name = name,
                Provider = "ollama",
                Members = new List<AiMemberDefinition>(),
                Capabilities = new Dictionary<string, AiCapabilityConfig>()
            };
        }
    }

    private sealed class StubZenGardenProvider : IZenGardenInitializationProvider
    {
        private readonly Func<ZenGardenConnectionIntent, ZenGardenOfferingResolution?> _resolver;
        public List<ZenGardenConnectionIntent> ResolveRequests { get; } = new();
        public List<ZenGardenConnectionIntent> WishRequests { get; } = new();

        public StubZenGardenProvider(Func<ZenGardenConnectionIntent, ZenGardenOfferingResolution?> resolver)
        {
            _resolver = resolver;
        }

        public bool TryGetDefaultOffering(string adapterId, out string offering)
        {
            offering = "ollama";
            return string.Equals(adapterId, "ollama", StringComparison.OrdinalIgnoreCase);
        }

        public ValueTask<ZenGardenOfferingResolution?> ResolveAsync(
            ZenGardenConnectionIntent intent,
            CancellationToken cancellationToken = default)
        {
            ResolveRequests.Add(intent);
            return ValueTask.FromResult(_resolver(intent));
        }

        public ValueTask<ZenGardenCapabilityWishReceipt?> WishCapabilitiesAsync(
            ZenGardenConnectionIntent intent,
            CancellationToken cancellationToken = default)
        {
            WishRequests.Add(intent);
            return ValueTask.FromResult<ZenGardenCapabilityWishReceipt?>(new ZenGardenCapabilityWishReceipt
            {
                RequestId = Guid.NewGuid().ToString("N"),
                ToolFqid = "ollama",
                OfferingSelector = intent.ToOfferingSelector(),
                Requested = intent.Capabilities,
                Missing = intent.Capabilities,
                IsFulfilled = false,
                Status = "requested",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private T _current;

        public TestOptionsMonitor(T current)
        {
            _current = current;
        }

        public T CurrentValue => _current;

        public T Get(string? name) => _current;

        public IDisposable OnChange(Action<T, string> listener) => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
