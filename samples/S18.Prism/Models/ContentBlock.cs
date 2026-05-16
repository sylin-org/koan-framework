namespace S18.Prism.Models;

public sealed record ContentBlock
{
    public ContentKind Kind { get; init; }
    public string Content { get; init; } = "";
    public string? StructuredContent { get; init; }
    public int Order { get; init; }
    public ContentSource? Source { get; init; }
    public Dictionary<string, string> Meta { get; init; } = [];
}

public enum ContentKind
{
    Text,
    Table,
    Image,
    Audio,
    Data
}

public sealed record ContentSource(
    string FileName,
    string? MimeType = null,
    string? Section = null,
    string? Extractor = null);
