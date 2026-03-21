using FluentAssertions;
using Koan.AI.Eval;
using Xunit;

namespace Koan.AI.Eval.Tests;

public class MetricTests
{
    [Fact]
    public void MetricConstants_AreNotNull()
    {
        Metric.RougeL.Should().NotBeNullOrEmpty();
        Metric.Bleu.Should().NotBeNullOrEmpty();
        Metric.BERTScore.Should().NotBeNullOrEmpty();
        Metric.Perplexity.Should().NotBeNullOrEmpty();
        Metric.Accuracy.Should().NotBeNullOrEmpty();
        Metric.Latency.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void MetricConstants_AreUnique()
    {
        var allMetrics = new[]
        {
            Metric.RougeL, Metric.Bleu, Metric.BERTScore, Metric.Perplexity,
            Metric.Coherence, Metric.Toxicity,
            Metric.Faithfulness, Metric.ContextRelevancy, Metric.AnswerRelevancy, Metric.ContextRecall,
            Metric.Accuracy, Metric.F1, Metric.Precision, Metric.Recall,
            Metric.RecallAtK, Metric.NDCG, Metric.MRR,
            Metric.Latency, Metric.CostPerQuery, Metric.ErrorRate
        };

        allMetrics.Should().OnlyHaveUniqueItems();
    }
}
