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
    public async Task ContributeAsync_with_explicit_and_services_registers_expected_source()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Koan:Ai:Ollama:Urls:0"] = "http://localhost:6001",
            ["Koan:Ai:Ollama:DefaultModel"] = "phi3",
            ["Koan:Ai:Ollama:Policy"] = "RoundRobin",
            ["Koan:Ai:Services:Ollama:0:Id"] = "EdgeHost",
            ["Koan:Ai:Services:Ollama:0:BaseUrl"] = "http://localhost:6002",
            ["Koan:Ai:Services:Ollama:0:DefaultModel"] = "llama3",
            ["Koan:Ai:Services:Ollama:0:Weight"] = "3"
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
        source.Members.Should().HaveCount(2);

        var explicitMember = source.Members[0];
        explicitMember.Name.Should().Be("ollama::explicit-1");
        explicitMember.Order.Should().Be(0);
        explicitMember.Origin.Should().Be("config-urls");
        explicitMember.IsAutoDiscovered.Should().BeFalse();
        explicitMember.Capabilities.Should().NotBeNull();
        explicitMember.Capabilities!.Should().ContainKey("Chat").WhoseValue.Model.Should().Be("phi3");

        var serviceMember = source.Members[1];
        serviceMember.Name.Should().Be("ollama::edgehost");
        serviceMember.Order.Should().Be(1);
        serviceMember.Origin.Should().Be("config-services");
        serviceMember.Weight.Should().Be(3);
        serviceMember.Capabilities.Should().NotBeNull();
        serviceMember.Capabilities!.Should().ContainKey("Chat").WhoseValue.Model.Should().Be("llama3");

        adapterRegistry.All.Should().ContainSingle(adapter => adapter.Id == "ollama");
    }

    [Fact]
    public async Task ContributeAsync_without_explicit_combines_services_and_additional_urls()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Koan:Ai:Ollama:DefaultModel"] = "phi3",
            ["Koan:Ai:Ollama:AdditionalUrls:0"] = "http://localhost:6003",
            ["Koan:Ai:Services:Ollama:0:BaseUrl"] = "http://localhost:6004"
        });

        var sourceRegistry = new RecordingSourceRegistry();
        var adapterRegistry = new InMemoryAdapterRegistry();
        using var provider = BuildServiceProvider(configuration, sourceRegistry, adapterRegistry);

        var contributor = new OllamaAdapterContributor();
        await contributor.ContributeAsync(provider, CancellationToken.None);

        var source = sourceRegistry.RegisteredSources.Single();
        source.Origin.Should().Be("config-services");
        source.IsAutoDiscovered.Should().BeFalse();

        source.Members.Should().ContainSingle(member => member.ConnectionString == "http://localhost:6004" && member.Origin == "config-services");
        var serviceMember = source.Members.Single(member => member.ConnectionString == "http://localhost:6004");
        serviceMember.IsAutoDiscovered.Should().BeFalse();
        serviceMember.Capabilities.Should().NotBeNull();
        serviceMember.Capabilities!.Should().ContainKey("Chat");

        source.Members.Should().Contain(member => member.ConnectionString == "http://localhost:6003" && member.Origin == "config-additional-urls");
        var additionalMember = source.Members.Single(member => member.ConnectionString == "http://localhost:6003");
        additionalMember.IsAutoDiscovered.Should().BeFalse();
        additionalMember.Capabilities.Should().NotBeNull();
        additionalMember.Capabilities!.Should().ContainKey("Chat");

        adapterRegistry.All.Should().ContainSingle(adapter => adapter.Id == "ollama");
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
