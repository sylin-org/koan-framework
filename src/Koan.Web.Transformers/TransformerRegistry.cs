using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Options;

namespace Koan.Web.Transformers;

internal sealed class TransformerRegistry : ITransformerRegistry
{
    private sealed record Registration(string ContentType, IEntityTransformerInvoker Invoker, int Priority);

    private readonly Dictionary<Type, List<Registration>> _map = new();
    private readonly IServiceProvider _sp;
    private readonly TransformerBindings _bindings;
    private bool _initialized;

    public TransformerRegistry(IServiceProvider sp, IOptions<TransformerBindings> bindings)
    {
        _sp = sp;
        _bindings = bindings.Value;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        // Execute deferred bindings to populate registry from DI
        foreach (var action in _bindings.Bindings.ToList()) action(_sp);
        _initialized = true;
    }

    public void Register<TEntity, TShape>(IEntityTransformer<TEntity, TShape> transformer, string[] contentTypes, int priority = (int)TransformerPriority.Discovered)
    {
        var key = typeof(TEntity);
        if (!_map.TryGetValue(key, out var list))
        {
            list = new List<Registration>();
            _map[key] = list;
        }

        var invoker = new EntityTransformerInvoker<TEntity, TShape>(transformer);

        foreach (var ct in contentTypes)
        {
            var normalized = ct?.Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                continue;
            }

            var existingIndex = list.FindIndex(x => string.Equals(x.ContentType, normalized, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                if (priority > list[existingIndex].Priority)
                {
                    list[existingIndex] = new Registration(normalized, invoker, priority);
                }

                continue;
            }

            list.Add(new Registration(normalized, invoker, priority));
        }
    }

    public TransformerSelection? ResolveForOutput(Type entityType, IEnumerable<string> acceptTypes)
    {
        EnsureInitialized();
        if (!_map.TryGetValue(entityType, out var list) || list.Count == 0)
        {
            return null;
        }

        var explicitRanges = new List<(string Type, string SubType, double Quality, int Order)>();
        var wildcardRanges = new List<(string Type, string SubType, double Quality, int Order)>();
        int order = 0;

        foreach (var accept in acceptTypes)
        {
            foreach (var raw in accept.Split(','))
            {
                var trimmed = raw.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                var parts = trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var media = parts[0];
                var quality = 1.0;

                for (var i = 1; i < parts.Length; i++)
                {
                    var kv = parts[i].Split('=', 2, StringSplitOptions.TrimEntries);
                    if (kv.Length == 2 && kv[0].Equals("q", StringComparison.OrdinalIgnoreCase) &&
                        double.TryParse(kv[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                    {
                        quality = parsed;
                    }
                }

                var segments = media.Split('/');
                if (segments.Length != 2)
                {
                    continue;
                }

                var entry = (segments[0].ToLowerInvariant(), segments[1].ToLowerInvariant(), quality, order++);
                if (segments[0] == "*" || segments[1] == "*")
                {
                    wildcardRanges.Add(entry);
                }
                else
                {
                    explicitRanges.Add(entry);
                }
            }
        }

        if (explicitRanges.Count == 0 && wildcardRanges.Count == 0)
        {
            return null;
        }

        Registration? bestRegistration = null;
        double bestScore = double.MinValue;
        int bestPriority = int.MinValue;
        int bestOrder = int.MaxValue;

        bool TryScoreAgainst(IEnumerable<(string Type, string SubType, double Quality, int Order)> ranges)
        {
            var any = false;

            foreach (var registration in list)
            {
                var segments = registration.ContentType.Split('/');
                if (segments.Length != 2)
                {
                    continue;
                }

                var registeredType = segments[0].ToLowerInvariant();
                var registeredSubType = segments[1].ToLowerInvariant();

                foreach (var range in ranges)
                {
                    var typeMatch = range.Type == "*" || range.Type == registeredType;
                    var subMatch = range.SubType == "*" || range.SubType == registeredSubType;
                    if (!typeMatch || !subMatch)
                    {
                        continue;
                    }

                    any = true;
                    var specificity = (range.Type == "*" && range.SubType == "*") ? 0.5 : (range.SubType == "*" ? 0.8 : 1.0);
                    var score = range.Quality * specificity;

                    if (bestRegistration is null || score > bestScore ||
                        (Math.Abs(score - bestScore) < double.Epsilon && registration.Priority > bestPriority) ||
                        (Math.Abs(score - bestScore) < double.Epsilon && registration.Priority == bestPriority && range.Order < bestOrder))
                    {
                        bestRegistration = registration;
                        bestScore = score;
                        bestPriority = registration.Priority;
                        bestOrder = range.Order;
                    }
                }
            }

            return any;
        }

        var hadExplicitMatch = TryScoreAgainst(explicitRanges);
        if (!hadExplicitMatch)
        {
            // If only wildcard Accepts (e.g., */*), do NOT force a transformer; let MVC default (JSON) handle it.
            // Only match wildcards if there was also at least one explicit media range.
            return null;
        }

        return bestRegistration is null
            ? null
            : new TransformerSelection(entityType, bestRegistration.ContentType, bestRegistration.Invoker);
    }

    public TransformerSelection? ResolveForInput(Type entityType, string contentType)
    {
        EnsureInitialized();
        if (!_map.TryGetValue(entityType, out var list) || list.Count == 0)
        {
            return null;
        }

        var normalized = contentType.Split(';')[0].Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        var registration = list
            .Where(x => string.Equals(x.ContentType, normalized, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Priority)
            .FirstOrDefault();

        return registration is null
            ? null
            : new TransformerSelection(entityType, registration.ContentType, registration.Invoker);
    }

    public IReadOnlyList<string> GetContentTypes(Type entityType)
    {
        EnsureInitialized();
        if (!_map.TryGetValue(entityType, out var list) || list.Count == 0)
        {
            return Array.Empty<string>();
        }

        return list
            .Select(x => x.ContentType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}