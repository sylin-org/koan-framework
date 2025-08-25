namespace S5.Recs.Controllers;

public record SeedRequest(string Source = "local", int Limit = 50, bool Overwrite = false);