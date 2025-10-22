namespace Koan.Jobs.Model;

public enum JobExecutionStatus
{
    Pending = 0,
    Running = 10,
    Succeeded = 20,
    Faulted = 30,
    Cancelled = 40,
    Skipped = 50
}
