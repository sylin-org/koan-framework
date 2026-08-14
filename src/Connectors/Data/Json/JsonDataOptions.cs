using System.ComponentModel.DataAnnotations;

namespace Koan.Data.Connector.Json;

/// <summary>Physical placement choices for a JSON data source.</summary>
public sealed class JsonDataOptions
{
    [Required]
    public string DirectoryPath { get; set; } = "data";

    /// <summary>How the source groups persisted Entities.</summary>
    public JsonStorageLayout Layout { get; set; } = JsonStorageLayout.Aggregate;

    /// <summary>
    /// Relative path template used by <see cref="JsonStorageLayout.IndividualFiles"/>. The template must contain one
    /// <c>{id}</c> token and may contain one <c>{storage}</c> token.
    /// </summary>
    [Required]
    public string IndividualFilePath { get; set; } = Infrastructure.Constants.Storage.DefaultIndividualFilePath;
}
