using Koan.AI.Contracts.Adapters;
using Koan.AI.Initialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.AI.Unit.Specs.Initialization;

[Trait("Category", "Unit")]
public sealed class AiModuleLifecycleSpec
{
    [Fact]
    public async Task Start_compiles_registered_adapter_contributors()
    {
        var contributor = new RecordingContributor();
        using var services = BuildServices(contributor);

        await new AiModule().Start(services, CancellationToken.None);

        contributor.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Start_rejects_a_contributor_that_cannot_honor_configured_intent()
    {
        using var services = BuildServices(new RejectingContributor());

        var action = () => new AiModule().Start(services, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("configured AI intent cannot be honored");
    }

    private static ServiceProvider BuildServices(IAiAdapterContributor contributor)
    {
        var registrations = new ServiceCollection();
        registrations.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        registrations.AddLogging();

        var module = new AiModule();
        module.Register(registrations);
        registrations.AddSingleton(contributor);

        return registrations.BuildServiceProvider();
    }

    private sealed class RecordingContributor : IAiAdapterContributor
    {
        public int Calls { get; private set; }

        public ValueTask Contribute(IServiceProvider services, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RejectingContributor : IAiAdapterContributor
    {
        public ValueTask Contribute(IServiceProvider services, CancellationToken cancellationToken)
            => ValueTask.FromException(new InvalidOperationException("configured AI intent cannot be honored"));
    }
}
