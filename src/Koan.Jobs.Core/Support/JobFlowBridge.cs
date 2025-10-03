using System;
using System.Reflection;
using Koan.Jobs.Model;
using Koan.Jobs.Progress;

namespace Koan.Jobs.Support;

internal static class JobFlowBridge
{
    public static void TryPublishEvent(Job job, string eventType, string? error)
    {
        var type = Type.GetType("Koan.Flow.Core.Notifications.JobEvent, Koan.Flow.Core");
        if (type == null)
            return;

        try
        {
            var ctor = type.GetConstructor(new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(DateTimeOffset) });
            var instance = ctor?.Invoke(new object?[]
            {
                job.Id,
                job.Status.ToString(),
                eventType,
                error,
                DateTimeOffset.UtcNow
            });
            var publishMethod = type.GetMethod("Publish", BindingFlags.Public | BindingFlags.Static);
            publishMethod?.Invoke(null, new[] { instance });
        }
        catch
        {
            // Flow bridge is optional; swallow reflection errors
        }
    }

    public static void TryPublishProgress(Job job, JobProgressUpdate update)
    {
        var type = Type.GetType("Koan.Flow.Core.Notifications.JobProgressEvent, Koan.Flow.Core");
        if (type == null)
            return;

        try
        {
            var ctor = type.GetConstructor(new[] { typeof(string), typeof(double), typeof(string), typeof(DateTimeOffset) });
            var instance = ctor?.Invoke(new object?[]
            {
                job.Id,
                update.Percentage,
                update.Message,
                update.UpdatedAt
            });
            var publishMethod = type.GetMethod("Publish", BindingFlags.Public | BindingFlags.Static);
            publishMethod?.Invoke(null, new[] { instance });
        }
        catch
        {
        }
    }
}
