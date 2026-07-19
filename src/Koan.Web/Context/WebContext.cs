using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Security.Claims;
using Koan.Core.Context;
using Koan.Data.Abstractions.Filtering;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Context;

/// <summary>
/// The single contribution target for request-derived context. Application contributors use standard
/// <see cref="HttpContext"/> evidence, reject invalid applicable requests, and may add typed Entity read predicates.
/// Capability modules may queue an ambient <see cref="Use(Func{IDisposable})"/> scope; Koan enters it after the
/// contributor returns so asynchronous resolution cannot lose an <see cref="System.Threading.AsyncLocal{T}"/> update.
/// </summary>
public sealed class WebContext
{
    private readonly List<Func<IDisposable>> _pendingScopes = new();
    private readonly Dictionary<Type, List<Filter>> _pendingReadFilters = new();

    internal WebContext(HttpContext httpContext)
        => HttpContext = httpContext ?? throw new ArgumentNullException(nameof(httpContext));

    /// <summary>The standard ASP.NET Core request context.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>The canonical authenticated subject id: <c>sub</c>, then <see cref="ClaimTypes.NameIdentifier"/>.</summary>
    public string? SubjectId
    {
        get
        {
            var principal = HttpContext.User;
            if (principal?.Identity?.IsAuthenticated != true) return null;
            var subject = principal.FindFirst("sub")?.Value;
            return !string.IsNullOrEmpty(subject)
                ? subject
                : principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }

    /// <summary>True when a contributor rejected the request before authorization/endpoints.</summary>
    public bool IsRejected { get; private set; }

    /// <summary>The corrective HTTP status used when <see cref="IsRejected"/> is true.</summary>
    public int RejectionStatusCode { get; private set; } = StatusCodes.Status404NotFound;

    /// <summary>
    /// Queue an ambient capability scope. The factory is invoked synchronously by Koan after this contributor returns
    /// and before the next contributor runs. This is the framework-authoring verb for contexts such as Tenancy.
    /// </summary>
    public void Use(Func<IDisposable> enterScope)
    {
        ArgumentNullException.ThrowIfNull(enterScope);
        _pendingScopes.Add(enterScope);
    }

    /// <summary>
    /// Queue a replacement request principal for later contributors, authorization, and endpoints. The original
    /// principal is restored when the request context unwinds.
    /// </summary>
    public void UsePrincipal(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        Use(() => new PrincipalScope(HttpContext, principal));
    }

    /// <summary>
    /// AND a typed, server-authored predicate into every Koan read of <typeparamref name="TEntity"/> for the remainder
    /// of this request. The expression is lowered immediately to Data's normalized filter model.
    /// </summary>
    public void Where<TEntity>(Expression<Func<TEntity, bool>> predicate)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (!_pendingReadFilters.TryGetValue(typeof(TEntity), out var filters))
        {
            filters = new List<Filter>();
            _pendingReadFilters[typeof(TEntity)] = filters;
        }
        filters.Add(LinqFilterCompiler.Compile(predicate));
    }

    /// <summary>Reject an applicable request before authorization/endpoints. Defaults to existence-hiding 404.</summary>
    public void Reject(int statusCode = StatusCodes.Status404NotFound)
    {
        if (statusCode is < 400 or > 599)
            throw new ArgumentOutOfRangeException(nameof(statusCode), statusCode, "A rejection status must be between 400 and 599.");
        IsRejected = true;
        RejectionStatusCode = statusCode;
    }

    internal IDisposable? EnterPending()
    {
        if (_pendingScopes.Count == 0 && _pendingReadFilters.Count == 0) return null;

        var factories = _pendingScopes.ToArray();
        _pendingScopes.Clear();
        var pendingFilters = _pendingReadFilters.ToArray();
        _pendingReadFilters.Clear();

        var entered = new List<IDisposable>(factories.Length + (pendingFilters.Length > 0 ? 1 : 0));
        try
        {
            foreach (var factory in factories)
                entered.Add(factory() ?? throw new InvalidOperationException("A Web context scope factory returned null."));

            if (pendingFilters.Length > 0)
            {
                var inherited = KoanContext.Get<ReadState>()?.Filters;
                var merged = inherited is null
                    ? new Dictionary<Type, Filter>()
                    : new Dictionary<Type, Filter>(inherited);

                foreach (var (entityType, filters) in pendingFilters)
                {
                    var contributed = filters.Count == 1 ? filters[0] : Filter.All(filters.ToArray());
                    merged[entityType] = merged.TryGetValue(entityType, out var existing)
                        ? Filter.All(existing, contributed)
                        : contributed;
                }

                var snapshot = new ReadOnlyDictionary<Type, Filter>(merged);
                entered.Add(KoanContext.Push(new ReadState(snapshot)));
            }

            return new EnteredScopes(entered);
        }
        catch
        {
            DisposeReverse(entered);
            throw;
        }
    }

    internal void DiscardPending()
    {
        _pendingScopes.Clear();
        _pendingReadFilters.Clear();
    }

    internal static Filter? CurrentReadFilter(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return KoanContext.Get<ReadState>()?.Filters.TryGetValue(entityType, out var filter) == true
            ? filter
            : null;
    }

    private static void DisposeReverse(IReadOnlyList<IDisposable> scopes)
    {
        for (var index = scopes.Count - 1; index >= 0; index--)
            scopes[index].Dispose();
    }

    private sealed record ReadState(IReadOnlyDictionary<Type, Filter> Filters);

    private sealed class PrincipalScope : IDisposable
    {
        private readonly HttpContext _context;
        private readonly ClaimsPrincipal _original;
        private bool _disposed;

        public PrincipalScope(HttpContext context, ClaimsPrincipal replacement)
        {
            _context = context;
            _original = context.User;
            context.User = replacement;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _context.User = _original;
        }
    }

    private sealed class EnteredScopes(IReadOnlyList<IDisposable> scopes) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisposeReverse(scopes);
        }
    }
}
