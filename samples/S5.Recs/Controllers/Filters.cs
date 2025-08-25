namespace S5.Recs.Controllers;

public record Filters(
    string[]? Genres,
    int? EpisodesMax,
    bool SpoilerSafe = true,
    string[]? PreferTags = null,
    double? PreferWeight = null
);