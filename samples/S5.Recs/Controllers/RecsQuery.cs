namespace S5.Recs.Controllers;

public record RecsQuery(
    string? Text,
    string? AnchorMediaId,
    Filters? Filters,
    int TopK = 20,
    string? UserId = null,
    string? Sort = null,
    double? Alpha = null  // Hybrid search: semantic (1.0) vs keyword (0.0) balance
);