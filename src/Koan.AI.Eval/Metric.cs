namespace Koan.AI.Eval;

/// <summary>
/// Well-known evaluation metric names. Use these constants instead of raw strings
/// for type safety and discoverability.
/// </summary>
public static class Metric
{
    // ── Text Generation ──
    public const string RougeL = "rouge-l";
    public const string Bleu = "bleu";
    public const string BERTScore = "bert-score";
    public const string Perplexity = "perplexity";
    public const string Coherence = "coherence";
    public const string Toxicity = "toxicity";

    // ── RAG Quality ──
    public const string Faithfulness = "faithfulness";
    public const string ContextRelevancy = "context-relevancy";
    public const string AnswerRelevancy = "answer-relevancy";
    public const string ContextRecall = "context-recall";

    // ── Classification ──
    public const string Accuracy = "accuracy";
    public const string F1 = "f1";
    public const string Precision = "precision";
    public const string Recall = "recall";

    // ── Retrieval ──
    public const string RecallAtK = "recall@k";
    public const string NDCG = "ndcg";
    public const string MRR = "mrr";

    // ── Operational ──
    public const string Latency = "latency";
    public const string CostPerQuery = "cost-per-query";
    public const string ErrorRate = "error-rate";
}
