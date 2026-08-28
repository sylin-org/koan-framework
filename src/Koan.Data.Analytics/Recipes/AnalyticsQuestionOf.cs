using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Analytics;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Analytics.Recipes;

/// <summary>
/// The typed question: carries the composed recipe and the closed-generic execution path captured at
/// declaration, so no caller — including an agent tool — ever needs to recover type arguments to run it.
/// </summary>
public sealed class AnalyticsQuestion<TEntity, TKey> : AnalyticsQuestion
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    internal AnalyticsQuestion(
        string name,
        int rowCap,
        Filter? filter,
        string? groupMember,
        AnalyticsMeasureKind measureKind,
        string? measureMember,
        AnalyticsProjectionPolicy? projection,
        IReadOnlyList<AnalyticsProjectionColumn> columns,
        Func<IServiceProvider, AnalyticsQuestion, int, CancellationToken, Task<AnalyticsResult>> runner,
        Func<IServiceProvider, CancellationToken, Task<ProjectionRefreshReceipt>> refresher)
        : base(name, typeof(TEntity), rowCap, filter, groupMember, measureKind, measureMember, projection, columns)
    {
        Runner = runner;
        RefreshRunner = refresher;
        RefreshRunner = refresher;
    }

    /// <summary>The closed-generic execution path captured at declaration.</summary>
    internal Func<IServiceProvider, AnalyticsQuestion, int, CancellationToken, Task<AnalyticsResult>> Runner { get; }

    /// <summary>The closed-generic refresh path captured at declaration.</summary>
    internal Func<IServiceProvider, CancellationToken, Task<ProjectionRefreshReceipt>> RefreshRunner { get; }

    public override Task<AnalyticsResult> ExecuteAsync(IServiceProvider services, int rowCap, CancellationToken cancellationToken) =>
        Runner(services, this, rowCap, cancellationToken);

    public override Task<ProjectionRefreshReceipt> RefreshAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        projection_Refresh(services, cancellationToken);

    private Task<ProjectionRefreshReceipt> projection_Refresh(IServiceProvider services, CancellationToken cancellationToken)
    {
        if (Projection is null)
            throw new NotSupportedException(
                "On-demand questions compute live; only materialized questions (declared with Materialize) can be refreshed.");
        return RefreshRunner(services, cancellationToken);
    }
}
