using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

public sealed class AnalysisType : Entity<AnalysisType>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Version { get; set; } = 1;

    public List<string> Tags { get; set; } = new();
    public List<string> Descriptors { get; set; } = new();
    public string Instructions { get; set; } = string.Empty;
    public string OutputTemplate { get; set; } = string.Empty;
    public string JsonSchema { get; set; } = string.Empty;
    public List<string> RequiredSourceTypes { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
