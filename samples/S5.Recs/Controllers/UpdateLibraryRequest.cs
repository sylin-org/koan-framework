namespace S5.Recs.Controllers;

public sealed record UpdateLibraryRequest(bool? Favorite, bool? Watched, bool? Dropped, int? Rating);