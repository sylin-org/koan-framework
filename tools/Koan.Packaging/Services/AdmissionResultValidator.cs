using System.Xml.Linq;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class AdmissionResultValidator
{
    public AdmissionReport Validate(
        string cellId,
        string project,
        string filter,
        string lane,
        string phase,
        int deadlineSeconds,
        string reproductionCommand,
        ProcessResult process,
        string resultPath)
    {
        RequireValue(cellId, "cell ID");
        RequireValue(project, "project");
        RequireValue(filter, "filter");
        RequireValue(phase, "phase");
        RequireValue(reproductionCommand, "reproduction command");
        RequireValue(resultPath, "TRX result path");
        if (lane is not (PackagingConstants.Admission.DeterministicLane or PackagingConstants.Admission.NativeLane))
            throw new InvalidOperationException("Admission lane must be 'deterministic' or 'native'.");
        if (deadlineSeconds is < PackagingConstants.Admission.MinimumDeadlineSeconds
            or > PackagingConstants.Admission.MaximumDeadlineSeconds)
        {
            throw new InvalidOperationException(
                $"Admission deadline must be between {PackagingConstants.Admission.MinimumDeadlineSeconds} " +
                $"and {PackagingConstants.Admission.MaximumDeadlineSeconds} seconds.");
        }

        var reasons = new List<string>();
        var results = new List<AdmissionTestResult>();

        if (process.TimedOut)
        {
            reasons.Add(
                $"cell '{cellId}' phase '{phase}' exceeded its {deadlineSeconds}s deadline" +
                (process.KilledProcessTree ? "; the owned process tree was terminated" : string.Empty));
        }

        if (process.ExitCode != 0)
            reasons.Add($"cell '{cellId}' test process exited with code {process.ExitCode}");

        if (!File.Exists(resultPath))
        {
            reasons.Add($"cell '{cellId}' produced no TRX result at '{resultPath}'");
        }
        else
        {
            try
            {
                var document = XDocument.Load(resultPath, LoadOptions.None);
                var definitions = document.Descendants()
                    .Where(element => element.Name.LocalName == "UnitTest")
                    .Select(element => new
                    {
                        Id = (string?)element.Attribute("id"),
                        Method = element.Descendants()
                            .FirstOrDefault(child => child.Name.LocalName == "TestMethod")
                            ?.Attribute("name")?.Value
                    })
                    .Where(definition => definition.Id is not null)
                    .GroupBy(definition => definition.Id!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First().Method,
                        StringComparer.OrdinalIgnoreCase);

                foreach (var element in document.Descendants().Where(node => node.Name.LocalName == "UnitTestResult"))
                {
                    var testId = (string?)element.Attribute("testId");
                    definitions.TryGetValue(testId ?? string.Empty, out var method);
                    var name = method
                        ?? (string?)element.Attribute("testName")
                        ?? "[unknown]";
                    var outcome = (string?)element.Attribute("outcome") ?? "[missing]";
                    var detail = element.Descendants()
                        .FirstOrDefault(node => node.Name.LocalName == "Message")
                        ?.Value.Trim();
                    results.Add(new AdmissionTestResult(name, outcome, detail));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                reasons.Add($"cell '{cellId}' TRX could not be read: {exception.Message}");
            }
        }

        if (results.Count == 0)
            reasons.Add($"cell '{cellId}' produced zero test results for filter '{filter}'");

        var passed = results.Count(result => result.Outcome.Equals("Passed", StringComparison.OrdinalIgnoreCase));
        var failed = results.Count(result => result.Outcome.Equals("Failed", StringComparison.OrdinalIgnoreCase));
        var skipped = results.Count(result =>
            result.Outcome.Equals("NotExecuted", StringComparison.OrdinalIgnoreCase)
            || result.Outcome.Equals("Skipped", StringComparison.OrdinalIgnoreCase));
        var unknown = results.Count - passed - failed - skipped;

        if (failed > 0) reasons.Add($"cell '{cellId}' contains {failed} failed result(s)");
        if (skipped > 0) reasons.Add($"cell '{cellId}' contains {skipped} skipped/not-executed result(s)");
        if (unknown > 0) reasons.Add($"cell '{cellId}' contains {unknown} unknown-outcome result(s)");

        return new AdmissionReport(
            cellId,
            project,
            filter,
            lane,
            phase,
            deadlineSeconds,
            reproductionCommand,
            reasons.Count == 0 ? PackagingConstants.Admission.PassedVerdict : PackagingConstants.Admission.FailedVerdict,
            process.ExitCode,
            process.TimedOut,
            process.KilledProcessTree,
            passed,
            failed,
            skipped,
            unknown,
            results,
            reasons);
    }

    private static void RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Admission {name} is required.");
    }
}
