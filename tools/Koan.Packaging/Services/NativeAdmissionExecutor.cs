using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class NativeAdmissionExecutor
{
    private readonly Func<NativeAdmissionCell, string, bool, CancellationToken, Task<AdmissionReport>> runCell;

    public NativeAdmissionExecutor(AdmissionRunner admissionRunner)
        : this((cell, configuration, noBuild, cancellationToken) => admissionRunner.RunAsync(
            cell.Id,
            cell.Project,
            cell.Filter,
            cell.Lane,
            cell.Phase,
            cell.DeadlineSeconds,
            configuration,
            noBuild,
            cancellationToken))
    {
    }

    internal NativeAdmissionExecutor(
        Func<NativeAdmissionCell, string, bool, CancellationToken, Task<AdmissionReport>> runCell)
    {
        this.runCell = runCell;
    }

    public async Task<NativeAdmissionReport> RunAsync(
        NativeAdmissionPlan plan,
        string configuration,
        bool noBuild,
        CancellationToken cancellationToken)
    {
        if (plan.Applicability == PackagingConstants.Admission.NotApplicable)
        {
            return new NativeAdmissionReport(
                plan,
                PackagingConstants.Admission.NotApplicable,
                [],
                []);
        }

        var results = new List<AdmissionReport>();
        var reasons = new List<string>();
        foreach (var cell in plan.Cells)
        {
            try
            {
                var result = await runCell(cell, configuration, noBuild, cancellationToken);
                results.Add(result);
                if (result.Verdict != PackagingConstants.Admission.PassedVerdict)
                    reasons.Add($"native cell '{cell.Id}' failed: {string.Join("; ", result.Reasons)}");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                reasons.Add($"native cell '{cell.Id}' produced no admissible result: {exception.Message}");
            }
        }

        var returnedIds = results.Select(result => result.CellId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var missing in plan.Cells.Where(cell => !returnedIds.Contains(cell.Id)))
            reasons.Add($"required native cell '{missing.Id}' is missing for candidate {plan.CandidateCommit}");

        return new NativeAdmissionReport(
            plan,
            reasons.Count == 0
                ? PackagingConstants.Admission.PassedVerdict
                : PackagingConstants.Admission.FailedVerdict,
            results,
            reasons.Distinct(StringComparer.Ordinal).ToArray());
    }
}
