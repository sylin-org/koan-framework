using System.ComponentModel.DataAnnotations;

namespace Koan.Data.Connector.Json;

/// <summary>The one JSON adapter choice: where this source keeps its entity files.</summary>
public sealed class JsonDataOptions
{
    [Required]
    public string DirectoryPath { get; set; } = "data";
}
