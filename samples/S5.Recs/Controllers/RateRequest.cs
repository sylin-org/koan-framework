namespace S5.Recs.Controllers;

public record RateRequest(string UserId, string MediaId, int Rating);