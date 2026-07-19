using AnimeRecommendations.Infrastructure;
using Koan.AI;
using Koan.Data.Abstractions;
using Koan.Data.Core.Sorting;
using Koan.Data.Vector;

namespace AnimeRecommendations.Domain;

/// <summary>Turns a viewer's strongest ratings and present mood into explainable anime recommendations.</summary>
public static class AnimeDiscovery
{
    public static async Task<RecommendationFeed> Recommend(
        string viewerId,
        string? mood,
        int take,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(viewerId))
            throw new ArgumentException("Choose a viewer before requesting recommendations.", nameof(viewerId));
        if (take is < 1 or > AnimeRecommendationsConstants.Limits.MaximumRecommendations)
            throw new ArgumentOutOfRangeException(
                nameof(take),
                $"Request between 1 and {AnimeRecommendationsConstants.Limits.MaximumRecommendations} recommendations.");

        var statedMood = mood?.Trim();
        if (statedMood?.Length > AnimeRecommendationsConstants.Limits.MaximumMoodLength)
            throw new ArgumentException(
                $"Keep the mood under {AnimeRecommendationsConstants.Limits.MaximumMoodLength} characters.",
                nameof(mood));

        var viewer = await Viewer.Get(viewerId, ct)
            ?? throw new KeyNotFoundException($"Viewer '{viewerId}' does not exist.");

        var ratings = await LibraryEntry.Query(
            entry => entry.ViewerId == viewer.Id,
            QueryDefinition.All
                .WithSort<LibraryEntry>("-Rating,-RatedAt")
                .WithPagination(1, AnimeRecommendationsConstants.Limits.MaximumTasteRatings),
            ct);
        var positivelyRated = ratings
            .Where(entry => entry.Rating >= 4)
            .OrderByDescending(entry => entry.Rating)
            .ThenByDescending(entry => entry.RatedAt)
            .ToArray();
        var anchors = await Anime.Get(positivelyRated.Select(entry => entry.AnimeId), ct);
        var taste = positivelyRated.Zip(anchors)
            .Where(pair => pair.Second is not null)
            .Select(pair => new TasteAnchor(pair.Second!, pair.First.Rating))
            .ToArray();
        if (string.IsNullOrWhiteSpace(statedMood) && taste.Length == 0)
        {
            throw new InvalidOperationException(
                "Describe what you feel like watching or rate at least one anime with 4 or 5 stars.");
        }

        var intent = BuildIntent(statedMood, taste);
        var vector = await Client.Embed(intent, ct);
        var candidateCount = Math.Min(
            AnimeRecommendationsConstants.Limits.MaximumCandidates,
            Math.Max(take * 3, 12));
        var search = await Vector<Anime>.Search(vector, topK: candidateCount, ct: ct);
        var candidates = await Anime.Get(search.Matches.Select(match => match.Id), ct);
        var alreadyRated = ratings.Select(entry => entry.AnimeId).ToHashSet(StringComparer.Ordinal);

        var items = search.Matches.Zip(candidates)
            .Where(pair => pair.Second is not null && !alreadyRated.Contains(pair.Second.Id))
            .Take(take)
            .Select(pair => new Recommendation(
                pair.Second!,
                Math.Round(pair.First.Score, 4),
                Explain(pair.Second!, statedMood, taste)))
            .ToArray();

        return new RecommendationFeed(
            viewer.Id,
            intent,
            taste.Select(anchor => anchor.Anime.Title).ToArray(),
            items);
    }

    private static string BuildIntent(string? mood, IReadOnlyList<TasteAnchor> taste)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(mood))
            parts.Add($"The viewer wants: {mood}.");

        foreach (var anchor in taste)
        {
            var emphasis = anchor.Rating == 5 ? "loves" : "likes";
            parts.Add(
                $"The viewer {emphasis} {anchor.Anime.Title}: " +
                $"{string.Join(", ", anchor.Anime.Genres.Concat(anchor.Anime.Themes))}. " +
                anchor.Anime.Synopsis);
        }

        return string.Join("\n", parts);
    }

    private static string Explain(Anime candidate, string? mood, IReadOnlyList<TasteAnchor> taste)
    {
        var tasteSignals = taste
            .SelectMany(anchor => anchor.Anime.Genres.Concat(anchor.Anime.Themes))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shared = candidate.Genres.Concat(candidate.Themes)
            .Where(tasteSignals.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(mood) && shared.Length > 0)
            return $"Fits “{mood}” and your taste for {NaturalList(shared)}.";
        if (!string.IsNullOrWhiteSpace(mood))
            return $"A close semantic match for “{mood}”.";
        if (shared.Length > 0)
            return $"Shares {NaturalList(shared)} with anime you rated highly.";
        return "Close in meaning to anime you rated highly.";
    }

    private static string NaturalList(IReadOnlyList<string> values) => values.Count switch
    {
        0 => "similar stories",
        1 => values[0],
        _ => $"{values[0]} and {values[1]}"
    };

    private sealed record TasteAnchor(Anime Anime, int Rating);
}
