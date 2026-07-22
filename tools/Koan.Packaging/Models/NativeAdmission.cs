namespace Koan.Packaging.Models;

internal sealed record NativeAdmissionCell(
    string ClaimId,
    string Id,
    string Project,
    string Filter,
    string Lane,
    string Phase,
    int DeadlineSeconds);

internal sealed record NativeAdmissionPlan(
    string BaseCommit,
    string CandidateCommit,
    string Applicability,
    IReadOnlyList<string> ChangedPaths,
    IReadOnlyList<string> AffectedClaims,
    IReadOnlyList<NativeAdmissionCell> Cells,
    string Reason);

internal sealed record NativeAdmissionReport(
    NativeAdmissionPlan Plan,
    string Verdict,
    IReadOnlyList<AdmissionReport> Results,
    IReadOnlyList<string> Reasons);
