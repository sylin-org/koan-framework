using System.Linq.Expressions;
using AwesomeAssertions;
using Koan.Core;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>A row carrying one property of each scalar type a set filter is written over, held in the JSON document.</summary>
public sealed class SetMemberDoc : Entity<SetMemberDoc, Guid>
{
    public Guid Ref { get; set; }
    public DateTime At { get; set; }
    public DateTimeOffset Seen { get; set; }
    public double Ratio { get; set; }
    public float Single { get; set; }
    public bool Flag { get; set; }
    public decimal Amount { get; set; }
    public long Tally { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>The same row with every property mapped to its own typed column (<see cref="SetMembershipConvergence.Declare"/>).</summary>
public sealed class SetMemberColumns : Entity<SetMemberColumns, Guid>
{
    public Guid Ref { get; set; }
    public DateTime At { get; set; }
    public double Ratio { get; set; }
    public float Single { get; set; }
    public bool Flag { get; set; }
    public decimal Amount { get; set; }
    public long Tally { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>The scalar types beyond the common ones a set filter is written over, held in the JSON document.</summary>
public sealed class SetMemberMore : Entity<SetMemberMore, Guid>
{
    public int Count32 { get; set; }
    public short Small { get; set; }
    public byte Tiny { get; set; }
    public TimeSpan Span { get; set; }
    public DateOnly Day { get; set; }
    public TimeOnly Clock { get; set; }
    public SetMemberTier Tier { get; set; }
    public char Letter { get; set; }
}

/// <summary>The same row with every property mapped to its own typed column.</summary>
public sealed class SetMemberMoreColumns : Entity<SetMemberMoreColumns, Guid>
{
    public int Count32 { get; set; }
    public short Small { get; set; }
    public byte Tiny { get; set; }
    public SetMemberTier Tier { get; set; }
    public char Letter { get; set; }
}

public enum SetMemberTier { Free, Pro, Enterprise }

/// <summary>A TimeSpan, a DateOnly and a TimeOnly mapped to typed columns, apart because SQL Server neither writes a
/// TimeSpan column (its DDL makes it bigint while its write binds time) nor hydrates a date or time column read back.</summary>
public sealed class SetMemberClocks : Entity<SetMemberClocks, Guid>
{
    public TimeSpan Span { get; set; }
    public DateOnly Day { get; set; }
    public TimeOnly Clock { get; set; }
}

/// <summary>An offset mapped to its own typed column, apart from <see cref="SetMemberColumns"/> because PostgreSQL's
/// timestamptz column does not hold one (<see cref="SetMembershipConvergence.Declare"/>).</summary>
public sealed class SetMemberOffsets : Entity<SetMemberOffsets, Guid>
{
    public DateTimeOffset Seen { get; set; }
}

/// <summary>
/// Cross-adapter ORACLE for set filters (DATA-0162): an <c>In</c> answers what <c>Eq</c> answers member by member, and a
/// <c>Nin</c> answers the rest, for Guid keys and Guid, DateTime, DateTimeOffset, double, single, bool, decimal, long, int,
/// short, byte, TimeSpan, DateOnly, TimeOnly, enum, char and string properties, on document fields and on typed columns,
/// with sets of one, two, 2,502 and 70,000 members (fewer where the type has fewer values), in a statement that holds
/// two sets of 1,501, and in statements of many boolean or byte lists that together pass a store's bound. The row values
/// are the hard cases: a non-dyadic double, doubles of magnitude 1e14 to 1e19, a subnormal, singles, temporal values with
/// sub-millisecond ticks, a decimal with fourteen integral digits and one with scale 28, a long past 2^53, text past
/// 4,000 characters, a quote and non-Latin chars. Eq is the reference rather than
/// the CLR value, because a store may hold a value inexactly; a set member must compare exactly as the member alone does.
/// Every query is executed by the store.
/// </summary>
public static class SetMembershipConvergence
{
    public const int Medium = 2_502;
    public const int Large = 70_000;
    public const int Pair = 1_501;
    public const string ColumnsContainer = "KOAN_SET_COLUMNS";
    public const string OffsetsContainer = "KOAN_SET_OFFSETS";
    public const string MoreColumnsContainer = "KOAN_SET_MORE_COLUMNS";
    public const string ClocksContainer = "KOAN_SET_CLOCKS";

    /// <summary>Bit and tinyint lists cannot pass the statement's bound alone, so statements holding many of them do.</summary>
    public const int ManyFlagSets = 1_050;
    public const int ManyTinySets = 9;

    private static readonly int[] Count32s = [10, -1, int.MaxValue, int.MinValue + 1, 0, 123_456];
    private static readonly short[] Smalls = [10, -1, short.MaxValue, short.MinValue, 0, 1_234];
    private static readonly byte[] Tinies = [10, 0, 255, 1, 128, 77];
    private static readonly TimeSpan[] Spans =
    [
        TimeSpan.FromHours(1.5), TimeSpan.Zero, TimeSpan.FromDays(-2), TimeSpan.FromTicks(1),
        new TimeSpan(0, 12, 34, 56, 789), new TimeSpan(0, 23, 59, 59).Add(TimeSpan.FromTicks(9_999_999)),
    ];
    private static readonly DateOnly[] Days =
        [new(2026, 3, 1), new(1999, 12, 31), new(9999, 12, 31), new(1, 1, 1), new(2024, 2, 29), new(2026, 3, 2)];
    private static readonly TimeOnly[] Clocks =
    [
        new TimeOnly(9, 30, 0).Add(TimeSpan.FromTicks(1_234_567)), TimeOnly.MinValue, TimeOnly.MaxValue,
        new TimeOnly(12, 0, 0, 500), new TimeOnly(23, 59, 59).Add(TimeSpan.FromTicks(1)), new TimeOnly(0, 0, 0, 1),
    ];
    private static readonly SetMemberTier[] Tiers =
        [SetMemberTier.Free, SetMemberTier.Pro, SetMemberTier.Enterprise, SetMemberTier.Pro, SetMemberTier.Free, SetMemberTier.Enterprise];
    private static readonly char[] Letters = ['a', 'B', '\u00e9', '\'', '0', '\u5b57'];

    private static readonly DateTime T0 = new DateTime(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc).AddTicks(1_234_567);

    private static readonly Guid[] Ids = Enumerable.Range(1, 6).Select(static i => Guid.Parse($"00000000-0000-4000-8000-00000000000{i}")).ToArray();
    private static readonly Guid[] Refs =
    [
        Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"),
        Guid.Parse("e1a2b3c4-0000-4000-8000-000000000001"), Guid.Parse("a3b1c2d4-1111-4222-8333-444455556666"),
        Guid.Parse("b4c2d3e5-2222-4333-8444-555566667777"), Guid.Parse("c5d3e4f6-3333-4444-8555-666677778888"),
    ];
    private static readonly DateTime[] Ats =
    [
        T0, T0.AddHours(1).AddTicks(3), T0.AddDays(-1).AddTicks(5), new DateTime(2026, 3, 1, 11, 0, 0, DateTimeKind.Utc),
        T0.AddMinutes(-1).AddTicks(9_999), new DateTime(9038, 10, 9, 2, 57, 34, DateTimeKind.Utc).AddTicks(5_909_284),
    ];
    // UTC offsets only: Npgsql refuses to write any other offset to a timestamptz column, whichever way it is read.
    private static readonly DateTimeOffset[] Seens =
    [
        new DateTimeOffset(T0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 1, 11, 30, 0, TimeSpan.Zero).AddTicks(1),
        new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero).AddTicks(9_999), new DateTimeOffset(1999, 12, 31, 23, 59, 59, TimeSpan.Zero),
        new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero).AddTicks(7), new DateTimeOffset(9000, 1, 1, 0, 0, 0, TimeSpan.Zero),
    ];
    private static readonly double[] Ratios = [0.1, 1.0 / 3, -88127016363724600, 1.2345678901234567E+18, 5E-324, 123456789012345.67];
    private static readonly float[] Singles = [0.1f, 5.565794E+16f, 1f / 3, -2.5f, 1e-30f, 16_777_217f];
    private static readonly bool[] Flags = [true, false, true, false, true, false];
    private static readonly decimal[] Amounts = [10.25m, 0.1m, 12345678901234.123456m, -1m, 0m, 1m / 3];
    private static readonly long[] Tallies = [10, -1, 9_007_199_254_740_993, long.MinValue + 1, 0, 1_000_000_007];
    private static readonly string[] Names = ["alpha", "Bravo", "çé ünïcode", "", new string('x', 4_500), "o'brien"];

    /// <summary>Maps <see cref="SetMemberColumns"/> to typed columns on the default source while the host composes.</summary>
    public static void Declare(KoanApplicationBuilder koan) => Declaration(offsetColumns: true, clockColumns: true)(koan);

    /// <summary>
    /// The typed-column maps for a store: <paramref name="offsetColumns"/> and <paramref name="clockColumns"/> say whether
    /// its typed columns write a DateTimeOffset, and a TimeSpan, DateOnly and TimeOnly, and read them back (PostgreSQL's
    /// timestamptz holds no offset; SQL Server writes no TimeSpan column and hydrates no date or time column). Those are
    /// storage matters outside DATA-0162, and the properties stay covered on the document there.
    /// </summary>
    public static Action<KoanApplicationBuilder> Declaration(bool offsetColumns, bool clockColumns) => koan =>
    {
        DeclareColumns(koan);
        if (offsetColumns)
            koan.Data.Source("Default").Map<SetMemberOffsets>(map => map
                .Container(OffsetsContainer)
                .Key(row => row.Id).Name("ID")
                .Property(row => row.Seen).Name("SEEN"));
        if (clockColumns)
            koan.Data.Source("Default").Map<SetMemberClocks>(map => map
                .Container(ClocksContainer)
                .Key(row => row.Id).Name("ID")
                .Property(row => row.Span).Name("SPAN")
                .Property(row => row.Day).Name("DAY_VALUE")
                .Property(row => row.Clock).Name("CLOCK"));
    };

    private static void DeclareColumns(KoanApplicationBuilder koan)
    {
        koan.Data.Source("Default").Map<SetMemberMoreColumns>(map => map
            .Container(MoreColumnsContainer)
            .Key(row => row.Id).Name("ID")
            .Property(row => row.Count32).Name("COUNT32")
            .Property(row => row.Small).Name("SMALL")
            .Property(row => row.Tiny).Name("TINY")
            .Property(row => row.Tier).Name("TIER")
            .Property(row => row.Letter).Name("LETTER"));
        DeclareCommonColumns(koan);
    }

    private static void DeclareCommonColumns(KoanApplicationBuilder koan) =>
        koan.Data.Source("Default").Map<SetMemberColumns>(map => map
            .Container(ColumnsContainer)
            .Key(row => row.Id).Name("ID")
            .Property(row => row.Ref).Name("REF")
            .Property(row => row.At).Name("AT")
            .Property(row => row.Ratio).Name("RATIO")
            .Property(row => row.Single).Name("SINGLE_VALUE")
            .Property(row => row.Flag).Name("FLAG")
            .Property(row => row.Amount).Name("AMOUNT")
            .Property(row => row.Tally).Name("TALLY")
            .Property(row => row.Name).Name("NAME"));

    /// <summary>Seeds both entities and asserts every case, listing every divergence.</summary>
    public static async Task AssertConvergesAsync(IServiceProvider services, bool offsetColumns = true, bool clockColumns = true)
    {
        var failures = new List<string>();
        await Seed<SetMemberDoc>(index => new SetMemberDoc
        {
            Id = Ids[index], Ref = Refs[index], At = Ats[index], Seen = Seens[index], Ratio = Ratios[index], Single = Singles[index],
            Flag = Flags[index], Amount = Amounts[index], Tally = Tallies[index], Name = Names[index]
        });
        await Seed<SetMemberColumns>(index => new SetMemberColumns
        {
            Id = Ids[index], Ref = Refs[index], At = Ats[index], Ratio = Ratios[index], Single = Singles[index],
            Flag = Flags[index], Amount = Amounts[index], Tally = Tallies[index], Name = Names[index]
        });

        await Converge<SetMemberDoc>(services, "document", failures,
            d => d.Id, d => d.Ref, d => d.At, d => d.Seen, d => d.Ratio, d => d.Single, d => d.Flag, d => d.Amount, d => d.Tally, d => d.Name);
        await Converge<SetMemberColumns>(services, "columns", failures,
            d => d.Id, d => d.Ref, d => d.At, null, d => d.Ratio, d => d.Single, d => d.Flag, d => d.Amount, d => d.Tally, d => d.Name);
        await Seed<SetMemberMore>(index => new SetMemberMore
        {
            Id = Ids[index], Count32 = Count32s[index], Small = Smalls[index], Tiny = Tinies[index], Span = Spans[index],
            Day = Days[index], Clock = Clocks[index], Tier = Tiers[index], Letter = Letters[index]
        });
        await Seed<SetMemberMoreColumns>(index => new SetMemberMoreColumns
        {
            Id = Ids[index], Count32 = Count32s[index], Small = Smalls[index], Tiny = Tinies[index],
            Tier = Tiers[index], Letter = Letters[index]
        });
        await ConvergeMore<SetMemberMore>(services, "document", failures,
            d => d.Count32, d => d.Small, d => d.Tiny, d => d.Span, d => d.Day, d => d.Clock, d => d.Tier, d => d.Letter);
        await ConvergeMore<SetMemberMoreColumns>(services, "columns", failures,
            d => d.Count32, d => d.Small, d => d.Tiny, null, null, null, d => d.Tier, d => d.Letter);
        if (clockColumns)
        {
            await Seed<SetMemberClocks>(index => new SetMemberClocks
            {
                Id = Ids[index], Span = Spans[index], Day = Days[index], Clock = Clocks[index]
            });
            var clockRows = (await Data<SetMemberClocks, Guid>.All()).Select(static row => row.Id).ToHashSet();
            var clocks = new Context<SetMemberClocks>(services, "columns", failures, clockRows);
            await clocks.Property(row => row.Span, Spans, static i => TimeSpan.FromDays(10).Add(TimeSpan.FromSeconds(i)));
            await clocks.Property(row => row.Day, Days, static i => new DateOnly(2050, 1, 1).AddDays(i));
            await clocks.Property(row => row.Clock, Clocks, static i => TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(i)));
        }

        if (offsetColumns)
        {
            await Seed<SetMemberOffsets>(index => new SetMemberOffsets { Id = Ids[index], Seen = Seens[index] });
            var all = (await Data<SetMemberOffsets, Guid>.All()).Select(static row => row.Id).ToHashSet();
            await new Context<SetMemberOffsets>(services, "columns", failures, all)
                .Property(row => row.Seen, Seens, static i => new DateTimeOffset(2032, 3, 1, 9, 30, 0, TimeSpan.Zero).AddSeconds(i));
        }

        failures.Should().BeEmpty("a set filter must answer what Eq answers member by member; divergences:\n" +
                                  string.Join("\n", failures));
    }

    private static async Task Seed<T>(Func<int, T> row) where T : Entity<T, Guid>
    {
        await Data<T, Guid>.DeleteAll();
        await Data<T, Guid>.UpsertMany(Enumerable.Range(0, Ids.Length).Select(row).ToList());
    }

    private static async Task Converge<T>(
        IServiceProvider services,
        string shape,
        List<string> failures,
        Expression<Func<T, Guid>> id,
        Expression<Func<T, Guid>> reference,
        Expression<Func<T, DateTime>> at,
        Expression<Func<T, DateTimeOffset>>? seen,
        Expression<Func<T, double>> ratio,
        Expression<Func<T, float>> single,
        Expression<Func<T, bool>> flag,
        Expression<Func<T, decimal>> amount,
        Expression<Func<T, long>> tally,
        Expression<Func<T, string>> name)
        where T : Entity<T, Guid>
    {
        var all = (await Data<T, Guid>.All()).Select(static row => row.Id).ToHashSet();
        var context = new Context<T>(services, shape, failures, all);
        await context.Property(id, Ids, static i => Guid.NewGuid(), exact: true);
        await context.Property(reference, Refs, static i => Guid.NewGuid(), exact: true);
        await context.Property(at, Ats, static i => T0.AddYears(5).AddSeconds(i));
        if (seen is not null)
            await context.Property(seen, Seens, static i => new DateTimeOffset(2032, 3, 1, 9, 30, 0, TimeSpan.Zero).AddSeconds(i));
        var ratios = await context.Property(ratio, Ratios, static i => 1_000_000.5 + i);
        await context.Property(single, Singles, static i => 100_000f + i);
        await context.Property(flag, Flags, static _ => true, sizes: [1, 2], exact: true);
        await context.Property(amount, Amounts, static i => 1_000_000.25m + i);
        await context.Property(tally, Tallies, static i => 1_000_000L + i, exact: true);
        var names = await context.Property(name, Names, static i => $"pad-{i}");

        // A bit list never passes the statement's bound alone; many of them together do, and some move to one value.
        var bothFlags = new List<bool> { true, false };
        var flags = Combine(Enumerable.Repeat(0, ManyFlagSets).Select(_ => In(flag, bothFlags)).ToList());
        await context.Expect($"{shape}:{ManyFlagSets}-flag-sets", flags, all);

        // One statement holding two sets, each within SQL Server's parameters alone and past them together.
        var nameSet = Padded(Names, static i => $"pad-{i}", Pair);
        var ratioSet = Padded(Ratios, static i => 1_000_000.5 + i, Pair);
        var both = Combine(In(name, nameSet), In(ratio, ratioSet));
        await context.Expect($"{shape}:two-sets-of-{Pair}", both, names.Intersect(ratios).ToHashSet());
    }

    private static async Task ConvergeMore<T>(
        IServiceProvider services,
        string shape,
        List<string> failures,
        Expression<Func<T, int>> count32,
        Expression<Func<T, short>> small,
        Expression<Func<T, byte>> tiny,
        Expression<Func<T, TimeSpan>>? span,
        Expression<Func<T, DateOnly>>? day,
        Expression<Func<T, TimeOnly>>? clock,
        Expression<Func<T, SetMemberTier>> tier,
        Expression<Func<T, char>> letter)
        where T : Entity<T, Guid>
    {
        var all = (await Data<T, Guid>.All()).Select(static row => row.Id).ToHashSet();
        var context = new Context<T>(services, shape, failures, all);
        await context.Property(count32, Count32s, static i => 1_000_000 + i, exact: true);
        await context.Property(small, Smalls, static i => (short)(2_000 + i), sizes: [1, 2, Medium], exact: true);
        var tinies = await context.Property(tiny, Tinies, static i => (byte)i, sizes: [1, 2, 256], exact: true);
        if (span is not null)
            await context.Property(span, Spans, static i => TimeSpan.FromDays(10).Add(TimeSpan.FromSeconds(i)));
        if (day is not null) await context.Property(day, Days, static i => new DateOnly(2050, 1, 1).AddDays(i));
        if (clock is not null) await context.Property(clock, Clocks, static i => TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(i)));
        await context.Property(tier, Tiers, static i => (SetMemberTier)(i % 3), sizes: [1, 2, 3]);
        await context.Property(letter, Letters, static i => (char)(0x4E00 + 1_000 + i), sizes: [1, 2, Medium]);

        // A tinyint list never passes the statement's bound alone; nine whole-domain lists together do.
        var everyTiny = Padded(Tinies, static i => (byte)i, 256);
        var many = Combine(Enumerable.Repeat(0, ManyTinySets).Select(_ => In(tiny, everyTiny)).ToList());
        await context.Expect($"{shape}:{ManyTinySets}-tinyint-sets", many, tinies);
    }

    private sealed class Context<T>(IServiceProvider services, string shape, List<string> failures, HashSet<Guid> all)
        where T : Entity<T, Guid>
    {
        private static readonly int[] DefaultSizes = [1, 2, Medium, Large];

        /// <summary>Asserts In and Nin over sets of each size against Eq, and returns the ids Eq finds for the row values.</summary>
        public async Task<HashSet<Guid>> Property<V>(
            Expression<Func<T, V>> property,
            IReadOnlyList<V> values,
            Func<int, V> pad,
            int[]? sizes = null,
            bool exact = false)
        {
            var label = $"{shape}:{((MemberExpression)property.Body).Member.Name}";
            // Eq's answer for each member, one at a time: the reference.
            var eq = new Dictionary<int, HashSet<Guid>>();
            for (var index = 0; index < values.Count; index++)
            {
                try
                {
                    eq[index] = await Ids(Eq(property, values[index]));
                }
                catch (Exception error)
                {
                    failures.Add($"  [{label} eq #{index}] THREW {error.GetType().Name}: {First(error)}");
                    return [];
                }
            }

            // Eq must find something, or every comparison below is between two empty answers; a type every store holds
            // exactly finds each row by its own value.
            if (eq.Values.All(static ids => ids.Count == 0))
                failures.Add($"  [{label}] eq found no row for any of its own values");
            if (exact)
                for (var index = 0; index < values.Count; index++)
                    if (!eq[index].Contains(SetMembershipConvergence.Ids[index]) && !typeof(V).Equals(typeof(bool)))
                        failures.Add($"  [{label} eq #{index}] did not find its own row");

            foreach (var size in sizes ?? DefaultSizes)
            {
                var members = size <= values.Count
                    ? values.Take(size).ToList()
                    : Padded(values, pad, size);
                var expected = Enumerable.Range(0, Math.Min(size, values.Count)).SelectMany(index => eq[index]).ToHashSet();
                await Expect($"{label} in {size}", In(property, members), expected);
                await Expect($"{label} nin {size}", Nin(property, members), all.Except(expected).ToHashSet());
            }
            return eq.Values.SelectMany(static ids => ids).ToHashSet();
        }

        public async Task Expect(string name, Expression<Func<T, bool>> predicate, HashSet<Guid> expected)
        {
            try
            {
                var actual = await Ids(predicate, name);
                if (!actual.SetEquals(expected))
                    failures.Add($"  [{name}] eq: [{Show(expected)}] set: [{Show(actual)}]");
            }
            catch (Exception error)
            {
                failures.Add($"  [{name}] THREW {error.GetType().Name}: {First(error)}");
            }
        }

        private async Task<HashSet<Guid>> Ids(Expression<Func<T, bool>> predicate, string? name = null)
        {
            HashSet<Guid> ids = [];
            await PushdownGuard.NothingFallsBack(services, name ?? "eq", async () =>
                ids = (await Data<T, Guid>.Query(predicate)).Select(static row => row.Id).ToHashSet());
            return ids;
        }

        private static string Show(HashSet<Guid> ids) =>
            string.Join(",", ids.Select(static id => id.ToString()[^1..]).Order());

        private static string First(Exception error) => error.Message.Split('\n')[0].Trim();
    }

    private static Expression<Func<T, bool>> Eq<T, V>(Expression<Func<T, V>> property, V value) =>
        Expression.Lambda<Func<T, bool>>(
            Expression.Equal(property.Body, Expression.Constant(value, typeof(V))), property.Parameters);

    private static Expression<Func<T, bool>> In<T, V>(Expression<Func<T, V>> property, List<V> members) =>
        Expression.Lambda<Func<T, bool>>(
            Expression.Call(Expression.Constant(members), typeof(List<V>).GetMethod(nameof(List<V>.Contains))!, property.Body),
            property.Parameters);

    private static Expression<Func<T, bool>> Nin<T, V>(Expression<Func<T, V>> property, List<V> members) =>
        Expression.Lambda<Func<T, bool>>(Expression.Not(In(property, members).Body), property.Parameters);

    private static Expression<Func<T, bool>> Combine<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var parameter = left.Parameters[0];
        var rightBody = new Rebind(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(left.Body, rightBody), parameter);
    }

    private static List<V> Padded<V>(IReadOnlyList<V> values, Func<int, V> pad, int total)
    {
        var set = values.Distinct().ToList();
        var held = set.ToHashSet();
        for (var i = 0; set.Count < total; i++)
        {
            var member = pad(i);
            if (held.Add(member)) set.Add(member);
        }
        return set;
    }

    // A balanced conjunction, so a statement of a thousand lists is not a thousand-deep expression.
    private static Expression<Func<T, bool>> Combine<T>(IReadOnlyList<Expression<Func<T, bool>>> parts)
    {
        if (parts.Count == 1) return parts[0];
        var half = parts.Count / 2;
        return Combine(Combine(parts.Take(half).ToList()), Combine(parts.Skip(half).ToList()));
    }

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}
