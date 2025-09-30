using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Data.Core;
using Koan.Canon;
using Koan.Canon.Attributes;
using Koan.Canon.Diagnostics;
using Koan.Canon.Infrastructure;
using Koan.Canon.Model;
using Koan.Canon.Options;
using Xunit;

namespace Koan.Canon.Core.Tests;

// Minimal typed model used for discovery in tests
public sealed class TestModel : CanonEntity<TestModel>, IDictionary<string, object?>
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

    // Presence of at least one aggregation key avoids early NO_KEYS gate due to zero keys
    [AggregationKey]
    public string? Dummy
    {
        get => TryGetValue("dummy", out var value) ? value?.ToString() : null;
        set => _values["dummy"] = value;
    }

    public object? this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : null;
        set => _values[key] = value;
    }

    public ICollection<string> Keys => _values.Keys;

    public ICollection<object?> Values => _values.Values;

    public new int Count => _values.Count;

    public bool IsReadOnly => false;

    public void Add(string key, object? value) => _values.Add(key, value);

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public bool Remove(string key) => _values.Remove(key);

    public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);

    public void Add(KeyValuePair<string, object?> item) => ((IDictionary<string, object?>)_values).Add(item);

    public void Clear() => _values.Clear();

    public bool Contains(KeyValuePair<string, object?> item) => ((IDictionary<string, object?>)_values).Contains(item);

    public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex) => ((IDictionary<string, object?>)_values).CopyTo(array, arrayIndex);

    public bool Remove(KeyValuePair<string, object?> item) => ((IDictionary<string, object?>)_values).Remove(item);

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}

// Separate model for identity tests to avoid cross-test state
public sealed class IdentityModel : CanonEntity<IdentityModel>, IDictionary<string, object?>
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

    [AggregationKey]
    public string? Dummy
    {
        get => TryGetValue("dummy", out var value) ? value?.ToString() : null;
        set => _values["dummy"] = value;
    }

    public object? this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : null;
        set => _values[key] = value;
    }

    public ICollection<string> Keys => _values.Keys;

    public ICollection<object?> Values => _values.Values;

    public new int Count => _values.Count;

    public bool IsReadOnly => false;

    public void Add(string key, object? value) => _values.Add(key, value);

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public bool Remove(string key) => _values.Remove(key);

    public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);

    public void Add(KeyValuePair<string, object?> item) => ((IDictionary<string, object?>)_values).Add(item);

    public void Clear() => _values.Clear();

    public bool Contains(KeyValuePair<string, object?> item) => ((IDictionary<string, object?>)_values).Contains(item);

    public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex) => ((IDictionary<string, object?>)_values).CopyTo(array, arrayIndex);

    public bool Remove(KeyValuePair<string, object?> item) => ((IDictionary<string, object?>)_values).Remove(item);

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}

// Envelope declaring an external-id for IdentityModel via reserved identifier.external.* key
public sealed class IdentityEnvelope
{
    public required string ExternalRef { get; init; }

    public required string System { get; init; }
    public required string Adapter { get; init; }
}

public class TypedCanonTests
{
    private static IHost? _host;

    private static async Task<IServiceProvider> CreateHostAsync(string workDir, Action<CanonOptions>? configure = null)
    {
        // Ensure no background workers from the previous host can reinitialize Data caches with a soon-to-be-disposed provider
        if (_host is not null)
        {
            try { Koan.Core.Hosting.App.AppHost.Current = null; } catch { }
            try { await _host.StopAsync(TimeSpan.FromSeconds(1)); } catch { }
            try { _host.Dispose(); } catch { }
            _host = null;
        }
        // Now reset Data static configs so the next host rebinds cleanly
        Koan.Data.Core.TestHooks.ResetDataConfigs();
        var cfgDict = new Dictionary<string, string?>
        {
            { "Koan_DATA_PROVIDER", "json" },
            { "Koan:Data:Json:DirectoryPath", workDir },
        };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(cfgDict!).Build();
        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddKoanDataCore();
        sc.AddKoanCanon();

        if (configure is not null)
        {
            sc.Configure(configure);
        }
        else
        {
            sc.Configure<CanonOptions>(o =>
            {
                o.BatchSize = 100;
                o.PurgeEnabled = false;
            });
        }

        // (Previous host, if any, already disposed above)

        var host = new HostBuilder().ConfigureServices(s => { foreach (var sd in sc) s.Add(sd); }).Build();
        await host.StartAsync();
        // Wire Koan AppHost for DataService resolution
        Koan.Core.Hosting.App.AppHost.Current = host.Services;
        _host = host;
        return host.Services;
    }

    [Fact]
    public async Task Associate_Project_Materialize_TypedModel()
    {
        var dir = NewWorkDir();
        using var _ = new TempDir(dir);

        await CreateHostAsync(dir, o =>
        {
            o.BatchSize = 100;
            o.PurgeEnabled = false;
            o.ParkAndSweepEnabled = true;
        });
        var ct = CancellationToken.None;

        // Ingest two records with the same aggregation key "dummy" → same reference
        var rec1 = new StageRecord<TestModel>
        {
            Id = Guid.CreateVersion7().ToString("n"),
            SourceId = "src-proj-1",
            OccurredAt = DateTimeOffset.UtcNow,
            Data = new TestModel
            {
                ["dummy"] = "alpha",
                ["name.first"] = "Ann",
                ["name.last"] = "Lee"
            }
        };
        var rec2 = new StageRecord<TestModel>
        {
            Id = Guid.CreateVersion7().ToString("n"),
            SourceId = "src-proj-2",
            OccurredAt = DateTimeOffset.UtcNow,
            Data = new TestModel
            {
                ["dummy"] = "alpha",
                ["name.first"] = "Ann",
                ["name.last"] = "Lee"
            }
        };
        using (DataSetContext.With(CanonSets.StageShort(CanonSets.Intake)))
        {
            await Data<StageRecord<TestModel>, string>.UpsertAsync(rec1, CanonSets.StageShort(CanonSets.Intake), ct);
            await Data<StageRecord<TestModel>, string>.UpsertAsync(rec2, CanonSets.StageShort(CanonSets.Intake), ct);
        }

        // Wait until projection is produced: Canonical view present for the reference
        string? refId = null;
        await WaitUntilAsync(async () =>
        {
            using var _keyed = DataSetContext.With(CanonSets.StageShort(CanonSets.Keyed));
            var page = await Data<StageRecord<TestModel>, string>.FirstPage(100, ct);
            var match = page.FirstOrDefault(p => p.SourceId == rec1.SourceId || p.SourceId == rec2.SourceId);
            refId = match?.CorrelationId;
            return !string.IsNullOrWhiteSpace(refId);
        }, TimeSpan.FromSeconds(8));

        refId.Should().NotBeNullOrWhiteSpace();

        // Wait for Canonical to exist and projection task to be cleared
        await WaitUntilAsync(async () =>
        {
            var can = await Data<CanonicalProjection<TestModel>, string>.GetAsync($"{Constants.Views.Canonical}::{refId}", CanonSets.ViewShort(Constants.Views.Canonical), ct);
            return can is not null;
        }, TimeSpan.FromSeconds(10));

        // Assert Canonical view content shape
        var Canonical = await Data<CanonicalProjection<TestModel>, string>.GetAsync($"{Constants.Views.Canonical}::{refId}", CanonSets.ViewShort(Constants.Views.Canonical), ct);
        Canonical.Should().NotBeNull();
        Canonical!.ReferenceId.Should().Be(refId);
        Canonical.ViewName.Should().Be(Constants.Views.Canonical);
        Canonical.Model.Should().NotBeNull();
        // Verify nested Model contains name.first range with "Ann"
        var modelObj = Canonical.Model as IDictionary<string, object?>;
        modelObj.Should().NotBeNull();
        var nameObj = modelObj!["name"] as IDictionary<string, object?>;
        nameObj.Should().NotBeNull();
        var firstArr = nameObj!["first"] as IEnumerable<object?>;
        firstArr.Should().NotBeNull();
        firstArr!.Select(x => x?.ToString()).Should().Contain("Ann");

        // Assert Lineage view exists
        var lineage = await Data<LineageProjection<TestModel>, string>.GetAsync($"{Constants.Views.Lineage}::{refId}", CanonSets.ViewShort(Constants.Views.Lineage), ct);
        lineage.Should().NotBeNull();
        lineage!.ReferenceId.Should().Be(refId);
        lineage.ViewName.Should().Be(Constants.Views.Lineage);

        // Assert materialized root and policy state exist
        var root = await Data<DynamicCanonEntity<TestModel>, string>.GetAsync(refId!, ct);
        root.Should().NotBeNull();
        root!.Id.Should().Be(refId);
        root.Model.Should().NotBeNull();

        var policies = await Data<PolicyState<TestModel>, string>.GetAsync(refId!, ct);
        policies.Should().NotBeNull();
        policies!.ReferenceId.Should().Be(refId);
    }

    private static string NewWorkDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Koan-Canon-tests", Guid.CreateVersion7().ToString("n"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan timeout, TimeSpan? poll = null)
    {
        var start = DateTimeOffset.UtcNow;
        var interval = poll ?? TimeSpan.FromMilliseconds(200);
        while (DateTimeOffset.UtcNow - start < timeout)
        {
            if (await predicate()) return;
            await Task.Delay(interval);
        }
        await predicate();
    }

    [Fact]
    public async Task ParkAndSweep_Parks_NoKey_Records()
    {
        var dir = NewWorkDir();
        using var _ = new TempDir(dir);

        await CreateHostAsync(dir, o =>
        {
            o.BatchSize = 50;
            o.PurgeEnabled = true; // enable purge to validate TTL-based cleanup
            o.PurgeInterval = TimeSpan.FromMilliseconds(200);
            o.ParkedTtl = TimeSpan.FromSeconds(2); // allow observation before purge
            o.ParkAndSweepEnabled = true;
        });
        var ct = CancellationToken.None;

        // Ingest a record with no aggregation values and no envelope → should be rejected and parked
        var rec = new StageRecord<TestModel>
        {
            Id = Guid.CreateVersion7().ToString("n"),
            SourceId = "src-no-keys",
            OccurredAt = DateTimeOffset.UtcNow,
            Data = new TestModel { ["foo"] = "bar" }
        };
        using (DataSetContext.With(CanonSets.StageShort(CanonSets.Intake)))
        {
            await Data<StageRecord<TestModel>, string>.UpsertAsync(rec, CanonSets.StageShort(CanonSets.Intake), ct);
        }

        // Assert rejection report created
        await WaitUntilAsync(async () => (await RejectionReport.All(ct)).Any(r => r.ReasonCode == Constants.Rejections.NoKeys), TimeSpan.FromSeconds(6));
        var rejects = await RejectionReport.All(ct);
        rejects.Should().NotBeEmpty();

        // Assert parked copy exists
        using (DataSetContext.With(CanonSets.StageShort(CanonSets.Parked)))
        {
            await WaitUntilAsync(async () => (await Data<ParkedRecord<TestModel>, string>.FirstPage(50, ct)).Any(), TimeSpan.FromSeconds(4));
            var parked = await Data<ParkedRecord<TestModel>, string>.FirstPage(50, ct);
            parked.Should().Contain(p => p.Id == rec.Id && p.ReasonCode == Constants.Rejections.NoKeys);
        }

        // After TTL elapses and with purge enabled, parked should be cleaned up
        using (DataSetContext.With(CanonSets.StageShort(CanonSets.Parked)))
        {
            // Wait a bit longer than TTL to allow purge loop to catch up
            await WaitUntilAsync(async () =>
            {
                var page = await Data<ParkedRecord<TestModel>, string>.FirstPage(50, ct);
                return !page.Any();
            }, TimeSpan.FromSeconds(8));
            var parkedAfter = await Data<ParkedRecord<TestModel>, string>.FirstPage(50, ct);
            parkedAfter.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Canonical_ExcludePrefixes_Suppresses_Tags()
    {
        var dir = NewWorkDir();
        using var _ = new TempDir(dir);

        await CreateHostAsync(dir, o =>
        {
            o.BatchSize = 50;
            o.PurgeEnabled = false;
            o.ParkAndSweepEnabled = true;
            o.CanonicalExcludeTagPrefixes = new[] { "name." };
        });
        var ct = CancellationToken.None;

        var rec = new StageRecord<TestModel>
        {
            Id = Guid.CreateVersion7().ToString("n"),
            SourceId = "src-exclude-1",
            OccurredAt = DateTimeOffset.UtcNow,
            Data = new TestModel
            {
                ["dummy"] = "k1",
                ["name.first"] = "Ann",
                ["name.last"] = "Lee",
                ["age"] = "42"
            }
        };
        using (DataSetContext.With(CanonSets.StageShort(CanonSets.Intake)))
        {
            await Data<StageRecord<TestModel>, string>.UpsertAsync(rec, CanonSets.StageShort(CanonSets.Intake), ct);
        }

        string? refId = null;
        await WaitUntilAsync(async () =>
        {
            using var _keyed = DataSetContext.With(CanonSets.StageShort(CanonSets.Keyed));
            var page = await Data<StageRecord<TestModel>, string>.FirstPage(50, ct);
            var matched = page.FirstOrDefault(p => p.SourceId == rec.SourceId);
            refId = matched?.CorrelationId;
            return matched is not null && !string.IsNullOrWhiteSpace(refId);
        }, TimeSpan.FromSeconds(8));

        refId.Should().NotBeNullOrWhiteSpace();

        // Wait for Canonical to exist
        await WaitUntilAsync(async () =>
        {
            var can = await Data<CanonicalProjection<TestModel>, string>.GetAsync($"{Constants.Views.Canonical}::{refId}", CanonSets.ViewShort(Constants.Views.Canonical), ct);
            return can is not null;
        }, TimeSpan.FromSeconds(8));

        var Canonical = await Data<CanonicalProjection<TestModel>, string>.GetAsync($"{Constants.Views.Canonical}::{refId}", CanonSets.ViewShort(Constants.Views.Canonical), ct);
        Canonical.Should().NotBeNull();
        var modelObj = Canonical!.Model as IDictionary<string, object?>;
        modelObj.Should().NotBeNull();
        // Excluded: name.*
        modelObj!.ContainsKey("name").Should().BeFalse();
        // Not excluded: age
        modelObj!.ContainsKey("age").Should().BeTrue();

        var lineage = await Data<LineageProjection<TestModel>, string>.GetAsync($"{Constants.Views.Lineage}::{refId}", CanonSets.ViewShort(Constants.Views.Lineage), ct);
        lineage.Should().NotBeNull();
        // Lineage should not include excluded tag keys
        var lineageView = lineage!.View;
        lineageView.Should().NotBeNull();
        lineageView!.ContainsKey("name.first").Should().BeFalse();
        lineageView!.ContainsKey("name.last").Should().BeFalse();
        lineageView!.ContainsKey("age").Should().BeTrue();
    }

    [Fact]
    public async Task ProjectionTask_Cleared_And_ReferenceItem_Flags_Reset()
    {
        var dir = NewWorkDir();
        using var _ = new TempDir(dir);

        await CreateHostAsync(dir, o =>
        {
            o.BatchSize = 50;
            o.PurgeEnabled = false;
            o.ParkAndSweepEnabled = true;
        });
        var ct = CancellationToken.None;

        var rec = new StageRecord<TestModel>
        {
            Id = Guid.CreateVersion7().ToString("n"),
            SourceId = "src-projtask-1",
            OccurredAt = DateTimeOffset.UtcNow,
            Data = new TestModel
            {
                ["dummy"] = "z1",
                ["city"] = "Paris"
            }
        };
        using (DataSetContext.With(CanonSets.StageShort(CanonSets.Intake)))
        {
            await Data<StageRecord<TestModel>, string>.UpsertAsync(rec, CanonSets.StageShort(CanonSets.Intake), ct);
        }

        string? refId = null;
        await WaitUntilAsync(async () =>
        {
            using var _keyed = DataSetContext.With(CanonSets.StageShort(CanonSets.Keyed));
            var page = await Data<StageRecord<TestModel>, string>.FirstPage(50, ct);
            var matched = page.FirstOrDefault(p => p.SourceId == rec.SourceId);
            refId = matched?.CorrelationId;
            return matched is not null && !string.IsNullOrWhiteSpace(refId);
        }, TimeSpan.FromSeconds(8));

        refId.Should().NotBeNullOrWhiteSpace();

        // Wait for ReferenceItem to be updated and RequiresProjection cleared
        await WaitUntilAsync(async () =>
        {
            var ri = await Data<ReferenceItem<TestModel>, string>.GetAsync(refId!, ct);
            return ri is not null && ri.RequiresProjection == false && ri.Version > 0;
        }, TimeSpan.FromSeconds(10));

        var refItem = await Data<ReferenceItem<TestModel>, string>.GetAsync(refId!, ct);
        refItem.Should().NotBeNull();
        refItem!.RequiresProjection.Should().BeFalse();
        refItem!.Version.Should().BeGreaterThan(0UL);

        // Ensure there are no pending ProjectionTask<TestModel>
        var tasks = await Data<ProjectionTask<TestModel>, string>.FirstPage(50, ct);
        tasks.Should().BeEmpty();
    }

    [Fact]
    public async Task Identity_On_Miss_Issues_Uuid_And_Creates_Provisional_Link()
    {
        var dir = NewWorkDir();
        using var _ = new TempDir(dir);

        await CreateHostAsync(dir, o =>
        {
            o.BatchSize = 50;
            o.PurgeEnabled = false;
            o.ParkAndSweepEnabled = true;
        });
        var ct = CancellationToken.None;

        // Prepare a payload with envelope + discovered external id; skip tag values to force identity path
        var env = new IdentityEnvelope { System = "sysA", Adapter = "adp1", ExternalRef = "ext-123" };
        var payload = new IdentityModel
        {
            [Constants.Envelope.System] = env.System,
            [Constants.Envelope.Adapter] = env.Adapter,
            [$"{Koan.Canon.Infrastructure.Constants.Reserved.IdentifierExternalPrefix}default"] = env.ExternalRef
        };

        var rec = new StageRecord<IdentityModel>
        {
            Id = Guid.CreateVersion7().ToString("n"),
            SourceId = "src-identity-1",
            OccurredAt = DateTimeOffset.UtcNow,
            Data = payload
        };
        using (DataSetContext.With(CanonSets.StageShort(CanonSets.Intake)))
        {
            await Data<StageRecord<IdentityModel>, string>.UpsertAsync(rec, CanonSets.StageShort(CanonSets.Intake), ct);
        }

        // Wait for move to keyed and ensure CorrelationId (UUID) assigned
        string? correlation = null;
        await WaitUntilAsync(async () =>
        {
            using var _keyed = DataSetContext.With(CanonSets.StageShort(CanonSets.Keyed));
            var page = await Data<StageRecord<IdentityModel>, string>.FirstPage(50, ct);
            var matched = page.FirstOrDefault(p => p.SourceId == rec.SourceId);
            correlation = matched?.CorrelationId;
            return matched is not null && !string.IsNullOrWhiteSpace(correlation);
        }, TimeSpan.FromSeconds(8));

        correlation.Should().NotBeNullOrWhiteSpace();

        // Verify identity link created and provisional, pointing to correlation UUID
        var compositeKey = string.Join('|', env.System, env.Adapter, env.ExternalRef);
        var link = await Data<IdentityLink<IdentityModel>, string>.GetAsync(compositeKey, ct);
        link.Should().NotBeNull();
        link!.ReferenceId.Should().Be(correlation);
        link.Provisional.Should().BeTrue();

        // Verify a KeyIndex exists for the composite candidate
        var ki = await Data<KeyIndex<IdentityModel>, string>.GetAsync(compositeKey, ct);
        ki.Should().NotBeNull();
        ki!.ReferenceId.Should().Be(correlation);
    }

    [Fact]
    public async Task MultiOwnerCollision_Parks_Record_With_Both_Keys()
    {
        var dir = NewWorkDir();
        using var _ = new TempDir(dir);

        await CreateHostAsync(dir, o =>
        {
            o.BatchSize = 50;
            o.PurgeEnabled = true;
            o.PurgeInterval = TimeSpan.FromMilliseconds(200);
            o.ParkAndSweepEnabled = true;
        });
        var ct = CancellationToken.None;

        // Seed two owners by creating two separate references
        var r1 = new StageRecord<TestModel>
        {
            Id = Guid.CreateVersion7().ToString("n"),
            SourceId = "seed-1",
            OccurredAt = DateTimeOffset.UtcNow,
            Data = new TestModel { ["dummy"] = "ka" }
        };
        var r2 = new StageRecord<TestModel>
        {
            Id = Guid.CreateVersion7().ToString("n"),
            SourceId = "seed-2",
            OccurredAt = DateTimeOffset.UtcNow,
            Data = new TestModel { ["dummy"] = "kb" }
        };
        using (DataSetContext.With(CanonSets.StageShort(CanonSets.Intake)))
        {
            await Data<StageRecord<TestModel>, string>.UpsertAsync(r1, CanonSets.StageShort(CanonSets.Intake), ct);
            await Data<StageRecord<TestModel>, string>.UpsertAsync(r2, CanonSets.StageShort(CanonSets.Intake), ct);
        }
        // Wait both move to keyed
        await WaitUntilAsync(async () =>
        {
            using var _keyed = DataSetContext.With(CanonSets.StageShort(CanonSets.Keyed));
            var page = await Data<StageRecord<TestModel>, string>.FirstPage(100, ct);
            return page.Any(p => p.SourceId == r1.SourceId) && page.Any(p => p.SourceId == r2.SourceId);
        }, TimeSpan.FromSeconds(8));

        // Now produce a record that includes both keys → collision
        var rx = new StageRecord<TestModel>
        {
            Id = Guid.CreateVersion7().ToString("n"),
            SourceId = "src-collision",
            OccurredAt = DateTimeOffset.UtcNow,
            Data = new TestModel
            {
                ["dummy"] = new[] { "ka", "kb" }
            }
        };
        using (DataSetContext.With(CanonSets.StageShort(CanonSets.Intake)))
        {
            await Data<StageRecord<TestModel>, string>.UpsertAsync(rx, CanonSets.StageShort(CanonSets.Intake), ct);
        }

        await WaitUntilAsync(async () => (await RejectionReport.All(ct)).Any(r => r.ReasonCode == Constants.Rejections.MultiOwnerCollision), TimeSpan.FromSeconds(8));
        var rejects = await RejectionReport.All(ct);
        rejects.Should().Contain(r => r.ReasonCode == Constants.Rejections.MultiOwnerCollision);

        // Parked copy should exist and then eventually be purged by TTL if configured
        using (DataSetContext.With(CanonSets.StageShort(CanonSets.Parked)))
        {
            await WaitUntilAsync(async () => (await Data<ParkedRecord<TestModel>, string>.FirstPage(50, ct)).Any(p => p.Id == rx.Id), TimeSpan.FromSeconds(8));
            var parked = await Data<ParkedRecord<TestModel>, string>.FirstPage(50, ct);
            parked.Should().Contain(p => p.Id == rx.Id && p.ReasonCode == Constants.Rejections.MultiOwnerCollision);
        }
    }

    [Fact]
    public async Task Identity_Expired_Provisional_Link_Is_Purged()
    {
        var dir = NewWorkDir();
        using var _ = new TempDir(dir);

        await CreateHostAsync(dir, o =>
        {
            o.BatchSize = 10;
            o.PurgeEnabled = true;
            o.PurgeInterval = TimeSpan.FromMilliseconds(200);
            o.ParkAndSweepEnabled = false;
        });
        var ct = CancellationToken.None;

        // Insert an expired provisional identity link
        var link = new IdentityLink<TestModel>
        {
            Id = "sysX|adpY|extZ",
            System = "sysX",
            Adapter = "adpY",
            ExternalId = "extZ",
            ReferenceId = Guid.CreateVersion7().ToString("n"),
            Provisional = true,
            ExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(-200)
        };
        await Data<IdentityLink<TestModel>, string>.UpsertAsync(link, ct);

        // Verify it exists, then wait for purge
        (await Data<IdentityLink<TestModel>, string>.GetAsync(link.Id, ct)).Should().NotBeNull();
        await WaitUntilAsync(async () => (await Data<IdentityLink<TestModel>, string>.GetAsync(link.Id, ct)) is null, TimeSpan.FromSeconds(6));
        var after = await Data<IdentityLink<TestModel>, string>.GetAsync(link.Id, ct);
        after.Should().BeNull();
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _dir;
        public TempDir(string dir) { _dir = dir; }
        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
        }
    }
}




