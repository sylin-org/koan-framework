namespace S5.Recs.Controllers;

/// <summary>
/// Request for vector-only upsert. Limit = null means process all items.
/// </summary>
public record VectorOnlyRequest(int? Limit = null);