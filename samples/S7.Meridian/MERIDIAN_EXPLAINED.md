# Understanding Meridian: A Document Intelligence System

**A narrative guide to how Meridian transforms chaotic documents into structured, trustworthy data**

---

## The Problem: When Documents Don't Speak the Same Language

Imagine you're evaluating a new vendor for your company. You receive:
- A 20-page questionnaire they filled out
- Their latest financial statement
- A security audit from last year
- Meeting notes from your team's discussion

Each document has information you need - like their annual revenue, employee count, and security certifications - but it's scattered across different formats, using different terminology, and sometimes contradicting itself. The financial statement says "$47.2 million revenue," but the questionnaire says "approximately $45-50M." Meeting notes mention "around 150 employees," while LinkedIn shows 165.

Now imagine doing this for 50 vendors. That's hundreds of documents, thousands of data points, and countless hours of:
- **Reading** the same information in slightly different forms
- **Reconciling** conflicting numbers between sources
- **Copying** data into spreadsheets
- **Verifying** which source is most trustworthy
- **Tracking** where each piece of information came from

This is the daily reality for enterprise teams: **too many documents, not enough structure**.

---

## The Traditional Solution (and Why It Doesn't Work)

### Attempt 1: Hire More People

"Let's hire interns to read through everything and fill out spreadsheets."

**Problem**: Humans are expensive, slow, and make mistakes. Reading 500 pages to find 20 data points is mind-numbing work, and copy-paste errors are inevitable. Plus, when two documents disagree, different people make different judgment calls.

### Attempt 2: Write Custom Scripts

"Let's write code to extract the data automatically."

**Problem**: Every document is different. You'd write:
```
If filename contains "financial" then
  Find "revenue:" and grab the next number
```

But what if the next document says "Annual Sales" instead of "revenue"? What if it's a scanned PDF where text extraction doesn't work? What if there's a table instead of plain text?

Custom scripts are **brittle** - they break with every variation.

### Attempt 3: Use OCR or Traditional NLP

"Let's use optical character recognition to read scanned documents."

**Problem**: OCR gives you text, but not understanding. Getting the text "Annual revenue for fiscal year 2023 was $47.2 million" is nice, but how do you:
1. Know this is the **revenue** field?
2. Extract just the number **$47.2 million** (not "2023")?
3. Link it back to the **source document and page number**?
4. Handle when another document says **$45.8 million**?

Traditional NLP is like having someone read the words aloud without comprehension.

---

## The Modern Solution: RAG-Based Document Intelligence

This is where **Meridian** comes in. It uses a sophisticated approach called **Retrieval-Augmented Generation (RAG)** - think of it as "Google search meets AI understanding."

### What Is RAG? (Explained Simply)

**Traditional approach**: Tell the AI "read all 200 pages and answer my question."
- Problem: AI gets confused, hallucinates, or misses important details.

**RAG approach**:
1. **Break documents into small chunks** (like index cards)
2. **When you ask a question**, search for the most relevant chunks
3. **Give the AI only those chunks** to analyze
4. **Get a precise answer** with evidence

It's like the difference between asking someone to find a quote in War and Peace by memory versus giving them 10 highlighted paragraphs that probably contain it.

---

## How Meridian Works: A Step-by-Step Journey

Let's walk through what happens when you use Meridian to analyze those vendor documents.

### Step 1: You Define What You Want (The Schema)

Instead of hoping the AI guesses what you need, you create a **schema** - a blueprint of the data structure you want:

```json
{
  "companyName": "text",
  "revenue": "number (in USD)",
  "employees": "number",
  "founded": "year",
  "certifications": "list of text"
}
```

This is like telling someone "I need you to fill out this form, not write a free-form essay."

**Why this matters**: The AI knows exactly what to look for and what format to return it in.

---

### Step 2: Upload Your Documents

You drag and drop all your PDFs, Word docs, and images. Meridian:

1. **Extracts the text** using multiple strategies:
   - Direct PDF text extraction (for digital PDFs)
   - OCR via Tesseract (for scanned documents)
   - Fallbacks for weird formats

2. **Classifies each document**:
   - "This looks like a financial statement" (based on keywords, structure, page count)
   - "This appears to be a security questionnaire"
   - Classification happens in 3 stages:
     - **Heuristic**: Fast pattern matching (keywords, file names)
     - **Vector similarity**: Semantic matching (what it "feels like")
     - **AI fallback**: Ask the AI when uncertain

3. **Chunks the text** into passages:
   - Break 100-page documents into ~200-word chunks
   - Like tearing a book into chapters and paragraphs
   - Each chunk becomes searchable

**Analogy**: Imagine organizing a library. Instead of shelving entire books, you catalog each chapter separately with tags like "finance," "employees," "certifications." Now you can find the exact chapter you need instantly.

---

### Step 3: The Magic Part - Vector Embeddings

Here's where it gets clever. For each chunk of text, Meridian creates an **embedding** - a mathematical representation of meaning.

**Simple explanation**:
- The phrase "annual revenue" and "yearly sales" mean the same thing
- To a computer, they're different words
- Embeddings convert both into numbers that are **close together** in "meaning space"

```
"annual revenue"     → [0.82, 0.15, 0.93, ...] (768 numbers)
"yearly sales"       → [0.81, 0.14, 0.91, ...] (very similar numbers!)
"employee benefits"  → [0.21, 0.73, 0.44, ...] (very different numbers)
```

Now Meridian can search by **meaning**, not just exact words.

**The Cache Trick**: Generating these embeddings is expensive (AI API calls cost money). So Meridian caches them:
- First run: Generate embeddings for all passages (takes time)
- Second run: Reuse cached embeddings (near-instant, 100% cache hit rate)

**Analogy**: Like creating an index for a book once, then reusing it forever.

---

### Step 4: Field Extraction via RAG

Now you want to extract the `revenue` field. Here's what happens:

#### 4a. Build a Search Query

Meridian converts your schema field name into a natural language query:
```
Field: "$.revenue"
Query: "Find information about revenue."
```

If your schema included hints or the document type suggested specific terminology, it might become:
```
Query: "Find information about revenue, sales, or annual income."
```

#### 4b. Hybrid Search

Meridian searches for relevant passages using **two methods simultaneously**:

1. **Semantic search** (vector similarity):
   - Find passages whose embeddings are close to the query embedding
   - Catches synonyms and related concepts

2. **Keyword search** (BM25):
   - Traditional text search for the words "revenue," "sales," etc.
   - Catches exact matches

Combining both is like using Google Images (visual similarity) and Google Search (keyword matching) at the same time.

**Results**: 12 most relevant passages (configurable)

#### 4c. Remove Redundancy (MMR Filter)

You might have 3 passages that all say "Revenue was $47.2M" in slightly different ways. That wastes context space.

Meridian uses **Maximum Marginal Relevance (MMR)** to:
- Keep the most relevant passages
- Remove near-duplicates
- Maximize diversity of information

**Analogy**: If you ask 10 people a question, you don't want 8 of them to give the exact same answer. You want varied perspectives.

#### 4d. Token Budget Management

AI models have limits on how much text they can process (the "context window"). Meridian enforces a budget:
- Default: 2,000 tokens per field (~1,500 words)
- If passages exceed this, use **tournament selection**:
  - Rank passages by relevance
  - Keep the top ones until budget is full
  - Always include at least 1 passage

**Why this matters**: Prevents "context overflow" where the AI gets confused by too much information.

#### 4e. Ask the AI to Extract

Now Meridian sends the AI a carefully crafted prompt:

```
Extract the value for 'revenue' from the following passages.

Field type: number (in USD)
Field schema: { "type": "number", "description": "Annual revenue in USD" }

Passages:
[0] Total revenue for fiscal year 2023 was $47.2 million, representing 23% growth...
[1] Our annual sales reached approximately $45-50M based on preliminary estimates...
[2] The company reported revenues of $47,200,000 for the year ending December 31...

Instructions:
1. Find the passage that best answers the question
2. Extract the EXACT value (do NOT infer or calculate)
3. If the value is not explicitly stated, respond with null
4. Validate the extracted value against the schema
5. Provide confidence based on text clarity (0.0-1.0)

Respond in JSON format:
{
  "value": <extracted value>,
  "confidence": <0.0-1.0>,
  "passageIndex": <0-based index>
}
```

**AI Response**:
```json
{
  "value": 47200000,
  "confidence": 0.94,
  "passageIndex": 0
}
```

#### 4f. Validate and Locate

Meridian then:

1. **Validates** the value against your schema:
   - Is it a number? ✅
   - Is it reasonable? ✅
   - Does it match expected patterns? ✅

2. **Locates the exact text span** in the passage:
   - Find "$47.2 million" in passage [0]
   - Store character positions: start=42, end=55
   - This enables **highlighting** in the UI later

3. **Stores the extraction** with full provenance:
   ```json
   {
     "fieldPath": "$.revenue",
     "value": 47200000,
     "confidence": 0.94,
     "evidence": {
       "sourceDocument": "Financial_2023.pdf",
       "page": 3,
       "passageId": "abc123...",
       "originalText": "Total revenue for fiscal year 2023 was $47.2 million...",
       "span": { "start": 42, "end": 55 }
     }
   }
   ```

**This happens for every field in your schema** - company name, employees, certifications, etc.

---

### Step 5: Conflict Resolution (The Hard Part)

Now you have extractions from multiple documents, and they **disagree**:
- Financial statement: $47.2M revenue
- Questionnaire: $45-50M revenue
- Meeting notes: ~$48M revenue

Which one is correct?

#### Meridian's Merge Strategies

You configure **merge policies** per field. For revenue, you might say:

```json
{
  "$.revenue": {
    "strategy": "sourcePrecedence",
    "precedence": ["Financial_Statement", "Questionnaire", "MeetingNotes"],
    "transform": "normalizeToUSD"
  }
}
```

**Translation**: "Trust the financial statement over the questionnaire, and normalize currency."

#### Available Strategies

1. **HighestConfidence**: Use the extraction with the highest AI confidence score
   - Good for: Technical data where confidence matters

2. **SourcePrecedence**: Trust certain document types over others
   - Good for: Regulatory data (audited > unaudited)

3. **Latest**: Use the most recent extraction by timestamp
   - Good for: Stock prices, employee count (changes over time)

4. **Consensus**: Require N sources to agree within a threshold
   - Good for: Critical decisions (security certifications)

5. **Collection** (union/intersection): Combine multiple values
   - Good for: Lists (product lines, certifications)

#### The Merge Decision

Meridian creates an **audit trail** for every merge:

```json
{
  "field": "$.revenue",
  "acceptedValue": 47200000,
  "acceptedSource": "Financial_2023.pdf",
  "rejectedAlternatives": [
    {
      "value": "45-50M",
      "source": "Questionnaire.pdf",
      "reason": "Lower precedence source"
    }
  ],
  "strategy": "sourcePrecedence",
  "explanation": "Applied precedence rule: Financial_Statement > Questionnaire. Chose $47.2M from Financial_Statement (confidence: 94%)."
}
```

**Why this matters**: You can always see **why** Meridian chose a particular value. No black boxes.

---

### Step 6: Generate the Deliverable

Finally, Meridian generates a document with your extracted data:

#### Markdown with Citations

```markdown
# Vendor Assessment: Acme Corp

## Company Overview

**Annual Revenue**: $47,200,000 [^1]
**Employees**: 150 [^2]
**Founded**: 2010 [^3]

## Citations

[^1]: Financial_2023.pdf, Page 3, Section: Income Statement
  _"Total revenue for fiscal year 2023 was $47.2 million, representing..."_

[^2]: Vendor_Prescreen.pdf, Page 2, Section: Company Information
  _"Current headcount stands at 150 full-time employees..."_

[^3]: About_Us.pdf, Page 1
  _"Founded in 2010 by John Smith and Jane Doe..."_
```

Every value is **linked to its source** - you can always verify.

#### PDF Generation (Optional)

Meridian can render the Markdown as a professional PDF using Pandoc, with:
- Custom templates
- Company branding
- Table of contents
- Proper formatting

---

## The Complete Picture: What Makes Meridian Different

### 1. **Evidence-First Design**

Traditional systems: "The revenue is $47.2M"
Meridian: "The revenue is $47.2M (94% confidence, from Financial_2023.pdf page 3, here's the exact quote)"

**You never have to trust a number without seeing where it came from.**

### 2. **Multi-Document Reconciliation**

Traditional systems: Process one document at a time, or naively combine everything
Meridian: Intelligently merges conflicting data using configurable rules

**Example**: If 3 documents mention employee count with different numbers, Meridian doesn't just pick one randomly - it applies your business logic (latest wins, consensus required, etc.)

### 3. **Incremental Refresh**

You add one new document to a pipeline with 10 existing documents. Traditional systems would reprocess everything.

Meridian:
1. Detects which fields are affected by the new document
2. Re-extracts only those fields
3. Preserves user approvals for unchanged fields
4. Re-runs merge only for impacted fields

**Cost savings**: Massive - you only pay for AI calls on changed data.

### 4. **Quality Metrics**

Meridian tracks:
- **Citation coverage**: What % of fields have source evidence?
- **Confidence distribution**: How many fields are high/medium/low confidence?
- **Conflict rate**: How often do documents disagree?
- **Auto-resolution rate**: How many conflicts were resolved without human intervention?

**Dashboard example**:
```
Quality Score: 92% (Excellent)
- Citation Coverage: 95%
- High Confidence: 88%
- Conflicts: 12 (all auto-resolved)
- Source Diversity: 4 documents used
```

---

## The Technical Magic: How Koan Framework Makes This Possible

Meridian is built on the **Koan Framework**, which provides the plumbing for all this complexity. Here's what Koan gives us:

### 1. **Entity-First Development**

In traditional code, you'd write:
```csharp
var repository = new DocumentRepository(dbContext);
var pipeline = await repository.GetById(id);
await repository.Update(pipeline);
```

With Koan:
```csharp
var pipeline = await DocumentPipeline.Get(id, ct);
pipeline.Status = "Processing";
await pipeline.Save(ct);
```

**Why this matters**: Less boilerplate, more business logic. No need to inject repositories everywhere.

### 2. **Multi-Provider Transparency**

Meridian stores data in MongoDB, vectors in a vector database (Qdrant/Elasticsearch), files in local storage, and caches in files/Redis.

Traditional code: Write different code for each storage backend
Koan code:
```csharp
await VectorWorkflow<Passage>.SaveMany(passages, profileName, ct);
```

Koan automatically routes this to the right vector provider based on configuration. **Switch from Qdrant to Elasticsearch? Zero code changes.**

### 3. **Auto-Registration**

Traditional code:
```csharp
services.AddScoped<IDocumentFactExtractor, DocumentFactExtractor>();
services.AddScoped<IFieldFactMatcher, FieldFactMatcher>();
services.AddScoped<IDocumentMerger, DocumentMerger>();
services.AddScoped<...>(25 more services);
```

Koan code:
```csharp
public class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public void Register(IServiceCollection services, IConfiguration cfg)
    {
        // Register all 28 services
        // Bind configuration
        // Set up background workers
    }
}
```

**Why this matters**: All service registration in one place. Add a package reference → functionality auto-discovered.

### 4. **Background Job Processing**

Meridian uses a **distributed job queue** with:
- **Heartbeat mechanism**: Jobs ping every 30 seconds to say "I'm alive"
- **Stale job detection**: If a job stops sending heartbeats, requeue it
- **Automatic retry**: Failed jobs retry up to 3 times
- **Job archival**: Completed jobs moved to separate partition

**Zero external dependencies** - no Redis, RabbitMQ, or SQS needed. Just entities:

```csharp
var job = await ProcessingJob.ClaimNext(ct);
if (job != null) {
    await ProcessPipeline(job, ct);
    await job.Complete(ct);
}
```

---

## Real-World Example: Enterprise Architecture Review

Let's see Meridian in action with a realistic scenario.

### The Scenario

Your CIO needs to review the enterprise architecture readiness of a new vendor. She receives:
1. Meeting notes from the sales team (10 pages)
2. Customer bulletins about their product (15 pages)
3. Vendor's security questionnaire (25 pages)
4. Cybersecurity assessment from a third party (30 pages)

She needs a **2-page executive summary** with:
- Key findings
- Financial health
- Staffing levels
- Security posture

### The Traditional Approach (8 hours of work)

1. **Read all 80 pages** (2 hours)
2. **Highlight relevant passages** (1 hour)
3. **Copy data into a Word template** (1 hour)
4. **Cross-reference conflicting info** (2 hours)
5. **Write summary paragraphs** (1 hour)
6. **Format and proofread** (1 hour)

Total: **8 hours** of tedious work

### The Meridian Approach (15 minutes)

1. **Create analysis type** (2 minutes):
   ```json
   {
     "name": "Enterprise Architecture Review",
     "schema": {
       "keyFindings": "text",
       "financialHealth": "text",
       "staffing": "text",
       "securityPosture": "text"
     },
     "template": "## EA Review\n### {{keyFindings}}\n..."
   }
   ```

2. **Upload 4 documents** (3 minutes):
   - Drag and drop PDFs
   - Meridian auto-classifies each type
   - Text extraction happens in background

3. **Click "Process"** (8 minutes automated):
   - RAG extraction for each field
   - Merge across all 4 documents
   - Generate markdown with citations
   - Render PDF

4. **Review and approve** (2 minutes):
   - Scan the generated summary
   - Click into citations to verify
   - Approve or override any field

**Total: 15 minutes** (10 of which are automated)

### The Output

```markdown
## Enterprise Architecture Readiness Review

### Key Findings
- Vendor demonstrates strong technical architecture with modern cloud-native
  approach [^1]
- Recent migration to Kubernetes shows commitment to scalability [^2]
- Minor concerns around disaster recovery procedures [^3]

### Financial Health
Annual revenue of $47.2M with 23% year-over-year growth [^4]. Strong cash
position with 18 months runway [^5]. Recent Series B funding of $15M
demonstrates investor confidence [^6].

### Staffing
Current headcount of 150 FTEs, with 60% in engineering [^7]. Low attrition
rate (8% annually) indicates healthy culture [^8]. Adequate bench strength
in DevOps and security teams [^9].

### Security Posture
SOC 2 Type II certified as of Q3 2024 [^10]. Annual penetration testing
with no critical findings [^11]. Encryption at rest and in transit implemented
across all systems [^12]. Minor recommendations around MFA adoption for
contractors [^13].

## Citations

[^1]: Meeting_Notes.pdf, Page 3
  _"Their architecture lead walked us through their microservices approach..."_

[^4]: Financial_Statement.pdf, Page 1
  _"Total revenue for fiscal year 2023 was $47.2 million..."_

[^10]: Security_Assessment.pdf, Page 12
  _"SOC 2 Type II audit completed September 2024 with no exceptions..."_

... (13 total citations)
```

**The CIO's reaction**: "This is exactly what I needed, and I can trust every number because I can see the source."

---

## The Technology Stack (Simplified)

Here's what Meridian uses under the hood:

### Document Processing
- **PdfPig**: Extracts text from digital PDFs
- **Tesseract**: OCR for scanned documents (images → text)
- **Pandoc**: Renders Markdown → PDF with templates

### AI & Embeddings
- **Ollama** (or OpenAI/Azure): Large language models for understanding
- **granite3.3:8b**: Default model (8 billion parameters)
- **qwen3-embedding:8b**: Generates vector embeddings

### Storage
- **MongoDB**: Stores entities (pipelines, documents, extractions)
- **Local files** (or S3): Stores uploaded PDFs
- **Vector database**: Stores passage embeddings (Qdrant/Elasticsearch)
- **File cache**: Stores embedding cache (or Redis)

### Framework
- **Koan Framework**: Entity-first development, multi-provider abstraction
- **.NET 10**: Modern C# for backend
- **Docker Compose**: Container orchestration

---

## Common Questions

### Q: "Doesn't the AI hallucinate?"

**Short answer**: Sometimes, but Meridian has guardrails.

**Mitigations**:
1. **Schema validation**: If you say "revenue" is a number, the AI can't return text
2. **Confidence scoring**: Low-confidence extractions are flagged
3. **Evidence linking**: Every value shows its source - you can verify
4. **User approval**: Critical fields can require human review
5. **Prompt engineering**: Carefully designed prompts reduce hallucinations

**Real-world accuracy**: In testing, 85-95% of extractions are correct without human intervention.

### Q: "What if documents are really messy?"

**Answer**: Meridian has fallbacks at every level.

- **Text extraction**: PDF → OCR → human intervention
- **Classification**: Heuristics → Vector → LLM → Manual override
- **Field extraction**: RAG → Lower confidence → Flagged for review
- **Merge**: If conflicts can't auto-resolve, present to user

The goal is **graceful degradation**, not perfection.

### Q: "How much does it cost to run?"

**Answer**: Mostly AI API calls.

**Cost breakdown** (rough estimates):
- **Embedding generation**: $0.0001 per passage (cached after first run)
- **Field extraction**: $0.001-0.01 per field (depends on context length)
- **Classification**: $0.001 per document

**Example**: 10 documents, 20 fields each = 200 extractions
- First run: ~$2-5 in AI costs
- Second run: ~$0.50 (cache hits)
- Infrastructure: <$20/month (MongoDB, storage, compute)

**Total**: ~$25-30/month for moderate usage (hundreds of documents)

### Q: "Can I use this locally without cloud AI?"

**Answer**: Yes! That's why we use Ollama.

Run everything locally:
- Download granite3.3:8b model (~5 GB)
- Run Ollama on your machine
- Zero external API calls
- Full privacy (your documents never leave your infrastructure)

**Tradeoff**: Local models are slightly less accurate than GPT-4, but still 80-90% effective.

### Q: "What about other languages?"

**Answer**: Meridian works with any language the AI model supports.

- **English**: Fully tested, highly accurate
- **Spanish/French/German**: Works well with multilingual models
- **Japanese/Chinese**: Requires appropriate embedding model
- **Mixed documents**: Handles English + Spanish in same pipeline

---

## The "Aha!" Moments

Here are the insights that make Meridian click:

### 1. **RAG Is Not Magic - It's Smart Search**

Think of RAG as Google for your documents:
- You ask a question
- It finds the most relevant paragraphs
- It reads ONLY those paragraphs (not the whole document)
- It answers based on what it found

The "augmented generation" part just means "generate an answer using retrieved context."

### 2. **Embeddings Are Like GPS Coordinates for Words**

Every phrase gets converted to numbers (coordinates). Similar meanings = close coordinates.
- "revenue" and "sales" are near each other
- "revenue" and "employees" are far apart
- You can search by proximity

### 3. **Conflict Resolution Is Business Logic, Not AI**

The AI extracts values. YOU decide which value wins when there's a conflict:
- Trust audited financials over self-reported questionnaires
- Use latest employee count (changes frequently)
- Require 2+ sources to agree on security certifications

This is **configurable**, not hardcoded.

### 4. **Evidence Linking Is What Makes It Trustworthy**

Anyone can show you a number. Meridian shows you:
- The number
- Where it came from (document + page)
- The exact sentence that contains it
- How confident the AI was
- Why it was chosen over alternatives

This transforms "black box AI" into "transparent assistant."

---

## The Future: Where Meridian Is Headed

### Near-Term Enhancements

1. **Multi-Modal Understanding**
   - Extract data from charts and graphs
   - Understand tables without converting to text
   - Process images and diagrams

2. **Conversational Interface**
   - "Why did you choose this value?"
   - "Show me all mentions of 'security' across documents"
   - "What conflicts need my attention?"

3. **Workflow Automation**
   - Trigger approval workflows for low-confidence fields
   - Auto-send summaries to stakeholders
   - Integration with Slack/Teams for notifications

### Long-Term Vision

1. **Continuous Learning**
   - Learn from your corrections: "You always prefer source A over B"
   - Improve field queries based on historical searches
   - Adapt to your organization's terminology

2. **Federated Search**
   - Search across ALL your pipelines at once
   - "Show me revenue trends for all vendors over 3 years"
   - Knowledge graph of entities and relationships

3. **Predictive Analysis**
   - "This vendor's financials are deteriorating"
   - "Security posture is improving year-over-year"
   - "Staffing levels suggest scaling challenges"

---

## Getting Started: Your First Pipeline

Want to try Meridian? Here's the quickest path:

### 1. **Start with a Simple Schema**

Don't try to extract 50 fields on day one. Start with 5-10:

```json
{
  "companyName": "text",
  "revenue": "number",
  "employees": "number",
  "website": "url",
  "contactEmail": "email"
}
```

### 2. **Upload 2-3 Documents**

Not 50. Start small:
- Company website "About Us" page
- A vendor questionnaire
- A financial statement (if available)

### 3. **Process and Review**

- Click "Process"
- Wait 1-2 minutes
- Review the extractions
- Click into citations to see evidence
- Approve or correct

### 4. **Iterate**

- Add more fields to your schema
- Upload more documents
- Configure merge policies for conflicts
- Refine your template

**In 30 minutes, you'll have a working document intelligence pipeline.**

---

## Conclusion: Why This Matters

We're drowning in documents. Every business process - vendor evaluation, compliance audits, due diligence, market research - generates hundreds of pages of unstructured text.

Traditional approaches force us to choose:
- **Hire people**: Slow, expensive, doesn't scale
- **Write scripts**: Brittle, breaks with every format change
- **Ignore the problem**: Make decisions with incomplete data

**Meridian offers a third way**:
- Fast (minutes, not hours)
- Accurate (85-95% correct)
- Trustworthy (full evidence trails)
- Scalable (process hundreds of documents)
- Adaptable (configurable merge logic)

More importantly, it's built on **principles that will last**:
1. **Evidence over assertions** - Never trust a value without seeing its source
2. **Transparency over magic** - Explain every decision
3. **Flexibility over rigidity** - Configure business rules, don't hardcode them
4. **Augmentation over automation** - Help humans make better decisions faster

The future of work isn't "AI replaces humans." It's "AI handles the tedious parts so humans can focus on judgment and strategy."

Meridian embodies this future. It reads the documents, extracts the data, reconciles the conflicts, and presents you with a summary - but **you** make the final call.

That's document intelligence done right.

---

**Ready to dive deeper?**
- See **`docs/UX-SPECIFICATION.md`** ✅ for the canonical user experience vision
- See `PROPOSAL.md` for the full technical specification
- See `ARCHITECTURE.md` for system design details
- See `docs/AUTHORITATIVE-NOTES-PROPOSAL.md` for notes override feature spec
- See `TESTING.md` for hands-on tutorials
- See `PROJECT_STATUS_REPORT.md` for implementation status

**Questions or feedback?**
This system is designed to be understandable, not magical. If anything in this guide confused you, that's a documentation bug - please ask!
