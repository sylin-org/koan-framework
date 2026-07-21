using System.Linq.Expressions;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Sorting;

namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>
/// Abstract generic CRTP base that DECLARES every composite scalar the comparable-encoding contract
/// (DATA-0100) governs. Mirrors <c>Job&lt;T&gt; : Entity&lt;T&gt;</c> deliberately: the members live on
/// an abstract generic intermediate base, not the concrete leaf. This is the shape that exposed the
/// residual reaper throw — the Mongo class-map member lookup must find INHERITED members, not only
/// declared ones. A flat entity (e.g. ConvergenceWidget) does not exercise that path.
/// </summary>
public abstract class TemporalWidgetBase<T> : Entity<T> where T : TemporalWidgetBase<T>, new()
{
    public string Name { get; set; } = "";
    public DateTimeOffset Ts { get; set; }
    public DateTimeOffset? OptTs { get; set; }
    public TimeSpan Dur { get; set; }
    public TimeSpan? OptDur { get; set; }
    public DateOnly Day { get; set; }
    public TimeOnly Tod { get; set; }
}

/// <summary>Concrete leaf used by <see cref="TemporalConvergence"/> — carries no members of its own, so
/// every governed scalar is inherited (the <c>Job&lt;T&gt;</c> hierarchy shape).</summary>
public sealed class TemporalWidget : TemporalWidgetBase<TemporalWidget> { }

/// <summary>
/// Cross-adapter ORACLE for the comparable-encoding contract (DATA-0100, ARCH-0079). Proves that range
/// comparisons on composite scalars produce the SAME id-set on a real adapter as pure CLR evaluation —
/// i.e. that each adapter persists these types in a representation whose store-native ordering equals
/// the CLR ordering.
///
/// The corpus is deliberately adversarial: <see cref="DateTimeOffset"/> rows carry MIXED offsets (two
/// rows are the same instant via +00:00 and +05:00; others are +14:00 / -12:00) so a naive
/// offset-bearing text/lexicographic comparison diverges from the chronological one; and the
/// <see cref="TimeSpan"/> rows straddle the day boundary (90m, 2h, 23h, 24h, 48h) so the driver's
/// default duration STRING ("1.00:00:00") sorts 24h BEFORE 23h — the exact bug the contract closes.
///
/// Comparands are CLR values via LINQ (no JSON-DSL string parsing), and the oracle is the compiled C#
/// predicate itself — the ultimate ground truth. Seconds-precision values keep Mongo's millisecond
/// BSON-date round-trip identical to the relational ISO/ticks round-trip, so the matrix is
/// adapter-agnostic.
/// </summary>
public static class TemporalConvergence
{
    private static readonly DateTimeOffset InstantA = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);        // 2026-01-01T00:00:00Z

    public static IReadOnlyList<TemporalWidget> Corpus { get; } = new TemporalWidget[]
    {
        // t1/t2 are the SAME instant expressed with different offsets (+00:00 vs +05:00) -> the
        // offset-stripping + instant-equality probe.
        new() { Id = "t1", Name = "utc-midnight", Ts = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),            OptTs = null,                                      Dur = TimeSpan.FromMinutes(90), OptDur = null,                  Day = new(2025, 12, 31), Tod = new(0, 5) },
        new() { Id = "t2", Name = "plus5-same",   Ts = new(2026, 1, 1, 5, 0, 0, TimeSpan.FromHours(5)),    OptTs = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),   Dur = TimeSpan.FromHours(2),    OptDur = TimeSpan.FromHours(5),  Day = new(2026, 2, 28), Tod = new(9, 30) },
        new() { Id = "t3", Name = "plus14-june",  Ts = new(2026, 6, 15, 12, 0, 0, TimeSpan.FromHours(14)), OptTs = null,                                      Dur = TimeSpan.FromHours(23),   OptDur = TimeSpan.FromHours(15), Day = new(2026, 3, 1),  Tod = new(13, 45) },
        new() { Id = "t4", Name = "minus12-june", Ts = new(2026, 6, 15, 0, 0, 0, TimeSpan.FromHours(-12)), OptTs = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),   Dur = TimeSpan.FromDays(1),     OptDur = null,                  Day = new(2026, 12, 9), Tod = new(23, 59) },
        new() { Id = "t5", Name = "next-year",    Ts = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),            OptTs = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),   Dur = TimeSpan.FromDays(2),     OptDur = TimeSpan.FromHours(1),  Day = new(2027, 6, 1),  Tod = new(12, 0) },
    };

    public static IEnumerable<(string Name, Expression<Func<TemporalWidget, bool>> Predicate)> Cases()
    {
        var june15Z = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);     // instant boundary
        yield return ("dto-lt-instant", w => w.Ts < june15Z);                       // {t1,t2,t3} (t3 instant=06-14T22:00Z)
        yield return ("dto-gte-instant", w => w.Ts >= june15Z);                     // {t4,t5}
        yield return ("dto-eq-same-instant", w => w.Ts == InstantA);                // {t1,t2} same instant, different offsets
        yield return ("dto-opt-null", w => w.OptTs == null);                        // {t1,t3}
        yield return ("dto-opt-lt", w => w.OptTs != null && w.OptTs < june15Z);     // {t2(2026-03-01),t5(2026-01-01)}

        yield return ("ts-lt-1day", w => w.Dur < TimeSpan.FromDays(1));             // {t1(90m),t2(2h),t3(23h)}  <-- string-encoding bug anchor
        yield return ("ts-gte-23h", w => w.Dur >= TimeSpan.FromHours(23));          // {t3,t4,t5}
        yield return ("ts-opt-not-null-lt", w => w.OptDur != null && w.OptDur < TimeSpan.FromHours(10)); // {t2(5h),t5(1h)}

        yield return ("dateonly-lt", w => w.Day < new DateOnly(2026, 3, 1));        // {t1,t2}
        yield return ("timeonly-lt", w => w.Tod < new TimeOnly(13, 45));            // {t1(00:05),t2(09:30),t5(12:00)}
    }

    /// <summary>
    /// Clears + seeds the corpus into the currently-configured adapter (honouring the ambient partition),
    /// then asserts every case's adapter id-set equals the compiled-predicate oracle. Throws listing all
    /// divergences (a divergence == the adapter mis-encoded a composite scalar's ordering).
    /// </summary>
    public static async Task AssertConvergesAsync()
    {
        foreach (var existing in await Data<TemporalWidget, string>.Query("{}")) await existing.Remove();
        await TemporalWidget.UpsertMany(Corpus);

        var failures = new List<string>();
        foreach (var (name, predicate) in Cases())
        {
            var oracle = Corpus.Where(predicate.Compile()).Select(w => w.Id).OrderBy(x => x).ToArray();

            string[] actual;
            try
            {
                actual = (await TemporalWidget.Query(predicate)).Select(w => w.Id).OrderBy(x => x).ToArray();
            }
            catch (Exception ex)
            {
                failures.Add($"  [{name}]\n      THREW {ex.GetType().Name}: {ex.Message.Split('\n')[0].Trim()}");
                continue;
            }

            if (!actual.SequenceEqual(oracle))
                failures.Add($"  [{name}]\n      oracle:  [{string.Join(",", oracle)}]\n      adapter: [{string.Join(",", actual)}]");
        }

        // Sort coverage: ORDER BY must use the SAME comparable encoding as filters. Dur (TimeSpan) is the
        // regression anchor — under the string encoding it sorts 24h before 23h; on relational adapters the
        // ORDER BY column must receive the numeric cast (DATA-0100). ThenBy(Id) makes the same-instant
        // t1/t2 tie deterministic across stores.
        await AssertSortAsync("dur-asc", s => s.OrderBy(x => x.Dur).ThenBy(x => x.Id),
            Corpus.OrderBy(w => w.Dur).ThenBy(w => w.Id), failures);
        await AssertSortAsync("ts-asc", s => s.OrderBy(x => x.Ts).ThenBy(x => x.Id),
            Corpus.OrderBy(w => w.Ts).ThenBy(w => w.Id), failures);

        failures.Should().BeEmpty(
            "the adapter must converge with the compiled-predicate oracle for every composite-scalar comparison (DATA-0100); divergences:\n"
            + string.Join("\n", failures));
    }

    private static async Task AssertSortAsync(string name, Action<ISortBuilder<TemporalWidget>> sort,
        IOrderedEnumerable<TemporalWidget> oracleOrdered, List<string> failures)
    {
        var oracle = oracleOrdered.Select(w => w.Id).ToArray();
        string[] actual;
        try { actual = (await TemporalWidget.Query(w => w.Name != null, sort)).Select(w => w.Id).ToArray(); }
        catch (Exception ex)
        {
            failures.Add($"  [sort:{name}]\n      THREW {ex.GetType().Name}: {ex.Message.Split('\n')[0].Trim()}");
            return;
        }
        if (!actual.SequenceEqual(oracle))
            failures.Add($"  [sort:{name}]\n      oracle:  [{string.Join(",", oracle)}]\n      adapter: [{string.Join(",", actual)}]");
    }

    /// <summary>
    /// Round-trip + offset-stripping contract for stores that SERIALIZE (Mongo, relational, JSON file,
    /// Redis). Upserts one entity whose <see cref="DateTimeOffset"/> carries a non-UTC offset and whose
    /// <see cref="TimeSpan"/> straddles the day boundary, reloads it, and asserts (a) the INSTANT and the
    /// duration round-trip exactly, and (b) the persisted offset is normalised to UTC (DATA-0100: the
    /// offset is not part of the persisted contract). NOT applicable to the in-memory adapter, which
    /// keeps the live CLR object (offset preserved) and never serialises.
    /// </summary>
    public static async Task AssertRoundTripAndOffsetStrippedAsync()
    {
        var id = "rt-" + Guid.NewGuid().ToString("N")[..8];
        var instant = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.FromHours(5)); // instant 2026-06-15T07:00:00Z
        await new TemporalWidget { Id = id, Name = "round-trip", Ts = instant, Dur = TimeSpan.FromHours(25), Day = new(2026, 6, 15), Tod = new(7, 0) }.Save();

        var reloaded = await TemporalWidget.Get(id);
        reloaded.Should().NotBeNull();
        reloaded!.Ts.Should().Be(instant);                       // DateTimeOffset equality is by INSTANT
        reloaded.Ts.Offset.Should().Be(TimeSpan.Zero);           // contract: offset normalised to UTC on persist
        reloaded.Dur.Should().Be(TimeSpan.FromHours(25));        // duration round-trips exactly (ticks)
        await reloaded.Remove();
    }
}
