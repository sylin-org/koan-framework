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

/// <summary>Widget whose interesting order keys live inside a nested collection.</summary>
public sealed class SortedWidget : Entity<SortedWidget>
{
    public string Name { get; set; } = "";
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
        new() { Id = "a", Name = "alpha", Sightings = [S(2024, 6, 20), S(2019, 1, 2)] },
        new() { Id = "b", Name = "bravo", Sightings = [S(2023, 1, 10), S(2026, 1, 30)] },
        new() { Id = "c", Name = "charlie", Sightings = [S(2025, 6, 3)] },
        new() { Id = "d", Name = "delta", Sightings = [S(2027, 1, 40), S(2020, 5, 4)] },
        // No sightings at all: the aggregate is NULL, and NULL has to land where the framework puts it.
        new() { Id = "e", Name = "echo", Sightings = [] },
    ];

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

    private static SortedWidget Clone(SortedWidget source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Sightings = source.Sightings
            .Select(static sighting => new SortedSighting { LastChangedAt = sighting.LastChangedAt, Index = sighting.Index })
            .ToList()
    };
}
