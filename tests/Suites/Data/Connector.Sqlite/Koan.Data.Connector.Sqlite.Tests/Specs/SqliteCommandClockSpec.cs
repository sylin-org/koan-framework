using Koan.Data.Core.Commands;
using Microsoft.Extensions.DependencyInjection;
using static Koan.Data.Connector.Sqlite.Tests.Specs.SqliteCommandSpec;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>A clock that always says one time, so a spec can tell clocks apart by what they read.</summary>
internal sealed class StoppedClock(DateTimeOffset at) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => at;
}

/// <summary>Writes a slip stamped by the clock its hold runs by, and answers with that clock.</summary>
public sealed record StampASlip(string Id) : ICommand<TimeProvider>
{
    public async Task<Outcome<TimeProvider>> Run(CancellationToken ct)
    {
        var clock = EntityContext.Clock;
        await new CommandSlip { Id = Id, Sender = "stamp", Body = clock.GetUtcNow().ToString("O"), Number = await CommandSlip.Numbers.Next(ct) }.Save(ct);
        return new Outcome<TimeProvider>.Done(clock);
    }
}

/// <summary>DATA-0169: every hold runs by one clock, its sender's for that send, otherwise the host's, otherwise the system's.</summary>
public sealed class SqliteCommandClockSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output)
{
    private static readonly StoppedClock Named = new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    [Fact]
    public async Task A_command_reads_its_senders_clock_otherwise_the_hosts_and_outside_a_hold_the_accessor_answers_the_hosts()
    {
        RequireBackingStore();
        var path = TempDatabase("clock");
        try
        {
            var hosts = new StoppedClock(new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero));
            await using var host = await Start(Settings(path), configure: services => services.AddSingleton<TimeProvider>(hosts));

            Done(await new StampASlip("named").Send(Named, Cancellation)).Should().BeSameAs(Named);
            (await CommandSlip.Get("named", Cancellation))!.Body.Should().Be("2026-01-02T03:04:05.0000000+00:00");
            Done(await new StampASlip("unnamed").Send(Cancellation)).Should().BeSameAs(hosts, "a send that names none runs by the host's, the next send included");
            using (await EntityContext.Hold("plain", Cancellation))
                EntityContext.Clock.Should().BeSameAs(hosts, "a plain hold runs by the host's");
            EntityContext.Clock.Should().BeSameAs(hosts).And.BeSameAs(host.Services.GetRequiredService<TimeProvider>());
            await FluentActions.Awaiting(() => new StampASlip("none").Send((TimeProvider)null!, Cancellation)).Should().ThrowAsync<ArgumentNullException>();
        }
        finally { Delete(path); }
    }

    [Fact]
    public async Task A_host_with_no_clock_of_its_own_runs_by_the_systems()
    {
        RequireBackingStore();
        var path = TempDatabase("clock-system");
        try
        {
            await using var host = await Start(Settings(path));
            host.Services.GetService<TimeProvider>().Should().BeNull("Koan.Data.Core registers no clock");

            Done(await new StampASlip("system").Send(Cancellation)).Should().BeSameAs(TimeProvider.System);
            Done(await new StampASlip("named").Send(Named, Cancellation)).Should().BeSameAs(Named);
            EntityContext.Clock.Should().BeSameAs(TimeProvider.System);
        }
        finally { Delete(path); }
    }
}
