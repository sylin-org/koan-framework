using System;
using System.Collections.Generic;
using Koan.Samples.Meridian.Models;

namespace Koan.Samples.Meridian.SeedData;

/// <summary>
/// Seed data for DocumentStyle classifications.
/// Defines standard document styles with detection hints for LLM-based classification.
/// </summary>
public static class DocumentStyleSeedData
{
    public static List<DocumentStyle> GetDocumentStyles()
    {
        return new List<DocumentStyle>
        {
            CreateNarrative(),
            CreateSparseForm(),
            CreateDenseForm(),
            CreateTechnicalSpec(),
            CreateDialogue(),
            CreateMixed()
        };
    }

    private static DocumentStyle CreateNarrative()
    {
        return new DocumentStyle
        {
            Id = "019a2200-0000-7000-a000-000000000001",
            Name = "Narrative",
            Code = "NARRATIVE",
            Description = "Flowing prose, paragraphs, complete sentences. Architecture memos, reports, essays, analysis documents.",
            Version = 1,
            Tags = new List<string>
            {
                "prose",
                "narrative",
                "report",
                "memo",
                "essay",
                "analysis"
            },
            DetectionHints = new List<string>
            {
                "Multiple paragraphs of continuous text",
                "Complete sentences forming coherent thoughts",
                "Flowing narrative structure",
                "Section headers with explanatory content",
                "Minimal forms, tables, or Q&A patterns",
                "Author presenting information or analysis"
            },
            SignalPhrases = new List<string>
            {
                "Executive Summary",
                "Background",
                "Introduction",
                "Analysis",
                "Conclusion",
                "Recommendations"
            },
            ExtractionStrategy = @"NARRATIVE DOCUMENT EXTRACTION:
- Full document context is valuable
- Facts embedded within flowing prose
- Use standard extraction with complete text
- Pay attention to conclusions and summary sections
- Author intent and reasoning are important context",
            UsePassageRetrieval = false,
            PassageRetrievalTopK = 0,
            ExpandPassageContext = false,
            ContextWindowSize = 0
        };
    }

    private static DocumentStyle CreateSparseForm()
    {
        return new DocumentStyle
        {
            Id = "019a2200-0000-7000-a000-000000000002",
            Name = "Sparse Form",
            Code = "SPARSE",
            Description = "Template or form with mostly empty fields, checkbox responses, or minimal filled content. High noise-to-signal ratio. Vendor questionnaires and prescreen forms.",
            Version = 2,
            Tags = new List<string>
            {
                "form",
                "questionnaire",
                "template",
                "sparse",
                "checkbox",
                "empty-fields",
                "vendor-form",
                "prescreen"
            },
            DetectionHints = new List<string>
            {
                "Repetitive 'Question X:' or 'Field:' patterns",
                "Multiple choice options listed (A/B/C or checkboxes)",
                "Many empty fields or 'N/A' responses",
                "Template boilerplate text with minimal filled content",
                "Instructional text about how to fill the form",
                "More question templates than actual answers",
                "Vendor questionnaire or prescreen form structure",
                "Form metadata like 'Version' dates or form IDs",
                "Empty contact information fields or placeholder text",
                "Ratio of instructions to actual content is high"
            },
            SignalPhrases = new List<string>
            {
                "Question",
                "Field:",
                "Select one:",
                "Check all that apply:",
                "Options include:",
                "Please complete",
                "Required field",
                "N/A",
                "Not applicable",
                "Vendor Prescreen",
                "Vendor Questionnaire",
                "Version 20",
                "designed to help identify",
                "designed to determine",
                "Request Type options"
            },
            ExtractionStrategy = @"SPARSE FORM EXTRACTION:
CRITICAL - High noise, low signal:
1. Use RAG passage retrieval to filter out template boilerplate
2. ONLY extract actual filled answers, NOT question templates or options
3. Distinguish between form questions and filled responses
4. If text contains only templates with no answers, return null
5. Ignore meta-descriptions like 'designed to determine...'

EXAMPLES TO AVOID:
❌ 'Request Type options include: A, B, C' (this is a QUESTION)
❌ 'designed to help identify...' (this is META-DESCRIPTION)

EXAMPLES TO EXTRACT:
✓ 'Selected option: Vendor-hosted/Cloud' (this is an ANSWER)
✓ 'TLS v1.1 enabled' (this is a FACT)",
            UsePassageRetrieval = true,
            PassageRetrievalTopK = 5,
            ExpandPassageContext = false,
            ContextWindowSize = 0
        };
    }

    private static DocumentStyle CreateDenseForm()
    {
        return new DocumentStyle
        {
            Id = "019a2200-0000-7000-a000-000000000003",
            Name = "Dense Form",
            Code = "DENSE",
            Description = "Form with most fields filled with detailed, substantial answers. Structured but information-rich. High signal-to-noise ratio.",
            Version = 2,
            Tags = new List<string>
            {
                "form",
                "filled",
                "structured",
                "dense",
                "complete",
                "detailed-responses"
            },
            DetectionHints = new List<string>
            {
                "Form structure with field labels AND detailed filled responses",
                "Most fields have substantial multi-sentence answers (not just checkboxes)",
                "Structured Q&A format where answers are longer than questions",
                "Minimal empty fields or 'N/A' responses (under 20% empty)",
                "Rich information density - more content than template",
                "Actual data values and descriptions, not option lists",
                "Paragraphs of explanation in response fields"
            },
            SignalPhrases = new List<string>
            {
                "Response:",
                "Answer:",
                "Details:",
                "Filled by:",
                "Completed on:"
            },
            ExtractionStrategy = @"DENSE FORM EXTRACTION:
- Leverage structured format for systematic extraction
- Field labels help identify fact types
- Standard extraction works well
- Focus on answer sections, not question labels
- High signal-to-noise ratio - full context acceptable",
            UsePassageRetrieval = false,
            PassageRetrievalTopK = 0,
            ExpandPassageContext = false,
            ContextWindowSize = 0
        };
    }

    private static DocumentStyle CreateTechnicalSpec()
    {
        return new DocumentStyle
        {
            Id = "019a2200-0000-7000-a000-000000000004",
            Name = "Technical Specification",
            Code = "TECHSPEC",
            Description = "Tables, diagrams, structured technical data, specifications, architecture diagrams.",
            Version = 1,
            Tags = new List<string>
            {
                "technical",
                "specification",
                "tables",
                "diagrams",
                "structured-data"
            },
            DetectionHints = new List<string>
            {
                "Multiple tables with rows and columns",
                "Technical terminology and jargon",
                "Specifications, measurements, or metrics",
                "System architecture descriptions",
                "Version numbers and technical identifiers",
                "Minimal narrative prose"
            },
            SignalPhrases = new List<string>
            {
                "Specification:",
                "Version:",
                "Component:",
                "Protocol:",
                "Configuration:",
                "Architecture:",
                "System:",
                "Technical Requirements:"
            },
            ExtractionStrategy = @"TECHNICAL SPEC EXTRACTION:
- Tables contain concentrated information
- Pay attention to version numbers and identifiers
- Technical terms are precise - extract verbatim
- System relationships and dependencies matter
- Standard extraction with full context works well",
            UsePassageRetrieval = false,
            PassageRetrievalTopK = 0,
            ExpandPassageContext = false,
            ContextWindowSize = 0
        };
    }

    private static DocumentStyle CreateDialogue()
    {
        return new DocumentStyle
        {
            Id = "019a2200-0000-7000-a000-000000000005",
            Name = "Dialogue",
            Code = "DIALOGUE",
            Description = "Emails, meeting transcripts, chat logs, correspondence. Multi-party communication with temporal flow.",
            Version = 1,
            Tags = new List<string>
            {
                "email",
                "transcript",
                "meeting",
                "chat",
                "dialogue",
                "conversation",
                "correspondence"
            },
            DetectionHints = new List<string>
            {
                "Email headers (From:, To:, Subject:, Date:)",
                "Speaker labels or attributions (John:, Mary said:)",
                "Reply chains (Re:, Fwd:)",
                "Timestamps within document",
                "Meeting attendee lists",
                "Back-and-forth conversation structure",
                "Multiple voices or perspectives"
            },
            SignalPhrases = new List<string>
            {
                "From:",
                "To:",
                "Subject:",
                "Date:",
                "Re:",
                "Fwd:",
                "Attendees:",
                "Meeting Notes:",
                "Transcript:",
                "said:",
                "replied:",
                "wrote:"
            },
            ExtractionStrategy = @"DIALOGUE EXTRACTION:
CRITICAL - Temporal and contextual awareness:
1. Use RAG passage retrieval to find relevant discussion
2. Expand retrieved passages with surrounding context (conversation window)
3. Track WHO said WHAT - attribution matters
4. LATER statements override EARLIER ones if contradictory
5. Vague references require previous message context
6. Chronological order must be preserved

EXAMPLES:
✓ Include context: 'Per John's earlier email about TLS, we'll disable v1.1'
✓ Track decisions: Mary said 'Approved' (overrides earlier 'Under review')
❌ Don't extract without context: 'Yes, let's do that' (what is 'that'?)",
            UsePassageRetrieval = true,
            PassageRetrievalTopK = 3,
            ExpandPassageContext = true,
            ContextWindowSize = 2
        };
    }

    private static DocumentStyle CreateMixed()
    {
        return new DocumentStyle
        {
            Id = "019a2200-0000-7000-a000-000000000006",
            Name = "Mixed",
            Code = "MIXED",
            Description = "Combination of multiple styles. Email thread with attached form, narrative with embedded tables, etc.",
            Version = 1,
            Tags = new List<string>
            {
                "mixed",
                "hybrid",
                "multi-style",
                "composite"
            },
            DetectionHints = new List<string>
            {
                "Contains multiple distinct sections with different formats",
                "Email body with attached document content",
                "Narrative sections interspersed with tables or forms",
                "Multiple document types combined",
                "Varied structure throughout"
            },
            SignalPhrases = new List<string>
            {
                "Attached:",
                "See below:",
                "Forwarded message:",
                "Appendix:",
                "Section:",
                "Part"
            },
            ExtractionStrategy = @"MIXED DOCUMENT EXTRACTION:
- Analyze which style dominates each section
- Use RAG to isolate relevant portions
- Apply appropriate extraction strategy per section
- Be flexible with context needs
- Preserve relationships between different parts",
            UsePassageRetrieval = true,
            PassageRetrievalTopK = 5,
            ExpandPassageContext = false,
            ContextWindowSize = 1
        };
    }
}
