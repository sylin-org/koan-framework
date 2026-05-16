namespace Koan.Rag.Retrieval;

/// <summary>
/// Defines the retrieval tool set available to the agentic retrieval pipeline.
/// Each tool has a name, description, and parameter schema that the agent uses
/// to decide which tools to invoke per query.
/// </summary>
internal static class RagRetrievalTools
{
    /// <summary>
    /// Tool definitions emitted as function schemas in the agent's system prompt.
    /// The agent calls these by name; the pipeline dispatches to the implementation.
    /// </summary>
    public static IReadOnlyList<RagToolDefinition> GetToolSet(bool hasGraphStore, bool hasReranker)
    {
        var tools = new List<RagToolDefinition>
        {
            // Primary tools (always available)
            new(
                Name: "semantic_search",
                Description: "Search for content by meaning. Returns chunks ranked by semantic similarity to the query.",
                Parameters: "query: string, topK: int (default 10)"),

            new(
                Name: "keyword_search",
                Description: "Search for content by exact keywords. Returns chunks matching specific terms. Use when the query contains specific names, codes, or technical terms.",
                Parameters: "query: string, topK: int (default 10)"),

            new(
                Name: "chunk_read",
                Description: "Read the full text of a specific chunk by its ID. Use after search to get complete context.",
                Parameters: "chunkId: string"),

            new(
                Name: "sufficiency_check",
                Description: "Evaluate whether the retrieved context is sufficient to answer the original question. Returns 'sufficient' or 'insufficient' with reasoning.",
                Parameters: "question: string, context: string (summary of what you have so far)")
        };

        // Graph-dependent tools
        if (hasGraphStore)
        {
            tools.Add(new(
                Name: "concept_explore",
                Description: "Explore concepts related to a given entity in the knowledge graph. Returns connected entities and their descriptions. Use to discover cross-document relationships.",
                Parameters: "entityName: string, depth: int (default 1)"));
        }

        // Reranker-dependent tools
        if (hasReranker)
        {
            tools.Add(new(
                Name: "rerank",
                Description: "Rerank a set of chunks by relevance to the original query. Use after gathering candidates from multiple searches.",
                Parameters: "query: string, chunkIds: string[] (IDs of chunks to rerank)"));
        }

        // Metadata filter (always available — entities have structured properties)
        tools.Add(new(
            Name: "metadata_filter",
            Description: "Filter documents by structured metadata (date ranges, categories, properties). Use to narrow search scope.",
            Parameters: "filters: object (key-value pairs to match)"));

        return tools;
    }
}

/// <summary>
/// Definition of a retrieval tool for the agent's function-calling context.
/// </summary>
internal sealed record RagToolDefinition(
    string Name,
    string Description,
    string Parameters);
