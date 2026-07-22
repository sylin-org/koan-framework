using System.Text.Json.Serialization;
using Koan.Packaging.Infrastructure;

namespace Koan.Packaging.Models;

internal sealed class ProductClaims
{
    [JsonRequired]
    public int SchemaVersion { get; init; }
    public List<ProductClaimInput> Claims { get; init; } = [];
}

internal sealed record ProductClaimInput
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string Maturity { get; init; }
    public List<string> Packages { get; init; } = [];
    public List<string> Documentation { get; init; } = [];
    public List<string> Evidence { get; init; } = [];
}

internal sealed class ProductSurface
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = PackagingConstants.ProductSurface.Schema;
    public required string Source { get; init; }
    public List<ProductClaim> Claims { get; init; } = [];
    public List<ProductPackage> Packages { get; init; } = [];
}

internal sealed record ProductClaim(
    string Id,
    string Title,
    string Summary,
    string Maturity,
    IReadOnlyList<string> Packages,
    IReadOnlyList<string> Documentation,
    IReadOnlyList<string> Evidence);

internal sealed record ProductPackage(
    string PackageId,
    string? VersionIntent,
    string Shape,
    string Description,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<string> Dependencies,
    string? Readme,
    bool OwnsReadme,
    string? TechnicalDocumentation,
    IReadOnlyList<string> Claims);
