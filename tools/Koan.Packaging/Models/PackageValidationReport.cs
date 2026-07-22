namespace Koan.Packaging.Models;

internal sealed record PackageValidationReport(
    int SupportedOwners,
    int AssemblyOwners,
    int ConfiguredBaselines,
    int FirstPublicationPending,
    int ContentOnlyOwners);
