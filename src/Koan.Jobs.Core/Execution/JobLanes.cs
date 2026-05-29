namespace Koan.Jobs.Execution;

/// <summary>Well-known lane names (JOBS-0002/JOBS-0003). The default lane is used when a job does
/// not override <c>Lane</c>.</summary>
public static class JobLanes
{
    public const string Default = "default";
}
