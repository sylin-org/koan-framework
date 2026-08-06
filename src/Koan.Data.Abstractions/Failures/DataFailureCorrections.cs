namespace Koan.Data.Abstractions.Failures;

/// <summary>Framework-owned stable public wording; adapters cannot inject native messages or arbitrary facts.</summary>
public static class DataFailureCorrections
{
    public static string Message(DataFailureKind kind) => kind switch
    {
        DataFailureKind.Configuration => "The Data source configuration is invalid.",
        DataFailureKind.PolicyDenied => "The source policy denied the requested effect.",
        DataFailureKind.Authentication => "The provider did not authenticate the configured identity.",
        DataFailureKind.Authorization => "The configured identity is not authorized for the requested effect.",
        DataFailureKind.Unavailable => "The Data source is unavailable.",
        DataFailureKind.Timeout => "The Data operation exceeded its declared time bound.",
        DataFailureKind.Conflict => "The Data operation conflicted with current provider state.",
        DataFailureKind.MissingTarget => "The declared Data target is missing.",
        DataFailureKind.InvalidShape => "The provider shape does not match the declared shape.",
        DataFailureKind.Constraint => "The Data operation violated a declared provider constraint.",
        DataFailureKind.Conversion => "A value could not be converted at the provider boundary.",
        DataFailureKind.Cancelled => "The Data operation was cancelled by its caller.",
        _ => "The Data provider returned an unclassified failure."
    };

    public static string Correction(DataFailureKind kind) => kind switch
    {
        DataFailureKind.Configuration => "Correct the source configuration before retrying.",
        DataFailureKind.PolicyDenied => "Use an allowed effect or deliberately change the source policy.",
        DataFailureKind.Authentication => "Correct the configured provider identity through the secret owner.",
        DataFailureKind.Authorization => "Grant only the required provider permission or use an allowed operation.",
        DataFailureKind.Unavailable => "Restore provider reachability before retrying under the declared retry policy.",
        DataFailureKind.Timeout => "Correct reachability or change the explicit operation timeout deliberately.",
        DataFailureKind.Conflict => "Reload current state and apply the declared conflict policy.",
        DataFailureKind.MissingTarget => "Provision the target outside Koan or authorize Managed lifecycle explicitly.",
        DataFailureKind.InvalidShape => "Align the declared mapping with the provider shape; External sources cannot be repaired by Koan.",
        DataFailureKind.Constraint => "Correct the value or operation so it satisfies the declared constraint.",
        DataFailureKind.Conversion => "Declare a compatible codec or correct the physical value.",
        DataFailureKind.Cancelled => "Retry only when the caller deliberately starts a new operation.",
        _ => "Inspect restricted native evidence and add an exact type/code classifier before retrying."
    };
}
