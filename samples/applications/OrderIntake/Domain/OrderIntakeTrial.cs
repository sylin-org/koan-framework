using System.Diagnostics;
using System.Runtime.InteropServices;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderIntake.Infrastructure;

namespace OrderIntake.Domain;

/// <summary>A durable, idempotent order-intake workload and its reviewable receipt.</summary>
public sealed class OrderIntakeTrial : Entity<OrderIntakeTrial>, IKoanJob<OrderIntakeTrial>
{
    public WorkloadTarget Target { get; set; }
    public int RequestedOrderCount { get; set; }
    public TrialReceipt? Receipt { get; set; }

    public static OrderIntakeTrial Open(WorkloadTarget target, int count)
    {
        if (count is < OrderIntakeConstants.Limits.MinimumOrders or > OrderIntakeConstants.Limits.MaximumOrders)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                $"A trial must contain between {OrderIntakeConstants.Limits.MinimumOrders} and " +
                $"{OrderIntakeConstants.Limits.MaximumOrders} orders.");
        }

        return new OrderIntakeTrial
        {
            Target = target,
            RequestedOrderCount = count
        };
    }

    public static async Task Execute(OrderIntakeTrial trial, JobContext context, CancellationToken ct)
    {
        var totalStarted = Stopwatch.GetTimestamp();
        var orders = Enumerable.Range(1, trial.RequestedOrderCount)
            .Select(sequence => TrialOrder.For(trial.Id, sequence))
            .ToArray();
        var ids = orders.Select(order => order.Id).ToArray();

        var provider = "";
        string[] capabilities = [];
        var written = 0;
        var readBack = 0;
        var verified = 0;
        int removed = 0;
        var writeDuration = TimeSpan.Zero;
        var readDuration = TimeSpan.Zero;
        var cleanupDuration = TimeSpan.Zero;
        Exception? operationFailure = null;

        try
        {
            var phaseStarted = Stopwatch.GetTimestamp();
            using (EntityContext.Source(trial.Target.ToString()))
            {
                var declared = Data<TrialOrder, string>.Capabilities;
                capabilities = declared.All
                    .Select(capability => capability.Id)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                provider = context.Services
                    .GetRequiredService<IDataDiagnostics>()
                    .GetAdapterParticipationsSnapshot()
                    .Single(participation =>
                        string.Equals(participation.Source, trial.Target.ToString(), StringComparison.OrdinalIgnoreCase))
                    .Provider;
                written = await orders.Save(ct);
            }
            writeDuration = Stopwatch.GetElapsedTime(phaseStarted);
            await context.Progress(0.34, $"Accepted {written} orders through {trial.Target}");

            phaseStarted = Stopwatch.GetTimestamp();
            IReadOnlyList<TrialOrder?> loaded;
            using (EntityContext.Source(trial.Target.ToString()))
            {
                loaded = await TrialOrder.Get(ids, ct);
            }
            readDuration = Stopwatch.GetElapsedTime(phaseStarted);
            readBack = loaded.Count(order => order is not null);
            verified = loaded
                .Select((actual, index) => actual?.Matches(orders[index]) == true)
                .Count(matches => matches);

            if (written != orders.Length || readBack != orders.Length || verified != orders.Length)
            {
                throw new InvalidOperationException(
                    $"The {trial.Target} trial did not round-trip every order " +
                    $"(requested={orders.Length}, written={written}, read={readBack}, verified={verified}).");
            }

            await context.Progress(0.67, $"Verified {verified} orders through {trial.Target}");
        }
        catch (Exception ex)
        {
            operationFailure = ex;
            throw;
        }
        finally
        {
            var cleanupStarted = Stopwatch.GetTimestamp();
            try
            {
                using var cleanup = new CancellationTokenSource(OrderIntakeConstants.Limits.CleanupTimeout);
                using (EntityContext.Source(trial.Target.ToString()))
                {
                    removed = await TrialOrder.Remove(ids, cleanup.Token);
                }
            }
            catch (Exception cleanupError) when (operationFailure is not null)
            {
                context.Logger.LogWarning(
                    cleanupError,
                    "Cleanup also failed after the {Target} intake operation failed; preserving the original error",
                    trial.Target);
            }
            cleanupDuration = Stopwatch.GetElapsedTime(cleanupStarted);
        }

        if (removed != orders.Length)
        {
            throw new InvalidOperationException(
                $"The {trial.Target} trial removed {removed} of {orders.Length} owned order records.");
        }

        await context.Progress(1, $"Receipt ready; removed {removed} trial-owned orders");
        trial.Receipt = new TrialReceipt
        {
            Target = trial.Target,
            Provider = provider,
            Capabilities = capabilities,
            Requested = orders.Length,
            Written = written,
            ReadBack = readBack,
            Verified = verified,
            Removed = removed,
            WriteDuration = writeDuration,
            ReadDuration = readDuration,
            CleanupDuration = cleanupDuration,
            TotalDuration = Stopwatch.GetElapsedTime(totalStarted),
            CompletedAt = DateTimeOffset.UtcNow,
            Framework = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString()
        };
    }
}
