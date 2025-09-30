using System.ComponentModel.DataAnnotations;

namespace Koan.Data.Connector.Json;

/// <summary>
/// Options for the JSON file-backed data adapter.
/// </summary>
public sealed class JsonDataOptions
{
    [Required]
    public string DirectoryPath { get; set; } = ".\\data";
    // Paging guardrails (used only via options paths for explicit paging; no-options materialization is handled by facade loops)
    public int DefaultPageSize { get; set; } = 1000;
    public int MaxPageSize { get; set; } = 10_000;
}
