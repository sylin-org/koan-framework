using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// SQLite writes a real scalar column in DATA-0100's order-preserving form (DateTimeOffset as UTC text, TimeSpan as
/// ticks). A filter comparand and an identity key bound to that column must take the same form, or equality misses
/// the row and a range compares two representations (NK-058).
/// </summary>
public sealed class SqliteScalarColumnEncodingSpec(SqliteFixture fixture)
{
    private static readonly DateTimeOffset Noon = new(2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public async Task Explicit_map_scalar_columns_filter_in_the_form_they_are_written()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-sqlite-scalar-{Guid.CreateVersion7():N}.db");
        try
        {
            await using var host = await Boot(path, koan => koan.Data.Source("Stamped").Map<StampedRow>(map => map
                .Container("STAMPED")
                .Key(row => row.Id).Name("ID")
                .Property(row => row.At).Name("AT")
                .Property(row => row.Elapsed).Name("ELAPSED")));

            StampedRow[] rows =
            [
                new() { Id = 1, At = Noon, Elapsed = TimeSpan.FromMinutes(2) },
                new() { Id = 2, At = Noon.AddHours(1), Elapsed = TimeSpan.FromSeconds(30) },
                new() { Id = 3, At = Noon.AddDays(-1), Elapsed = TimeSpan.FromHours(3) },
            ];
            using (EntityContext.Source("Stamped"))
            {
                foreach (var row in rows) await row.Save();

                (await StampedRow.Query(row => row.At == Noon)).Select(static row => row.Id).Should().Equal(1);
                // The same instant at another offset is the same value; DATA-0100 stores the instant.
                (await StampedRow.Query(row => row.At == Noon.ToUniversalTime())).Select(static row => row.Id).Should().Equal(1);
                (await StampedRow.Query(row => row.At > Noon)).Select(static row => row.Id).Should().Equal(2);
                (await StampedRow.Query(row => row.Elapsed > TimeSpan.FromMinutes(1)))
                    .Select(static row => row.Id).Order().Should().Equal(1, 3);
                (await StampedRow.Query(row => row.Elapsed < TimeSpan.FromMinutes(1)))
                    .Select(static row => row.Id).Should().Equal(2);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task A_temporal_key_column_is_read_and_deleted_in_the_form_it_is_written()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-sqlite-scalar-key-{Guid.CreateVersion7():N}.db");
        try
        {
            await using var host = await Boot(path, koan => koan.Data.Source("Stamped").Map<Reading>(map => map
                .Container("READING")
                .Key(row => row.Id).Name("TAKEN_AT")
                .Property(row => row.Value).Name("VALUE")));

            using (EntityContext.Source("Stamped"))
            {
                await new Reading { Id = Noon, Value = 42 }.Save();
                (await Reading.Get(Noon))?.Value.Should().Be(42);
                (await Reading.Get(Noon.ToUniversalTime())).Should().NotBeNull("the key is an instant");
                (await Reading.Query(row => row.Id == Noon)).Should().ContainSingle();
                (await Data<Reading, DateTimeOffset>.Delete(Noon)).Should().BeTrue();
                (await Reading.All()).Should().BeEmpty();
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private async Task<IntegrationHost> Boot(string path, Action<KoanApplicationBuilder> configure)
    {
        var settings = new Dictionary<string, string?>(fixture.SettingsForBoot(), StringComparer.Ordinal)
        {
            ["Koan:Data:Sources:Stamped:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Stamped:ConnectionString"] = $"Data Source={path}",
        };
        return await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(services => services.AddKoan(configure))
            .StartAsync(TestContext.Current.CancellationToken);
    }

    public sealed class StampedRow : Entity<StampedRow, long>
    {
        public DateTimeOffset At { get; set; }
        public TimeSpan Elapsed { get; set; }
    }

    public sealed class Reading : Entity<Reading, DateTimeOffset>
    {
        public int Value { get; set; }
    }
}
