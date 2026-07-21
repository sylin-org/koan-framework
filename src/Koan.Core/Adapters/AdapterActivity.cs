using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Koan.Core.Adapters;

/// <summary>
/// The shared adapter operation-tracing template (ARCH-0103 cross-cutting promotion): open one <see cref="Activity"/>
/// for a backend op, let the caller tag it, run the op, and record the error status on a throw. Any adapter family base
/// composes this so every backend op is traced consistently (span name · tags · error status) instead of each adapter
/// repeating the boilerplate once per method. A no-op when nothing is listening (<c>ActivitySource.StartActivity</c>
/// returns null), so it is free on the hot path.
/// </summary>
public static class AdapterActivity
{
    public static async Task<T> TraceAsync<T>(ActivitySource source, string name, Action<Activity> tag, Func<Task<T>> op)
    {
        using var activity = source.StartActivity(name);
        if (activity is not null) tag(activity);
        try
        {
            return await op().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
