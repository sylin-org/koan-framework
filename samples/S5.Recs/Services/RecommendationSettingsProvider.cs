using Microsoft.Extensions.Logging;
using S5.Recs.Models;
using Koan.Data.Core;

namespace S5.Recs.Services;

internal sealed class RecommendationSettingsProvider(IServiceProvider sp, ILogger<RecommendationSettingsProvider>? logger = null)
    : IRecommendationSettingsProvider
{
    private readonly IServiceProvider _sp = sp;
    private readonly ILogger<RecommendationSettingsProvider>? _logger = logger;
    private (DateTimeOffset at, double ptw, int mpt, double dw)? _cache;
    private static (double, int, double) Defaults => (
        Infrastructure.Constants.Scoring.PreferTagsWeightDefault,
        Infrastructure.Constants.Scoring.MaxPreferredTagsDefault,
        Infrastructure.Constants.Scoring.DiversityWeightDefault
    );

    public (double PreferTagsWeight, int MaxPreferredTags, double DiversityWeight) GetEffective()
    {
        var now = DateTimeOffset.UtcNow;
        var cached = _cache;
        if (cached is { } c && (now - c.at) < TimeSpan.FromSeconds(60))
            return (c.ptw, c.mpt, c.dw);
        try
        {
            var doc = SettingsDoc.Get("recs:settings", CancellationToken.None).GetAwaiter().GetResult();
            if (doc is null)
            {
                var d = Defaults;
                _cache = (now, d.Item1, d.Item2, d.Item3);
                return d;
            }
            var ptw = Clamp(doc.PreferTagsWeight, 0, 1.0);
            var mpt = Math.Clamp(doc.MaxPreferredTags, 1, 5);
            var dw = Clamp(doc.DiversityWeight, 0, 0.2);
            _cache = (now, ptw, mpt, dw);
            return (ptw, mpt, dw);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Settings provider: falling back to defaults");
            var d = Defaults;
            _cache = (now, d.Item1, d.Item2, d.Item3);
            return d;
        }
    }

    public Task InvalidateAsync(CancellationToken ct = default)
    {
        _cache = null;
        return Task.CompletedTask;
    }

    private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
}
