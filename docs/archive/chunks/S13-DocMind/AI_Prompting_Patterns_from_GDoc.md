# AI Prompting Patterns for S13.DocMind
**Source**: Harvested from `references/GDoc` implementation
**Purpose**: Implementation examples for automated document type creation and analysis queries

---

## Executive Summary

The GDoc reference implementation provides sophisticated AI prompting patterns for:
- **Automated Document Type Creation**: Generate document type configurations from user intent
- **Template-Based Document Analysis**: Fill structured templates using multi-document analysis
- **Structured Information Extraction**: Parse documents with confidence scoring and citations
- **Multi-Document Synthesis**: Consolidate information across multiple source documents

These patterns can be directly integrated into S13.DocMind's `SemanticTypeProfile` and `InsightSynthesisService` systems.

---

## 1. Document Type Auto-Generation Pattern

### 1.1 System Prompt Structure
```csharp
// From: GDoc.Api/Services/LlmService.cs:GenerateDocumentTypeAsync()
private const string DocTypeJsonStart = "---DOCUMENT_TYPE_JSON_START---";
private const string DocTypeJsonEnd = "---DOCUMENT_TYPE_JSON_END---";

var systemPrompt = string.Join('\n', new []
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
    "10. Prefer minimal essential sections in Template."
});
```

### 1.2 Schema Example Pattern
```json
{
  "Name": "Feature Request Evaluation",
  "Code": "FREQ",
  "Description": "Evaluates feature requests for feasibility and impact.",
  "Instructions": "Analyze the request; populate each section; cite if sources available.",
  "Template": "# Feature Request Evaluation\n\n## Summary\n{{SUMMARY}}\n\n## Business Impact\n{{BUSINESS_IMPACT}}\n\n## Technical Considerations\n{{TECH_CONSIDERATIONS}}\n\n## Risks\n{{RISKS}}\n\n## Recommendation\n{{RECOMMENDATION}}",
  "Tags": ["feature", "evaluation", "product"]
}
```

### 1.3 S13.DocMind Integration Pattern
```csharp
// Implement in SemanticTypeProfileService
public async Task<SemanticTypeProfile> GenerateProfileFromPromptAsync(string userPrompt)
{
    var systemPrompt = BuildDocumentTypeGenerationPrompt();
    var userRequest = $"User intent: {userPrompt}\nProvide a new, purpose-appropriate configuration.";

    var response = await _aiService.GenerateAsync(systemPrompt, userRequest);
    var json = ExtractDelimitedJson(response, DocTypeJsonStart, DocTypeJsonEnd);

    var profileData = JsonSerializer.Deserialize<DocumentTypeData>(json);
    return MapToSemanticTypeProfile(profileData);
}
```

---

## 2. Multi-Document Analysis Pattern

### 2.1 Consolidated Document Analysis Structure
```csharp
// From: GDoc debug request logs - Template Fill Pattern
public class ConsolidatedAnalysis
{
    public int TotalDocuments { get; set; }
    public List<DocumentSummary> Documents { get; set; } = new();
    public List<string> ConsolidatedTopics { get; set; } = new();
    public Dictionary<string, List<string>> ConsolidatedEntities { get; set; } = new();
    public List<KeyFact> ConsolidatedKeyFacts { get; set; } = new();
}

public class DocumentSummary
{
    public string FileName { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public double Confidence { get; set; }
    public string Summary { get; set; } = "";
    public List<string> Topics { get; set; } = new();
}

public class KeyFact
{
    public string Fact { get; set; } = "";
    public string Source { get; set; } = "";
    public double Confidence { get; set; }
}
```

### 2.2 Analysis Prompt Template
```csharp
// Template Fill Prompt Pattern (from GDoc debug logs)
var analysisPrompt = @"
=== CONTEXT ===
=== CONSOLIDATED DOCUMENT ANALYSIS ===
Total Documents: {documentCount}

{documentSummaries}

=== CONSOLIDATED TOPICS ===
{consolidatedTopics}

=== CONSOLIDATED ENTITIES ===
{consolidatedEntities}

=== CONSOLIDATED KEY FACTS ===
{consolidatedKeyFacts}

=== INSTRUCTIONS ===
Your task is to gather, verify, and populate all required fields in the provided template.

IMPORTANT CONTEXT:
- You are working with PRE-EXTRACTED and STRUCTURED information from multiple documents
- The raw content has already been analyzed by a previous AI step
- Focus on synthesizing and formatting this structured information; do NOT re-analyze raw text
- Use consolidated sections (topics, entities, key facts) to understand relationships
- Maintain fidelity to extracted information while creating a cohesive narrative

STRUCTURED DATA USAGE:
- Document summaries provide high-level overviews of each source
- Consolidated topics show themes across all documents
- Consolidated entities list all people, organizations, locations mentioned
- Consolidated key facts contain important details with confidence scores
- Document references (e.g., 'from DOC_01') indicate information sources

=== TEMPLATE MARKUP ===
{templateContent}
";
```

### 2.3 S13.DocMind Integration Pattern
```csharp
// Integrate into InsightSynthesisService
public async Task<StructuredInsightResult> SynthesizeFromMultipleDocumentsAsync(
    List<SourceDocument> documents,
    SemanticTypeProfile profile)
{
    // Step 1: Build consolidated analysis
    var consolidatedAnalysis = await BuildConsolidatedAnalysisAsync(documents);

    // Step 2: Generate analysis prompt
    var prompt = BuildAnalysisPrompt(consolidatedAnalysis, profile.Prompt.UserTemplate);

    // Step 3: Execute AI analysis
    var response = await _aiService.AnalyzeAsync(prompt);

    // Step 4: Extract structured insights
    return ParseStructuredInsights(response, profile.ExtractionSchema);
}
```

---

## 3. Lean Prompt Building Pattern

### 3.1 Structured Prompt Architecture
```csharp
// From: GDoc.Api/Services/DocumentProcessing/PromptBuilder.cs
public class LeanPromptBuilder
{
    public string BuildAnalysisPrompt(ValidatedDocumentRequest request)
    {
        var sb = new StringBuilder();

        // 1. System directive
        sb.AppendLine("SYSTEM");
        sb.AppendLine("fill the template using ALL documents. cite sources as DOC_## (e.g. DOC_01). if notes exist they override conflicting document content. if conflict: mention both and prefer notes. do not invent content. use 'UNKNOWN' for missing required info. keep answers concise.");
        sb.AppendLine();

        // 2. Meta information (machine-readable contract)
        sb.AppendLine("META");
        sb.AppendLine($"docs: {docNames.Count}");
        sb.AppendLine("citation_format: DOC_##");
        sb.AppendLine("delimiters: FILLED_DOCUMENT_TYPE, CONTEXT_UNDERSTANDING");
        sb.AppendLine("unknown_token: UNKNOWN");
        sb.AppendLine("required_blocks: filled_document_type, context_understanding");
        sb.AppendLine();

        // 3. Contextual inputs
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            sb.AppendLine("NOTES");
            sb.AppendLine(request.Notes.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("INSTRUCTIONS");
        sb.AppendLine(request.Instructions.Trim());
        sb.AppendLine();

        sb.AppendLine("TEMPLATE");
        sb.AppendLine(request.Template.Trim());
        sb.AppendLine();

        // 4. Document listing with citation mapping
        sb.AppendLine("DOCUMENTS");
        sb.AppendLine("List (index -> filename):");
        for (int i = 0; i < docNames.Count; i++)
        {
            sb.AppendLine($"DOC_{(i+1).ToString("D2")} | {docNames[i]}");
        }
        sb.AppendLine();
        sb.AppendLine("Full content follows (same order; cite using DOC_##):");
        sb.AppendLine(request.DocumentText.Trim());
        sb.AppendLine();

        // 5. Output requirements with delimiters
        sb.AppendLine("OUTPUT REQUIREMENT");
        sb.AppendLine("Return ONLY these blocks in order, no extra commentary:");
        sb.AppendLine("---FILLED_DOCUMENT_TYPE_START---");
        sb.AppendLine("(filled template with placeholders replaced, include source DOC_## citations inline where relevant)");
        sb.AppendLine("---FILLED_DOCUMENT_TYPE_END---");
        sb.AppendLine("---CONTEXT_UNDERSTANDING_START---");
        sb.AppendLine("(2-3 sentence synthesis: documents count, major sources used, conflicts handled)");
        sb.AppendLine("---CONTEXT_UNDERSTANDING_END---");
        sb.AppendLine();

        // 6. Enforcement rules
        sb.AppendLine("RULES");
        sb.AppendLine("use all docs; cite DOC_##; prefer notes on conflict; acknowledge conflicts; no hallucinations; use UNKNOWN if info absent; no extra sections; do not restate instructions");

        return sb.ToString();
    }
}
```

### 3.2 S13.DocMind Prompt Builder Integration
```csharp
// Implement in DocumentAnalysisService
public class DocumentAnalysisPromptBuilder
{
    public string BuildInsightExtractionPrompt(
        List<DocumentExtractionResult> documents,
        SemanticTypeProfile profile,
        string? userNotes = null)
    {
        var sb = new StringBuilder();

        // System directive
        sb.AppendLine("SYSTEM");
        sb.AppendLine($"Analyze documents and extract structured insights according to the {profile.Name} profile. Use provided schema for structured data extraction. Cite sources as DOC_##. Use confidence scores (0.0-1.0) for extracted information.");
        sb.AppendLine();

        // Profile information
        sb.AppendLine("ANALYSIS PROFILE");
        sb.AppendLine($"Name: {profile.Name}");
        sb.AppendLine($"Description: {profile.Description}");
        sb.AppendLine($"Instructions: {profile.Prompt.SystemPrompt}");
        sb.AppendLine();

        // Schema definition
        sb.AppendLine("EXTRACTION SCHEMA");
        sb.AppendLine(JsonSerializer.Serialize(profile.ExtractionSchema.Fields,
            new JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine();

        // Document content
        sb.AppendLine("DOCUMENTS");
        for (int i = 0; i < documents.Count; i++)
        {
            sb.AppendLine($"DOC_{(i+1).ToString("D2")} | {documents[i].WordCount} words | {documents[i].PageCount} pages");
            sb.AppendLine(documents[i].Text);
            sb.AppendLine();
        }

        // Output format
        sb.AppendLine("OUTPUT FORMAT");
        sb.AppendLine("Return JSON with extracted insights, confidence scores, and source citations:");
        sb.AppendLine("{ \"insights\": [...], \"metadata\": {...}, \"confidence\": 0.85 }");

        return sb.ToString();
    }
}
```

---

## 4. Template Examples from GDoc Seed Data

### 4.1 Technical Specification Template
```markdown
# Technical Specification

## Project Overview
**Project Name:** {{PROJECT_NAME}}
**Version:** {{VERSION}}
**Date:** {{DATE}}
**Author(s):** {{AUTHORS}}
**Stakeholders:** {{STAKEHOLDERS}}

## Executive Summary
{{EXECUTIVE_SUMMARY}}

## Requirements
**Functional Requirements:**
{{FUNCTIONAL_REQUIREMENTS}}

**Non-Functional Requirements:**
{{NON_FUNCTIONAL_REQUIREMENTS}}

**Business Requirements:**
{{BUSINESS_REQUIREMENTS}}

## System Architecture
**High-Level Architecture:**
{{HIGH_LEVEL_ARCHITECTURE}}

**Component Architecture:**
{{COMPONENT_ARCHITECTURE}}

**Data Architecture:**
{{DATA_ARCHITECTURE}}

## Technology Stack
**Frontend Technologies:** {{FRONTEND_TECH}}
**Backend Technologies:** {{BACKEND_TECH}}
**Database Technologies:** {{DATABASE_TECH}}
**Infrastructure:** {{INFRASTRUCTURE}}
**Third-party Integrations:** {{THIRD_PARTY_INTEGRATIONS}}

## API Specifications
{{API_SPECIFICATIONS}}

## Data Models
{{DATA_MODELS}}

## Security Considerations
{{SECURITY_CONSIDERATIONS}}

## Performance Requirements
{{PERFORMANCE_REQUIREMENTS}}

## Implementation Plan
**Development Phases:** {{DEVELOPMENT_PHASES}}
**Timeline:** {{TIMELINE}}
**Resource Requirements:** {{RESOURCE_REQUIREMENTS}}

## Testing Strategy
{{TESTING_STRATEGY}}

## Deployment Strategy
{{DEPLOYMENT_STRATEGY}}

## Risk Assessment
{{RISK_ASSESSMENT}}

## Assumptions and Dependencies
{{ASSUMPTIONS_DEPENDENCIES}}
```

### 4.2 Meeting Summary Template
```markdown
# Meeting Summary

## Meeting Details
**Meeting Title:** {{MEETING_TITLE}}
**Date:** {{MEETING_DATE}}
**Time:** {{MEETING_TIME}}
**Duration:** {{DURATION}}
**Location/Platform:** {{LOCATION}}
**Meeting Type:** {{MEETING_TYPE}}

## Attendees
**Present:**
{{ATTENDEES_PRESENT}}

**Absent:**
{{ATTENDEES_ABSENT}}

**Meeting Lead:** {{MEETING_LEAD}}

## Agenda Items
{{AGENDA_ITEMS}}

## Key Discussion Points
{{DISCUSSION_POINTS}}

## Decisions Made
{{DECISIONS_MADE}}

## Action Items
{{ACTION_ITEMS}}

## Issues/Risks Raised
{{ISSUES_RISKS}}

## Next Steps
{{NEXT_STEPS}}

## Next Meeting
**Date:** {{NEXT_MEETING_DATE}}
**Purpose:** {{NEXT_MEETING_PURPOSE}}

## Additional Notes
{{ADDITIONAL_NOTES}}
```

### 4.3 Risk Assessment Template
```markdown
# Project Risk Assessment

## Project Information
**Project Name:** {{PROJECT_NAME}}
**Assessment Date:** {{ASSESSMENT_DATE}}
**Risk Assessor:** {{RISK_ASSESSOR}}
**Project Manager:** {{PROJECT_MANAGER}}
**Assessment Period:** {{ASSESSMENT_PERIOD}}

## Executive Summary
{{EXECUTIVE_SUMMARY}}

## Project Scope and Context
**Project Objectives:** {{PROJECT_OBJECTIVES}}
**Key Deliverables:** {{KEY_DELIVERABLES}}
**Project Timeline:** {{PROJECT_TIMELINE}}
**Budget:** {{PROJECT_BUDGET}}
**Key Stakeholders:** {{KEY_STAKEHOLDERS}}

## Identified Risks

### High Priority Risks
{{HIGH_PRIORITY_RISKS}}

### Medium Priority Risks
{{MEDIUM_PRIORITY_RISKS}}

### Low Priority Risks
{{LOW_PRIORITY_RISKS}}

## Risk Categories Analysis

### Technical Risks
{{TECHNICAL_RISKS}}

### Business Risks
{{BUSINESS_RISKS}}

### Operational Risks
{{OPERATIONAL_RISKS}}

### External Risks
{{EXTERNAL_RISKS}}

## Mitigation Strategies

### Immediate Actions Required
{{IMMEDIATE_ACTIONS}}

### Short-term Mitigation Plans
{{SHORT_TERM_MITIGATION}}

### Long-term Risk Management
{{LONG_TERM_MITIGATION}}

## Risk Ownership and Accountability
{{RISK_OWNERSHIP}}

## Contingency Plans
{{CONTINGENCY_PLANS}}

## Monitoring and Review
**Risk Monitoring Process:** {{MONITORING_PROCESS}}
**Review Frequency:** {{REVIEW_FREQUENCY}}
**Escalation Procedures:** {{ESCALATION_PROCEDURES}}
**Key Risk Indicators:** {{KEY_RISK_INDICATORS}}

## Recommendations
{{RECOMMENDATIONS}}

## Next Steps
{{NEXT_STEPS}}
```

---

## 5. Implementation Roadmap for S13.DocMind

### 5.1 Phase 1: Document Type Auto-Generation
- **Target**: `SemanticTypeProfileService`
- **Implementation**: Add `GenerateProfileFromPromptAsync()` method
- **Prompt Pattern**: Use GDoc's structured JSON generation approach
- **Output**: `SemanticTypeProfile` with generated templates and extraction schemas

### 5.2 Phase 2: Multi-Document Analysis Enhancement
- **Target**: `InsightSynthesisService`
- **Implementation**: Enhance with consolidated analysis patterns
- **Prompt Pattern**: Use GDoc's multi-document consolidation approach
- **Output**: Enhanced insight extraction with cross-document synthesis

### 5.3 Phase 3: Template-Based Analysis
- **Target**: New `TemplateAnalysisService`
- **Implementation**: Implement GDoc's template filling patterns
- **Prompt Pattern**: Use structured prompt building with delimiters
- **Output**: Template-driven document analysis with confidence scoring

### 5.4 Phase 4: Advanced Prompt Engineering
- **Target**: `DocumentAnalysisPromptBuilder`
- **Implementation**: Systematic prompt construction with metadata
- **Prompt Pattern**: Use GDoc's lean prompt architecture
- **Output**: Consistent, high-quality AI prompts for all document analysis tasks

---

## 6. Key Implementation Notes

### 6.1 Delimiter Strategy
- Use clear start/end delimiters for structured output parsing
- Examples: `---DOCUMENT_TYPE_JSON_START---`, `---FILLED_DOCUMENT_TYPE_START---`
- Enables reliable extraction from LLM responses

### 6.2 Citation Strategy
- Use `DOC_##` format for multi-document citations
- Maintain source traceability throughout analysis pipeline
- Essential for audit trails and confidence assessment

### 6.3 Confidence Scoring
- Implement confidence scores (0.0-1.0) for all extracted information
- Use confidence thresholds for automated decision making
- Essential for production document intelligence systems

### 6.4 Error Handling
- Implement fallback extraction methods for malformed AI responses
- Use retry with "STRICT MODE" prompts for improved output quality
- Provide meaningful defaults when AI extraction fails

---

## 7. Conclusion

The GDoc reference implementation provides battle-tested AI prompting patterns that can significantly enhance S13.DocMind's document intelligence capabilities. The structured approach to prompt engineering, multi-document analysis, and template-based extraction offers a clear roadmap for implementing sophisticated document analysis features.

**Next Steps:**
1. Implement document type auto-generation using the harvested prompt patterns
2. Enhance multi-document analysis with consolidated synthesis approaches
3. Add template-based document analysis capabilities
4. Establish systematic prompt engineering practices across all AI interactions

These patterns represent proven approaches for production document intelligence systems and should be prioritized for implementation in S13.DocMind.