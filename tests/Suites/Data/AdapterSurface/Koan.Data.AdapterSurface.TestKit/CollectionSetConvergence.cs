using System.Text.Json;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>A row holding one JSON array of each element type a list filter is written over.</summary>
public sealed class ListMemberDoc : Entity<ListMemberDoc>
{
    public List<string>? Names { get; set; }
    public List<Guid>? Refs { get; set; }
    public List<decimal>? Amounts { get; set; }
    public List<double>? Ratios { get; set; }
    public List<int>? Counts { get; set; }
    public List<long>? Tallies { get; set; }
    public List<bool>? Flags { get; set; }
    public List<DateTime>? Ats { get; set; }
}

/// <summary>
/// Cross-adapter ORACLE for list filters over JSON array columns (DATA-0162): <c>HasAny</c>, <c>HasAll</c> and
/// <c>HasNone</c> answer what the per-member form answers — the union, the intersection and the complement of what
/// <c>Has</c> answers for each member — for text, Guid, decimal, double, int, long, bool and DateTime elements, with
/// lists of one, two and 2,502 members (and 70,000 for text and long), over arrays holding duplicates, nothing, or
/// null. A store that binds the list as one value must compare each element exactly as the per-member form does. Every
/// query is executed by the store, and a query that fails is a failure.
///
/// <para>The per-member form must find every value a row holds as read back: one it misses is a failure. A value no
/// row holds once read back — a double the relational write path keeps to fifteen significant digits, such as one
/// third — is returned as a note, and the list is held to what the per-member form answers. The doubles include
/// exponent forms and a negative zero against a stored zero, which print differently as text and as numbers. A
/// DateTimeOffset list is not covered: the JSON filter language reads an offset string as a DateTime first and cannot
/// express one.</para>
/// </summary>
public static class CollectionSetConvergence
{
    public const int Medium = 2_502;
    public const int Large = 70_000;

    private static readonly Guid G1 = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid G2 = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    private static readonly Guid G3 = Guid.Parse("e1a2b3c4-0000-4000-8000-000000000001");
    private static readonly DateTime T0 = new DateTime(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc).AddTicks(1_234_567);

    // Five rows: two values, one duplicated, a third value alone, an empty array, and a null.
    private static IReadOnlyList<ListMemberDoc> Corpus() =>
    [
        new()
        {
            Id = "l1", Names = ["alpha", "Bravo"], Refs = [G1, G2], Amounts = [1.5m, 2m], Ratios = [0.1, 1.0 / 3, 1e-5, 1e20], Counts = [7, 8],
            Tallies = [9_007_199_254_740_993, 9], Flags = [true], Ats = [T0, T0.AddHours(1)]
        },
        new()
        {
            Id = "l2", Names = ["alpha", "alpha"], Refs = [G1, G1], Amounts = [1.5m, 1.5m], Ratios = [0.1, 0.1, 1e-7], Counts = [7, 7],
            Tallies = [9, 9], Flags = [true, true], Ats = [T0, T0]
        },
        new()
        {
            Id = "l3", Names = ["çé"], Refs = [G3], Amounts = [12345678901234.123456m], Ratios = [-88127016363724600, 2.5e-5, 0.0], Counts = [-1],
            Tallies = [long.MinValue + 1], Flags = [false], Ats = [T0.AddDays(-1).AddTicks(5)]
        },
        new() { Id = "l4", Names = [], Refs = [], Amounts = [], Ratios = [], Counts = [], Tallies = [], Flags = [], Ats = [] },
        new() { Id = "l5" },
    ];

    /// <summary>Seeds the rows and asserts every case, listing every divergence; returns a note for each value the
    /// per-member form finds in no row although a row holds it.</summary>
    public static async Task<IReadOnlyList<string>> AssertConvergesAsync(IServiceProvider services)
    {
        foreach (var existing in await ListMemberDoc.All()) await existing.Remove();
        foreach (var row in Corpus()) await row.Save();

        var failures = new List<string>();
        var all = Corpus().Select(static row => row.Id).ToHashSet();
        var notes = new List<string>();
        var context = new Context(services, failures, notes, all);
        await context.Field("Names", ["alpha", "Bravo", "çé"], static i => $"pad-{i}", [1, 2, Medium, Large]);
        await context.Field("Refs", [G1, G2, G3], static _ => Guid.NewGuid(), [1, 2, Medium]);
        await context.Field("Amounts", [1.5m, 2m, 12345678901234.123456m], static i => 1_000_000.25m + i, [1, 2, Medium]);
        await context.Field("Ratios", [0.1, 1.0 / 3, -88127016363724600, 1e-5, 2.5e-5, 1e-7, 1e20, -0.0], static i => 1_000_000.5 + i, [1, 2, Medium]);
        await context.Field("Counts", [7, 8, -1], static i => 1_000_000 + i, [1, 2, Medium]);
        await context.Field("Tallies", [9_007_199_254_740_993, 9, long.MinValue + 1], static i => 1_000_000L + i, [1, 2, Medium, Large]);
        await context.Field("Flags", [true, false], static _ => true, [1, 2]);
        await context.Field("Ats", [T0, T0.AddHours(1), T0.AddDays(-1).AddTicks(5)], static i => T0.AddYears(5).AddSeconds(i), [1, 2, Medium]);

        failures.Should().BeEmpty("a list filter must answer what its per-member form answers; divergences:\n" +
                                  string.Join("\n", failures));
        return notes;
    }

    private sealed class Context(IServiceProvider services, List<string> failures, List<string> notes, HashSet<string> all)
    {
        public async Task Field<V>(string field, IReadOnlyList<V> values, Func<int, V> pad, int[] sizes) where V : notnull
        {
            // The per-member reference: Has for each value a row holds.
            var has = new List<HashSet<string>>();
            foreach (var value in values)
            {
                try { has.Add(await Ids(Json(field, value), $"{field} has {value}")); }
                catch (Exception error)
                {
                    failures.Add($"  [{field} has {value}] THREW {error.GetType().Name}: {First(error)}");
                    return;
                }
            }
            // A value the per-member form misses is a failure when a row holds it as read back, and a note when the write
            // path did not keep it.
            var stored = (await ListMemberDoc.All()).ToList();
            var property = typeof(ListMemberDoc).GetProperty(field)!;
            for (var index = 0; index < values.Count; index++)
            {
                if (has[index].Count > 0) continue;
                var value = values[index];
                var holders = stored
                    .Where(row => property.GetValue(row) is System.Collections.IEnumerable list && list.Cast<object>().Any(item => Equals(item, value)))
                    .Select(static row => row.Id).ToList();
                if (holders.Count > 0)
                    failures.Add($"  [{field} has {value}] finds no row, though {string.Join(",", holders)} holds it as read back");
                else
                    notes.Add($"{field}: no row holds {value} as read back (the write path did not keep it); the list is held to what the per-member form answers");
            }

            foreach (var size in sizes)
            {
                var held = Math.Min(size, values.Count);
                var members = Padded(values.Take(held).ToList(), pad, size);
                var padded = members.Count > held;
                var any = Enumerable.Range(0, held).SelectMany(index => has[index]).ToHashSet();
                var every = padded
                    ? []
                    : Enumerable.Range(0, held).Select(index => has[index])
                        .Aggregate(all.ToHashSet(), static (acc, ids) => acc.Intersect(ids).ToHashSet());
                await Expect($"{field} $in {size}", Json(field, "$in", members), any);
                await Expect($"{field} $all {size}", Json(field, "$all", members), every);
                await Expect($"{field} $nin {size}", Json(field, "$nin", members), all.Except(any).ToHashSet());
            }
        }

        private async Task Expect(string name, string json, HashSet<string> expected)
        {
            try
            {
                var actual = await Ids(json, name);
                if (!actual.SetEquals(expected))
                    failures.Add($"  [{name}] per member: [{string.Join(",", expected.Order())}] list: [{string.Join(",", actual.Order())}]");
            }
            catch (Exception error)
            {
                failures.Add($"  [{name}] THREW {error.GetType().Name}: {First(error)}");
            }
        }

        private async Task<HashSet<string>> Ids(string json, string name)
        {
            HashSet<string> ids = [];
            await PushdownGuard.NothingFallsBack(services, name, async () =>
                ids = (await ListMemberDoc.Query(json)).Select(static row => row.Id).ToHashSet());
            return ids;
        }
    }

    private static string Json<V>(string field, V value) =>
        JsonSerializer.Serialize(new Dictionary<string, object?> { [field] = value });

    private static string Json<V>(string field, string op, List<V> members) =>
        JsonSerializer.Serialize(new Dictionary<string, object?> { [field] = new Dictionary<string, object?> { [op] = members } });

    private static List<V> Padded<V>(List<V> values, Func<int, V> pad, int total) where V : notnull
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

    private static string First(Exception error) => error.Message.Split('\n')[0].Trim();
}
