namespace Koan.Jobs.Model;

public enum JobStatus
{
    Created = 0,
    Queued = 10,
    Running = 20,
    Succeeded = 90,
    Completed = 100,
    Failed = 110,
    Cancelled = 120
}
