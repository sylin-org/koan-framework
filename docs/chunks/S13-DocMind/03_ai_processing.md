### **2. AI-First Processing Architecture**

#### **File Analysis Service**
```csharp
public class FileAnalysisService
{
    private readonly ILogger<FileAnalysisService> _logger;

    // Analyze individual file and return results for embedding in File entity
    public async Task<FileAnalysisResult> AnalyzeFileAsync(File file, Type type, CancellationToken ct = default)
    {
        var prompt = $"""
            {type.ExtractionPrompt}

            Document content:
            {file.ExtractedText}

            Instructions:
            Extract information according to the template below and provide structured output.

            Template:
            {type.TemplateStructure}
            """;

        var response = await AI.Prompt(prompt)
            .WithModel("gpt-4-turbo")
            .WithMaxTokens(2000)
            .WithTemperature(0.1)
            .ExecuteAsync(ct);

        var filledTemplate = await ApplyTemplateAsync(type.TemplateStructure, response.Content, ct);
        var confidence = CalculateConfidence(response.Content, type);

        return new FileAnalysisResult
        {
            Content = response.Content,
            FilledTemplate = filledTemplate,
            ConfidenceScore = confidence,
            ModelUsed = response.Model ?? "gpt-4-turbo",
            InputTokens = response.Usage?.InputTokens ?? 0,
            OutputTokens = response.Usage?.OutputTokens ?? 0,
            ProcessingDuration = response.ProcessingTime ?? TimeSpan.Zero
        };
    }

    public async Task<string> ExtractContentAsync(string storageKey, string contentType, CancellationToken ct)
    {
        return contentType switch
        {
            "application/pdf" => await _pdfExtractor.ExtractTextAsync(storageKey, ct),
            "image/jpeg" or "image/png" => await _ocrService.ExtractTextAsync(storageKey, ct),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" =>
                await _docxExtractor.ExtractTextAsync(storageKey, ct),
            "text/plain" => await _storage.ReadTextAsync(storageKey, ct),
            _ => throw new NotSupportedException($"Content type {contentType} not supported")
        };
    }

    public async Task<Type> GenerateTypeAsync(string prompt, CancellationToken ct)
    {
        var generationPrompt = $"""
            Create a document type configuration based on: {prompt}

            Output JSON format:
            {{
              "name": "Document Type Name",
              "code": "SHORT_CODE",
              "description": "Brief description",
              "extractionPrompt": "Instructions for AI analysis...",
              "templateStructure": "Output format template with placeholders"
            }}
            """;

        var response = await AI.Prompt(generationPrompt).ExecuteAsync(ct);
        var typeData = JsonSerializer.Deserialize<TypeGenerationData>(response.Content);

        var type = new Type
        {
            Name = typeData.Name,
            Code = typeData.Code,
            Description = typeData.Description,
            ExtractionPrompt = typeData.ExtractionPrompt,
            TemplateStructure = typeData.TemplateStructure
        };

        return await type.Save(ct);
    }
}

// Result container for individual file analysis (embedded in File entity)
public class FileAnalysisResult
{
    public string Content { get; set; } = "";
    public string FilledTemplate { get; set; } = "";
    public double ConfidenceScore { get; set; }
    public string ModelUsed { get; set; } = "";
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public TimeSpan ProcessingDuration { get; set; }
}

#### **Request Analysis Service (Multi-Document Processing)**
```csharp
public class RequestAnalysisService
{
    private readonly ILogger<RequestAnalysisService> _logger;

    public RequestAnalysisService(ILogger<RequestAnalysisService> logger)
    {
        _logger = logger;
    }

    // Multi-document analysis using individual file results (GDoc pattern)
    public async Task<Analysis> AnalyzeRequestAsync(Analysis analysis, CancellationToken ct = default)
    {
        analysis.State = AnalysisState.Analyzing;
        await analysis.Save(ct);

        try
        {
            // Get all files associated with this analysis request
            var files = await analysis.GetFiles();
            var analyzedFiles = files.Where(f => !string.IsNullOrEmpty(f.AnalysisResult)).ToList();

            if (analyzedFiles.Count == 0)
            {
                throw new InvalidOperationException("No files have been analyzed yet. Analyze individual files first.");
            }

            // Get template for final output formatting
            Type? template = null;
            if (analysis.TypeId.HasValue)
            {
                template = await Type.Get(analysis.TypeId.Value, ct);
            }

            // Combine individual file analyses
            var combinedAnalyses = string.Join("\n\n", analyzedFiles.Select(f =>
                $"**File: {f.FileName}**\n{f.AnalysisResult}"));

            // Generate final aggregated analysis
            var prompt = template != null ? $"""
                Based on these individual document analyses, generate a comprehensive summary using the template below:

                Individual Analyses:
                {combinedAnalyses}

                Template Instructions:
                {template.ExtractionPrompt}

                Output Template:
                {template.TemplateStructure}
                """ : $"""
                Based on these individual document analyses, provide a comprehensive summary:

                Individual Analyses:
                {combinedAnalyses}

                Generate a structured analysis covering key themes, insights, and findings.
                """;

            var response = await AI.Prompt(prompt)
                .WithModel("gpt-4-turbo")
                .WithMaxTokens(4000)
                .WithTemperature(0.1)
                .ExecuteAsync(ct);

            // Apply template if available
            string? filledTemplate = null;
            if (template != null)
            {
                filledTemplate = await ApplyTemplateAsync(template.TemplateStructure, response.Content, ct);
            }

            // Update analysis with results
            analysis.FinalAnalysis = response.Content;
            analysis.FilledTemplate = filledTemplate;
            analysis.ConfidenceScore = CalculateAggregateConfidence(analyzedFiles);
            analysis.AnalyzedAt = DateTime.UtcNow;
            analysis.ModelUsed = response.Model ?? "gpt-4-turbo";
            analysis.InputTokens = response.Usage?.InputTokens ?? 0;
            analysis.OutputTokens = response.Usage?.OutputTokens ?? 0;
            analysis.ProcessingDuration = response.ProcessingTime ?? TimeSpan.Zero;
            analysis.State = AnalysisState.Completed;

            await analysis.Save(ct);

            _logger.LogInformation("Multi-document analysis completed for request {AnalysisId} with {FileCount} files",
                analysis.Id, analyzedFiles.Count);

            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Multi-document analysis failed for request {AnalysisId}", analysis.Id);

            analysis.State = AnalysisState.Failed;
            analysis.ProcessingError = ex.Message;
            await analysis.Save(ct);

            throw;
        }
    }

    private static double CalculateAggregateConfidence(List<File> analyzedFiles)
    {
        if (analyzedFiles.Count == 0) return 0.0;

        var validConfidences = analyzedFiles
            .Where(f => f.ConfidenceScore.HasValue)
            .Select(f => f.ConfidenceScore!.Value)
            .ToList();

        return validConfidences.Count > 0 ? validConfidences.Average() : 0.0;
    }
}
```

#### **AI Model Management Service (Following Original gdoc Patterns)**
```csharp
public class ModelManagementService
{
    public async Task<List<AvailableModel>> GetAvailableModelsAsync()
    {
        // Koan AI abstraction layer for cross-provider model discovery
        var ollamaModels = await AI.Models.GetAvailable("ollama");
        var openaiModels = await AI.Models.GetAvailable("openai");

        return ollamaModels.Concat(openaiModels)
            .Select(m => new AvailableModel
            {
                Name = m.Name,
                Provider = m.Provider,
                IsVisionCapable = m.HasCapability("vision"),
                IsInstalled = m.IsInstalled,
                Size = m.Size,
                Description = m.Description,
                Tags = m.Tags
            })
            .OrderBy(m => m.Name)
            .ToList();
    }

    public async Task<ModelConfiguration> GetCurrentConfigurationAsync()
    {
        return new ModelConfiguration
        {
            DefaultTextModel = await AI.Models.GetCurrent("text"),
            DefaultVisionModel = await AI.Models.GetCurrent("vision"),
            AvailableProviders = await AI.Providers.GetAvailable(),
            ActiveProvider = await AI.Providers.GetActive(),
            TextModelOptions = await GetTextModelOptionsAsync(),
            VisionModelOptions = await GetVisionModelOptionsAsync()
        };
    }

    public async Task<bool> SetCurrentTextModelAsync(string modelName, string? provider = null)
    {
        return await AI.Models.SetCurrent("text", modelName, provider);
    }

    public async Task<bool> SetCurrentVisionModelAsync(string modelName, string? provider = null)
    {
        return await AI.Models.SetCurrent("vision", modelName, provider);
    }

    public async Task<bool> InstallModelAsync(string modelName, string provider = "ollama")
    {
        // Dynamic model installation via provider-specific mechanisms
        return await AI.Models.Install(modelName, provider);
    }

    public async Task<Analysis> AnalyzeWithSelectedModel(File file, Type type, string? modelOverride = null)
    {
        var modelName = modelOverride ?? await DetermineOptimalModel(file, type);

        if (IsImageFile(file.ContentType))
        {
            return await AnalyzeImageContent(file, type, modelName);
        }
        else
        {
            return await AnalyzeTextContent(file, type, modelName);
        }
    }

    private async Task<Analysis> AnalyzeImageContent(File file, Type type, string modelName)
    {
        var base64Image = await GetBase64ImageAsync(file.StorageKey);

        var response = await AI.VisionPrompt($"""
            {type.ExtractionPrompt}

            Instructions: Analyze this image and extract structured information.
            Template: {type.TemplateStructure}
            """, base64Image)
            .WithModel(modelName)
            .WithMaxTokens(2000)
            .ExecuteAsync();

        return new Analysis
        {
            FileId = file.Id,
            TypeId = type.Id,
            ExtractedContext = response.Content,
            ModelUsed = response.Model,
            InputTokens = response.Usage?.InputTokens ?? 0,
            OutputTokens = response.Usage?.OutputTokens ?? 0,
            ProcessingDuration = response.ProcessingTime ?? TimeSpan.Zero
        };
    }

    private bool IsImageFile(string contentType) =>
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

public class AvailableModel
{
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public bool IsVisionCapable { get; set; }
    public bool IsInstalled { get; set; }
    public string Size { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Tags { get; set; } = new();
}

public class ModelConfiguration
{
    public string DefaultTextModel { get; set; } = "";
    public string DefaultVisionModel { get; set; } = "";
    public List<string> AvailableProviders { get; set; } = new();
    public string ActiveProvider { get; set; } = "";
    public List<AvailableModel> TextModelOptions { get; set; } = new();
    public List<AvailableModel> VisionModelOptions { get; set; } = new();
}
```

---

### **Enhanced AI Services (GDoc Feature Parity)**

#### **Enhanced File Analysis Service with Rich Extraction**
```csharp
public class EnhancedFileAnalysisService
{
    private readonly ILogger<EnhancedFileAnalysisService> _logger;
    private readonly DocumentTypeMatchingService _typeMatching;

    public EnhancedFileAnalysisService(
        ILogger<EnhancedFileAnalysisService> logger,
        DocumentTypeMatchingService typeMatching)
    {
        _logger = logger;
        _typeMatching = typeMatching;
    }

    // Enhanced analysis with rich structured extraction (matches GDoc capabilities)
    public async Task<ExtractedDocumentInformation> AnalyzeFileStructuredAsync(
        File file, Type type, CancellationToken ct = default)
    {
        var extractionPrompt = $"""
            Analyze the following document and extract comprehensive structured information:

            Document Type Context: {type.ExtractionPrompt}

            Document Content:
            {file.ExtractedText}

            Extract and structure the following information:
            1. ENTITIES: People, organizations, dates, locations, technical terms
            2. TOPICS: Main themes and subjects discussed
            3. DOCUMENT_TYPE: Inferred category (meeting, technical_spec, proposal, etc.)
            4. KEY_FACTS: Important decisions, action items, requirements with confidence
            5. STRUCTURED_DATA: Any structured information like dates, numbers, lists
            6. SUMMARY: Concise overview of document purpose and content

            Provide response in JSON format:
            {{
                "entities": {{ "people": [...], "organizations": [...], "dates": [...], "technical_terms": [...] }},
                "topics": [...],
                "inferredDocumentType": "category",
                "keyFacts": [
                    {{ "type": "decision|action_item|requirement|other", "content": "...", "confidence": 0.9, "context": "..." }}
                ],
                "structuredData": {{ "key": "value" }},
                "summary": "Document summary...",
                "confidenceScore": 0.85
            }}
            """;

        var response = await AI.Prompt(extractionPrompt)
            .WithModel("gpt-4-turbo")
            .WithMaxTokens(3000)
            .WithTemperature(0.1)
            .ExecuteAsync(ct);

        try
        {
            var extractedData = JsonSerializer.Deserialize<ExtractedDocumentInformation>(response.Content);
            if (extractedData != null)
            {
                extractedData.ExtractedAt = DateTime.UtcNow;
                extractedData.ModelUsed = response.Model ?? "gpt-4-turbo";

                _logger.LogInformation("Rich analysis completed for file {FileId}: {EntityCount} entities, {TopicCount} topics, {FactCount} facts",
                    file.Id, extractedData.Entities.Values.Sum(v => v.Count), extractedData.Topics.Count, extractedData.KeyFacts.Count);

                return extractedData;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse structured extraction response for file {FileId}, falling back to simple analysis", file.Id);
        }

        // Fallback to simpler analysis if structured parsing fails
        return await FallbackAnalysisAsync(file, type, response.Content, ct);
    }

    // Auto-suggest document types for uploaded files
    public async Task<List<TypeMatchResult>> SuggestDocumentTypesAsync(File file, CancellationToken ct = default)
    {
        return await _typeMatching.SuggestTypesAsync(file, maxSuggestions: 3);
    }

    private async Task<ExtractedDocumentInformation> FallbackAnalysisAsync(
        File file, Type type, string rawResponse, CancellationToken ct)
    {
        return new ExtractedDocumentInformation
        {
            Summary = rawResponse.Length > 500 ? rawResponse[..500] + "..." : rawResponse,
            InferredDocumentType = "unknown",
            ConfidenceScore = 0.5,
            ExtractedAt = DateTime.UtcNow,
            ModelUsed = "gpt-4-turbo"
        };
    }
}
```

#### **Document Type Matching Service**
```csharp
public class DocumentTypeMatchingService
{
    private readonly ILogger<DocumentTypeMatchingService> _logger;

    public DocumentTypeMatchingService(ILogger<DocumentTypeMatchingService> logger)
    {
        _logger = logger;
    }

    public async Task<List<TypeMatchResult>> SuggestTypesAsync(File file, int maxSuggestions = 3)
    {
        var content = file.ExtractedText;
        if (string.IsNullOrWhiteSpace(content)) return new List<TypeMatchResult>();

        // Get content embedding for semantic similarity
        var contentEmbedding = await AI.Embed(content).ExecuteAsync();

        // Get all enabled types for matching
        var types = await Type.Query(t => t.EnableAutoMatching).ToList();
        var matches = new List<TypeMatchResult>();

        foreach (var type in types)
        {
            var confidence = await CalculateTypeConfidenceAsync(content, contentEmbedding, type);

            if (confidence.Confidence >= type.ConfidenceThreshold)
            {
                matches.Add(confidence);
            }
        }

        return matches
            .OrderByDescending(m => m.Confidence)
            .Take(maxSuggestions)
            .ToList();
    }

    private async Task<TypeMatchResult> CalculateTypeConfidenceAsync(
        string content, double[] contentEmbedding, Type type)
    {
        // Semantic similarity using embeddings
        double semanticSimilarity = 0.0;
        if (type.TypeEmbedding != null && type.TypeEmbedding.Length > 0)
        {
            semanticSimilarity = CalculateCosineSimilarity(contentEmbedding, type.TypeEmbedding);
        }

        // Keyword matching
        var keywordMatches = GetMatchingKeywords(content, type.KeywordTriggers);
        double keywordSimilarity = keywordMatches.Count > 0 && type.KeywordTriggers.Count > 0
            ? (double)keywordMatches.Count / type.KeywordTriggers.Count
            : 0.0;

        // Weighted final confidence
        var finalConfidence = (semanticSimilarity * 0.7) + (keywordSimilarity * 0.3);

        return new TypeMatchResult
        {
            Type = type,
            Confidence = finalConfidence,
            SemanticSimilarity = semanticSimilarity,
            KeywordSimilarity = keywordSimilarity,
            KeywordMatches = keywordMatches,
            Reasoning = $"Semantic: {semanticSimilarity:P1}, Keywords: {keywordSimilarity:P1} ({keywordMatches.Count}/{type.KeywordTriggers.Count})"
        };
    }

    private List<string> GetMatchingKeywords(string content, List<string> keywords)
    {
        var contentLower = content.ToLowerInvariant();
        return keywords.Where(keyword =>
            contentLower.Contains(keyword.ToLowerInvariant())).ToList();
    }

    private static double CalculateCosineSimilarity(double[] vector1, double[] vector2)
    {
        if (vector1.Length != vector2.Length) return 0.0;

        var dotProduct = vector1.Zip(vector2, (a, b) => a * b).Sum();
        var magnitude1 = Math.Sqrt(vector1.Sum(x => x * x));
        var magnitude2 = Math.Sqrt(vector2.Sum(x => x * x));

        return magnitude1 * magnitude2 == 0 ? 0.0 : dotProduct / (magnitude1 * magnitude2);
    }
}
```

#### **Image Understanding Service**
```csharp
public class ImageUnderstandingService
{
    private readonly ILogger<ImageUnderstandingService> _logger;

    public ImageUnderstandingService(ILogger<ImageUnderstandingService> logger)
    {
        _logger = logger;
    }

    public async Task<DocumentImage> AnalyzeImageAsync(File file, CancellationToken ct = default)
    {
        if (!IsImageFile(file.ContentType))
        {
            throw new InvalidOperationException($"File {file.Id} is not an image file suitable for diagram analysis");
        }

        var base64Image = await GetBase64ImageAsync(file.StorageKey);

        var diagramPrompt = $"""
            Analyze this diagram/image and provide comprehensive understanding:

            Focus on extracting:
            1. SUMMARY: What this diagram represents and its purpose
            2. FLOW_STEPS: Sequential steps or process flow (if applicable)
            3. KEY_SERVICES: Components, services, or systems shown
            4. SECURITY_MECHANISMS: Security features, authentication, authorization
            5. RISKS: Potential vulnerabilities or risks visible in the design
            6. GRAPH: Structured representation of nodes, edges, and relationships

            Provide response in JSON format:
            {{
                "summary": "Diagram overview...",
                "flowSteps": ["Step 1", "Step 2", ...],
                "keyServices": [
                    {{ "name": "Service Name", "role": "Primary function", "interactions": "How it connects" }}
                ],
                "securityMechanisms": ["Auth mechanism", "Encryption", ...],
                "risks": ["Potential risk 1", "Risk 2", ...],
                "diagramGraph": {{
                    "nodes": [{{ "id": "node1", "label": "Label", "category": "service", "shape": "rectangle" }}],
                    "edges": [{{ "id": "edge1", "from": "node1", "to": "node2", "type": "flow", "label": "connection" }}],
                    "groups": [{{ "id": "group1", "label": "Boundary", "boundaryType": "system", "nodes": ["node1"] }}],
                    "notes": ["Additional observations"]
                }}
            }}
            """;

        var response = await AI.VisionPrompt(diagramPrompt, base64Image)
            .WithModel("gpt-4-vision-preview")
            .WithMaxTokens(4000)
            .WithTemperature(0.1)
            .ExecuteAsync(ct);

        var image = new DocumentImage
        {
            FileId = file.Id,
            RawLlmResponse = response.Content,
            AnalyzedAt = DateTime.UtcNow,
            ModelUsed = response.Model ?? "gpt-4-vision-preview",
            InputTokens = response.Usage?.InputTokens ?? 0,
            OutputTokens = response.Usage?.OutputTokens ?? 0,
            ProcessingDuration = response.ProcessingTime ?? TimeSpan.Zero
        };

        try
        {
            var analysisResult = JsonSerializer.Deserialize<DiagramAnalysisResponse>(response.Content);
            if (analysisResult != null)
            {
                diagram.Summary = analysisResult.Summary;
                diagram.FlowSteps = analysisResult.FlowSteps;
                diagram.KeyServices = analysisResult.KeyServices.Select(s =>
                    new KeyService(s.Name, s.Role, s.Interactions)).ToList();
                diagram.SecurityMechanisms = analysisResult.SecurityMechanisms;
                diagram.Risks = analysisResult.Risks;
                diagram.DiagramGraphJson = JsonSerializer.Serialize(analysisResult.DiagramGraph);
                diagram.ConfidenceScore = 0.85; // High confidence for structured response
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse diagram analysis response for file {FileId}, using raw response", file.Id);

            // Fallback: extract key information from raw response
            diagram.Summary = ExtractSummaryFromRawResponse(response.Content);
            diagram.ConfidenceScore = 0.6;
        }

        await diagram.Save(ct);

        _logger.LogInformation("Diagram analysis completed for file {FileId}: {Summary}",
            file.Id, diagram.Summary.Length > 100 ? diagram.Summary[..100] + "..." : diagram.Summary);

        return diagram;
    }

    private bool IsImageFile(string contentType) =>
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private async Task<string> GetBase64ImageAsync(string? storageKey)
    {
        // Implementation depends on storage provider
        // This is a placeholder - actual implementation would retrieve from storage
        return "base64_image_data_placeholder";
    }

    private string ExtractSummaryFromRawResponse(string rawResponse)
    {
        // Simple fallback extraction
        var lines = rawResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[0] : "Diagram analysis completed";
    }

    // Supporting classes for JSON deserialization
    private class DiagramAnalysisResponse
    {
        public string Summary { get; set; } = "";
        public List<string> FlowSteps { get; set; } = new();
        public List<ServiceInfo> KeyServices { get; set; } = new();
        public List<string> SecurityMechanisms { get; set; } = new();
        public List<string> Risks { get; set; } = new();
        public DiagramGraph DiagramGraph { get; set; } = new();
    }

    private class ServiceInfo
    {
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
        public string Interactions { get; set; } = "";
    }
}
```

#### **Document Chunking Service**
```csharp
public class DocumentChunkingService
{
    private readonly ILogger<DocumentChunkingService> _logger;
    private readonly EnhancedFileAnalysisService _analysisService;

    public DocumentChunkingService(
        ILogger<DocumentChunkingService> logger,
        EnhancedFileAnalysisService analysisService)
    {
        _logger = logger;
        _analysisService = analysisService;
    }

    public async Task<List<DocumentChunk>> ChunkDocumentAsync(File file, CancellationToken ct = default)
    {
        if (file.ExtractedText.Length <= file.MaxChunkSize)
        {
            _logger.LogInformation("File {FileId} ({Length} chars) does not require chunking",
                file.Id, file.ExtractedText.Length);
            return new List<DocumentChunk>(); // No chunking needed
        }

        _logger.LogInformation("Chunking file {FileId} ({Length} chars) into {ChunkSize} char chunks",
            file.Id, file.ExtractedText.Length, file.MaxChunkSize);

        var chunks = SplitIntoChunks(file.ExtractedText, (int)file.MaxChunkSize);
        var chunkEntities = new List<DocumentChunk>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = new DocumentChunk
            {
                FileId = file.Id,
                ChunkIndex = i,
                Content = chunks[i].Content,
                StartPosition = chunks[i].StartPosition,
                EndPosition = chunks[i].EndPosition
            };

            await chunk.Save(ct);
            chunkEntities.Add(chunk);
        }

        // Update file with chunking status
        file.IsChunked = true;
        file.ChunkCount = chunks.Count;
        await file.Save(ct);

        _logger.LogInformation("Created {ChunkCount} chunks for file {FileId}", chunks.Count, file.Id);
        return chunkEntities;
    }

    public async Task<List<DocumentChunk>> AnalyzeChunksAsync(File file, Type type, CancellationToken ct = default)
    {
        var chunks = await file.GetChunks();
        if (chunks.Count == 0) return chunks;

        _logger.LogInformation("Analyzing {ChunkCount} chunks for file {FileId}", chunks.Count, file.Id);

        foreach (var chunk in chunks)
        {
            if (!string.IsNullOrEmpty(chunk.ChunkAnalysis)) continue; // Already analyzed

            try
            {
                // Create temporary File object for chunk analysis
                var chunkFile = new File
                {
                    ExtractedText = chunk.Content,
                    ContentType = file.ContentType,
                    FileName = $"{file.FileName}_chunk_{chunk.ChunkIndex}"
                };

                var extractedInfo = await _analysisService.AnalyzeFileStructuredAsync(chunkFile, type, ct);

                chunk.ExtractedInformation = extractedInfo;
                chunk.ConfidenceScore = extractedInfo.ConfidenceScore;
                chunk.ChunkAnalysis = extractedInfo.Summary;
                chunk.AnalyzedAt = DateTime.UtcNow;
                chunk.ModelUsed = extractedInfo.ModelUsed;

                await chunk.Save(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze chunk {ChunkIndex} for file {FileId}", chunk.ChunkIndex, file.Id);
            }
        }

        return chunks;
    }

    private List<ChunkInfo> SplitIntoChunks(string text, int maxChunkSize, int overlap = 500)
    {
        var chunks = new List<ChunkInfo>();
        int position = 0;

        while (position < text.Length)
        {
            int chunkEnd = Math.Min(position + maxChunkSize, text.Length);

            // Try to break at word boundaries
            if (chunkEnd < text.Length)
            {
                int lastSpace = text.LastIndexOf(' ', chunkEnd, Math.Min(200, chunkEnd - position));
                if (lastSpace > position) chunkEnd = lastSpace;
            }

            chunks.Add(new ChunkInfo
            {
                Content = text[position..chunkEnd],
                StartPosition = position,
                EndPosition = chunkEnd
            });

            // Move position with overlap
            position = chunkEnd - overlap;
            if (position >= chunkEnd) break; // Prevent infinite loop
        }

        return chunks;
    }

    private record ChunkInfo(string Content, int StartPosition, int EndPosition);
}
```

### **3. User-Driven Processing Pipeline**
