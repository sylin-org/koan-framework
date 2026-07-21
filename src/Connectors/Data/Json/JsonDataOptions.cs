using System.ComponentModel.DataAnnotations;

namespace Koan.Data.Connector.Json;

/// <summary>
/// Options for the JSON file-backed data adapter.
/// </summary>
public sealed class JsonDataOptions
{
    [Required]
    public string DirectoryPath { get; set; } = ".\\data";
}
