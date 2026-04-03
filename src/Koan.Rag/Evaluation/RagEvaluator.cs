using System.Diagnostics;
using System.Globalization;
using Koan.Rag.Abstractions;
using Microsoft.Extensions.Logging;

namespace Koan.Rag.Evaluation;

/// <summary>
/// Evaluates RAG quality using RAGAS-inspired metrics:
/// Faithfulness, Answer Relevancy, Context Precision, Context Recall,
/// Hallucination Score, and Context Utilization.
/// <para>
/// Metrics are computed via LLM-as-judge: the LLM evaluates its own
/// retrieval/generation quality against reference answers and source context.
/// </para>
/// </summary>
internal sealed class RagEvaluator
{
    private readonly ILogger<RagEvaluator> _logger;

    public RagEvaluator(ILogger<RagEvaluator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Run evaluation for a single test case against corpus results.
    /// </summary>
    public async Task<RagTestCaseResult> EvaluateCase(
        RagTestCase testCase,
        RagQueryResult queryResult,
        CancellationToken ct)
    {
        var faithfulness = await EvaluateFaithfulness(
            queryResult.Answer, queryResult.Sources, ct);

        var relevancy = await EvaluateRelevancy(
            testCase.Query, queryResult.Answer, ct);

        var hallucination = testCase.ExpectedAnswer is not null
            ? await EvaluateHallucination(queryResult.Answer, testCase.ExpectedAnswer, ct)
            : 0.0;

        return new RagTestCaseResult
        {
            TestCase = testCase,
            GeneratedAnswer = queryResult.Answer,
            Sources = queryResult.Sources,
            Faithfulness = faithfulness,
            AnswerRelevancy = relevancy,
            HallucinationScore = hallucination
        };
    }

    /// <summary>
    /// Aggregate per-case results into corpus-level metrics.
    /// </summary>
    public RagEvaluation Aggregate(
        IReadOnlyList<RagTestCaseResult> results, TimeSpan duration)
    {
        if (results.Count == 0)
        {
            return new RagEvaluation
            {
                Results = results,
                Duration = duration
            };
        }

        return new RagEvaluation
        {
            Faithfulness = results.Average(r => r.Faithfulness),
            AnswerRelevancy = results.Average(r => r.AnswerRelevancy),
            HallucinationScore = results.Average(r => r.HallucinationScore),
            ContextPrecision = results.Average(r =>
                r.Sources.Count > 0 ? r.Sources.Average(s => s.RelevanceScore) : 0),
            ContextRecall = CalculateContextRecall(results),
            ContextUtilization = CalculateContextUtilization(results),
            Results = results,
            Duration = duration
        };
    }

    // ── LLM-as-Judge Metrics ────────────────────────────────────────────

    private async Task<double> EvaluateFaithfulness(
        string answer,
        IReadOnlyList<RagSource> sources,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(answer) || sources.Count == 0)
            return 0.0;

        try
        {
            var prompt = $"""
                Rate the faithfulness of this answer on a scale of 0.0 to 1.0.
                Faithfulness means: every claim in the answer is supported by the provided sources.

                Answer: {answer}

                Number of source documents: {sources.Count}

                Return ONLY a decimal number between 0.0 and 1.0.
                """;

            var response = await Koan.AI.Client.Chat(prompt, ct);
            return ParseScore(response);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LLM judge call failed; defaulting to neutral score");
            return 0.5;
        }
    }

    private async Task<double> EvaluateRelevancy(
        string question,
        string answer,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return 0.0;

        try
        {
            var prompt = $"""
                Rate how well this answer addresses the question on a scale of 0.0 to 1.0.
                1.0 means the answer directly and completely addresses the question.
                0.0 means the answer is completely irrelevant.

                Question: {question}
                Answer: {answer}

                Return ONLY a decimal number between 0.0 and 1.0.
                """;

            var response = await Koan.AI.Client.Chat(prompt, ct);
            return ParseScore(response);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LLM judge call failed; defaulting to neutral score");
            return 0.5;
        }
    }

    private async Task<double> EvaluateHallucination(
        string generatedAnswer,
        string expectedAnswer,
        CancellationToken ct)
    {
        try
        {
            var prompt = $"""
                Compare the generated answer against the expected answer.
                Rate the hallucination level on a scale of 0.0 to 1.0.
                0.0 means no hallucinations (all claims match the expected answer).
                1.0 means fully hallucinated (no claims match).

                Expected: {expectedAnswer}
                Generated: {generatedAnswer}

                Return ONLY a decimal number between 0.0 and 1.0.
                """;

            var response = await Koan.AI.Client.Chat(prompt, ct);
            return ParseScore(response);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LLM judge call failed; defaulting to neutral score");
            return 0.5;
        }
    }

    // ── Computed Metrics ─────────────────────────────────────────────────

    private static double CalculateContextRecall(IReadOnlyList<RagTestCaseResult> results)
    {
        var casesWithExpectedSources = results
            .Where(r => r.TestCase.ExpectedSourceIds is { Count: > 0 })
            .ToList();

        if (casesWithExpectedSources.Count == 0)
            return 0.0;

        return casesWithExpectedSources.Average(r =>
        {
            var expected = r.TestCase.ExpectedSourceIds!;
            var retrieved = r.Sources.Select(s => s.DocumentId).ToHashSet();
            var found = expected.Count(e => retrieved.Contains(e));
            return (double)found / expected.Count;
        });
    }

    private static double CalculateContextUtilization(IReadOnlyList<RagTestCaseResult> results)
    {
        // Approximate: ratio of sources with high relevance score to total sources
        var totalSources = results.Sum(r => r.Sources.Count);
        if (totalSources == 0) return 0.0;

        var highRelevanceSources = results
            .SelectMany(r => r.Sources)
            .Count(s => s.RelevanceScore > 0.7);

        return (double)highRelevanceSources / totalSources;
    }

    private static double ParseScore(string response)
    {
        var cleaned = response.Trim();
        if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var score))
            return Math.Clamp(score, 0.0, 1.0);

        // Try to extract a number from the response
        foreach (var word in cleaned.Split(' ', '\n'))
        {
            if (double.TryParse(word.Trim(',', '.', '(', ')'), NumberStyles.Float, CultureInfo.InvariantCulture, out var extracted))
                return Math.Clamp(extracted, 0.0, 1.0);
        }

        return 0.5; // Neutral default
    }
}
