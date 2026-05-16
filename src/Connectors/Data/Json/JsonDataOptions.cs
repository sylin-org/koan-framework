using System.ComponentModel.DataAnnotations;

namespace Koan.Data.Connector.Json;

/// <summary>
/// Options for the JSON file-backed data adapter.
/// </summary>
public sealed class JsonDataOptions
{
    [Required]
    public string DirectoryPath { get; set; } = ".\\data";
    // Default fallback only (NOT a cap). Per ADR no adapter-side cap.
    public int DefaultPageSize { get; set; } = 1000;
}
