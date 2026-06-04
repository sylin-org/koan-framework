using System;
using AwesomeAssertions;
using Koan.Data.Core;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Routing;

/// <summary>
/// Enforcement specs for partition-name validation (re-enabled in EntityContext.With). The rule rejects
/// names that would NOT survive identifier sanitization unchanged — exactly the lossy names that could
/// collide a distinct partition onto the same physical store (silent cross-partition data bleed) — while
/// accepting GUIDs and already identifier-safe names so real usage (slugs, GUIDs, underscores) keeps working.
/// </summary>
public class PartitionNameValidationSpec
{
    [Theory]
    [InlineData("archive")]
    [InlineData("cold-tier")]
    [InlineData("backup.v2")]
    [InlineData("tenant_7")]
    [InlineData("A")]
    [InlineData("prod-us-east-1")]
    [InlineData("42")]
    [InlineData("019a5aff-79cb-7815-8dae-3700a698f840")] // GUID — first-class partition value
    public void Valid_partition_names_are_accepted(string partition)
    {
        var act = () => { using var _ = EntityContext.Partition(partition); };
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("tenant/7")]
    [InlineData("a b")]
    [InlineData("$scope")]
    [InlineData("%x%")]
    [InlineData("system$")]
    public void Collision_prone_partition_names_are_rejected(string partition)
    {
        var act = () => { using var _ = EntityContext.Partition(partition); };
        act.Should().Throw<ArgumentException>().WithParameterName("Partition");
    }

    [Fact]
    public void Whitespace_only_partition_is_treated_as_no_partition_and_not_rejected()
    {
        var act = () => { using var _ = EntityContext.Partition("   "); };
        act.Should().NotThrow();
    }

    [Fact]
    public void Lossy_collision_forms_are_rejected_so_only_the_safe_form_reaches_storage()
    {
        // "tenant/7" and "tenant 7" both sanitize to "tenant_7"; rejecting the lossy forms means only the
        // already-safe "tenant_7" can reach storage — the silent merge can no longer happen.
        FluentActions.Invoking(() => { using var _ = EntityContext.Partition("tenant_7"); }).Should().NotThrow();
        FluentActions.Invoking(() => { using var _ = EntityContext.Partition("tenant/7"); }).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => { using var _ = EntityContext.Partition("tenant 7"); }).Should().Throw<ArgumentException>();
    }
}
