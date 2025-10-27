using Koan.Samples.Meridian.Models;

namespace Koan.Samples.Meridian.SeedData;

/// <summary>
/// Static seed data for SourceType templates.
/// These are default source document types with field extraction schemas and instructions.
/// </summary>
public static class SourceTypeSeedData
{
    public static List<SourceType> GetSourceTypes()
    {
        return new List<SourceType>
        {
            CreateMeetingSummary(),
            CreateInvoice(),
            CreateContract(),
            CreateTechnicalReport(),
            CreateEmail(),
            CreateNonDocument(),
            CreateUnspecified()
        };
    }

    private static SourceType CreateMeetingSummary()
    {
        return new SourceType
        {
            Id = Guid.Parse("db793db7-4812-4212-b8c2-a222720e82d6").ToString(),
            Name = "Meeting Summary/Notes",
            Code = "MEET",
            Description = "Comprehensive template for documenting meeting discussions, decisions, and action items",
            Version = 1,
            Tags = new List<string>
            {
                "meeting-summary",
                "meeting-notes",
                "decisions",
                "action-items",
                "discussions",
                "agenda",
                "attendees",
                "collaboration"
            },
            DescriptorHints = new List<string>
            {
                "minutes",
                "agenda",
                "attendees",
                "action items",
                "decisions",
                "meeting notes"
            },
            SignalPhrases = new List<string>
            {
                "meeting minutes",
                "attendees:",
                "action items:",
                "decisions made:",
                "next meeting",
                "agenda items"
            },
            SupportsManualSelection = true,
            MimeTypes = new List<string>
            {
                "application/pdf",
                "text/plain",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/msword"
            },
            Instructions = @"You are creating a comprehensive meeting summary from the provided meeting notes, recordings, or related documents.

INSTRUCTIONS:
1. Extract meeting administrative details (title, date, time, location, attendees)
2. Identify the meeting lead/facilitator and agenda items discussed
3. Summarize key discussion points and conversations
4. Document all decisions that were made during the meeting
5. List specific action items with owners and due dates where mentioned
6. Note any issues, risks, or concerns that were raised
7. Capture next steps and future meeting plans

IMPORTANT: Replace each {{PLACEHOLDER}} with specific information found in the meeting documents. If information is not available, write ""Not specified in meeting notes"" for that field.

Focus on accuracy and completeness of the meeting record.",
            OutputTemplate = @"# Meeting Summary

---

## Meeting Details
**Meeting Title:** {{MEETING_TITLE}}
**Date:** {{MEETING_DATE}}
**Time:** {{MEETING_TIME}}
**Duration:** {{DURATION}}
**Location/Platform:** {{LOCATION}}
**Meeting Type:** {{MEETING_TYPE}}

---

## Attendees
**Present:**
{{ATTENDEES_PRESENT}}

**Absent:**
{{ATTENDEES_ABSENT}}

**Meeting Lead:** {{MEETING_LEAD}}

---

## Agenda Items
{{AGENDA_ITEMS}}

---

## Key Discussion Points
{{DISCUSSION_POINTS}}

---

## Decisions Made
{{DECISIONS_MADE}}

---

## Action Items
{{ACTION_ITEMS}}

---

## Issues/Risks Raised
{{ISSUES_RISKS}}

---

## Next Steps
{{NEXT_STEPS}}

---

## Next Meeting
**Date:** {{NEXT_MEETING_DATE}}
**Purpose:** {{NEXT_MEETING_PURPOSE}}

---

## Additional Notes
{{ADDITIONAL_NOTES}}",
            FieldQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["meetingTitle"] = "What is the title or subject of this meeting?",
                ["meetingDate"] = "What is the date of this meeting?",
                ["meetingTime"] = "What time did the meeting start?",
                ["duration"] = "How long was the meeting?",
                ["location"] = "Where was the meeting held (physical location or virtual platform)?",
                ["meetingType"] = "What type of meeting was this (e.g., team sync, board meeting, planning session)?",
                ["attendeesPresent"] = "Who attended this meeting?",
                ["attendeesAbsent"] = "Who was invited but did not attend?",
                ["meetingLead"] = "Who led or facilitated this meeting?",
                ["agendaItems"] = "What were the agenda items or topics discussed?",
                ["discussionPoints"] = "What were the key points discussed during the meeting?",
                ["decisionsMade"] = "What decisions were made during this meeting?",
                ["actionItems"] = "What action items were assigned, to whom, and with what due dates?",
                ["issuesRisks"] = "What issues, risks, or concerns were raised?",
                ["nextSteps"] = "What are the next steps following this meeting?",
                ["nextMeetingDate"] = "When is the next meeting scheduled?",
                ["nextMeetingPurpose"] = "What is the purpose of the next meeting?",
                ["additionalNotes"] = "Are there any other important notes or context?"
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static SourceType CreateInvoice()
    {
        return new SourceType
        {
            Id = Guid.Parse("a1b2c3d4-5678-90ab-cdef-123456789abc").ToString(),
            Name = "Invoice",
            Code = "INV",
            Description = "Commercial invoice for goods or services with line items, amounts, and payment terms",
            Version = 1,
            Tags = new List<string>
            {
                "invoice",
                "billing",
                "payment",
                "financial",
                "accounts-payable",
                "vendor"
            },
            DescriptorHints = new List<string>
            {
                "invoice number",
                "invoice date",
                "amount due",
                "payment terms",
                "line items",
                "vendor"
            },
            SignalPhrases = new List<string>
            {
                "invoice #",
                "invoice number:",
                "amount due:",
                "payment terms:",
                "bill to:",
                "subtotal",
                "total amount"
            },
            SupportsManualSelection = true,
            ExpectedPageCountMin = 1,
            ExpectedPageCountMax = 5,
            MimeTypes = new List<string>
            {
                "application/pdf",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "application/vnd.ms-excel"
            },
            Instructions = @"You are extracting structured data from an invoice document.

INSTRUCTIONS:
1. Identify invoice header information (number, date, vendor details)
2. Extract billing and shipping addresses
3. List all line items with descriptions, quantities, unit prices, and totals
4. Calculate or extract subtotal, tax, and total amounts
5. Note payment terms, due date, and payment methods
6. Identify any special notes, discounts, or conditions

IMPORTANT: For monetary values, include currency symbols. For dates, use consistent ISO format (YYYY-MM-DD). If information is missing, note as ""Not specified on invoice"".",
            OutputTemplate = @"# Invoice Summary

---

## Invoice Details
**Invoice Number:** {{INVOICE_NUMBER}}
**Invoice Date:** {{INVOICE_DATE}}
**Due Date:** {{DUE_DATE}}
**Purchase Order:** {{PO_NUMBER}}

---

## Vendor Information
**Vendor Name:** {{VENDOR_NAME}}
**Vendor Address:** {{VENDOR_ADDRESS}}
**Vendor Contact:** {{VENDOR_CONTACT}}

---

## Bill To
{{BILL_TO_NAME}}
{{BILL_TO_ADDRESS}}

---

## Ship To
{{SHIP_TO_NAME}}
{{SHIP_TO_ADDRESS}}

---

## Line Items
{{LINE_ITEMS}}

---

## Financial Summary
**Subtotal:** {{SUBTOTAL}}
**Tax:** {{TAX_AMOUNT}}
**Shipping:** {{SHIPPING_AMOUNT}}
**Discount:** {{DISCOUNT_AMOUNT}}
**Total Amount Due:** {{TOTAL_AMOUNT}}

---

## Payment Terms
{{PAYMENT_TERMS}}

---

## Notes
{{INVOICE_NOTES}}",
            FieldQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["invoiceNumber"] = "What is the invoice number?",
                ["invoiceDate"] = "What is the invoice date?",
                ["dueDate"] = "What is the payment due date?",
                ["poNumber"] = "What is the purchase order number?",
                ["vendorName"] = "Who is the vendor or seller?",
                ["vendorAddress"] = "What is the vendor's address?",
                ["vendorContact"] = "What is the vendor's contact information?",
                ["billToName"] = "Who is being billed (customer name)?",
                ["billToAddress"] = "What is the billing address?",
                ["shipToName"] = "What is the shipping recipient name?",
                ["shipToAddress"] = "What is the shipping address?",
                ["lineItems"] = "What are the line items (description, quantity, price)?",
                ["subtotal"] = "What is the subtotal before tax?",
                ["taxAmount"] = "What is the tax amount?",
                ["shippingAmount"] = "What is the shipping/delivery charge?",
                ["discountAmount"] = "What discounts were applied?",
                ["totalAmount"] = "What is the total amount due?",
                ["paymentTerms"] = "What are the payment terms (e.g., Net 30)?",
                ["invoiceNotes"] = "Are there any special notes or conditions?"
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static SourceType CreateContract()
    {
        return new SourceType
        {
            Id = Guid.Parse("b2c3d4e5-6789-01bc-def1-234567890bcd").ToString(),
            Name = "Contract/Agreement",
            Code = "CONT",
            Description = "Legal contract or agreement between parties outlining terms, conditions, and obligations",
            Version = 1,
            Tags = new List<string>
            {
                "contract",
                "agreement",
                "legal",
                "terms",
                "obligations",
                "parties"
            },
            DescriptorHints = new List<string>
            {
                "agreement",
                "parties",
                "effective date",
                "termination",
                "obligations",
                "governing law"
            },
            SignalPhrases = new List<string>
            {
                "this agreement",
                "parties agree",
                "effective date",
                "term of agreement",
                "obligations of",
                "termination clause",
                "governing law"
            },
            SupportsManualSelection = true,
            ExpectedPageCountMin = 2,
            ExpectedPageCountMax = 50,
            MimeTypes = new List<string>
            {
                "application/pdf",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/msword"
            },
            Instructions = @"You are analyzing a legal contract or agreement document.

INSTRUCTIONS:
1. Identify the parties involved (names, roles, addresses)
2. Extract key dates (effective date, execution date, term, renewal, termination)
3. Summarize the primary purpose and scope of the agreement
4. Document obligations and responsibilities of each party
5. Note payment terms, compensation, or financial arrangements
6. Identify termination conditions and notice requirements
7. Extract governing law, dispute resolution, and jurisdiction clauses
8. List any special conditions, warranties, or indemnification clauses

IMPORTANT: Use precise legal language. If specific clauses are ambiguous, note them for legal review. For confidential or redacted sections, note as ""[Redacted]"".",
            OutputTemplate = @"# Contract Summary

---

## Contract Details
**Contract Type:** {{CONTRACT_TYPE}}
**Contract Title:** {{CONTRACT_TITLE}}
**Effective Date:** {{EFFECTIVE_DATE}}
**Execution Date:** {{EXECUTION_DATE}}
**Contract ID:** {{CONTRACT_ID}}

---

## Parties
**Party A:**
{{PARTY_A_DETAILS}}

**Party B:**
{{PARTY_B_DETAILS}}

**Additional Parties:**
{{ADDITIONAL_PARTIES}}

---

## Scope & Purpose
{{SCOPE_PURPOSE}}

---

## Term & Termination
**Contract Term:** {{CONTRACT_TERM}}
**Renewal Terms:** {{RENEWAL_TERMS}}
**Termination Conditions:** {{TERMINATION_CONDITIONS}}
**Notice Period:** {{NOTICE_PERIOD}}

---

## Obligations & Responsibilities

### Party A Obligations
{{PARTY_A_OBLIGATIONS}}

### Party B Obligations
{{PARTY_B_OBLIGATIONS}}

---

## Financial Terms
**Payment Amount:** {{PAYMENT_AMOUNT}}
**Payment Schedule:** {{PAYMENT_SCHEDULE}}
**Payment Terms:** {{PAYMENT_TERMS}}

---

## Key Clauses

### Confidentiality
{{CONFIDENTIALITY_CLAUSE}}

### Intellectual Property
{{IP_CLAUSE}}

### Warranties
{{WARRANTIES}}

### Indemnification
{{INDEMNIFICATION}}

### Limitation of Liability
{{LIABILITY_LIMITATION}}

---

## Legal Framework
**Governing Law:** {{GOVERNING_LAW}}
**Jurisdiction:** {{JURISDICTION}}
**Dispute Resolution:** {{DISPUTE_RESOLUTION}}

---

## Special Conditions
{{SPECIAL_CONDITIONS}}

---

## Exhibits & Schedules
{{EXHIBITS_SCHEDULES}}",
            FieldQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["contractType"] = "What type of contract is this (e.g., Service Agreement, NDA, Lease)?",
                ["contractTitle"] = "What is the official title of this contract?",
                ["effectiveDate"] = "What is the effective date of the contract?",
                ["executionDate"] = "When was the contract executed/signed?",
                ["contractId"] = "What is the contract number or identifier?",
                ["partyADetails"] = "Who is Party A (name, address, role)?",
                ["partyBDetails"] = "Who is Party B (name, address, role)?",
                ["additionalParties"] = "Are there any additional parties to this agreement?",
                ["scopePurpose"] = "What is the scope and purpose of this contract?",
                ["contractTerm"] = "What is the term or duration of the contract?",
                ["renewalTerms"] = "What are the renewal terms?",
                ["terminationConditions"] = "Under what conditions can the contract be terminated?",
                ["noticePeriod"] = "What is the required notice period for termination?",
                ["partyAObligations"] = "What are Party A's obligations?",
                ["partyBObligations"] = "What are Party B's obligations?",
                ["paymentAmount"] = "What is the payment amount or compensation?",
                ["paymentSchedule"] = "What is the payment schedule?",
                ["paymentTerms"] = "What are the payment terms?",
                ["confidentialityClause"] = "What does the confidentiality clause state?",
                ["ipClause"] = "What are the intellectual property terms?",
                ["warranties"] = "What warranties are provided?",
                ["indemnification"] = "What are the indemnification terms?",
                ["liabilityLimitation"] = "What are the liability limitations?",
                ["governingLaw"] = "What is the governing law?",
                ["jurisdiction"] = "What is the jurisdiction for disputes?",
                ["disputeResolution"] = "How are disputes to be resolved?",
                ["specialConditions"] = "Are there any special conditions or clauses?",
                ["exhibitsSchedules"] = "What exhibits or schedules are attached?"
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static SourceType CreateTechnicalReport()
    {
        return new SourceType
        {
            Id = Guid.Parse("c3d4e5f6-7890-12cd-ef12-345678901cde").ToString(),
            Name = "Technical Report",
            Code = "RPT",
            Description = "Technical or research report documenting findings, analysis, and recommendations",
            Version = 1,
            Tags = new List<string>
            {
                "report",
                "technical",
                "research",
                "analysis",
                "findings",
                "recommendations"
            },
            DescriptorHints = new List<string>
            {
                "executive summary",
                "findings",
                "methodology",
                "conclusions",
                "recommendations",
                "references"
            },
            SignalPhrases = new List<string>
            {
                "executive summary",
                "introduction",
                "methodology",
                "findings",
                "results",
                "conclusions",
                "recommendations"
            },
            SupportsManualSelection = true,
            ExpectedPageCountMin = 3,
            ExpectedPageCountMax = 100,
            MimeTypes = new List<string>
            {
                "application/pdf",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/msword"
            },
            Instructions = @"You are summarizing a technical or research report.

INSTRUCTIONS:
1. Extract report metadata (title, author(s), date, organization)
2. Summarize the executive summary or abstract
3. Outline the purpose, objectives, and scope
4. Describe the methodology or approach used
5. Summarize key findings and results
6. Document conclusions drawn from the analysis
7. List recommendations or next steps
8. Note any limitations, risks, or areas for further research

IMPORTANT: Preserve technical accuracy. Use domain-specific terminology as it appears in the report. If data or charts are referenced, describe them contextually.",
            OutputTemplate = @"# Technical Report Summary

---

## Report Metadata
**Title:** {{REPORT_TITLE}}
**Report Number:** {{REPORT_NUMBER}}
**Author(s):** {{AUTHORS}}
**Organization:** {{ORGANIZATION}}
**Date:** {{REPORT_DATE}}
**Version:** {{VERSION}}

---

## Executive Summary
{{EXECUTIVE_SUMMARY}}

---

## Purpose & Objectives
**Purpose:** {{PURPOSE}}
**Objectives:** {{OBJECTIVES}}
**Scope:** {{SCOPE}}

---

## Methodology
{{METHODOLOGY}}

---

## Key Findings
{{KEY_FINDINGS}}

---

## Results & Analysis
{{RESULTS_ANALYSIS}}

---

## Conclusions
{{CONCLUSIONS}}

---

## Recommendations
{{RECOMMENDATIONS}}

---

## Limitations & Risks
{{LIMITATIONS_RISKS}}

---

## Future Work
{{FUTURE_WORK}}

---

## References
{{REFERENCES}}",
            FieldQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["reportTitle"] = "What is the title of the report?",
                ["reportNumber"] = "What is the report number or identifier?",
                ["authors"] = "Who authored this report?",
                ["organization"] = "What organization published this report?",
                ["reportDate"] = "What is the date of the report?",
                ["version"] = "What is the version of this report?",
                ["executiveSummary"] = "What is the executive summary or abstract?",
                ["purpose"] = "What is the purpose of this report?",
                ["objectives"] = "What are the objectives?",
                ["scope"] = "What is the scope of this report?",
                ["methodology"] = "What methodology was used?",
                ["keyFindings"] = "What are the key findings?",
                ["resultsAnalysis"] = "What are the results and analysis?",
                ["conclusions"] = "What conclusions were drawn?",
                ["recommendations"] = "What recommendations are made?",
                ["limitationsRisks"] = "What limitations or risks are noted?",
                ["futureWork"] = "What future work is suggested?",
                ["references"] = "What references or sources are cited?"
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static SourceType CreateEmail()
    {
        return new SourceType
        {
            Id = Guid.Parse("d4e5f6a7-8901-23de-f123-456789012def").ToString(),
            Name = "Email Communication",
            Code = "EMAIL",
            Description = "Email message or thread capturing correspondence, decisions, or information exchanges",
            Version = 1,
            Tags = new List<string>
            {
                "email",
                "communication",
                "correspondence",
                "message",
                "thread"
            },
            DescriptorHints = new List<string>
            {
                "from:",
                "to:",
                "subject:",
                "cc:",
                "sent:",
                "attachments"
            },
            SignalPhrases = new List<string>
            {
                "from:",
                "to:",
                "subject:",
                "cc:",
                "sent:",
                "date:",
                "attachments:"
            },
            SupportsManualSelection = true,
            ExpectedPageCountMin = 1,
            ExpectedPageCountMax = 10,
            MimeTypes = new List<string>
            {
                "application/pdf",
                "message/rfc822",
                "text/plain",
                "text/html"
            },
            Instructions = @"You are extracting and summarizing information from email communications.

INSTRUCTIONS:
1. Extract email header information (from, to, cc, bcc, subject, date)
2. Identify if this is a single email or part of a thread
3. Summarize the main message content and purpose
4. Note any decisions, commitments, or action items mentioned
5. List attachments if referenced
6. Identify any follow-up required or deadlines mentioned
7. Capture the tone and urgency of the communication

IMPORTANT: Preserve names and email addresses exactly as they appear. For email threads, maintain chronological order. Note any confidentiality notices.",
            OutputTemplate = @"# Email Summary

---

## Email Details
**From:** {{EMAIL_FROM}}
**To:** {{EMAIL_TO}}
**CC:** {{EMAIL_CC}}
**BCC:** {{EMAIL_BCC}}
**Subject:** {{EMAIL_SUBJECT}}
**Date Sent:** {{EMAIL_DATE}}
**Thread:** {{THREAD_STATUS}}

---

## Summary
{{EMAIL_SUMMARY}}

---

## Key Points
{{KEY_POINTS}}

---

## Decisions/Commitments
{{DECISIONS_COMMITMENTS}}

---

## Action Items
{{ACTION_ITEMS}}

---

## Attachments
{{ATTACHMENTS}}

---

## Follow-up Required
{{FOLLOWUP_REQUIRED}}

---

## Context & Background
{{CONTEXT_BACKGROUND}}

---

## Tone & Priority
**Tone:** {{TONE}}
**Priority:** {{PRIORITY}}",
            FieldQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["emailFrom"] = "Who sent this email?",
                ["emailTo"] = "Who are the primary recipients?",
                ["emailCc"] = "Who was CC'd on this email?",
                ["emailBcc"] = "Who was BCC'd (if visible)?",
                ["emailSubject"] = "What is the subject line?",
                ["emailDate"] = "When was this email sent?",
                ["threadStatus"] = "Is this a single email or part of a thread?",
                ["emailSummary"] = "What is the main content or message of this email?",
                ["keyPoints"] = "What are the key points discussed?",
                ["decisionsCommitments"] = "What decisions or commitments were made?",
                ["actionItems"] = "What action items are mentioned?",
                ["attachments"] = "What attachments are included?",
                ["followupRequired"] = "What follow-up is required?",
                ["contextBackground"] = "What is the context or background?",
                ["tone"] = "What is the tone of the email (formal, urgent, casual)?",
                ["priority"] = "What is the priority level?"
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static SourceType CreateNonDocument()
    {
        return new SourceType
        {
            Id = Guid.Parse("e5f6a7b8-9012-34ef-1234-567890123ef0").ToString(),
            Name = "Non-Document",
            Code = "NONE",
            Description = "Test files, sample data, or meaningless content that should not be processed",
            Version = 1,
            Tags = new List<string>
            {
                "test",
                "sample",
                "ignore",
                "non-document",
                "placeholder",
                "dummy"
            },
            DescriptorHints = new List<string>
            {
                "test file",
                "sample",
                "placeholder",
                "dummy data",
                "meaningless content",
                "ignore this"
            },
            SignalPhrases = new List<string>
            {
                "this is a test",
                "test file",
                "sample data",
                "lorem ipsum",
                "placeholder",
                "dummy content",
                "ignore this",
                "do not process",
                "test document"
            },
            SupportsManualSelection = false,
            SkipProcessing = true,
            ExpectedPageCountMin = 0,
            ExpectedPageCountMax = 999,
            MimeTypes = new List<string>
            {
                "text/plain",
                "application/pdf",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/msword"
            },
            Instructions = @"You are identifying test files or meaningless content that should be skipped.

INSTRUCTIONS:
1. Classify documents as Non-Document if they contain:
   - Test data or sample files
   - Placeholder text (Lorem Ipsum, etc.)
   - Dummy content with no real information
   - Explicit test markers or instructions to ignore
   - Repetitive meaningless text
   - Files that appear to be for testing purposes only

2. DO NOT classify as Non-Document if the content has any real business value, even if informal.

IMPORTANT: This classification triggers a no-op in the pipeline - the file will be marked as processed but no extraction will occur.",
            OutputTemplate = @"# Non-Document Classification

This file has been identified as test data or non-document content and will not be processed further.

Classification reason: {{CLASSIFICATION_REASON}}",
            FieldQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["classificationReason"] = "Why is this classified as a non-document (test file, placeholder, etc.)?"
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static SourceType CreateUnspecified()
    {
        return new SourceType
        {
            Id = "019a2200-0000-7000-a000-000000000099",
            Name = "Unspecified Document Type",
            Code = "UNSPECIFIED",
            Description = "Catch-all category for documents that could not be automatically classified. Extraction will still be attempted using generic strategies.",
            Version = 1,
            Tags = new List<string>
            {
                "unspecified",
                "unknown",
                "fallback",
                "generic"
            },
            DescriptorHints = new List<string>(),
            SignalPhrases = new List<string>(),
            SupportsManualSelection = false,
            SkipProcessing = false, // Still attempt extraction
            ExpectedPageCountMin = null,
            ExpectedPageCountMax = null,
            MimeTypes = new List<string>
            {
                "application/pdf",
                "text/plain",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/msword",
                "text/html"
            },
            Instructions = @"You are extracting facts from a document that could not be automatically classified into a specific type.

INSTRUCTIONS:
1. Identify any factual information present in the document
2. Extract key entities (people, organizations, dates, locations, amounts)
3. Note any decisions, actions, or commitments mentioned
4. Capture important technical details or specifications
5. Document any relationships or dependencies mentioned

IMPORTANT: Since the document type is unknown, focus on extracting concrete, verifiable facts rather than making assumptions about structure or format.",
            OutputTemplate = @"# Document Analysis (Unspecified Type)

---

## Key Facts Extracted
{{KEY_FACTS}}

---

## Entities Identified
**People:** {{PEOPLE}}
**Organizations:** {{ORGANIZATIONS}}
**Dates:** {{DATES}}
**Locations:** {{LOCATIONS}}
**Amounts/Values:** {{AMOUNTS}}

---

## Additional Context
{{ADDITIONAL_CONTEXT}}",
            FieldQueries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["keyFacts"] = "What are the most important facts or pieces of information in this document?",
                ["people"] = "What people are mentioned in the document?",
                ["organizations"] = "What organizations or companies are mentioned?",
                ["dates"] = "What significant dates are mentioned?",
                ["locations"] = "What locations or places are mentioned?",
                ["amounts"] = "What monetary amounts, quantities, or measurements are mentioned?",
                ["additionalContext"] = "What additional context or background information is provided?"
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
