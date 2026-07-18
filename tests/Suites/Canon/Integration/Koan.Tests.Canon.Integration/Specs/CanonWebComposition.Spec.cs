using Koan.Canon.Web.Catalog;
using Koan.Canon.Web.Initialization;

namespace Koan.Tests.Canon.Integration.Specs;

public sealed class CanonWebCompositionSpec
{
    [Fact]
    public async Task Web_projects_the_host_owned_Canon_plan_without_rediscovery()
    {
        var model = new CanonModelPlan(typeof(Account), []);
        var plan = new CanonCompositionPlan([model]);
        var services = new ServiceCollection();
        services.AddSingleton(plan);

        new CanonWebModule().Register(services);
        await using var provider = services.BuildServiceProvider();

        var catalog = provider.GetRequiredService<ICanonModelCatalog>();
        catalog.All.Should().ContainSingle();
        catalog.All[0].ModelType.Should().BeSameAs(model.ModelType);
        catalog.All[0].Slug.Should().Be("account");
        typeof(CanonWebModule).Assembly.GetTypes()
            .Should().NotContain(type => type.Name == "CanonAdminController");
    }

    [Fact]
    public void Duplicate_HTTP_slugs_reject_composition_with_both_types()
    {
        var plan = new CanonCompositionPlan(
        [
            new CanonModelPlan(typeof(First.Customer), []),
            new CanonModelPlan(typeof(Second.Customer), []),
        ]);
        var services = new ServiceCollection();
        services.AddSingleton(plan);

        var act = () => new CanonWebModule().Register(services);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*slug 'customer'*First+Customer*Second+Customer*Rename one model*");
    }

    private sealed class Account : CanonEntity<Account>
    {
        [AggregationKey]
        public string Key { get; set; } = "";
    }

    private static class First
    {
        internal sealed class Customer : CanonEntity<Customer>
        {
            [AggregationKey]
            public string Key { get; set; } = "";
        }
    }

    private static class Second
    {
        internal sealed class Customer : CanonEntity<Customer>
        {
            [AggregationKey]
            public string Key { get; set; } = "";
        }
    }
}
