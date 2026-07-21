using System;
using System.Linq;
using AwesomeAssertions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Pipeline;

/// <summary>
/// DATA-0105 phase 3b, Seams 1 + 3 — the generic managed-field registry and the managed-aware field
/// resolution, exercised with generic (non-tenant) descriptors so the seam is validated independent of any
/// axis. Pins: storage-name validation (the camel-case-stable invariant), per-type applicability, the
/// off = byte-identical gate, the managed ResolvedField shape, and the fail-loud in-memory guard.
/// </summary>
[Collection("managed-field-registry")]   // serialize: the registry is process-global static state
public sealed class ManagedFieldSeamSpec : IDisposable
{
    public ManagedFieldSeamSpec() => ManagedFieldRegistry.Reset();
    public void Dispose() => ManagedFieldRegistry.Reset();

    private sealed class Scoped { public string Id { get; set; } = ""; }
    private sealed class Exempt { public string Id { get; set; } = ""; }

    private static ManagedFieldDescriptor Field(string name, Func<Type, bool> appliesTo, Func<object?>? value = null)
        => new(name, typeof(string), value ?? (() => "v"), appliesTo);

    [Fact]
    public void IsEmpty_is_true_until_a_registration()
    {
        ManagedFieldRegistry.IsEmpty.Should().BeTrue();
        ManagedFieldRegistry.Register(Field("__x", _ => true));
        ManagedFieldRegistry.IsEmpty.Should().BeFalse();
    }

    [Theory]
    [InlineData("__koan_tenant")]   // leading underscore
    [InlineData("tenant_id")]        // all lowercase
    public void Register_accepts_a_camel_stable_storage_name(string name)
    {
        var act = () => ManagedFieldRegistry.Register(Field(name, _ => true));
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("TenantId")]   // leads with uppercase → camel-case would lowercase it
    [InlineData("Koan_Tenant")]
    [InlineData("")]
    public void Register_rejects_a_non_camel_stable_storage_name(string name)
    {
        var act = () => ManagedFieldRegistry.Register(Field(name, _ => true));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Register_is_idempotent_by_storage_name()
    {
        ManagedFieldRegistry.Register(Field("__x", _ => true));
        ManagedFieldRegistry.Register(Field("__x", _ => true));   // no-op
        ManagedFieldRegistry.All.Count(d => d.StorageName == "__x").Should().Be(1);
    }

    [Fact]
    public void ForType_filters_by_applicability()
    {
        ManagedFieldRegistry.Register(Field("__scoped", t => t == typeof(Scoped)));
        ManagedFieldRegistry.ForType(typeof(Scoped)).Should().ContainSingle(d => d.StorageName == "__scoped");
        ManagedFieldRegistry.ForType(typeof(Exempt)).Should().BeEmpty();
    }

    [Fact]
    public void FieldPathResolver_resolves_a_registered_managed_field_to_a_managed_field()
    {
        ManagedFieldRegistry.Register(Field("__scoped", t => t == typeof(Scoped)));

        var resolved = FieldPathResolver.Resolve(typeof(Scoped), FieldPath.Of("__scoped"));

        resolved.IsManaged.Should().BeTrue();
        resolved.StorageName.Should().Be("__scoped");
        resolved.Members.Should().BeEmpty();
        resolved.LeafType.Should().Be(typeof(string));
    }

    [Fact]
    public void FieldPathResolver_still_throws_for_an_unregistered_synthetic_field()
    {
        // The strict contract holds for any field that is neither a CLR member nor a registered managed field.
        var act = () => FieldPathResolver.Resolve(typeof(Scoped), FieldPath.Of("__never_registered"));
        act.Should().Throw<InvalidFilterFieldException>();
    }

    [Fact]
    public void A_managed_field_does_not_apply_to_an_exempt_type_and_stays_strict()
    {
        ManagedFieldRegistry.Register(Field("__scoped", t => t == typeof(Scoped)));
        // Exempt does not carry the field, so resolving it there is still a hard error (no silent pass-through).
        var act = () => FieldPathResolver.Resolve(typeof(Exempt), FieldPath.Of("__scoped"));
        act.Should().Throw<InvalidFilterFieldException>();
    }

    [Fact]
    public void Managed_GetValue_throws_so_a_residual_eval_can_never_silently_return_the_entity()
    {
        ManagedFieldRegistry.Register(Field("__scoped", t => t == typeof(Scoped)));
        var resolved = FieldPathResolver.Resolve(typeof(Scoped), FieldPath.Of("__scoped"));

        var act = () => resolved.GetValue(new Scoped { Id = "1" });

        act.Should().Throw<InvalidOperationException>().WithMessage("*pushed down*");
    }

    [Fact]
    public void ForType_orders_managed_fields_by_priority_stably()
    {
        // DATA-0105 §3 prerequisite (ARCH-0098 phase 0): a total, stable, explicit-priority order. Register out of
        // priority order; ForType must return them sorted by Priority (ties keep registration order).
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor("__late", typeof(string), () => "v", _ => true, Priority: 200));
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor("__early", typeof(string), () => "v", _ => true, Priority: 50));

        ManagedFieldRegistry.ForType(typeof(Scoped)).Select(d => d.StorageName)
            .Should().Equal("__early", "__late");
    }

    [Fact]
    public void Priority_defaults_to_zero_for_the_canonical_single_field()
    {
        ManagedFieldRegistry.Register(Field("__scoped", _ => true));
        ManagedFieldRegistry.ForType(typeof(Scoped)).Single().Priority.Should().Be(0);
    }
}
