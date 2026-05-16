using System.Text.Json.Serialization;

namespace Koan.AI.Connector.HuggingFace.Api;

/// <summary>
/// HuggingFace API file entry from the model tree endpoint.
/// </summary>
internal sealed record HfFileInfo
{
    [JsonPropertyName("rfilename")]
    public string FileName { get; init; } = "";

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("lfs")]
    public HfLfsInfo? Lfs { get; init; }
}

/// <summary>
/// LFS metadata for large files hosted on HuggingFace.
/// </summary>
internal sealed record HfLfsInfo
{
    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    [JsonPropertyName("pointer_size")]
    public long PointerSize { get; init; }
}
