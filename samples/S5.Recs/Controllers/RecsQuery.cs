namespace S5.Recs.Controllers;

public record RecsQuery(
	string? Text,
	string? AnchorAnimeId,
	Filters? Filters,
	int TopK = 20,
	string? UserId = null,
	string? Sort = null
);