using AwesomeAssertions;
using Koan.Web.Auth.Standing;
using Xunit;

namespace Koan.Web.Auth.Tests;

/// <summary>Contributors compose without order: any that says inactive decides, and roles are held together.</summary>
public sealed class SubjectStandingsTests
{
    private static Task<SubjectStanding?> Of(params ISubjectStandingContributor[] contributors)
        => new SubjectStandings(contributors).Of("subject", CancellationToken.None);

    [Fact]
    public async Task No_contributor_who_knows_the_subject_is_no_answer()
        => (await Of(new First(null), new Second(null))).Should().BeNull();

    [Fact]
    public async Task Any_contributor_saying_inactive_decides_whichever_is_registered_first()
    {
        var active = new First(new SubjectStanding(true, "Name", ["role"]));
        var inactive = new Second(new SubjectStanding(false));

        (await Of(active, inactive))!.Should().Be(new SubjectStanding(false));
        (await Of(inactive, active))!.Should().Be(new SubjectStanding(false));
    }

    [Fact]
    public async Task Roles_are_the_union_of_those_reported_and_silence_about_roles_is_not_none()
    {
        var saysNothing = await Of(new First(new SubjectStanding(true)));
        saysNothing!.Roles.Should().BeNull();

        var saysNone = await Of(new First(new SubjectStanding(true, Roles: [])));
        saysNone!.Roles.Should().BeEmpty();

        var together = await Of(new First(new SubjectStanding(true, Roles: ["a"])), new Second(new SubjectStanding(true, Roles: ["b", "a"])));
        together!.Roles.Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public async Task A_name_does_not_depend_on_registration_order()
    {
        var first = new First(new SubjectStanding(true, "From first"));
        var second = new Second(new SubjectStanding(true, "From second"));

        (await Of(first, second))!.Name.Should().Be("From first");
        (await Of(second, first))!.Name.Should().Be("From first");
    }

    [Fact]
    public async Task A_contributor_that_fails_fails_the_question()
    {
        var read = () => Of(new First(new SubjectStanding(true)), new Failing());

        await read.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class First(SubjectStanding? standing) : ISubjectStandingContributor
    {
        public Task<SubjectStanding?> Read(string subject, CancellationToken ct) => Task.FromResult(standing);
    }

    private sealed class Second(SubjectStanding? standing) : ISubjectStandingContributor
    {
        public Task<SubjectStanding?> Read(string subject, CancellationToken ct) => Task.FromResult(standing);
    }

    private sealed class Failing : ISubjectStandingContributor
    {
        public Task<SubjectStanding?> Read(string subject, CancellationToken ct) => throw new InvalidOperationException("The store cannot answer.");
    }
}
