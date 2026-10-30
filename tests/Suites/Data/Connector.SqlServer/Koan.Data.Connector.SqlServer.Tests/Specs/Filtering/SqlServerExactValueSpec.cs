using Koan.Core;
using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.SqlServer.Tests.Specs.Filtering;

/// <summary>A DateTime in its own typed column, where a bound value reaches the store as the parameter's type holds it.</summary>
public sealed class ExactClockRow : Entity<ExactClockRow>
{
    public DateTime At { get; set; }
    public string Note { get; set; } = "";
}

/// <summary>A row written from many writers at once.</summary>
public sealed class ExactRaceRow : Entity<ExactRaceRow>
{
    public int Writer { get; set; }
}

/// <summary>Ordinary numbers, ids, DateTime ticks and concurrent upserts on SQL Server (DATA-0164).</summary>
public sealed class SqlServerExactValueSpec(SqlServerFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqlServerFixture>(fixture, output)
{
    private static readonly DateTime SubMillisecond = new DateTime(2026, 3, 1, 9, 30, 0, DateTimeKind.Unspecified).AddTicks(1234567);

    private static void DeclareClock(KoanApplicationBuilder koan) =>
        koan.Data.Source("Default").Map<ExactClockRow>(map => map
            .Container("EXACT_CLOCK_ROW")
            .Key(row => row.Id).Name("ID")
            .Property(row => row.At).Name("AT_VALUE")
            .Property(row => row.Note).Name("NOTE"));

    [Fact(DisplayName = "SqlServer: doubles and ordinary decimals read back as written")]
    public async Task Numbers_round_trip()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await OrdinaryValueConvergence.AssertNumbersRoundTripAsync(7.9e28, double.MaxValue);
    }

    [Fact(DisplayName = "SqlServer: ids differing only by case or accent stay separate rows")]
    public async Task Ids_stay_distinct()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await OrdinaryValueConvergence.AssertIdsStayDistinctAsync();
    }

    [Fact(DisplayName = "SqlServer: a DateTime column holds and compares sub-millisecond ticks")]
    public async Task DateTime_is_exact_to_the_tick()
    {
        RequireBackingStore();
        await using var host = await BootAsync(DeclareClock);
        foreach (var existing in await ExactClockRow.All()) await existing.Remove();
        var near = SubMillisecond.AddTicks(1);
        await new ExactClockRow { Id = "a", At = SubMillisecond, Note = "a" }.Save();
        await new ExactClockRow { Id = "b", At = near, Note = "b" }.Save();

        (await ExactClockRow.Get("a"))!.At.Ticks.Should().Be(SubMillisecond.Ticks);
        (await ExactClockRow.Get("b"))!.At.Ticks.Should().Be(near.Ticks);

        var at = SubMillisecond;
        (await ExactClockRow.Query(row => row.At == at)).Select(row => row.Id).Should().Equal("a");
        (await ExactClockRow.Query(row => row.At > at)).Select(row => row.Id).Should().Equal("b");
    }

    [Fact(DisplayName = "SqlServer: a large set of DateTimes binds as one value and compares to the tick")]
    public async Task A_DateTime_set_binds_to_the_tick()
    {
        RequireBackingStore();
        await using var host = await BootAsync(DeclareClock);
        foreach (var existing in await ExactClockRow.All()) await existing.Remove();
        await new ExactClockRow { Id = "a", At = SubMillisecond, Note = "a" }.Save();

        // Past the parameters one statement binds, so the set travels as one JSON text.
        var at = SubMillisecond;
        var set = Enumerable.Range(1, 3_000).Select(tick => at.AddTicks(tick)).Append(at).ToArray();
        (await ExactClockRow.Query(row => set.Contains(row.At))).Select(row => row.Id).Should().Equal("a");
        var near = Enumerable.Range(1, 3_000).Select(tick => at.AddTicks(tick)).ToArray();
        (await ExactClockRow.Query(row => near.Contains(row.At))).Should().BeEmpty();
    }

    [Fact(DisplayName = "SqlServer: concurrent upserts of a new id all succeed, leaving one row")]
    public async Task Concurrent_upserts_of_a_new_id_do_not_throw()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        foreach (var existing in await ExactRaceRow.All()) await existing.Remove();

        for (var round = 0; round < 20; round++)
        {
            var id = $"race-{round}";
            using var gate = new Barrier(16);
            var writers = Enumerable.Range(0, 16).Select(writer => Task.Run(async () =>
            {
                gate.SignalAndWait(TestContext.Current.CancellationToken);
                await new ExactRaceRow { Id = id, Writer = writer }.Save();
            }));
            await Task.WhenAll(writers);
            (await ExactRaceRow.Get(id)).Should().NotBeNull();
        }

        (await ExactRaceRow.All()).Should().HaveCount(20);
    }
}
