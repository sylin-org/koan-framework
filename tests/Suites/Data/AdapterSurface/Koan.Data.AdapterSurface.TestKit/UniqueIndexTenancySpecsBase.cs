using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core;
using Koan.Testing.Containers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>
/// DATA-0150 through a real <c>AddKoan()</c> boot under row-scoped tenancy: a unique index is unique per tenant, a
/// duplicate is reported as a conflict that does not say where the value is held, and an existing deployment whose
/// unique index was built global keeps it, is told how to drop it, and holds both guarantees throughout.
/// Each adapter that builds unique indexes supplies only how an operator drops the reported index.
/// </summary>
public abstract class UniqueIndexTenancySpecsBase<TFixture> : KoanDataSpec<TFixture>
    where TFixture : KoanContainerFixture
{
    protected UniqueIndexTenancySpecsBase(TFixture fixture, ITestOutputHelper output) : base(fixture, output) { }

    /// <summary>
    /// Drops the global index Koan reported, as the operator following the warning would. <paramref name="report"/> is
    /// the warning's structured state: its <c>Index</c>, and the adapter's own handle (<c>Drop</c> for a relational
    /// statement, <c>Collection</c> for MongoDB).
    /// </summary>
    protected abstract Task DropReportedIndex(IReadOnlyDictionary<string, object?> report);

    [Fact(DisplayName = "DATA-0150/unique/Adapter: one value is accepted in two tenants and refused twice in one")]
    public async Task Unique_index_is_unique_per_tenant()
    {
        RequireBackingStore();
        ConformanceTenancy.Segmented = true;
        try
        {
            await using var host = await BootAsync();
            using var _ = Lease(NewPartition("unique"));

            using (ConformanceTenancy.Use("acme")) await new ConformanceMember { Email = "a@x.test" }.Save();
            using (ConformanceTenancy.Use("globex")) await new ConformanceMember { Email = "a@x.test" }.Save();

            using (ConformanceTenancy.Use("acme"))
            {
                await AssertConflict(() => new ConformanceMember { Email = "a@x.test" }.Save());
                (await ConformanceMember.All()).Should().ContainSingle();
            }
            using (ConformanceTenancy.Use("globex")) (await ConformanceMember.All()).Should().ContainSingle();
        }
        finally
        {
            ConformanceTenancy.Segmented = false;
        }
    }

    [Fact(DisplayName = "DATA-0150/migration/Adapter: an existing global unique index is kept, reported, and dropped without losing a guarantee")]
    public async Task Existing_global_unique_index_migrates()
    {
        RequireBackingStore();
        var partition = NewPartition("migrate");
        try
        {
            // An existing deployment: the unique index was built global, as every Koan before DATA-0150 built it.
            ConformanceTenancy.Segmented = false;
            await using (await BootAsync())
            using (Lease(partition))
                await new ConformanceMember { Email = "seed@x.test" }.Save();

            // The upgrade builds the per-tenant index beside the global one and reports the global one.
            ConformanceTenancy.Segmented = true;
            var logs = new CapturingLoggerProvider();
            await using var host = await BootAsync(services => services.AddSingleton<ILoggerProvider>(logs));
            // A store that makes tables at start reports the entity's own table there (ARCH-0142). This is about the partition's.
            var atStart = logs.Warnings.Count;
            using var _ = Lease(partition);
            using (ConformanceTenancy.Use("acme")) await new ConformanceMember { Email = "a@x.test" }.Save();

            // Until the old index is dropped both refuse: nothing it used to refuse is accepted.
            using (ConformanceTenancy.Use("acme")) await AssertConflict(() => new ConformanceMember { Email = "a@x.test" }.Save());
            using (ConformanceTenancy.Use("globex")) await AssertConflict(() => new ConformanceMember { Email = "a@x.test" }.Save());

            var report = logs.Warnings.Skip(atStart).Should().ContainSingle(entry =>
                    entry.Message.Contains("global unique index", StringComparison.Ordinal))
                .Which;
            Output.WriteLine(report.Message);
            report.State.Should().ContainKey("Index");

            // The operator follows the warning; afterwards the value is unique per tenant.
            await DropReportedIndex(report.State);
            using (ConformanceTenancy.Use("globex")) await new ConformanceMember { Email = "a@x.test" }.Save();
            using (ConformanceTenancy.Use("acme")) await AssertConflict(() => new ConformanceMember { Email = "a@x.test" }.Save());
            using (ConformanceTenancy.Use("globex")) await AssertConflict(() => new ConformanceMember { Email = "a@x.test" }.Save());
            using (ConformanceTenancy.Use("acme")) (await ConformanceMember.All()).Should().ContainSingle();
            using (ConformanceTenancy.Use("globex")) (await ConformanceMember.All()).Should().ContainSingle();
        }
        finally
        {
            ConformanceTenancy.Segmented = false;
        }
    }

    private static async Task AssertConflict(Func<Task> save)
    {
        var refused = (await save.Should().ThrowAsync<UniqueValueConflictException>()).Which;
        refused.Failure.Kind.Should().Be(DataFailureKind.Conflict);
        refused.InnerException.Should().BeNull("the store's error names the index that refused the value");
        refused.Message.Should().NotContainAny("tenant", "acme", "globex", "IX_", "__koan", "a@x.test");
    }

    /// <summary>Keeps every warning a host logs, with its structured state.</summary>
    protected sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<Entry> _warnings = new();

        public IReadOnlyList<Entry> Warnings => _warnings.ToArray();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose() { }

        public sealed record Entry(string Message, IReadOnlyDictionary<string, object?> State);

        private sealed class Logger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                    foreach (var pair in pairs) values.TryAdd(pair.Key, pair.Value);
                owner._warnings.Enqueue(new Entry(formatter(state, exception), values));
            }
        }
    }
}
