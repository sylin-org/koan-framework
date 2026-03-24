using System;
using System.Collections.Generic;
using Koan.Data.Core.Model;
using Koan.Samples.Meridian.Infrastructure;

namespace Koan.Samples.Meridian.Models;

public sealed class AnalysisType : Entity<AnalysisType>
{
    public string Name { get; set; } = "";

    /// <summary>Exclusive short code for this analysis type (e.g., "EAR", "VDD", "SEC").</summary>
    public string Code { get; set; } = "";

    public string Description { get; set; } = "";
    public int Version { get; set; } = 1;

    public List<string> Tags { get; set; } = new();
    public List<string> Descriptors { get; set; } = new();
    public string Instructions { get; set; } = "";
    public string OutputTemplate { get; set; } = "";
    public string JsonSchema { get; set; } = "";
    public List<FactBlueprint.FactCategory> FactCategories { get; set; } = new();
    public List<FactBlueprint.FieldMapping> FieldMappings { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
