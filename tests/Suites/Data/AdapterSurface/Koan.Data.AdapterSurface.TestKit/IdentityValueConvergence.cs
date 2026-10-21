using System.Linq.Expressions;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>A document carrying the two scalar types DATA-0100 does not govern: Guid and DateTime.</summary>
public sealed class IdentityValueWidget : Entity<IdentityValueWidget>
{
    public Guid CustomerId { get; set; }
    public Guid? OptCustomerId { get; set; }
    public DateTime PlacedAt { get; set; }
}

/// <summary>
/// Cross-adapter oracle for Guid and DateTime document properties (NK-052): <c>o.CustomerId == id</c> and
/// <c>o.PlacedAt &gt; cutoff</c> are among the most common filters an application writes, and a store must neither
/// throw on them nor answer differently from the compiled CLR predicate. DateTime values are UTC with a sub-microsecond
/// tick so an equality that rounds one side and truncates the other is caught.
/// </summary>
public static class IdentityValueConvergence
{
    private static readonly Guid A = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid B = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    private static readonly Guid C = Guid.Parse("e1a2b3c4-0000-4000-8000-000000000001");
    private static readonly DateTime T0 = new DateTime(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc).AddTicks(1234567);

    public static IReadOnlyList<IdentityValueWidget> Corpus { get; } =
    [
        new() { Id = "g1", CustomerId = A, OptCustomerId = A, PlacedAt = T0 },
        new() { Id = "g2", CustomerId = B, OptCustomerId = null, PlacedAt = T0.AddHours(1) },
        new() { Id = "g3", CustomerId = C, OptCustomerId = B, PlacedAt = T0.AddDays(-1).AddTicks(5) },
        new() { Id = "g4", CustomerId = A, OptCustomerId = C, PlacedAt = T0.AddMinutes(-1) },
        // No fraction and no zone: the serializer trims the fraction and writes no suffix for an Unspecified Kind.
        new() { Id = "g5", CustomerId = B, OptCustomerId = A, PlacedAt = new DateTime(2026, 3, 1, 11, 0, 0, DateTimeKind.Unspecified) },
    ];

    public static IEnumerable<(string Name, Expression<Func<IdentityValueWidget, bool>> Predicate)> Cases()
    {
        var a = A; var b = B; var t0 = T0;
        return
        [
            ("guid-eq", w => w.CustomerId == a),
            ("guid-ne", w => w.CustomerId != a),
            ("guid-in", w => new[] { a, b }.Contains(w.CustomerId)),
            ("guid-nullable-eq", w => w.OptCustomerId == b),
            ("guid-nullable-ne", w => w.OptCustomerId != b),
            ("datetime-eq", w => w.PlacedAt == t0),
            ("datetime-gt", w => w.PlacedAt > t0),
            ("datetime-lte", w => w.PlacedAt <= t0),
            ("datetime-range", w => w.PlacedAt > t0.AddDays(-2) && w.PlacedAt < t0),
        ];
    }

    /// <summary>Clears, seeds and asserts every case against the compiled CLR predicate. Lists every divergence.</summary>
    public static async Task AssertConvergesAsync()
    {
        foreach (var existing in await IdentityValueWidget.All()) await existing.Remove();
        await IdentityValueWidget.UpsertMany(Corpus);

        var failures = new List<string>();
        foreach (var (name, predicate) in Cases())
        {
            var oracle = Corpus.Where(predicate.Compile()).Select(static w => w.Id).Order().ToArray();
            try
            {
                var actual = (await IdentityValueWidget.Query(predicate)).Select(static w => w.Id).Order().ToArray();
                if (!actual.SequenceEqual(oracle))
                    failures.Add($"  [{name}] oracle: [{string.Join(",", oracle)}] adapter: [{string.Join(",", actual)}]");
            }
            catch (Exception error)
            {
                failures.Add($"  [{name}] THREW {error.GetType().Name}: {error.Message.Split('\n')[0].Trim()}");
            }
        }

        failures.Should().BeEmpty("Guid and DateTime filters must converge with the CLR oracle; divergences:\n" +
                                  string.Join("\n", failures));
    }
}
