using System;
using System.Collections.Generic;
using Koan.Data.Vector.Abstractions.Schema;

namespace Koan.Context.Models;

/// <summary>
/// Strongly typed projection of chunk metadata consumed by vector adapters.
/// </summary>
public sealed class ChunkVectorMetadata : IVectorMetadataDictionary
{
    [VectorProperty(VectorSchemaPropertyType.Text, Name = "searchText", Required = true, Searchable = true)]
    public string SearchText { get; init; } = string.Empty;
    [VectorProperty(VectorSchemaPropertyType.Text, Name = "filePath", Filterable = true, Sortable = true)]
    public string FilePath { get; init; } = string.Empty;
    [VectorProperty(VectorSchemaPropertyType.Text, Name = "commitSha")]
    public string? CommitSha { get; init; }
    [VectorProperty(VectorSchemaPropertyType.Long, Name = "startByteOffset", Sortable = true)]
    public long StartByteOffset { get; init; }
    [VectorProperty(VectorSchemaPropertyType.Long, Name = "endByteOffset", Sortable = true)]
    public long EndByteOffset { get; init; }
    [VectorProperty(VectorSchemaPropertyType.Int, Name = "startLine", Sortable = true)]
    public int StartLine { get; init; }
    [VectorProperty(VectorSchemaPropertyType.Int, Name = "endLine", Sortable = true)]
    public int EndLine { get; init; }
    [VectorProperty(VectorSchemaPropertyType.Text, Name = "sourceUrl")]
    public string? SourceUrl { get; init; }
    [VectorProperty(VectorSchemaPropertyType.Text, Name = "title")]
    public string? Title { get; init; }
    [VectorProperty(VectorSchemaPropertyType.Text, Name = "language", Filterable = true)]
    public string? Language { get; init; }
    [VectorProperty(VectorSchemaPropertyType.Text, Name = "fileHash")]
    public string? FileHash { get; init; }
    [VectorProperty(VectorSchemaPropertyType.DateTime, Name = "fileLastModified", Sortable = true)]
    public DateTime? FileLastModified { get; init; }
    [VectorProperty(VectorSchemaPropertyType.TextArray, Name = "pathSegments", Filterable = true)]
    public IReadOnlyList<string> PathSegments { get; init; } = Array.Empty<string>();
    [VectorProperty(VectorSchemaPropertyType.TextArray, Name = "primaryTags", Filterable = true)]
    public IReadOnlyList<string> PrimaryTags { get; init; } = Array.Empty<string>();
    [VectorProperty(VectorSchemaPropertyType.TextArray, Name = "secondaryTags", Filterable = true)]
    public IReadOnlyList<string> SecondaryTags { get; init; } = Array.Empty<string>();
    [VectorProperty(VectorSchemaPropertyType.TextArray, Name = "fileTags", Filterable = true)]
    public IReadOnlyList<string> FileTags { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, object?> ToDictionary()
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["searchText"] = SearchText,
            ["filePath"] = FilePath,
            ["commitSha"] = CommitSha,
            ["startByteOffset"] = StartByteOffset,
            ["endByteOffset"] = EndByteOffset,
            ["startLine"] = StartLine,
            ["endLine"] = EndLine,
            ["sourceUrl"] = SourceUrl,
            ["title"] = Title,
            ["language"] = Language,
            ["fileHash"] = FileHash,
            ["fileLastModified"] = FileLastModified,
            ["pathSegments"] = PathSegments,
            ["primaryTags"] = PrimaryTags,
            ["secondaryTags"] = SecondaryTags,
            ["fileTags"] = FileTags
        };

        return map;
    }
}
