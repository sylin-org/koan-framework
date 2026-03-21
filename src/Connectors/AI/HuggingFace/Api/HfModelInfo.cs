using System.Text.Json.Serialization;

namespace Koan.AI.Connector.HuggingFace.Api;

/// <summary>
/// HuggingFace API model metadata response.
/// </summary>
internal sealed record HfModelInfo
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("modelId")]
    public string ModelId { get; init; } = "";

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("downloads")]
    public int Downloads { get; init; }

    [JsonPropertyName("likes")]
    public int Likes { get; init; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];

    [JsonPropertyName("pipeline_tag")]
    public string? PipelineTag { get; init; }

    [JsonPropertyName("library_name")]
    public string? LibraryName { get; init; }

    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; init; }

    [JsonPropertyName("sha")]
    public string? Sha { get; init; }

    [JsonPropertyName("private")]
    public bool IsPrivate { get; init; }

    [JsonPropertyName("disabled")]
    public bool Disabled { get; init; }

    [JsonPropertyName("cardData")]
    public HfCardData? CardData { get; init; }
}

/// <summary>
/// Model card metadata (license, language, datasets, etc.).
/// </summary>
internal sealed record HfCardData
{
    [JsonPropertyName("license")]
    public string? License { get; init; }

    [JsonPropertyName("language")]
    public List<string>? Languages { get; init; }

    [JsonPropertyName("datasets")]
    public List<string>? Datasets { get; init; }
}
