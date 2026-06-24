using System;
using AwesomeAssertions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Axes;
using Xunit;

namespace Koan.Data.Axes.Tests;

/// <summary>
/// ARCH-0101 §7/§8 (D1) — the <see cref="Axis"/> builder's argument guards and the per-axis <see cref="Axis.Validate"/>
/// shape rules (fail loud at boot, never ship a half-axis). The builder is purely accumulative; mode-specific validity
/// is enforced in one place. Pins the malformed-declaration set the design review flagged.
/// </summary>
public sealed class AxisBuilderSpec
{
    private sealed class Doc { }

    private static readonly Filter Hide = Filter.Eq("__x", "v");

    // --- verb argument guards ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Named_rejects_blank(string id)
        => FluentActions.Invoking(() => new Axis().Named(id)).Should().Throw<ArgumentException>();

    [Fact]
    public void Field_rejects_blank_storage_name()
        => FluentActions.Invoking(() => new Axis().Field("", () => "v")).Should().Throw<ArgumentException>();

    [Fact]
    public void Field_rejects_null_value_provider()
        => FluentActions.Invoking(() => new Axis().Field("__x", null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void Reads_rejects_null_predicate()
        => FluentActions.Invoking(() => new Axis().Reads(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void Carries_rejects_null_carrier()
        => FluentActions.Invoking(() => new Axis().Carries(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void AppliesTo_rejects_null_predicate()
        => FluentActions.Invoking(() => new Axis().AppliesTo(null!)).Should().Throw<ArgumentNullException>();

    // --- Logical helpers ---

    [Fact]
    public void Logical_SetTrue_sets_the_field_to_true()
    {
        var op = Logical.SetTrue("__deleted");
        op.Field.Should().Be("__deleted");
        op.OnDeleteValue.Should().Be(true);
    }

    [Fact]
    public void Logical_Set_sets_an_arbitrary_value()
    {
        var op = Logical.Set("__state", 7);
        op.Field.Should().Be("__state");
        op.OnDeleteValue.Should().Be(7);
    }

    // --- Validate: well-formed axes pass ---

    [Fact]
    public void A_soft_delete_shaped_axis_validates()
        => FluentActions.Invoking(() => new Axis()
            .Named("archived").AppliesTo(t => t == typeof(Doc))
            .Field("__archived", () => null, typeof(bool))
            .Reads(_ => Hide)
            .OnDelete(Logical.SetTrue("__archived"))
            .Validate()).Should().NotThrow();

    [Fact]
    public void A_tenant_shaped_equality_axis_validates()
        => FluentActions.Invoking(() => new Axis()
            .Named("tenant").Field("__koan_tenant", () => "acme")
            .Validate()).Should().NotThrow();

    [Fact]
    public void A_container_axis_validates()
        => FluentActions.Invoking(() => new Axis()
            .Named("tenant").Mode(AxisMode.Container).Field("__t", () => "T1")
            .Validate()).Should().NotThrow();

    // --- Validate: malformed axes fail loud ---

    [Fact]
    public void An_unnamed_axis_is_rejected()
        => FluentActions.Invoking(() => new Axis().Field("__x", () => "v").Validate())
            .Should().Throw<InvalidOperationException>().WithMessage("*Named*");

    [Fact]
    public void An_empty_axis_with_no_plane_is_rejected()
        => FluentActions.Invoking(() => new Axis().Named("empty").Validate())
            .Should().Throw<InvalidOperationException>().WithMessage("*no plane*");

    [Fact]
    public void OnDelete_without_a_matching_field_is_rejected()
        => FluentActions.Invoking(() => new Axis()
            .Named("archived").Field("__archived", () => null, typeof(bool))
            .OnDelete(Logical.SetTrue("__different"))
            .Validate()).Should().Throw<InvalidOperationException>().WithMessage("*matching .Field*");

    [Fact]
    public void OnDelete_with_no_field_at_all_is_rejected()
        => FluentActions.Invoking(() => new Axis()
            .Named("archived").Reads(_ => Hide).OnDelete(Logical.SetTrue("__archived"))
            .Validate()).Should().Throw<InvalidOperationException>();

    [Fact]
    public void Container_without_a_field_is_rejected()
        => FluentActions.Invoking(() => new Axis()
            .Named("tenant").Mode(AxisMode.Container).Carries(new FakeCarrier("k"))
            .Validate()).Should().Throw<InvalidOperationException>().WithMessage("*value source*");

    [Fact]
    public void OnDelete_value_type_must_match_the_field_clr_type()
        => FluentActions.Invoking(() => new Axis()
            .Named("archived").Field("__archived", () => null, typeof(bool))
            .OnDelete(Logical.Set("__archived", "nope"))   // a string value into a bool field
            .Validate()).Should().Throw<InvalidOperationException>().WithMessage("*assignable to the field's CLR type*");

    [Fact]
    public void OnDelete_with_a_null_value_is_allowed_regardless_of_field_type()
        => FluentActions.Invoking(() => new Axis()
            .Named("archived").Field("__archived", () => null, typeof(bool))
            .OnDelete(Logical.Set("__archived", null))     // null = clear the field; type-check skipped
            .Validate()).Should().NotThrow();

    [Fact]
    public void Container_with_a_non_string_field_clr_type_is_rejected()
        => FluentActions.Invoking(() => new Axis()
            .Named("tenant").Mode(AxisMode.Container).Field("__t", () => 1, typeof(int))
            .Validate()).Should().Throw<InvalidOperationException>().WithMessage("*string container token*");

    [Fact]
    public void Container_with_Reads_is_rejected()
        => FluentActions.Invoking(() => new Axis()
            .Named("tenant").Mode(AxisMode.Container).Field("__t", () => "T1").Reads(_ => Hide)
            .Validate()).Should().Throw<InvalidOperationException>().WithMessage("*cannot declare .Reads*");

    [Fact]
    public void Container_with_OnDelete_is_rejected()
        => FluentActions.Invoking(() => new Axis()
            .Named("tenant").Mode(AxisMode.Container).Field("__t", () => "T1").OnDelete(Logical.SetTrue("__t"))
            .Validate()).Should().Throw<InvalidOperationException>().WithMessage("*cannot declare .OnDelete*");

    [Fact]
    public void Database_without_a_carrier_is_rejected()
        => FluentActions.Invoking(() => new Axis()
            .Named("shard").Mode(AxisMode.Database).Field("__s", () => "s")
            .Validate()).Should().Throw<InvalidOperationException>().WithMessage("*must declare .Carries*");

    [Fact]
    public void Database_with_a_field_filter_or_override_is_rejected()
        => FluentActions.Invoking(() => new Axis()
            .Named("shard").Mode(AxisMode.Database).Carries(new FakeCarrier("k")).Field("__s", () => "s")
            .Validate()).Should().Throw<InvalidOperationException>().WithMessage("*cannot declare .Field*");

    [Fact]
    public void A_database_axis_with_only_a_carrier_validates()
        => FluentActions.Invoking(() => new Axis()
            .Named("shard").Mode(AxisMode.Database).Carries(new FakeCarrier("k"))
            .Validate()).Should().NotThrow();

    private sealed class FakeCarrier(string key) : IAmbientSliceCarrier
    {
        public string AxisKey => key;
        public string? Capture() => null;
        public IDisposable Restore(string captured) => new Noop();
        public IDisposable Suppress() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
