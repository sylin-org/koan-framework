using System;
using System.Threading;
using AwesomeAssertions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Pipeline;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Pipeline;

/// <summary>
/// ARCH-0102 — <see cref="ReadScopeFold.Compose"/> tags each contributor's predicate with the
/// <see cref="FieldProvenance"/> of the managed field(s) it references (the relocated vector store-aware-push logic,
/// now keyed on the Phase-1a descriptor flags). An ambient-stamped equality (tenant) is enforceable on a write-stamp-only
/// store (<c>CombineWriteStamped</c>); an operation-sourced predicate (soft-delete) is excluded there but kept by the
/// primary-store <c>CombineAll</c>. This is the ONE composer the facade, the diagnostic, and the vector path share.
/// </summary>
[Collection("managed-field-registry")]   // serialize: the registry is process-global static state
public sealed class AodbComposeSpec : IDisposable
{
    public AodbComposeSpec() => ManagedFieldRegistry.Reset();
    public void Dispose() { _t.Value = null; ManagedFieldRegistry.Reset(); }

    private static readonly AsyncLocal<string?> _t = new();
    private sealed class Doc { }

    // A fake non-equality contributor over an operation-sourced field (the soft-delete shape).
    private sealed class HideDeleted : IReadFilterContributor
    {
        public Filter? ReadFilter(Type t) => t == typeof(Doc)
            ? Filter.On(FieldPath.Of("__del"), FilterOperator.Ne, FilterValue.Of(true)) : null;
        public Capability? RequiredCapability => DataCaps.Isolation.RowScoped;
        public bool ExcludesFromCache(Type t) => t == typeof(Doc);
    }

    [Fact]
    public void Off_composes_to_empty()
        => ReadScopeFold.Compose(Array.Empty<IReadFilterContributor>(), typeof(Doc)).IsEmpty.Should().BeTrue();

    [Fact]
    public void An_ambient_equality_is_enforceable_on_a_write_stamp_only_store()
    {
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor("__t", typeof(string), () => _t.Value, t => t == typeof(Doc)));
        _t.Value = "acme";
        var aodb = ReadScopeFold.Compose(new IReadFilterContributor[] { new ManagedEqualityReadContributor() }, typeof(Doc));

        aodb.Elements.Should().ContainSingle();
        aodb.Elements[0].Provenance.Should().Be(FieldProvenance.AmbientStamped);
        aodb.CombineWriteStamped().Should().NotBeNull();   // a write-stamp-only store CAN keep an ambient field current
        aodb.CombineAll().Should().NotBeNull();
        aodb.Cacheable.Should().BeTrue();
    }

    [Fact]
    public void An_operation_sourced_predicate_is_excluded_from_a_write_stamp_only_store()
    {
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor("__del", typeof(bool), () => null, t => t == typeof(Doc),
            AutoReadFilter: false, Provenance: FieldProvenance.OperationSourced));
        var aodb = ReadScopeFold.Compose(new IReadFilterContributor[] { new HideDeleted() }, typeof(Doc));

        aodb.Elements.Should().ContainSingle();
        aodb.Elements[0].Provenance.Should().Be(FieldProvenance.OperationSourced);
        aodb.CombineWriteStamped().Should().BeNull();      // the secondary store can't keep it current ⇒ excluded
        aodb.CombineAll().Should().NotBeNull();            // the primary store still applies it
        aodb.Cacheable.Should().BeFalse();                 // a non-equality contributor excludes its entity from cache
    }

    [Fact]
    public void Ambient_and_operation_sourced_compose_store_aware()
    {
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor("__t", typeof(string), () => _t.Value, t => t == typeof(Doc)));
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor("__del", typeof(bool), () => null, t => t == typeof(Doc),
            AutoReadFilter: false, Provenance: FieldProvenance.OperationSourced));
        _t.Value = "acme";
        var aodb = ReadScopeFold.Compose(
            new IReadFilterContributor[] { new ManagedEqualityReadContributor(), new HideDeleted() }, typeof(Doc));

        aodb.Elements.Should().HaveCount(2);
        aodb.CombineWriteStamped().Should().BeOfType<FieldFilter>();   // ONLY the ambient tenant Eq (the vector push)
        aodb.CombineAll().Should().BeOfType<AllOf>();                  // tenant Eq AND hide-deleted (the data push)
    }
}
