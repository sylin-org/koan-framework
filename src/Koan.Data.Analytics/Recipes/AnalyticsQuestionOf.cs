using System.Linq.Expressions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Analytics;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Analytics.Recipes;

/// <summary>
/// The typed question: carries the raw Where expression (for ask-time parameter binding), the
/// parameter declarations, and the closed-generic run/refresh paths captured at declaration — so no
/// caller, including an agent tool, ever needs to recover type arguments.
/// </summary>
public sealed class AnalyticsQuestion<TEntity, TKey> : AnalyticsQuestion
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyParameters =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    internal AnalyticsQuestion(
        string name,
        int rowCap,
        Expression<Func<TEntity, bool>>? where,
        IReadOnlyList<AnalyticsParameterDeclaration> parameters,
        string? groupMember,
        AnalyticsMeasureKind measureKind,
        string? measureMember,
        AnalyticsProjectionPolicy? projection,
        IReadOnlyList<AnalyticsProjectionColumn> columns,
        Func<IServiceProvider, AnalyticsQuestion, int, IReadOnlyDictionary<string, object?>, CancellationToken, Task<AnalyticsResult>> runner,
        Func<IServiceProvider, CancellationToken, Task<ProjectionRefreshReceipt>> refresher)
        : base(name, typeof(TEntity), rowCap, where, parameters, groupMember, measureKind, measureMember, projection, columns)
    {
        Runner = runner;
        RefreshRunner = refresher;
    }

    /// <summary>The closed-generic execution path captured at declaration.</summary>
    internal Func<IServiceProvider, AnalyticsQuestion, int, IReadOnlyDictionary<string, object?>, CancellationToken, Task<AnalyticsResult>> Runner { get; }

    /// <summary>Bind ask-time parameter values into the Where expression, or null when there is none.</summary>
    internal Expression<Func<TEntity, bool>>? BindWhere(
        IReadOnlyDictionary<string, object?>? parameterValues,
        out string? corrective)
    {
        corrective = null;
        if (WhereExpression is not Expression<Func<TEntity, bool>> typed)
        {
            corrective = "The question has no translatable Where expression.";
            return null;
        }
        return AnalyticsParameterBinder.Bind(typed, Parameters, parameterValues ?? EmptyParameters, out corrective);
    }

    /// <summary>The closed-generic refresh path captured at declaration.</summary>
    internal Func<IServiceProvider, CancellationToken, Task<ProjectionRefreshReceipt>> RefreshRunner { get; }

    public override Task<AnalyticsResult> ExecuteAsync(
        IServiceProvider services,
        int rowCap,
        IReadOnlyDictionary<string, object?>? parameterValues,
        CancellationToken cancellationToken) =>
        Runner(services, this, rowCap, parameterValues ?? EmptyParameters, cancellationToken);

    public override Task<ProjectionRefreshReceipt> RefreshAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        RefreshRunner(services, cancellationToken);
}
