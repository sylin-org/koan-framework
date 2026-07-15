namespace Koan.Packaging.Models;

internal sealed record PackageProject(
    string ProjectPath,
    string ProjectDirectory,
    string PackageId,
    string Kind,
    bool SuppressDependenciesWhenPacking,
    bool IncludeSymbols,
    string? Readme,
    string Description,
    string PackageTags,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> SharedInputs);
