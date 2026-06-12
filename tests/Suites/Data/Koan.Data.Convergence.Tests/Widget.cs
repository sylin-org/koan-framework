using Koan.Data.Abstractions;

namespace Koan.Data.Convergence.Tests;

public enum Tier { Free, Pro, Enterprise }

/// <summary>
/// Convergence test entity. Mirrors the cross-architecture shape from DATA-XXXX §5.4:
/// a scalar set (Region/Tier/Level/Score), a nullable value type (Score), an enum (Tier),
/// and the headline <see cref="Tags"/> collection (List&lt;string&gt;) that the original
/// $in-on-collection bug was about.
/// </summary>
public sealed class Widget : IEntity<string>
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int? Score { get; set; }
    public Tier Tier { get; set; }
    public List<string> Tags { get; set; } = new();
}
