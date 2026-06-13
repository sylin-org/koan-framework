using AwesomeAssertions;
using Xunit;

namespace Koan.Web.OpenGraph.Tests;

public sealed class SocialCardRegistryTests
{
    public SocialCardRegistryTests() => SocialCards.Reset();

    [Fact]
    public void For_registers_one_card()
    {
        SocialCards.For<TestWork>("/work/{id}", id => TestWork.Get(id))
            .Title(w => w.Name);

        SocialCardRegistry.Registrations.Should().HaveCount(1);
        SocialCardRegistry.Has(typeof(TestWork)).Should().BeTrue();
    }

    [Fact]
    public void For_chains_across_types()
    {
        SocialCards
            .For<TestWork>("/work/{id}", id => TestWork.Get(id))
                .Title(w => w.Name)
            .For<TestArticle>("/articles/{slug}", slug => TestArticle.Get(slug))
                .Title(a => a.Title);

        SocialCardRegistry.Registrations.Should().HaveCount(2);
        SocialCardRegistry.Has(typeof(TestWork)).Should().BeTrue();
        SocialCardRegistry.Has(typeof(TestArticle)).Should().BeTrue();
    }

    [Fact]
    public void Registering_the_same_type_twice_throws()
    {
        SocialCards.For<TestWork>("/work/{id}", id => TestWork.Get(id));

        var act = () => SocialCards.For<TestWork>("/preview/{id}", id => TestWork.Get(id));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Reset_clears_all_registrations()
    {
        SocialCards.For<TestWork>("/work/{id}", id => TestWork.Get(id));

        SocialCards.Reset();

        SocialCardRegistry.Registrations.Should().BeEmpty();
        SocialCardRegistry.Has(typeof(TestWork)).Should().BeFalse();
    }

    [Fact]
    public void KeyFor_composes_the_type_discriminator_and_token()
    {
        SocialCards.For<TestWork>("/work/{id}", id => TestWork.Get(id));

        var registration = SocialCardRegistry.Registrations[0];

        registration.KeyFor("abc").Should().Be("TestWork:abc");
    }
}
