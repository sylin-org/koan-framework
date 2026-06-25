using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Model;
using Koan.Data.Core.Pipeline;
using Koan.Tests.Data.Core.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Pipeline;

/// <summary>
/// DATA-0106 §4 — fail-closed over the contributor UNION, through a real <c>AddKoan()</c> boot (ARCH-0079) on a
/// deliberately non-isolating adapter (the <c>fake-noniso</c> fixture — it does NOT announce
/// <c>Isolation.RowScoped</c>; since ARCH-0103 every real KV adapter, JSON included, isolates). Two proofs the prior
/// managed-field design could not make:
/// <list type="bullet">
/// <item>A <b>READ</b> (not just a write) under an active equality axis fails closed on a non-isolating adapter — the
/// read-side throw the old design only structurally implied.</item>
/// <item>The CRITICAL: a <b>pure predicate contributor with no managed field</b> (the moderation shape) fails closed
/// on a non-isolating adapter. The old fail-closed was gated on <c>_managed.Count &gt; 0</c>, so such a contributor
/// would have bypassed it entirely and read unfiltered / post-fetched (itself a leak). The trigger now rides the
/// contributor union.</item>
/// </list>
/// </summary>
[Collection("managed-field-registry")]   // serialize: the registry is process-global static state
public sealed class ReadScopeFailClosedSpec : IDisposable
{
    private static readonly AsyncLocal<string?> _scope = new();

    public ReadScopeFailClosedSpec() => ManagedFieldRegistry.Reset();
    public void Dispose() { _scope.Value = null; ManagedFieldRegistry.Reset(); }

    // Force the deliberately non-isolating fake adapter (FilterSupport.Full, but NO Isolation.RowScoped).
    private static IReadOnlyDictionary<string, string?> ForceNonIsolating()
        => new Dictionary<string, string?> { ["Koan:Data:Sources:Default:Adapter"] = "fake-noniso" };

    public sealed class JNote : Entity<JNote> { public string Title { get; set; } = ""; }

    // A pure predicate axis: a read-filter contributor with NO managed field behind it (the moderation shape).
    private sealed class PredicateOnlyContributor : IReadFilterContributor
    {
        public Filter? ReadFilter(Type entityType)
            => entityType == typeof(JNote)
                ? Filter.On(FieldPath.Of("Title"), FilterOperator.Ne, FilterValue.Of("x"))   // Filter has no Ne factory
                : null;
        public Capability? RequiredCapability => DataCaps.Isolation.RowScoped;
        public bool ExcludesFromCache(Type entityType) => entityType == typeof(JNote);
    }

    [Fact(DisplayName = "fail-closed: a READ under an active equality axis throws on a non-isolating adapter")]
    public async Task Read_under_an_equality_axis_fails_closed_on_a_non_isolating_adapter()
    {
        ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
            StorageName: "__scope",
            ClrType: typeof(string),
            ValueProvider: () => _scope.Value,
            AppliesTo: t => t == typeof(JNote),
            RequiredCapability: DataCaps.Isolation.RowScoped));

        await using var fx = await DataCoreRuntimeFixture.CreateAsync(extraSettings: ForceNonIsolating());
        fx.ResetEntityCaches();
        _scope.Value = "acme";
        using var _ = fx.UsePartition();

        var act = async () => await JNote.All();   // the read folds Eq(__scope, acme) → fails closed (no RowScoped)
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not announce*");
    }

    [Fact(DisplayName = "fail-closed (CRITICAL): a pure predicate contributor with no managed field throws on a non-isolating adapter")]
    public async Task A_pure_predicate_contributor_fails_closed_on_a_non_isolating_adapter()
    {
        // No managed field at all — the old fail-closed (gated on _managed.Count > 0) would have skipped this entirely.
        await using var fx = await DataCoreRuntimeFixture.CreateAsync(
            extraSettings: ForceNonIsolating(),
            configureServices: s => s.AddSingleton<IReadFilterContributor>(new PredicateOnlyContributor()));
        fx.ResetEntityCaches();
        using var _ = fx.UsePartition();

        var act = async () => await JNote.All();   // the contributor union yields a filter → fails closed (no RowScoped)
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not announce*");
    }
}
