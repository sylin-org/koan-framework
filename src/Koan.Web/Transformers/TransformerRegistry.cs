using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Koan.Web.Transformers;

internal sealed class TransformerRegistry(IServiceProvider sp, IOptions<TransformerBindings> bindings) : ITransformerRegistry
{
    private sealed record TerminalRegistration(string ContentType, IEntityTransformerInvoker Invoker, int Priority, int RegistrationOrder);
    private sealed record EnricherRegistration(Type EnricherType, IEntityEnricherInvoker Invoker, int Priority, int RegistrationOrder);

    private readonly Dictionary<Type, List<TerminalRegistration>> _terminals = new();
    private readonly Dictionary<Type, List<EnricherRegistration>> _enrichers = new();
    private readonly TransformerBindings _bindings = bindings.Value;
    private int _terminalCounter;
    private int _enricherCounter;
    private bool _initialized;

    private void EnsureInitialized()
    {
        if (_initialized) return;
        // Execute deferred bindings to populate the registry from DI.
        foreach (var action in _bindings.Bindings.ToList()) action(sp);
        _initialized = true;
    }

    public void Register<TEntity, TShape>(IEntityTransformer<TEntity, TShape> transformer, string[] contentTypes, int priority = (int)TransformerPriority.Discovered)
    {
        var key = typeof(TEntity);
        if (!_terminals.TryGetValue(key, out var list))
        {
            list = new List<TerminalRegistration>();
            _terminals[key] = list;
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
                // Higher priority wins; equal priority keeps the earlier registration.
                if (priority > list[existingIndex].Priority)
                {
                    list[existingIndex] = new TerminalRegistration(normalized, invoker, priority, list[existingIndex].RegistrationOrder);
                }

                continue;
            }

            list.Add(new TerminalRegistration(normalized, invoker, priority, _terminalCounter++));
        }
    }

    public void RegisterEnricher<TEntity>(IEntityEnricher<TEntity> enricher, int priority = (int)TransformerPriority.Discovered)
    {
        var key = typeof(TEntity);
        if (!_enrichers.TryGetValue(key, out var list))
        {
            list = new List<EnricherRegistration>();
            _enrichers[key] = list;
        }

        var enricherType = enricher.GetType();
        var existingIndex = list.FindIndex(r => r.EnricherType == enricherType);
        if (existingIndex >= 0)
        {
            // Higher priority wins (explicit DI overrides auto-discovery for the same type).
            if (priority > list[existingIndex].Priority)
            {
                var prev = list[existingIndex];
                list[existingIndex] = new EnricherRegistration(enricherType, new EntityEnricherInvoker<TEntity>(enricher), priority, prev.RegistrationOrder);
            }
            return;
        }

        var invoker = new EntityEnricherInvoker<TEntity>(enricher);
        list.Add(new EnricherRegistration(enricherType, invoker, priority, _enricherCounter++));
    }

    public TransformerOutputSelection ResolveOutput(Type entityType, IEnumerable<string> acceptTypes, HttpContext context)
    {
        EnsureInitialized();

        var pipeline = ResolvePipeline(entityType, context);
        var terminal = ResolveTerminal(entityType, acceptTypes, context);

        if (pipeline.Count == 0 && terminal is null)
        {
            return TransformerOutputSelection.Empty;
        }

        return new TransformerOutputSelection(pipeline, terminal);
    }

    private IReadOnlyList<EnricherSelection> ResolvePipeline(Type entityType, HttpContext context)
    {
        if (!_enrichers.TryGetValue(entityType, out var list) || list.Count == 0)
        {
            return System.Array.Empty<EnricherSelection>();
        }

        // Highest priority first, then registration order. Predicate filtering happens after ordering
        // so the final list is stable: an enricher with no predicate behaves identically to one whose
        // predicate always returns true.
        var ordered = list
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.RegistrationOrder);

        var activated = new List<EnricherSelection>(list.Count);
        foreach (var registration in ordered)
        {
            if (!registration.Invoker.ShouldActivate(context))
            {
                continue;
            }

            activated.Add(new EnricherSelection(entityType, registration.Invoker));
        }

        return activated;
    }

    private TransformerSelection? ResolveTerminal(Type entityType, IEnumerable<string> acceptTypes, HttpContext context)
    {
        if (!_terminals.TryGetValue(entityType, out var list) || list.Count == 0)
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

        TerminalRegistration? bestRegistration = null;
        double bestScore = double.MinValue;
        int bestPriority = int.MinValue;
        int bestOrder = int.MaxValue;

        bool TryScoreAgainst(IEnumerable<(string Type, string SubType, double Quality, int Order)> ranges)
        {
            var any = false;

            foreach (var registration in list)
            {
                // Predicate filter: a Terminal transformer with an activation predicate is only a
                // candidate when its predicate passes for the current request context.
                if (!registration.Invoker.ShouldActivate(context))
                {
                    continue;
                }

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
            // Pure */* Accept doesn't force a Terminal transformer — let MVC default JSON handle it.
            // Enricher activation has already happened upstream of this branch.
            return null;
        }

        return bestRegistration is null
            ? null
            : new TransformerSelection(entityType, bestRegistration.ContentType, bestRegistration.Invoker);
    }

    public TransformerSelection? ResolveForInput(Type entityType, string contentType, HttpContext context)
    {
        EnsureInitialized();
        if (!_terminals.TryGetValue(entityType, out var list) || list.Count == 0)
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
            .Where(x => x.Invoker.ShouldActivate(context))
            .OrderByDescending(x => x.Priority)
            .FirstOrDefault();

        return registration is null
            ? null
            : new TransformerSelection(entityType, registration.ContentType, registration.Invoker);
    }

    public IReadOnlyList<string> GetContentTypes(Type entityType)
    {
        EnsureInitialized();
        if (!_terminals.TryGetValue(entityType, out var list) || list.Count == 0)
        {
            return System.Array.Empty<string>();
        }

        return list
            .Select(x => x.ContentType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
