using Koan.Data.Core;
using Koan.Samples.Meridian.Models;

namespace Koan.Samples.Meridian.Services;

public interface IRunLogWriter
{
    Task AppendAsync(RunLog log, CancellationToken ct);
}

public sealed class RunLogWriter : IRunLogWriter
{
    public async Task AppendAsync(RunLog log, CancellationToken ct)
    {
        // Persisting via Entity<T>.Save keeps the implementation simple and provider-agnostic.
        await log.Save(ct);
    }
}
