using Koan.AI.Contracts;
using Koan.AI;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Routing;
using Koan.AI.Initialization;
using Koan.AI.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.AI.Unit.Specs.Initialization;

[Trait("Category", "Unit")]
public sealed class AiModuleLifecycleSpec
{
    [Fact]
    public async Task Start_activates_the_compiled_provider_plan_and_freezes_the_registry()
    {
        var activator = new RecordingActivator();
        using var services = BuildServices(activator);

        await services.GetRequiredService<AiProviderPlanInitializer>()
            .Initialize(CancellationToken.None);

        activator.Calls.Should().Be(1);
        services.GetRequiredService<IAiAdapterRegistry>().Get("recording").Should().NotBeNull();

        var repeat = () => services.GetRequiredService<AiProviderPlanInitializer>()
            .Initialize(CancellationToken.None);
        await repeat.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already compiled*this host*");
    }

    [Fact]
    public async Task Start_rejects_an_activator_that_cannot_honor_configured_intent()
    {
        using var services = BuildServices(new RejectingActivator());

        var action = () => services.GetRequiredService<AiProviderPlanInitializer>()
            .Initialize(CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("configured AI intent cannot be honored");
        services.GetRequiredService<IAiAdapterRegistry>().All.Should().BeEmpty();
    }

    private static ServiceProvider BuildServices<TActivator>(TActivator activator)
        where TActivator : class, IAiProviderActivator
    {
        var planBuilder = new AiProviderPlanBuilder();
        planBuilder.ForOwner("test.module").Add<TActivator>("recording");

        var registrations = new ServiceCollection();
        registrations.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        registrations.AddLogging();
        registrations.AddAi();
        registrations.AddSingleton(planBuilder.Build());
        registrations.AddSingleton(activator);
        registrations.AddSingleton<RecordingAdapter>();
        registrations.AddSingleton<AiProviderPlanInitializer>();
        return registrations.BuildServiceProvider();
    }

    private sealed class RecordingActivator : IAiProviderActivator
    {
        public int Calls { get; private set; }

        public ValueTask<AiProviderActivation?> Activate(
            IServiceProvider services,
            CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult<AiProviderActivation?>(new AiProviderActivation
            {
                Adapter = services.GetRequiredService<RecordingAdapter>()
            });
        }
    }

    private sealed class RejectingActivator : IAiProviderActivator
    {
        public ValueTask<AiProviderActivation?> Activate(
            IServiceProvider services,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<AiProviderActivation?>(
                new InvalidOperationException("configured AI intent cannot be honored"));
    }

    private sealed class RecordingAdapter : IAiAdapter
    {
        public string Id => "recording";
        public string Name => "Recording";
        public string Type => "recording";
        public IReadOnlySet<string> Capabilities { get; } = new HashSet<string> { AiCapability.Chat };
        public IAiModelManager? ModelManager => null;

        public Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AiModelDescriptor>>([]);
    }
}
