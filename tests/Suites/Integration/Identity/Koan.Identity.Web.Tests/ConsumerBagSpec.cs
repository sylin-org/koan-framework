using AwesomeAssertions;
using Koan.Identity.Access;
using Koan.Identity.Roles;
using Xunit;

namespace Koan.Identity.Web.Tests;

/// <summary>An assembly with no access to Identity's internals builds a bag and resolves it over rows it holds.</summary>
public sealed class ConsumerBagSpec
{
    [Fact]
    public void A_consumer_builds_a_bag_and_resolves_it_over_loaded_rows_without_io()
    {
        var bag = new RoleBag("person:consumer", true, ["role:guests"], ["global:view", "global:post"]);
        bag.Roles.Should().Equal("authenticated", "everyone", "role:guests");
        bag.Permissions.Should().Equal("global:post", "global:view");

        var deny = new PermissionOverwrite
        {
            Id = PermissionOverwrite.KeyFor("board:7", OverwriteSubjectKind.Role, "role:guests"),
            ResourceId = "board:7", SubjectKind = OverwriteSubjectKind.Role, SubjectId = "role:guests", Deny = ["global:post"],
        };
        var here = bag.On("board:7", [deny], DateTimeOffset.UtcNow);

        here.Permissions.Should().Equal("global:view");
        var decision = here.Explain("global:post");
        decision.Allowed.Should().BeFalse();
        decision.ContributingFacts.Should().ContainSingle().Which.Should().Match<AccessFact>(fact =>
            fact.Kind == AccessFactKind.Deny && fact.Subject == "role:guests" && fact.RowId == deny.Id);

        var refuse = () => new RoleBag("person:consumer", true, [], ["post"]);
        refuse.Should().Throw<ArgumentException>().WithMessage("*is not a permission*");
    }
}
