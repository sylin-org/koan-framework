using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Axes.Integration.Tests;

/// <summary>
/// DATA-0167: a read scope that hides rows inside one unique index hides the top of a numbered column, and the next number would
/// collide with a row nobody can see. A sequence over such an entity refuses, and says why.
/// </summary>
public sealed class SequenceReadScopeSpec
{
    [Archived]
    [Storage(Name = "KOAN_ARCHIVED_NUMBERED")]
    public sealed class ArchivedNumbered : Entity<ArchivedNumbered>
    {
        public static readonly Sequence Numbers = Sequence.Of<ArchivedNumbered>(row => row.Number);

        [Index(Name = "ix_archived_numbered_number", Unique = true)] public long Number { get; set; }
    }

    public sealed record NumberAnArchivedRow : ICommand<long>
    {
        public async Task<Outcome<long>> Run(CancellationToken ct)
        {
            var number = await ArchivedNumbered.Numbers.Next(ct);
            await new ArchivedNumbered { Number = number }.Save(ct);
            return new Outcome<long>.Done(number);
        }
    }

    [Fact]
    public async Task A_sequence_over_an_entity_whose_rows_a_read_scope_hides_refuses_rather_than_collide()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-sequence-scope-{Guid.CreateVersion7():n}.db");
        try
        {
            await using var host = await KoanIntegrationHost.Configure()
                .WithSettings(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                    ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path}",
                })
                .ConfigureServices(services => services.AddKoan())
                .StartAsync(TestContext.Current.CancellationToken);

            var hidden = await new ArchivedNumbered { Number = 1 }.Save(TestContext.Current.CancellationToken);
            await hidden.Remove(TestContext.Current.CancellationToken);

            var failure = (await new NumberAnArchivedRow().Send(TestContext.Current.CancellationToken))
                .Should().BeOfType<Outcome<long>.Failed>().Which;

            failure.Landed.Should().Be(DataCommitOutcome.NotCommitted);
            failure.Cause.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("read scope hides rows");

            using (ArchivedAmbient.Enter())
                (await ArchivedNumbered.Numbers.Highest(TestContext.Current.CancellationToken)).Should().Be(1, "with every row in view, the top is the true top");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }
}
