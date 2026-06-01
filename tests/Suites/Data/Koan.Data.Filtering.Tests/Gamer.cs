namespace Koan.Data.Filtering.Tests;

public enum Region { NA, EU, JP }

/// <summary>Test entity covering the member shapes the matrix exercises: scalar string/int,
/// nullable value type, enum, a List&lt;string&gt; collection, and a string[] collection.</summary>
public sealed class Gamer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int? Score { get; set; }
    public Region Region { get; set; }
    public List<string> Games { get; set; } = new();
    public string[]? Tags { get; set; }
}
