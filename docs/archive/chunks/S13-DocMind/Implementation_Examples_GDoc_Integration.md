# Implementation Examples: Integrating GDoc AI Patterns into S13.DocMind

**Source**: Concrete implementation examples for integrating harvested GDoc AI prompting patterns
**Target**: S13.DocMind existing services and architecture
**Purpose**: Bridge the gap between documented patterns and actual implementation

---

## Executive Summary

This document provides **concrete C# implementation examples** showing how to integrate the sophisticated AI prompting patterns harvested from GDoc into the existing S13.DocMind services. Each example includes:

- **Current S13.DocMind implementation analysis**
- **Enhanced implementation using GDoc patterns**
- **Integration with Koan Framework patterns**
- **Error handling and fallback strategies**
- **Testing and validation approaches**

---

## 1. Enhanced TemplateSuggestionService with Document Type Auto-Generation

### 1.1 Current Implementation Analysis

The existing `TemplateSuggestionService.GenerateAsync()` method uses basic AI prompting:

```csharp
// Current approach - basic prompting
var response = await _ai.PromptAsync(new AiChatRequest
{
    Model = _options.Ai.DefaultModel,
    Messages =
    {
        new AiMessage("system", "Design a JSON extraction template."),
        new AiMessage("user", prompt)
    }
}, cancellationToken).ConfigureAwait(false);

profile.Prompt = ParsePrompt(response.Text, request);
```

**Issues with current approach:**
- No structured output delimiters
- Basic string splitting for parsing (`response.Split("---")`)
- No confidence scoring or validation
- Limited error handling

### 1.2 Enhanced Implementation Using GDoc Patterns

```csharp
// Enhanced TemplateSuggestionService.cs
using System.Text.Json;
using Koan.AI.Contracts.Models;

public sealed class EnhancedTemplateSuggestionService : ITemplateSuggestionService
{
    private const string DocTypeJsonStart = "---DOCUMENT_TYPE_JSON_START---";
    private const string DocTypeJsonEnd = "---DOCUMENT_TYPE_JSON_END---";

    private readonly IAi? _ai;
    private readonly DocMindOptions _options;
    private readonly ILogger<EnhancedTemplateSuggestionService> _logger;

    public async Task<SemanticTypeProfile> GenerateAsync(TemplateGenerationRequest request, CancellationToken cancellationToken)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var profile = new SemanticTypeProfile
        {
            Name = request.Name,
            Description = request.Description,
            Metadata = request.Metadata is null ? new Dictionary<string, string>() :
                new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase)
        };

        if (_ai is not null)
        {
            try
            {
                var prompt = BuildEnhancedTemplatePrompt(request);
                var response = await _ai.PromptAsync(new AiChatRequest
                {
                    Model = _options.Ai.DefaultModel,
                    Messages =
                    {
                        new AiMessage("system", BuildSystemPrompt()),
                        new AiMessage("user", prompt)
                    }
                }, cancellationToken).ConfigureAwait(false);

                profile.Prompt = ParseStructuredPrompt(response.Text, request);
                profile.Metadata["generationMethod"] = "ai-structured";
                profile.Metadata["confidence"] = ExtractGenerationConfidence(response.Text).ToString("0.000");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Enhanced template generation failed for {Name}, using fallback", request.Name);
                profile.Prompt = BuildFallbackPrompt(request);
                profile.Metadata["generationMethod"] = "fallback";
            }
        }
        else
        {
            profile.Prompt = BuildFallbackPrompt(request);
            profile.Metadata["generationMethod"] = "no-ai";
        }

        return await profile.Save(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildSystemPrompt()
    {
        return string.Join('\n', new[]
        {
            "You are a strict API that outputs ONLY well-formed JSON for a new document type configuration.",
            "Return output wrapped EXACTLY between the delimiters on their own lines:",
            DocTypeJsonStart,
            "...JSON OBJECT...",
            DocTypeJsonEnd,
            "Rules:",
            "1. Output nothing before or after the delimiters.",
            "2. No markdown fences, no comments.",
            "3. Values MUST be concise; escape inner quotes.",
            "4. Code: 2-8 uppercase letters/numbers, no spaces (derive from name).",
            "5. Tags: 1-6 short kebab-case strings (a-z, numbers, hyphen).",
            "6. Template: markdown containing placeholders like {{FIELD_NAME}} (uppercase snake case).",
            "7. Always include all fields even if user prompt omits them (use placeholder text).",
            "8. Never hallucinate domain-specific proprietary info; keep generic if uncertain.",
            "9. Avoid backticks anywhere.",
            "10. Prefer minimal essential sections in Template.",
            "11. Include confidence score (0.0-1.0) in metadata field."
        });
    }

    private static string BuildEnhancedTemplatePrompt(TemplateGenerationRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"User intent: {request.Description}");

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            sb.AppendLine($"Document type name: {request.Name}");
        }

        if (!string.IsNullOrWhiteSpace(request.SampleText))
        {
            sb.AppendLine("Sample content:");
            sb.AppendLine(request.SampleText.Length > 2000 ? request.SampleText[..2000] + "..." : request.SampleText);
        }

        sb.AppendLine("Provide a new, purpose-appropriate configuration.");
        return sb.ToString();
    }

    private PromptTemplate ParseStructuredPrompt(string response, TemplateGenerationRequest request)
    {
        var json = ExtractDelimitedJson(response, DocTypeJsonStart, DocTypeJsonEnd);
        if (string.IsNullOrWhiteSpace(json))
        {
            _logger.LogWarning("No delimited JSON found in AI response for {Name}", request.Name);
            return BuildFallbackPrompt(request);
        }

        try
        {
            var documentType = JsonSerializer.Deserialize<DocumentTypeData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (documentType is null)
            {
                _logger.LogWarning("Failed to deserialize document type for {Name}", request.Name);
                return BuildFallbackPrompt(request);
            }

            return new PromptTemplate
            {
                SystemPrompt = documentType.Instructions ?? "Extract structured information from the supplied document.",
                UserTemplate = documentType.Template ?? "Analyze the document: {{text}}",
                Variables = new Dictionary<string, string>
                {
                    ["sample"] = request.SampleText ?? string.Empty,
                    ["code"] = documentType.Code ?? GenerateCode(request.Name),
                    ["tags"] = string.Join(", ", documentType.Tags ?? Array.Empty<string>())
                }
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse generated JSON for {Name}", request.Name);
            return BuildFallbackPrompt(request);
        }
    }

    private static string ExtractDelimitedJson(string response, string startDelimiter, string endDelimiter)
    {
        var startIndex = response.IndexOf(startDelimiter, StringComparison.Ordinal);
        if (startIndex == -1) return string.Empty;

        startIndex += startDelimiter.Length;
        var endIndex = response.IndexOf(endDelimiter, startIndex, StringComparison.Ordinal);
        if (endIndex == -1) return string.Empty;

        return response[startIndex..endIndex].Trim();
    }

    private static double ExtractGenerationConfidence(string response)
    {
        // Extract confidence from metadata if available
        try
        {
            var json = ExtractDelimitedJson(response, DocTypeJsonStart, DocTypeJsonEnd);
            if (!string.IsNullOrWhiteSpace(json))
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("confidence", out var confElement) &&
                    confElement.TryGetDouble(out var confidence))
                {
                    return Math.Clamp(confidence, 0.0, 1.0);
                }
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return 0.75; // Default confidence for successful AI generation
    }

    private static string GenerateCode(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "AUTO";

        var letters = name.Where(char.IsLetter).Take(8).ToArray();
        return new string(letters).ToUpperInvariant();
    }
}

// Supporting data structure
public sealed class DocumentTypeData
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public string[] Tags { get; set; } = Array.Empty<string>();
    public double? Confidence { get; set; }
}
```

### 1.3 Integration with Koan Framework Auto-Registration

```csharp
// Initialization/KoanAutoRegistrar.cs - Register enhanced service
public void RegisterServices(IServiceCollection services)
{
    // Replace default implementation with enhanced version
    services.RemoveAll<ITemplateSuggestionService>();
    services.AddScoped<ITemplateSuggestionService, EnhancedTemplateSuggestionService>();
}
```

---

## 2. Enhanced InsightSynthesisService with Multi-Document Analysis

### 2.1 Current Implementation Analysis

The existing `InsightSynthesisService` processes single documents:

```csharp
// Current - single document processing
public async Task<InsightSynthesisResult> GenerateAsync(
    SourceDocument document,
    DocumentExtractionResult extraction,
    IReadOnlyList<DocumentChunk> chunks,
    CancellationToken cancellationToken)
```

**Limitations:**
- Single document scope only
- No cross-document synthesis
- Basic prompt building
- Limited structured output parsing

### 2.2 Enhanced Multi-Document Synthesis Implementation

```csharp
// Enhanced InsightSynthesisService.cs
public sealed class EnhancedInsightSynthesisService : IInsightSynthesisService
{
    private readonly IAi? _ai;
    private readonly DocMindOptions _options;
    private readonly ILogger<EnhancedInsightSynthesisService> _logger;

    // Enhanced interface for multi-document analysis
    public async Task<InsightSynthesisResult> GenerateMultiDocumentAsync(
        List<SourceDocument> documents,
        SemanticTypeProfile profile,
        CancellationToken cancellationToken)
    {
        if (documents is null || documents.Count == 0)
            throw new ArgumentException("Documents required", nameof(documents));

        // Step 1: Build consolidated analysis
        var consolidatedAnalysis = await BuildConsolidatedAnalysisAsync(documents, cancellationToken);

        // Step 2: Generate enhanced analysis prompt
        var prompt = BuildMultiDocumentAnalysisPrompt(consolidatedAnalysis, profile);

        // Step 3: Execute AI analysis
        var response = await _ai.PromptAsync(new AiChatRequest
        {
            Model = _options.Ai.DefaultModel,
            Messages =
            {
                new AiMessage("system", BuildMultiDocumentSystemPrompt()),
                new AiMessage("user", prompt)
            }
        }, cancellationToken).ConfigureAwait(false);

        // Step 4: Parse structured insights with confidence scoring
        return await ParseEnhancedInsights(response, documents, profile, cancellationToken);
    }

    private async Task<ConsolidatedDocumentAnalysis> BuildConsolidatedAnalysisAsync(
        List<SourceDocument> documents,
        CancellationToken cancellationToken)
    {
        var analysis = new ConsolidatedDocumentAnalysis
        {
            TotalDocuments = documents.Count,
            Documents = new List<DocumentSummary>()
        };

        var allTopics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allEntities = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var allKeyFacts = new List<KeyFact>();

        for (int i = 0; i < documents.Count; i++)
        {
            var document = documents[i];
            var summary = await BuildDocumentSummary(document, i + 1, cancellationToken);
            analysis.Documents.Add(summary);

            // Aggregate topics, entities, key facts
            foreach (var topic in summary.Topics)
            {
                allTopics.Add(topic);
            }

            // Extract insights for cross-document synthesis
            var insights = await GetDocumentInsights(document, cancellationToken);
            foreach (var insight in insights)
            {
                if (insight.Confidence >= 0.7) // High confidence facts only
                {
                    allKeyFacts.Add(new KeyFact
                    {
                        Fact = insight.Body,
                        Source = $"DOC_{i + 1:D2}",
                        Confidence = insight.Confidence ?? 0.7
                    });
                }
            }
        }

        analysis.ConsolidatedTopics = allTopics.ToList();
        analysis.ConsolidatedEntities = allEntities;
        analysis.ConsolidatedKeyFacts = allKeyFacts;

        return analysis;
    }

    private static string BuildMultiDocumentSystemPrompt()
    {
        return @"SYSTEM
fill the template using ALL documents. cite sources as DOC_## (e.g. DOC_01). if notes exist they override conflicting document content. if conflict: mention both and prefer notes. do not invent content. use 'UNKNOWN' for missing required info. keep answers concise.

META
citation_format: DOC_##
delimiters: FILLED_DOCUMENT_TYPE, CONTEXT_UNDERSTANDING
unknown_token: UNKNOWN
required_blocks: filled_document_type, context_understanding";
    }

    private string BuildMultiDocumentAnalysisPrompt(
        ConsolidatedDocumentAnalysis analysis,
        SemanticTypeProfile profile)
    {
        var sb = new StringBuilder();

        // Context section
        sb.AppendLine("=== CONTEXT ===");
        sb.AppendLine("=== CONSOLIDATED DOCUMENT ANALYSIS ===");
        sb.AppendLine($"Total Documents: {analysis.TotalDocuments}");
        sb.AppendLine();

        // Document summaries
        foreach (var doc in analysis.Documents)
        {
            sb.AppendLine($"DOC_{analysis.Documents.IndexOf(doc) + 1:D2} | {doc.FileName}");
            sb.AppendLine($"Type: {doc.DocumentType}");
            sb.AppendLine($"Confidence: {doc.Confidence:0.000}");
            sb.AppendLine($"Summary: {doc.Summary}");
            sb.AppendLine($"Topics: {string.Join(", ", doc.Topics)}");
            sb.AppendLine();
        }

        // Consolidated sections
        sb.AppendLine("=== CONSOLIDATED TOPICS ===");
        sb.AppendLine(string.Join(", ", analysis.ConsolidatedTopics));
        sb.AppendLine();

        sb.AppendLine("=== CONSOLIDATED KEY FACTS ===");
        foreach (var fact in analysis.ConsolidatedKeyFacts.Take(20)) // Limit for prompt size
        {
            sb.AppendLine($"- {fact.Fact} (from {fact.Source}, confidence: {fact.Confidence:0.000})");
        }
        sb.AppendLine();

        // Instructions
        sb.AppendLine("=== INSTRUCTIONS ===");
        sb.AppendLine("Your task is to gather, verify, and populate all required fields in the provided template.");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT CONTEXT:");
        sb.AppendLine("- You are working with PRE-EXTRACTED and STRUCTURED information from multiple documents");
        sb.AppendLine("- The raw content has already been analyzed by a previous AI step");
        sb.AppendLine("- Focus on synthesizing and formatting this structured information; do NOT re-analyze raw text");
        sb.AppendLine("- Use consolidated sections (topics, entities, key facts) to understand relationships");
        sb.AppendLine("- Maintain fidelity to extracted information while creating a cohesive narrative");
        sb.AppendLine();

        // Template
        sb.AppendLine("=== TEMPLATE MARKUP ===");
        sb.AppendLine(profile.Prompt.UserTemplate);
        sb.AppendLine();

        // Output requirements
        sb.AppendLine("OUTPUT REQUIREMENT");
        sb.AppendLine("Return ONLY these blocks in order, no extra commentary:");
        sb.AppendLine("---FILLED_DOCUMENT_TYPE_START---");
        sb.AppendLine("(filled template with placeholders replaced, include source DOC_## citations inline where relevant)");
        sb.AppendLine("---FILLED_DOCUMENT_TYPE_END---");
        sb.AppendLine("---CONTEXT_UNDERSTANDING_START---");
        sb.AppendLine("(2-3 sentence synthesis: documents count, major sources used, conflicts handled)");
        sb.AppendLine("---CONTEXT_UNDERSTANDING_END---");

        return sb.ToString();
    }

    private async Task<DocumentSummary> BuildDocumentSummary(
        SourceDocument document,
        int docNumber,
        CancellationToken cancellationToken)
    {
        // Get existing insights for this document
        var insights = await GetDocumentInsights(document, cancellationToken);

        var summary = new DocumentSummary
        {
            FileName = document.DisplayName ?? document.FileName,
            DocumentType = document.AssignedProfileId ?? "unknown",
            Confidence = insights.Where(i => i.Confidence.HasValue).Select(i => i.Confidence!.Value).DefaultIfEmpty(0.5).Average(),
            Summary = document.Summary.PrimaryFindings ?? "No summary available",
            Topics = ExtractTopicsFromInsights(insights)
        };

        return summary;
    }

    private async Task<IReadOnlyList<DocumentInsight>> GetDocumentInsights(
        SourceDocument document,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(document.Id, out var documentId))
            return Array.Empty<DocumentInsight>();

        return await DocumentInsight.Query($"SourceDocumentId == '{documentId}'", cancellationToken);
    }

    private static List<string> ExtractTopicsFromInsights(IReadOnlyList<DocumentInsight> insights)
    {
        var topics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var insight in insights)
        {
            if (insight.StructuredPayload?.TryGetValue("tags", out var tagsObj) == true)
            {
                if (tagsObj is string[] tags)
                {
                    foreach (var tag in tags)
                    {
                        if (!string.IsNullOrWhiteSpace(tag))
                        {
                            topics.Add(tag.Trim());
                        }
                    }
                }
            }
        }

        return topics.Take(10).ToList(); // Limit topics per document
    }

    private async Task<InsightSynthesisResult> ParseEnhancedInsights(
        AiChatResponse response,
        List<SourceDocument> documents,
        SemanticTypeProfile profile,
        CancellationToken cancellationToken)
    {
        var filledTemplate = ExtractDelimitedContent(response.Text,
            "---FILLED_DOCUMENT_TYPE_START---",
            "---FILLED_DOCUMENT_TYPE_END---");

        var contextUnderstanding = ExtractDelimitedContent(response.Text,
            "---CONTEXT_UNDERSTANDING_START---",
            "---CONTEXT_UNDERSTANDING_END---");

        var insights = new List<DocumentInsight>();
        var metrics = new Dictionary<string, double>
        {
            ["documents.processed"] = documents.Count,
            ["synthesis.mode"] = "multi-document"
        };

        // Create synthesis insight from filled template
        if (!string.IsNullOrWhiteSpace(filledTemplate))
        {
            var synthesisInsight = new DocumentInsight
            {
                SourceDocumentId = Guid.Parse(documents.First().Id),
                Channel = InsightChannel.Text,
                Heading = $"Multi-Document {profile.Name} Analysis",
                Body = filledTemplate,
                Confidence = 0.85, // High confidence for successful template fill
                Section = "synthesis",
                StructuredPayload = new Dictionary<string, object?>
                {
                    ["type"] = "multi-document-synthesis",
                    ["profileId"] = profile.Id,
                    ["documentCount"] = documents.Count,
                    ["sources"] = documents.Select(d => d.Id).ToArray()
                },
                Metadata = new Dictionary<string, string>
                {
                    ["kind"] = "multi-document-synthesis",
                    ["model"] = response.Model ?? "unknown",
                    ["documentCount"] = documents.Count.ToString()
                }
            };

            insights.Add(synthesisInsight);
        }

        // Create context understanding insight
        if (!string.IsNullOrWhiteSpace(contextUnderstanding))
        {
            var contextInsight = new DocumentInsight
            {
                SourceDocumentId = Guid.Parse(documents.First().Id),
                Channel = InsightChannel.Text,
                Heading = "Cross-Document Analysis Context",
                Body = contextUnderstanding,
                Confidence = 0.9,
                Section = "context",
                StructuredPayload = new Dictionary<string, object?>
                {
                    ["type"] = "context-understanding",
                    ["documentCount"] = documents.Count
                },
                Metadata = new Dictionary<string, string>
                {
                    ["kind"] = "context-understanding",
                    ["model"] = response.Model ?? "unknown"
                }
            };

            insights.Add(contextInsight);
        }

        metrics["insights.total"] = insights.Count;

        var context = new Dictionary<string, string>
        {
            ["mode"] = "multi-document-synthesis",
            ["model"] = response.Model ?? "unknown",
            ["documentCount"] = documents.Count.ToString()
        };

        if (response.TokensIn.HasValue)
            context["tokensIn"] = response.TokensIn.Value.ToString();
        if (response.TokensOut.HasValue)
            context["tokensOut"] = response.TokensOut.Value.ToString();

        return new InsightSynthesisResult(
            insights,
            metrics,
            context,
            response.TokensIn,
            response.TokensOut);
    }

    private static string ExtractDelimitedContent(string response, string startDelimiter, string endDelimiter)
    {
        var startIndex = response.IndexOf(startDelimiter, StringComparison.Ordinal);
        if (startIndex == -1) return string.Empty;

        startIndex += startDelimiter.Length;
        var endIndex = response.IndexOf(endDelimiter, startIndex, StringComparison.Ordinal);
        if (endIndex == -1) return string.Empty;

        return response[startIndex..endIndex].Trim();
    }
}

// Supporting data structures
public sealed class ConsolidatedDocumentAnalysis
{
    public int TotalDocuments { get; set; }
    public List<DocumentSummary> Documents { get; set; } = new();
    public List<string> ConsolidatedTopics { get; set; } = new();
    public Dictionary<string, List<string>> ConsolidatedEntities { get; set; } = new();
    public List<KeyFact> ConsolidatedKeyFacts { get; set; } = new();
}

public sealed class DocumentSummary
{
    public string FileName { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> Topics { get; set; } = new();
}

public sealed class KeyFact
{
    public string Fact { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public double Confidence { get; set; }
}
```

---

## 3. Configuration and Error Handling Patterns

### 3.1 Enhanced Configuration Options

```csharp
// Infrastructure/DocMindOptions.cs - Enhanced AI configuration
public sealed class DocMindOptions
{
    public AiOptions Ai { get; set; } = new();

    public sealed class AiOptions
    {
        public string DefaultModel { get; set; } = string.Empty;
        public int MaxRetryAttempts { get; set; } = 3;
        public int TimeoutSeconds { get; set; } = 60;
        public double ConfidenceThreshold { get; set; } = 0.7;
        public bool EnableStructuredOutput { get; set; } = true;
        public bool EnableMultiDocumentAnalysis { get; set; } = true;
        public int MaxDocumentsPerAnalysis { get; set; } = 10;
        public int MaxPromptTokens { get; set; } = 8000;
    }
}
```

### 3.2 Robust Error Handling Service

```csharp
// Services/AiProcessingErrorHandler.cs
public sealed class AiProcessingErrorHandler
{
    private readonly ILogger<AiProcessingErrorHandler> _logger;
    private readonly DocMindOptions _options;

    public async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        Func<T> fallback,
        string operationName,
        CancellationToken cancellationToken) where T : class
    {
        var attempts = 0;
        var maxAttempts = _options.Ai.MaxRetryAttempts;

        while (attempts < maxAttempts)
        {
            try
            {
                attempts++;
                return await operation();
            }
            catch (Exception ex) when (attempts < maxAttempts && IsRetriableError(ex))
            {
                _logger.LogWarning(ex,
                    "AI operation {Operation} failed on attempt {Attempt}/{MaxAttempts}",
                    operationName, attempts, maxAttempts);

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempts)), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "AI operation {Operation} failed permanently after {Attempts} attempts",
                    operationName, attempts);
                break;
            }
        }

        _logger.LogInformation("Using fallback for {Operation} after {Attempts} failed attempts",
            operationName, attempts);
        return fallback();
    }

    private static bool IsRetriableError(Exception ex)
    {
        return ex is TimeoutException or HttpRequestException or TaskCanceledException;
    }
}
```

---

## 4. Testing Strategies

### 4.1 Unit Testing for Enhanced Services

```csharp
// Tests/Services/EnhancedTemplateSuggestionServiceTests.cs
[TestClass]
public sealed class EnhancedTemplateSuggestionServiceTests
{
    [TestMethod]
    public async Task GenerateAsync_WithValidResponse_ParsesStructuredOutput()
    {
        // Arrange
        var mockAi = new Mock<IAi>();
        var expectedResponse = @$"
{DocTypeJsonStart}
{{
    ""Name"": ""Test Document"",
    ""Code"": ""TEST"",
    ""Description"": ""Test document type"",
    ""Instructions"": ""Extract test information"",
    ""Template"": ""# Test\n\n## Summary\n{{SUMMARY}}"",
    ""Tags"": [""test"", ""example""],
    ""Confidence"": 0.95
}}
{DocTypeJsonEnd}";

        mockAi.Setup(x => x.PromptAsync(It.IsAny<AiChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatResponse { Text = expectedResponse });

        var service = new EnhancedTemplateSuggestionService(
            CreateServiceProvider(mockAi.Object),
            CreateOptions(),
            Mock.Of<IEmbeddingGenerator>(),
            Mock.Of<DocMindVectorHealth>(),
            Mock.Of<ILogger<EnhancedTemplateSuggestionService>>());

        var request = new TemplateGenerationRequest
        {
            Name = "Test Document",
            Description = "A test document type"
        };

        // Act
        var result = await service.GenerateAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("Test Document", result.Name);
        Assert.IsTrue(result.Prompt.UserTemplate.Contains("{{SUMMARY}}"));
        Assert.AreEqual("0.950", result.Metadata["confidence"]);
        Assert.AreEqual("ai-structured", result.Metadata["generationMethod"]);
    }

    [TestMethod]
    public async Task GenerateAsync_WithMalformedResponse_UsesGracefulFallback()
    {
        // Arrange
        var mockAi = new Mock<IAi>();
        mockAi.Setup(x => x.PromptAsync(It.IsAny<AiChatRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatResponse { Text = "Invalid response without delimiters" });

        var service = new EnhancedTemplateSuggestionService(/* ... */);
        var request = new TemplateGenerationRequest { Name = "Test", Description = "Test" };

        // Act
        var result = await service.GenerateAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("fallback", result.Metadata["generationMethod"]);
        Assert.IsTrue(result.Prompt.UserTemplate.Contains("{{text}}"));
    }
}
```

### 4.2 Integration Testing with Real AI Services

```csharp
// Tests/Integration/AiIntegrationTests.cs
[TestClass]
[TestCategory("Integration")]
public sealed class AiIntegrationTests
{
    [TestMethod]
    public async Task DocumentTypeGeneration_WithRealAI_ProducesValidOutput()
    {
        // Arrange - requires real AI configuration
        var serviceProvider = CreateRealServiceProvider();
        var service = serviceProvider.GetRequiredService<ITemplateSuggestionService>();

        var request = new TemplateGenerationRequest
        {
            Name = "Meeting Summary",
            Description = "Extract key information from meeting transcripts",
            SampleText = "Team discussed project timeline and assigned action items..."
        };

        // Act
        var result = await service.GenerateAsync(request, CancellationToken.None);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Prompt.SystemPrompt));
        Assert.IsTrue(result.Prompt.UserTemplate.Contains("{{"));
        Assert.IsTrue(double.TryParse(result.Metadata["confidence"], out var confidence));
        Assert.IsTrue(confidence >= 0.0 && confidence <= 1.0);
    }
}
```

---

## 5. Performance Monitoring and Optimization

### 5.1 AI Performance Metrics Service

```csharp
// Services/AiPerformanceMonitor.cs
public sealed class AiPerformanceMonitor
{
    private readonly ILogger<AiPerformanceMonitor> _logger;
    private readonly ConcurrentDictionary<string, AiOperationMetrics> _metrics = new();

    public async Task<T> TrackOperationAsync<T>(
        string operationName,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startTime = DateTimeOffset.UtcNow;

        try
        {
            var result = await operation();

            RecordSuccess(operationName, stopwatch.Elapsed, startTime);
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(operationName, stopwatch.Elapsed, startTime, ex);
            throw;
        }
    }

    private void RecordSuccess(string operationName, TimeSpan duration, DateTimeOffset startTime)
    {
        var metrics = _metrics.GetOrAdd(operationName, _ => new AiOperationMetrics());
        metrics.RecordSuccess(duration);

        _logger.LogInformation("AI operation {Operation} completed in {Duration}ms",
            operationName, duration.TotalMilliseconds);
    }

    private void RecordFailure(string operationName, TimeSpan duration, DateTimeOffset startTime, Exception ex)
    {
        var metrics = _metrics.GetOrAdd(operationName, _ => new AiOperationMetrics());
        metrics.RecordFailure(duration, ex.GetType().Name);

        _logger.LogWarning("AI operation {Operation} failed after {Duration}ms: {Error}",
            operationName, duration.TotalMilliseconds, ex.Message);
    }

    public Dictionary<string, object> GetPerformanceReport()
    {
        return _metrics.ToDictionary(
            kvp => kvp.Key,
            kvp => (object)new
            {
                SuccessCount = kvp.Value.SuccessCount,
                FailureCount = kvp.Value.FailureCount,
                AverageDuration = kvp.Value.AverageDuration.TotalMilliseconds,
                SuccessRate = kvp.Value.SuccessRate
            });
    }
}

public sealed class AiOperationMetrics
{
    private readonly object _lock = new();
    private long _successCount;
    private long _failureCount;
    private TimeSpan _totalDuration;

    public long SuccessCount => _successCount;
    public long FailureCount => _failureCount;
    public TimeSpan AverageDuration => _successCount > 0 ? TimeSpan.FromTicks(_totalDuration.Ticks / _successCount) : TimeSpan.Zero;
    public double SuccessRate => (_successCount + _failureCount) > 0 ? (double)_successCount / (_successCount + _failureCount) : 0.0;

    public void RecordSuccess(TimeSpan duration)
    {
        lock (_lock)
        {
            _successCount++;
            _totalDuration = _totalDuration.Add(duration);
        }
    }

    public void RecordFailure(TimeSpan duration, string errorType)
    {
        lock (_lock)
        {
            _failureCount++;
        }
    }
}
```

---

## 6. Next Steps and Implementation Priority

### 6.1 Phase 1: Enhanced Template Generation (Week 1-2)
1. Implement `EnhancedTemplateSuggestionService` with structured output parsing
2. Add unit tests for delimiter extraction and JSON parsing
3. Update existing `TemplateSuggestionService` registration in auto-registrar
4. Test with real AI models and validate output quality

### 6.2 Phase 2: Multi-Document Analysis (Week 3-4)
1. Extend `IInsightSynthesisService` interface for multi-document methods
2. Implement `ConsolidatedDocumentAnalysis` building
3. Add cross-document citation tracking (`DOC_##` format)
4. Create integration tests with multiple document scenarios

### 6.3 Phase 3: Error Handling and Performance (Week 5)
1. Implement `AiProcessingErrorHandler` for robust retry logic
2. Add `AiPerformanceMonitor` for operation tracking
3. Configure enhanced `DocMindOptions` for AI settings
4. Add performance dashboards and monitoring

### 6.4 Phase 4: Template Library and Validation (Week 6)
1. Create pre-built template library based on GDoc examples
2. Implement template validation and testing endpoints
3. Add confidence scoring and quality metrics
4. Create documentation and developer guides

---

## 7. Conclusion

These implementation examples provide **concrete, actionable code** that bridges the sophisticated AI prompting patterns from GDoc with the existing S13.DocMind architecture. Key benefits:

- **Structured Output**: Reliable JSON parsing with delimiters eliminates response parsing errors
- **Multi-Document Synthesis**: Cross-document analysis with proper citation tracking
- **Robust Error Handling**: Graceful fallbacks maintain system reliability
- **Performance Monitoring**: Track AI operation quality and optimize accordingly
- **Koan Framework Integration**: Follows auto-registration and entity patterns

The phased implementation approach ensures gradual integration while maintaining system stability and allowing for iterative improvements based on real-world usage patterns.