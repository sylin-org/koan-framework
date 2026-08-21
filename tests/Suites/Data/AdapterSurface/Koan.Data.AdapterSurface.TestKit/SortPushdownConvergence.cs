using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Sorting;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.AdapterSurface.TestKit;

public sealed class SortedSighting
{
    public DateTimeOffset LastChangedAt { get; set; }
    public int Index { get; set; }
}

public enum SortedTier { Bronze = 1, Silver = 2, Gold = 3, Platinum = 4, Diamond = 5 }

/// <summary>
/// Widget carrying one distinct value of every type whose ordering is asserted here — the scalars a caller
/// would reach for, plus a nested collection whose aggregate is an order key in its own right.
/// </summary>
public sealed class SortedWidget : Entity<SortedWidget>
{
    public string Name { get; set; } = "";
    public long Sequence { get; set; }
    public decimal Amount { get; set; }
    public double Ratio { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public DateOnly Day { get; set; }
    public TimeOnly Tod { get; set; }
    public TimeSpan Duration { get; set; }
    public SortedTier Tier { get; set; }
    public List<SortedSighting> Sightings { get; set; } = new();
}

/// <summary>
/// Cross-adapter ORACLE for order keys that reach through a collection (ARCH-0079).
///
/// <para><c>-Sightings.LastChangedAt</c> means "by each widget's latest sighting". It is an aggregate over a
/// nested array rather than a field, and every store Koan ships on can express one. Where an adapter does not,
/// the framework still answers correctly by sorting the whole result in memory — so a spec that only compares
/// orderings passes either way and says nothing about whether the store did the work. This one asserts both:
/// the ordering matches the in-memory oracle, <b>and</b> the adapter's receipt claims the key.</para>
///
/// <para>The corpus is deliberately adversarial. Widgets carry several sightings, so taking the first element
/// rather than the extreme one diverges. <c>Index</c> holds 2 and 10, so comparing the extracted JSON as text
/// rather than as a number diverges. One widget has no sightings at all, so the aggregate is NULL and its
/// placement — first ascending, last descending — must match the framework's sorter, which is not what every
/// store does by default.</para>
/// </summary>
public static class SortPushdownConvergence
{
    private static DateTimeOffset At(int year, int month) => new(year, month, 1, 0, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<SortedWidget> Corpus { get; } =
    [
        // Every scalar column below is distinct across the corpus, and deliberately not in id order, so an
        // adapter that ignored the key and returned insertion or key order would not accidentally agree.
        // Sequence spans int's range so a store that quietly narrows a long would lose the ordering.
        new() { Id = "a", Name = "alpha", Sequence = 9_000_000_000L, Amount = 10.5m, Ratio = 0.25,
                ObservedAt = At(2024, 6), Day = new(2024, 6, 1), Tod = new(6, 30), Duration = TimeSpan.FromHours(23),
                Tier = SortedTier.Gold, Sightings = [S(2024, 6, 20), S(2019, 1, 2)] },
        new() { Id = "b", Name = "bravo", Sequence = -9_000_000_000L, Amount = 2.25m, Ratio = 10.5,
                ObservedAt = At(2026, 1), Day = new(2026, 1, 2), Tod = new(23, 59), Duration = TimeSpan.FromDays(2),
                Tier = SortedTier.Bronze, Sightings = [S(2023, 1, 10), S(2026, 1, 30)] },
        new() { Id = "c", Name = "charlie", Sequence = 42L, Amount = 100.75m, Ratio = 2.5,
                ObservedAt = At(2025, 6), Day = new(2025, 6, 3), Tod = new(0, 5), Duration = TimeSpan.FromDays(1),
                Tier = SortedTier.Diamond, Sightings = [S(2025, 6, 3)] },
        new() { Id = "d", Name = "delta", Sequence = 3_000_000_000L, Amount = 3.5m, Ratio = 100.25,
                ObservedAt = At(2027, 1), Day = new(2027, 1, 4), Tod = new(12, 0), Duration = TimeSpan.FromMinutes(90),
                Tier = SortedTier.Silver, Sightings = [S(2027, 1, 40), S(2020, 5, 4)] },
        // No sightings at all: the aggregate is NULL, and NULL has to land where the framework puts it.
        new() { Id = "e", Name = "echo", Sequence = -7L, Amount = 0.5m, Ratio = 1.75,
                ObservedAt = At(2023, 3), Day = new(2023, 3, 9), Tod = new(18, 45), Duration = TimeSpan.FromHours(2),
                Tier = SortedTier.Platinum, Sightings = [] },
    ];

    /// <summary>
    /// The scalars whose ordering this oracle asserts on every adapter. Streaming admits an order key only
    /// where its convergence has been proven rather than assumed, so this list is the evidence that decides
    /// what <c>TypeClassification.IsPortableStreamSortScalar</c> may contain.
    /// </summary>
    public static IReadOnlyList<string> PortableScalars { get; } =
        ["Sequence", "Amount", "Ratio", "ObservedAt", "Day", "Tod", "Tier"];

    /// <summary>
    /// <c>Duration</c> is in the corpus and deliberately not in the list above. Couchbase is the one adapter
    /// DATA-0100's comparable encoding never reached, so it stores a TimeSpan in .NET's default form and orders
    /// <c>1.00:00:00</c> before <c>23:00:00</c> — twenty-four hours ahead of twenty-three. The value still
    /// round-trips and every other store orders it correctly, so this is not a reason to hold the type back
    /// everywhere; it is a reason not to promise a portable order for it until that gap closes (PMC-037).
    /// </summary>
    public const string UnprovenScalar = "Duration";

    /// <summary>
    /// Nothing the store was asked to do gets done in memory instead.
    ///
    /// <para>The rest of this oracle checks that orderings agree; this checks who performed them. The two are
    /// independent: a query finished by the framework returns exactly the right rows, so an oracle comparing
    /// rows stays green while the query reads the whole table to produce them. Koan records that decision where
    /// it makes it, and this reads the same fact an operator would see at <c>/.well-known/Koan/facts</c>.</para>
    ///
    /// <para>The sweep is deliberately ordinary — every ordering, a page, a stream, an unfiltered read —
    /// because the gap this catches is never in the exotic call. It is in the everyday one that quietly stopped
    /// being pushed down. Filters answer for themselves inside <see cref="FilterConvergence"/>, which owns that
    /// corpus.</para>
    /// </summary>
    public static async Task AssertNothingFallsBackAsync(IServiceProvider services)
    {
        await Seed();

        await PushdownGuard.NothingFallsBack(services, "ordering, paging and streaming", async () =>
        {
            await Seed();
            foreach (var field in PortableScalars.Concat(["Name", "Sightings.LastChangedAt"]))
            {
                _ = await Data<SortedWidget, string>.Page(1, 2, field);
                _ = await Data<SortedWidget, string>.Page(1, 2, "-" + field);
            }

            await foreach (var _ in Data<SortedWidget, string>.AllStream("-ObservedAt", 2)) { }
            _ = await SortedWidget.All();
        });
    }

    /// <summary>
    /// Every scalar in <see cref="PortableScalars"/> orders on the store exactly as the framework's own sorter
    /// would order it, ascending and descending. This is what "proven portable" has to mean before a stream may
    /// page on that key: a caller reading page after page from the provider gets the sequence the CLR defines,
    /// not the one a particular backend happens to produce.
    /// </summary>
    public static async Task AssertScalarOrderingConvergesAsync(IServiceProvider services)
    {
        await Seed();
        var repository = (IQueryRepository<SortedWidget, string>)services
            .GetRequiredService<IDataService>()
            .GetRepository<SortedWidget, string>();

        var divergences = new List<string>();
        foreach (var field in PortableScalars)
        {
            foreach (var sort in new[] { field, "-" + field })
            {
                var specs = SortSpecParser.ParseStrict<SortedWidget>(sort);
                var result = await repository.Query(new QueryDefinition().WithSort(specs), CancellationToken.None);

                result.SortHandled.Should().HaveCount(specs.Count,
                    $"'{sort}' is a plain column and the store must order it");

                // Collected rather than asserted one at a time: the first divergence is rarely the whole story,
                // and knowing which types disagree is what decides the floor.
                var expected = string.Join(",", InMemorySorter.Apply(Corpus, specs).Select(static widget => widget.Id));
                var actual = string.Join(",", result.Items.Select(static widget => widget.Id));
                if (expected != actual) divergences.Add($"{sort}: expected {expected}, store gave {actual}");
            }
        }

        divergences.Should().BeEmpty(
            "ordering has to mean the same on this store as it does in the framework before a stream may page on it");
    }

    private static SortedSighting S(int year, int month, int index) =>
        new() { LastChangedAt = At(year, month), Index = index };

    public static async Task AssertConvergesAsync(IServiceProvider services)
    {
        await Seed();

        var repository = (IQueryRepository<SortedWidget, string>)services
            .GetRequiredService<IDataService>()
            .GetRepository<SortedWidget, string>();

        foreach (var sort in new[]
                 {
                     "-Sightings.LastChangedAt",
                     "Sightings.LastChangedAt",
                     "-Sightings.Index",
                     "Sightings.Index",
                 })
        {
            var specs = SortSpecParser.ParseStrict<SortedWidget>(sort);
            var result = await repository.Query(new QueryDefinition().WithSort(specs), CancellationToken.None);

            result.SortHandled.Should().HaveCount(specs.Count,
                $"'{sort}' is an aggregate over a nested array, which this store can express — declining it " +
                "makes the framework materialize the whole result to answer one order key");

            var expected = InMemorySorter.Apply(Corpus, specs).Select(static widget => widget.Id);
            result.Items.Select(static widget => widget.Id).Should().Equal(expected,
                $"the store's ordering for '{sort}' must be the ordering the framework would have produced");
        }
    }

    /// <summary>
    /// The point of all of it: a caller streams in a chosen order and the provider pages it, never materializing
    /// the set. Both kinds of key are streamed — a plain column and an aggregate over a nested collection — and
    /// the sequence must be the one the framework's own sorter defines, across several provider pages.
    /// </summary>
    public static async Task AssertStreamsAsync()
    {
        await Seed();

        foreach (var sort in new[] { "-ObservedAt", "Sequence", "-Sightings.LastChangedAt" })
        {
            var specs = SortSpecParser.ParseStrict<SortedWidget>(sort);
            var expected = InMemorySorter.Apply(Corpus, specs).Select(static widget => widget.Id).ToArray();

            var streamed = new List<string>();
            // Two at a time against five rows, so the order has to survive being assembled from three pages.
            await foreach (var widget in Data<SortedWidget, string>.AllStream(sort, 2))
                streamed.Add(widget.Id);

            streamed.Should().Equal(expected,
                $"streaming by '{sort}' must yield the ordering the framework defines, page after page");
        }

        // A key whose comparison the store defines rather than Koan - a string orders by collation - streams
        // too. Koan cannot promise the same sequence on a different backend and does not assert one here; what
        // it must deliver is the sequence this store itself produces, assembled from pages, complete and in
        // order. Refusing this outright used to be the alternative, with "materialize the query" as the advice.
        var storeOrder = (await Data<SortedWidget, string>.Page(1, 50, "Name"))
            .Select(static widget => widget.Id).ToArray();
        var storeStreamed = new List<string>();
        await foreach (var widget in Data<SortedWidget, string>.AllStream("Name", 2))
            storeStreamed.Add(widget.Id);

        storeStreamed.Should().Equal(storeOrder,
            "a stream must reproduce the order the store itself returns, however that store compares strings");
        storeStreamed.Should().OnlyHaveUniqueItems().And.HaveCount(Corpus.Count,
            "paging an order must lose nothing and repeat nothing");
    }

    /// <summary>Paging is only meaningful over an order the store applied; this proves the window is real.</summary>
    public static async Task AssertPagesAsync()
    {
        await Seed();

        var specs = SortSpecParser.ParseStrict<SortedWidget>("-Sightings.LastChangedAt");
        var expected = InMemorySorter.Apply(Corpus, specs).Select(static widget => widget.Id).ToArray();

        for (var page = 1; page <= 3; page++)
        {
            var window = await Data<SortedWidget, string>.Page(page, 2, "-Sightings.LastChangedAt");
            window.Select(static widget => widget.Id)
                .Should().Equal(expected.Skip((page - 1) * 2).Take(2),
                    $"page {page} must be that window of the whole ordering");
        }
    }

    /// <summary>
    /// Writes the corpus. The ids are fixed and this Entity belongs to this oracle alone, so a write is enough
    /// and no clear is needed — and a clear would be the first statement to run, against a collection that a
    /// store need not have provisioned before anything was ever written to it.
    /// </summary>
    private static Task Seed() => Corpus.Select(Clone).ToList().Save();

    // Saving assigns and mutates, and the corpus is the oracle both sides compare against, so what goes to the
    // store is a copy. Every field belongs here: one omitted field seeds a default for every row, which ties the
    // whole corpus and quietly turns an ordering assertion into an assertion about insertion order.
    private static SortedWidget Clone(SortedWidget source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Sequence = source.Sequence,
        Amount = source.Amount,
        Ratio = source.Ratio,
        ObservedAt = source.ObservedAt,
        Day = source.Day,
        Tod = source.Tod,
        Duration = source.Duration,
        Tier = source.Tier,
        Sightings = source.Sightings
            .Select(static sighting => new SortedSighting { LastChangedAt = sighting.LastChangedAt, Index = sighting.Index })
            .ToList()
    };
}
