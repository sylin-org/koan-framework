using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Services;

/// <summary>
/// Service for resolving type codes to their corresponding entities.
/// Provides dynamic code lookup from database rather than hardcoded lists.
/// </summary>
public interface ITypeCodeResolver
{
    /// <summary>Gets all available analysis type codes from database.</summary>
    Task<List<string>> GetAvailableAnalysisCodesAsync(CancellationToken ct = default);

    /// <summary>Gets all available source type codes from database.</summary>
    Task<List<string>> GetAvailableSourceCodesAsync(CancellationToken ct = default);

    /// <summary>Resolves an analysis type code to its entity.</summary>
    Task<AnalysisType?> ResolveAnalysisTypeAsync(string code, CancellationToken ct = default);

    /// <summary>Resolves a source type code to its entity.</summary>
    Task<SourceType?> ResolveSourceTypeAsync(string code, CancellationToken ct = default);
}

/// <summary>
/// Default implementation of ITypeCodeResolver using Koan entity queries.
/// </summary>
public class TypeCodeResolver : ITypeCodeResolver
{
    private readonly ILogger<TypeCodeResolver> _logger;

    public TypeCodeResolver(ILogger<TypeCodeResolver> logger)
    {
        _logger = logger;
    }

    public async Task<List<string>> GetAvailableAnalysisCodesAsync(CancellationToken ct = default)
    {
        var types = await AnalysisType.All(ct);
        return types
            .Where(t => !string.IsNullOrWhiteSpace(t.Code))
            .Select(t => t.Code)
            .OrderBy(c => c)
            .ToList();
    }

    public async Task<List<string>> GetAvailableSourceCodesAsync(CancellationToken ct = default)
    {
        var types = await SourceType.All(ct);
        return types
            .Where(t => !string.IsNullOrWhiteSpace(t.Code))
            .Select(t => t.Code)
            .OrderBy(c => c)
            .ToList();
    }

    public async Task<AnalysisType?> ResolveAnalysisTypeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        // NOTE: Using All() + in-memory filter instead of Query with predicate due to
        // MongoDB connector issue where StringComparison.OrdinalIgnoreCase is not properly
        // translated, causing Query to return all entities instead of filtering.
        var allTypes = await AnalysisType.All(ct);
        var result = allTypes
            .FirstOrDefault(t => t.Code?.Equals(code, StringComparison.OrdinalIgnoreCase) == true);

        if (result == null)
        {
            _logger.LogWarning("Could not resolve analysis type code: '{Code}'", code);
        }

        return result;
    }

    public async Task<SourceType?> ResolveSourceTypeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        // NOTE: Using All() + in-memory filter instead of Query with predicate due to
        // MongoDB connector issue where StringComparison.OrdinalIgnoreCase is not properly
        // translated, causing Query to return all entities instead of filtering.
        var allTypes = await SourceType.All(ct);
        var result = allTypes
            .FirstOrDefault(t => t.Code?.Equals(code, StringComparison.OrdinalIgnoreCase) == true);

        if (result == null)
        {
            _logger.LogWarning("Could not resolve source type code: '{Code}'", code);
        }

        return result;
    }
}
