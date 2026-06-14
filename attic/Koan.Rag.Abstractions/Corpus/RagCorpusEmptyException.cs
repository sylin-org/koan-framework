namespace Koan.Rag.Abstractions;

/// <summary>
/// Thrown when <c>Ask(string)</c> is called on a corpus with no ingested documents.
/// Use <c>AskResult()</c> for structured error handling via <see cref="RagQueryStatus.EmptyCorpus"/>.
/// </summary>
public sealed class RagCorpusEmptyException : InvalidOperationException
{
    public RagCorpusEmptyException(string corpusName)
        : base($"Corpus '{corpusName}' contains no documents. Call Ingest() first.")
    {
        CorpusName = corpusName;
    }

    public string CorpusName { get; }
}

/// <summary>
/// Thrown when <c>Rag.Corpus&lt;T&gt;("name")</c> references a corpus that
/// has not been created.
/// </summary>
public sealed class RagCorpusNotFoundException : InvalidOperationException
{
    public RagCorpusNotFoundException(string corpusName, string entityType)
        : base($"No corpus named '{corpusName}' found for entity type '{entityType}'. " +
               $"Create it with Rag.Corpus<{entityType}>(\"{corpusName}\", directive) " +
               $"or declare [RagCorpus(\"{corpusName}\")] on the entity class.")
    {
        CorpusName = corpusName;
        EntityType = entityType;
    }

    public string CorpusName { get; }
    public string EntityType { get; }
}
