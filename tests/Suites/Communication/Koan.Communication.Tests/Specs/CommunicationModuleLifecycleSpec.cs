using Koan.Core;
using Koan.Core.Diagnostics;
using Koan.Data.Core;
using Koan.Communication.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Communication.Tests.Specs;

public sealed class CommunicationModuleLifecycleSpec
{
    [Fact]
    public void Communication_has_one_semantic_lifecycle_owner()
    {
        typeof(KoanCommunicationModule).Should().BeDerivedFrom<KoanModule>();
        typeof(KoanCommunicationModule).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(KoanModule).IsAssignableFrom(type))
            .Should().ContainSingle().Which.Should().Be(typeof(KoanCommunicationModule));
    }

    [Fact]
    public void Repeated_framework_registration_is_a_no_op_while_application_configuration_still_applies()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddKoanCommunication();
        var firstRegistrationCount = services.Count;

        services.AddKoanCommunication();

        services.Should().HaveCount(firstRegistrationCount);

        services.AddKoanCommunication(options => options.InProcessCapacity = 731);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<CommunicationOptions>>()
            .Value.InProcessCapacity.Should().Be(731);
    }

    [Fact]
    public async Task Console_bootstrap_projects_default_communication_without_collection_failure()
    {
        var services = new ServiceCollection();
        services.AddSingleton<EventTestState>();
        services.AddSingleton<TransportTestState>();
        var provider = services.StartKoan();

        try
        {
            provider.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts.Should().NotContain(fact =>
                fact.State == KoanFactState.CollectionFailed
                && fact.Subject == "Sylin.Koan.Communication");
        }
        finally
        {
            await ((IAsyncDisposable)provider).DisposeAsync();
        }
    }
}
