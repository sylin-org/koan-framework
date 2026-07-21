using AwesomeAssertions;
using Koan.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Web.OpenGraph.Tests;

public sealed class SocialCardRegistryTests
{
    [Fact]
    public void For_registers_one_card()
    {
        var registry = Compose(() => SocialCards.For<TestWork>("/work/{id}", id => TestWork.Get(id))
            .Title(w => w.Name));

        registry.Registrations.Should().HaveCount(1);
        registry.Has(typeof(TestWork)).Should().BeTrue();
    }

    [Fact]
    public void For_chains_across_types()
    {
        var registry = Compose(() => SocialCards
            .For<TestWork>("/work/{id}", id => TestWork.Get(id))
                .Title(w => w.Name)
            .For<TestArticle>("/articles/{slug}", slug => TestArticle.Get(slug))
                .Title(a => a.Title));

        registry.Registrations.Should().HaveCount(2);
        registry.Has(typeof(TestWork)).Should().BeTrue();
        registry.Has(typeof(TestArticle)).Should().BeTrue();
    }

    [Fact]
    public void Registering_the_same_type_twice_throws()
    {
        var act = () => Compose(() =>
        {
            SocialCards.For<TestWork>("/work/{id}", id => TestWork.Get(id));
            SocialCards.For<TestWork>("/preview/{id}", id => TestWork.Get(id));
        });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Separate_hosts_can_declare_the_same_type_independently()
    {
        var first = Compose(() => SocialCards.For<TestWork>("/work/{id}", id => TestWork.Get(id)));
        var second = Compose(() => SocialCards.For<TestWork>("/preview/{id}", id => TestWork.Get(id)));

        first.Should().NotBeSameAs(second);
        first.Registrations.Should().ContainSingle();
        second.Registrations.Should().ContainSingle();
        first.Registrations[0].Matcher.TryExtractToken("/work/one", out _).Should().BeTrue();
        second.Registrations[0].Matcher.TryExtractToken("/preview/two", out _).Should().BeTrue();
    }

    [Fact]
    public void KeyFor_composes_the_type_discriminator_and_token()
    {
        var registry = Compose(() => SocialCards.For<TestWork>("/work/{id}", id => TestWork.Get(id)));

        var registration = registry.Registrations[0];

        registration.KeyFor("abc").Should().Be("TestWork:abc");
    }

    private static SocialCardRegistry Compose(Action declaration)
    {
        var services = new ServiceCollection();
        services.AddKoan(declaration);
        return SocialCardRegistry.GetOrCreate(services);
    }
}
