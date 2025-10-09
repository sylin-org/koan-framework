using System;
using System.Collections.Generic;

using S13.DocMind.Models;

namespace S13.DocMind.Services;

public sealed record InsightSynthesisResult(
    IReadOnlyList<DocumentInsight> Insights,
    IReadOnlyDictionary<string, double> Metrics,
    IReadOnlyDictionary<string, string> Context,
    long? InputTokens,
    long? OutputTokens)
{
    public static readonly InsightSynthesisResult Empty = new(
        Array.Empty<DocumentInsight>(),
        new Dictionary<string, double>(),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        null,
        null);
}
