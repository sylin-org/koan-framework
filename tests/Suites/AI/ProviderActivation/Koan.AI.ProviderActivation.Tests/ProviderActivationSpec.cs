using AwesomeAssertions;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;
using Koan.AI.Providers;
using Koan.Core;
using Koan.Core.Semantics.Contributions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Koan.AI.ProviderActivation.Tests;

public sealed class ProviderActivationSpec
{
    [Fact]
    public async Task AddKoan_compiles_provider_references_preserves_explicit_intent_and_disposes_once()
    {
        DisposableAdapter.Reset();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Koan:Ai:AutoDiscoveryEnabled"] = "false",
            ["Koan:Ai:Ollama:Endpoints:0"] = "http://ollama.example:11434",
            ["Koan:Ai:Ollama:DefaultModel"] = "llama3.2",
            ["Koan:Ai:LMStudio:Endpoints:0"] = "http://lmstudio.example:1234",
            ["Koan:Ai:LMStudio:DefaultModel"] = "qwen3-4b"
        });
        builder.Services.AddKoan();

        using (var host = builder.Build())
        {
            await host.StartAsync();

            var adapters = host.Services.GetRequiredService<IAiAdapterRegistry>();
            adapters.Get("ollama").Should().NotBeNull();
            adapters.Get("lmstudio").Should().NotBeNull();
            adapters.Get("test-disposable").Should().NotBeNull();

            var ollama = host.Services.GetRequiredService<IAiSourceRegistry>().GetSource("ollama");
            ollama.Should().NotBeNull();
            ollama!.Members.Should().ContainSingle(member =>
                member.ConnectionString == "http://ollama.example:11434");
            host.Services.GetRequiredService<IAiSourceRegistry>().GetSource("Default")
                .Should().BeNull("provider configuration must be owned by the provider, not translated into a legacy shadow source");

            var lmstudio = host.Services.GetRequiredService<IAiSourceRegistry>().GetSource("lmstudio");
            lmstudio.Should().NotBeNull();
            lmstudio!.Members.Should().ContainSingle(member =>
                member.ConnectionString == "http://lmstudio.example:1234");

            await host.StopAsync();
        }

        DisposableAdapter.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Conflicting_explicit_provider_placement_fails_startup_correctively()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Koan:Ai:AutoDiscoveryEnabled"] = "false",
            ["ConnectionStrings:LMStudio"] = "http://lmstudio-a.example:1234",
            ["Koan:Ai:LMStudio:Endpoints:0"] = "http://lmstudio-b.example:1234"
        });
        builder.Services.AddKoan();
        using var host = builder.Build();

        var start = async () => await host.StartAsync();

        await start.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*LM Studio placement is configured twice*ConnectionStrings:LMStudio*Endpoints*");
    }
}

internal sealed class DisposableProviderModule : KoanModule, IContributeTo<AiProviderContributionTarget>
{
    public override void Register(IServiceCollection services)
    {
        services.TryAddSingleton<DisposableAdapter>();
    }

    public void Contribute(AiProviderContributionTarget target) =>
        target.Add<DisposableProviderActivator>("test-disposable");
}

internal sealed class DisposableProviderActivator : IAiProviderActivator
{
    public ValueTask<AiProviderActivation?> Activate(
        IServiceProvider services,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<AiProviderActivation?>(new AiProviderActivation
        {
            Adapter = services.GetRequiredService<DisposableAdapter>()
        });
}

internal sealed class DisposableAdapter : IAiAdapter, IDisposable
{
    private static int _disposeCount;
    private int _disposed;

    public static int DisposeCount => Volatile.Read(ref _disposeCount);
    public static void Reset() => Volatile.Write(ref _disposeCount, 0);

    public string Id => "test-disposable";
    public string Name => "Disposable test provider";
    public string Type => Id;
    public IReadOnlySet<string> Capabilities { get; } = new HashSet<string> { AiCapability.Chat };
    public IAiModelManager? ModelManager => null;

    public Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AiModelDescriptor>>([]);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Increment(ref _disposeCount);
    }
}
