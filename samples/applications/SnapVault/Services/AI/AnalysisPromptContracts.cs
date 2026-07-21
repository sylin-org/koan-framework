using SnapVault.Models;

namespace SnapVault.Services.AI;

/// <summary>Image and camera values available to analysis-prompt templates.</summary>
public sealed record PhotoContext(
    string PhotoId,
    int Width,
    int Height,
    double AspectRatio,
    string? CameraModel,
    DateTime? CapturedAt,
    Dictionary<string, string>? ExifData);

/// <summary>An analysis style projected for the application UI.</summary>
public sealed record AnalysisStyleDefinition(
    string Id,
    string Label,
    string Icon,
    string Description,
    int Priority);
