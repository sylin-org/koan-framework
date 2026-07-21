using AwesomeAssertions;
using Koan.Web.Extensions.GenericControllers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Web.Extensions.Tests;

public sealed class GenericControllerRegistryTests
{
    [Fact]
    public void Registrations_are_owned_by_the_service_collection()
    {
        var first = new ServiceCollection();
        var second = new ServiceCollection();

        first.AddEntityAuditController<FirstEntity>("api/first");
        second.AddEntityAuditController<SecondEntity>("api/second");

        var firstRegistry = GenericControllerRegistry.GetOrAdd(first);
        var secondRegistry = GenericControllerRegistry.GetOrAdd(second);

        firstRegistry.Should().NotBeSameAs(secondRegistry);
        firstRegistry.Registrations.Should().ContainSingle(registration => registration.EntityType == typeof(FirstEntity));
        secondRegistry.Registrations.Should().ContainSingle(registration => registration.EntityType == typeof(SecondEntity));
    }

    [Fact]
    public void One_generic_projection_cannot_silently_select_between_routes()
    {
        var services = new ServiceCollection();
        services.AddEntityAuditController<FirstEntity>("api/first");

        var act = () => services.AddEntityAuditController<FirstEntity>("api/alternate");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already registered*declare an explicit controller*");
    }

    private sealed class FirstEntity;
    private sealed class SecondEntity;
}
