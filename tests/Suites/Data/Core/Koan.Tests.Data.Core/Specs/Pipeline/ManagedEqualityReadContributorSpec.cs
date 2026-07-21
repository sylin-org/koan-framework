using System;
using System.Threading;
using AwesomeAssertions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Pipeline;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Pipeline;

/// <summary>
/// DATA-0106 §2 — the built-in <see cref="ManagedEqualityReadContributor"/> reproduces the former bespoke
/// <c>ManagedReadFilter</c> tri-state VERBATIM: zero survivors ⇒ null (the unfiltered fast path), one ⇒ that
/// <c>Filter.Eq</c>, many ⇒ <c>Filter.All</c>; a null ambient value (off / host) is skipped; an
/// <see cref="ManagedFieldDescriptor.AutoReadFilter"/> = <c>false</c> (non-equality) descriptor is skipped (it
/// supplies its own predicate). This is the re-home that makes tenancy's read-filter a registered contributor.
/// </summary>
[Collection("managed-field-registry")]   // serialize: the registry is process-global static state
public sealed class ManagedEqualityReadContributorSpec : IDisposable
{
    public ManagedEqualityReadContributorSpec() => ManagedFieldRegistry.Reset();
    public void Dispose() { _a.Value = null; _b.Value = null; ManagedFieldRegistry.Reset(); }

    private static readonly AsyncLocal<string?> _a = new();
    private static readonly AsyncLocal<string?> _b = new();
    private sealed class Doc { public string Id { get; set; } = ""; }

    private static readonly IReadFilterContributor Sut = new ManagedEqualityReadContributor();

    // Filter records can't be compared via .Be() (FieldPath carries a Segments list ⇒ reference equality); assert parts.
    private static void ShouldBeEq(Filter? f, string field, object? value)
    {
        f.Should().BeOfType<FieldFilter>();
        var ff = (FieldFilter)f!;
        ff.Field.Leaf.Should().Be(field);
        ff.Operator.Should().Be(FilterOperator.Eq);
        ff.Value.Should().Be(FilterValue.Of(value));
    }

    [Fact]
    public void Empty_registry_yields_null()
    {
        Sut.ReadFilter(typeof(Doc)).Should().BeNull();
    }

    [Fact]
    public void A_single_active_equality_axis_yields_one_Eq()
    {
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor("__a", typeof(string), () => _a.Value, t => t == typeof(Doc)));
        _a.Value = "acme";
        ShouldBeEq(Sut.ReadFilter(typeof(Doc)), "__a", "acme");
    }

    [Fact]
    public void A_null_ambient_value_is_skipped_yielding_null()
    {
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor("__a", typeof(string), () => _a.Value, t => t == typeof(Doc)));
        _a.Value = null;   // off / host
        Sut.ReadFilter(typeof(Doc)).Should().BeNull();
    }

    [Fact]
    public void An_AutoReadFilter_false_axis_is_skipped_even_when_active()
    {
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor("__a", typeof(string), () => _a.Value, t => t == typeof(Doc), AutoReadFilter: false));
        _a.Value = "acme";   // stamped on write, but NOT an equality read-filter
        Sut.ReadFilter(typeof(Doc)).Should().BeNull();
    }

    [Fact]
    public void Two_active_equality_axes_fold_into_an_AllOf_in_priority_order()
    {
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor("__a", typeof(string), () => _a.Value, t => t == typeof(Doc), Priority: 10));
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor("__b", typeof(string), () => _b.Value, t => t == typeof(Doc), Priority: 20));
        _a.Value = "acme";
        _b.Value = "blue";

        var folded = Sut.ReadFilter(typeof(Doc));

        folded.Should().BeOfType<AllOf>();
        var ops = ((AllOf)folded!).Operands;
        ops.Should().HaveCount(2);
        ShouldBeEq(ops[0], "__a", "acme");
        ShouldBeEq(ops[1], "__b", "blue");
    }

    [Fact]
    public void One_active_one_null_yields_just_the_active_Eq()
    {
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor("__a", typeof(string), () => _a.Value, t => t == typeof(Doc), Priority: 10));
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor("__b", typeof(string), () => _b.Value, t => t == typeof(Doc), Priority: 20));
        _a.Value = "acme";
        _b.Value = null;     // off ⇒ skipped, so no 1-element AllOf

        ShouldBeEq(Sut.ReadFilter(typeof(Doc)), "__a", "acme");
    }

    [Fact]
    public void Capability_is_null_so_per_descriptor_caps_drive_the_facade_fail_closed()
    {
        Sut.RequiredCapability.Should().BeNull();
    }
}
