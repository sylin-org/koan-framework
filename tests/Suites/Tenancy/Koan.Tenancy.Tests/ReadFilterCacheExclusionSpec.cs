using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Cache.Abstractions.Policies;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Pipeline;
using Koan.Tenancy.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// DATA-0106 §5 — a <see cref="CacheableAttribute"/> entity with a <b>non-equality</b> managed axis is excluded from
/// caching, and that exclusion is the only thing standing between a viewer and a hidden row. The id-keyed cache
/// namespace is equality-by-construction; a viewer-context predicate (moderation) cannot be a cache-key segment, so
/// <c>AppendManagedScope</c> skips it — meaning a moderator and a viewer compute the SAME key for the same id. If the
/// entity were cached, the moderator's read would populate that key and the viewer's read would hit it = LEAK. The
/// non-equality axis excludes the whole entity, so the viewer goes to the store where the read-filter predicate hides
/// the row. Hosted in the tenancy suite because it wires both Koan.Cache and the RowScoped SQLite adapter; the axis is
/// a generic fake (no tenancy). Proven through a real <c>AddKoan()</c> boot (ARCH-0079).
/// </summary>
public sealed class ReadFilterCacheExclusionSpec : IDisposable
{
    private static readonly AsyncLocal<string?> _writeStatus = new();
    private static readonly AsyncLocal<bool> _moderator = new();
    private static readonly AsyncLocal<string?> _region = new();

    public void Dispose()
    {
        _writeStatus.Value = null;
        _moderator.Value = false;
        _region.Value = null;
        ManagedFieldRegistry.Reset();
    }

    private static IDisposable Writing(string status) => Set(_writeStatus, status, () => _writeStatus.Value);
    private static IDisposable AsModerator() => Set(_moderator, true, () => _moderator.Value);
    private static IDisposable InRegion(string region) => Set(_region, region, () => _region.Value);

    private static IDisposable Set<T>(AsyncLocal<T> slot, T value, Func<T> read)
    {
        var prev = read();
        slot.Value = value;
        return new Pop(() => slot.Value = prev);
    }

    private sealed class Pop(Action undo) : IDisposable { public void Dispose() => undo(); }

    private static IDisposable Isolate() => EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

    // [HostScoped] so the tenant axis does not apply — this entity carries ONLY the fake non-equality moderation axis.
    [HostScoped]
    [Cacheable(300)]
    public sealed class VisDoc : Entity<VisDoc> { public string Title { get; set; } = ""; }

    private static void RegisterAxis() => ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
        StorageName: "__vis",
        ClrType: typeof(string),
        ValueProvider: () => _writeStatus.Value,
        AppliesTo: t => t == typeof(VisDoc),
        RequiredCapability: DataCaps.Isolation.RowScoped,
        AutoReadFilter: false));

    private sealed class VisReadContributor : IReadFilterContributor
    {
        public Filter? ReadFilter(Type entityType)
        {
            if (entityType != typeof(VisDoc)) return null;
            if (_moderator.Value) return null;
            return Filter.On(FieldPath.Of("__vis"), FilterOperator.Ne, FilterValue.Of("hidden"));   // Filter has no Ne factory
        }
        public Capability? RequiredCapability => DataCaps.Isolation.RowScoped;
        public bool ExcludesFromCache(Type entityType) => entityType == typeof(VisDoc);
    }

    // The PURE-predicate shape: a [Cacheable] entity scoped ONLY by a contributor, with NO managed field. Its visibility
    // is a CLR property (Status). The cache-exclusion must ride the contributor's ExcludesFromCache (the managed
    // registry can't see it) — adversarial-review HIGH #2.
    [HostScoped]
    [Cacheable(300)]
    public sealed class PredVisDoc : Entity<PredVisDoc> { public string Title { get; set; } = ""; public string Status { get; set; } = ""; }

    [HostScoped]
    [Cacheable(300)]
    public sealed class RegionDoc : Entity<RegionDoc> { public string Title { get; set; } = ""; }

    private static void RegisterDataLocalEqualityAxis() => ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
        StorageName: "__region",
        ClrType: typeof(string),
        ValueProvider: () => _region.Value,
        AppliesTo: type => type == typeof(RegionDoc),
        RequiredCapability: DataCaps.Isolation.RowScoped));

    private sealed class PredVisReadContributor : IReadFilterContributor
    {
        public Filter? ReadFilter(Type entityType)
        {
            if (entityType != typeof(PredVisDoc)) return null;
            if (_moderator.Value) return null;
            return Filter.On(FieldPath.Of("Status"), FilterOperator.Ne, FilterValue.Of("hidden"));
        }
        public Capability? RequiredCapability => DataCaps.Isolation.RowScoped;
        public bool ExcludesFromCache(Type entityType) => entityType == typeof(PredVisDoc);
    }

    [Fact(DisplayName = "no leak: a [Cacheable] entity with a non-equality axis is cache-excluded and never serves a hidden row")]
    public async Task Cacheable_with_a_non_equality_axis_is_excluded_and_does_not_leak()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(
            extraSettings: new Dictionary<string, string?> { ["Koan:Tenancy:Posture"] = "Closed" },
            configureServices: s => s.AddSingleton<IReadFilterContributor>(new VisReadContributor()));
        RegisterAxis();
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        VisDoc hidden;
        using (AsModerator()) using (Writing("hidden")) hidden = await new VisDoc { Title = "secret" }.Save();

        // The moderator reads it (this would populate the cache under key K if VisDoc were cached).
        using (AsModerator()) (await VisDoc.Get(hidden.Id))!.Title.Should().Be("secret");

        // A viewer computes the SAME key K (the non-equality axis is not a key segment). Were VisDoc cached this would
        // be a HIT and leak the hidden row; because the axis excludes it, the viewer hits the store → predicate hides it.
        (await VisDoc.Get(hidden.Id)).Should().BeNull();
    }

    [Fact(DisplayName = "no leak: a [Cacheable] entity scoped ONLY by a pure-predicate contributor (no managed field) is cache-excluded")]
    public async Task Cacheable_with_a_pure_predicate_contributor_is_excluded()
    {
        // No managed field at all — exclusion must come from the contributor's ExcludesFromCache, not the managed
        // registry. Without that signal the entity is cached by id with no scope segment and viewer B hits viewer A's
        // entry (adversarial-review HIGH #2).
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(
            extraSettings: new Dictionary<string, string?> { ["Koan:Tenancy:Posture"] = "Closed" },
            configureServices: s => s.AddSingleton<IReadFilterContributor>(new PredVisReadContributor()));
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        PredVisDoc hidden;
        using (AsModerator()) hidden = await new PredVisDoc { Title = "secret", Status = "hidden" }.Save();

        // The moderator reads it (this would populate the cache under key K if PredVisDoc were cached).
        using (AsModerator()) (await PredVisDoc.Get(hidden.Id))!.Title.Should().Be("secret");

        // A viewer computes the SAME key K (a pure-predicate axis contributes no key segment). Were it cached this is a
        // HIT and leaks the hidden row; the contributor's ExcludesFromCache excludes it, so the viewer hits the store →
        // the Status predicate hides it.
        (await PredVisDoc.Get(hidden.Id)).Should().BeNull();
    }

    [Fact(DisplayName = "no leak: a Data-only equality axis is cache-excluded until it joins cross-pillar segmentation")]
    public async Task Cacheable_with_a_data_only_equality_axis_is_excluded()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync();
        RegisterDataLocalEqualityAxis();
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        RegionDoc regional;
        using (InRegion("north"))
        {
            regional = await new RegionDoc { Title = "north-only" }.Save();
            (await RegionDoc.Get(regional.Id))!.Title.Should().Be("north-only");
        }

        using (InRegion("south"))
            (await RegionDoc.Get(regional.Id)).Should().BeNull();
    }
}
