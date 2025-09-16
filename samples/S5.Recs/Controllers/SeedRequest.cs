namespace S5.Recs.Controllers;

public record SeedRequest(string Source = "local", string? MediaType = null, int? Limit = null, bool Overwrite = false);