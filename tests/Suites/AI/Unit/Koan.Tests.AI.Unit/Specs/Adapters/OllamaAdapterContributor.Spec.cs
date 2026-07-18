using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.AI.Contracts.Options;
using Koan.AI.Contracts.Sources;
using Koan.AI.Connector.Ollama;
using Koan.AI.Connector.Ollama.Initialization;
using Koan.AI.Connector.Ollama.Options;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
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
    public async Task Explicit_endpoint_produces_one_deterministic_source()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Koan:Ai:Ollama:Endpoints:0"] = "http://localhost:6001",
            ["Koan:Ai:Ollama:DefaultModel"] = "phi3"
        });

        using var provider = BuildServiceProvider(configuration, new RecordingSourceRegistry());
        var activation = await new OllamaAdapterContributor().Activate(provider, CancellationToken.None);

        activation.Should().NotBeNull();
        var source = activation!.Sources.Should().ContainSingle().Subject;
        source.Name.Should().Be("ollama");
        source.Policy.Should().Be("Fallback");
        source.Origin.Should().Be("explicit-config");
        source.IsAutoDiscovered.Should().BeFalse();

        var member = source.Members.Should().ContainSingle().Subject;
        member.Name.Should().Be("ollama::member-1");
        member.ConnectionString.Should().Be("http://localhost:6001");
        member.Order.Should().Be(0);
        member.Origin.Should().Be("explicit-config");
        member.IsAutoDiscovered.Should().BeFalse();
        member.Capabilities.Should().ContainKey("Chat").WhoseValue.Model.Should().Be("phi3");
        activation.Adapter.Id.Should().Be("ollama");
    }

    [Fact]
    public async Task Explicit_zen_garden_intent_is_resolved_by_core_discovery()
    {
        const string intent = "zen-garden://ollama?cap=llama3.2,nomic-embed-text";
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Ollama"] = intent,
            ["Koan:Ai:Ollama:DefaultModel"] = "llama3.2"
        });
        var discovery = StubDiscoveryCoordinator.Resolving("http://zen-ollama:11434");

        using var provider = BuildServiceProvider(configuration, new RecordingSourceRegistry(), discovery);
        var activation = await new OllamaAdapterContributor().Activate(provider, CancellationToken.None);

        var source = activation!.Sources.Should().ContainSingle().Subject;
        source.Origin.Should().Be("explicit-config");
        var member = source.Members.Should().ContainSingle().Subject;
        member.Name.Should().Be("ollama::member-1");
        member.ConnectionString.Should().Be("http://zen-ollama:11434");
        member.Origin.Should().Be("explicit-config");
        discovery.ResolveRequests.Should().ContainSingle().Which.Should().Be(intent);
    }

    [Fact]
    public async Task Unresolved_explicit_zen_garden_intent_fails_without_fallback()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Ollama"] = "zen-garden://ollama",
            ["Koan:Ai:Ollama:DefaultModel"] = "phi3"
        });
        var sourceRegistry = new RecordingSourceRegistry();
        using var provider = BuildServiceProvider(
            configuration,
            sourceRegistry,
            StubDiscoveryCoordinator.Failing());

        var activate = async () =>
            await new OllamaAdapterContributor().Activate(provider, CancellationToken.None);

        await activate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Ollama explicit Zen Garden intent*could not be satisfied*")
            .WithMessage("*Koan.ZenGarden*automatic discovery*native Ollama HTTP endpoint*");
        sourceRegistry.RegisteredSources.Should().BeEmpty();
    }

    [Fact]
    public async Task Capability_bearing_intent_is_passed_unchanged_to_discovery()
    {
        const string intent = "zen-garden://ollama?cap=llama3.2,nomic-embed-text";
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Ollama"] = intent
        });
        var discovery = StubDiscoveryCoordinator.Resolving("http://zen-ollama:11434");
        using var provider = BuildServiceProvider(configuration, new RecordingSourceRegistry(), discovery);

        var activation = await new OllamaAdapterContributor().Activate(provider, CancellationToken.None);

        activation!.Sources.Should().ContainSingle();
        discovery.ResolveRequests.Should().ContainSingle().Which.Should().Be(intent);
    }

    [Fact]
    public async Task Existing_source_is_reused_without_publishing_a_second_source()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Koan:Ai:Ollama:Endpoints:0"] = "http://localhost:6005"
        });
        var sourceRegistry = new RecordingSourceRegistry();
        sourceRegistry.MarkExisting("ollama");
        using var provider = BuildServiceProvider(configuration, sourceRegistry);

        var activation = await new OllamaAdapterContributor().Activate(provider, CancellationToken.None);

        activation!.Sources.Should().BeEmpty();
        activation.Adapter.Id.Should().Be("ollama");
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ServiceProvider BuildServiceProvider(
        IConfiguration configuration,
        RecordingSourceRegistry sourceRegistry,
        IServiceDiscoveryCoordinator? discovery = null,
        AiOptions? aiOptions = null,
        OllamaOptions? ollamaOptions = null)
    {
        var resolvedOptions = ollamaOptions
            ?? configuration.GetSection("Koan:Ai:Ollama").Get<OllamaOptions>()
            ?? new OllamaOptions();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton<IAiSourceRegistry>(sourceRegistry);
        services.AddSingleton<IOptions<AiOptions>>(Options.Create(aiOptions ?? new AiOptions
        {
            AutoDiscoveryEnabled = true,
            AllowDiscoveryInNonDev = true
        }));
        services.AddSingleton<IOptionsMonitor<OllamaOptions>>(
            new TestOptionsMonitor<OllamaOptions>(resolvedOptions));
        services.AddSingleton<ILogger<OllamaAdapterContributor>>(
            NullLogger<OllamaAdapterContributor>.Instance);
        services.AddSingleton(new OllamaAdapter(
            new HttpClient(),
            NullLogger<OllamaAdapter>.Instance,
            resolvedOptions));

        if (discovery is not null) services.AddSingleton(discovery);
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

        public AiSourceDefinition? GetSource(string name) =>
            _sources.TryGetValue(name, out var source) ? source : null;

        public bool TryGetSource(string name, out AiSourceDefinition? source)
        {
            source = GetSource(name);
            return source is not null;
        }

        public IReadOnlyCollection<string> GetSourceNames() => _sources.Keys.ToArray();
        public IReadOnlyCollection<AiSourceDefinition> GetAllSources() => _sources.Values.ToArray();
        public bool HasSource(string name) => _sources.ContainsKey(name);

        public IReadOnlyCollection<AiSourceDefinition> GetSourcesWithCapability(string capabilityName) =>
            _sources.Values
                .Where(source => source.Capabilities.ContainsKey(capabilityName)
                    || source.Members.Any(member => member.Capabilities?.ContainsKey(capabilityName) == true))
                .ToArray();

        public void MarkExisting(string name)
        {
            _sources[name] = new AiSourceDefinition
            {
                Name = name,
                Provider = "ollama",
                Members = [],
                Capabilities = new Dictionary<string, AiCapabilityConfig>()
            };
        }
    }

    private sealed class StubDiscoveryCoordinator : IServiceDiscoveryCoordinator
    {
        private readonly AdapterDiscoveryResult _resolution;

        private StubDiscoveryCoordinator(AdapterDiscoveryResult resolution) => _resolution = resolution;

        public List<string> ResolveRequests { get; } = [];

        public static StubDiscoveryCoordinator Resolving(string endpoint) =>
            new(AdapterDiscoveryResult.Success("ollama", endpoint, "zen-garden"));

        public static StubDiscoveryCoordinator Failing() =>
            new(AdapterDiscoveryResult.Failed("ollama", "not available"));

        public Task<AdapterDiscoveryResult> DiscoverService(
            string serviceName,
            DiscoveryContext? context = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AdapterDiscoveryResult.Failed(serviceName, "not discovered"));

        public Task<AdapterDiscoveryResult> ResolveServiceIntent(
            string serviceName,
            string intent,
            DiscoveryContext? context = null,
            CancellationToken cancellationToken = default)
        {
            ResolveRequests.Add(intent);
            return Task.FromResult(_resolution);
        }

        public IServiceDiscoveryAdapter[] GetRegisteredAdapters() => [];
    }

    private sealed class TestOptionsMonitor<T>(T current) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = current;
        public T Get(string? name) => CurrentValue;
        public IDisposable OnChange(Action<T, string?> listener) => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
