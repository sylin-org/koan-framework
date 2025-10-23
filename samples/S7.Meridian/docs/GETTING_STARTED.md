# Getting Started with Meridian API

**Your first document intelligence pipeline in 15 minutes**

This guide walks you through creating your first Meridian pipeline from scratch. By the end, you'll have transformed a messy document into structured, trustworthy data with citations.

---

## Prerequisites

Before we begin, make sure you have:

- **Meridian running locally** - If you haven't set this up yet:
  ```bash
  cd samples/S7.Meridian
  ./start.bat  # Windows
  ```
  This starts Meridian at `http://localhost:5080`

- **A tool to make HTTP requests** - We'll use `curl` in these examples, but Postman, Insomnia, or any HTTP client works fine

- **A document to analyze** - Any PDF or text file will do. Don't have one? No problem - we'll show you how to create a simple test file.

**Check if Meridian is running**:
```bash
curl http://localhost:5080/health
```

If you see `{"status":"ok"}`, you're ready to go.

---

## The Big Picture: What We're Building

We're going to build a simple vendor evaluation pipeline that:

1. **Defines what we want** - Company name, revenue, and employee count
2. **Uploads a vendor document** - A PDF or text file with company info
3. **Extracts the data** - AI reads the document and pulls out the fields
4. **Gets the results** - Structured data with citations back to the source

Think of it like teaching a very smart assistant to read vendor documents for you and fill out a standardsheet - except this assistant never gets tired, always shows its work, and handles hundreds of documents without complaint.

---

## Step 1: Define What You Want (Create an Analysis Type)

An **Analysis Type** is like a template that says "here's what I want to extract from documents." It includes:
- A schema (the fields you want)
- Instructions for the AI
- A template for how to present the results

Let's create one for vendor evaluation:

```bash
curl -X POST http://localhost:5080/api/analysistypes \
  -H "Content-Type: application/json" \
  -d '{
  "name": "Vendor Quick Profile",
  "description": "Extract basic vendor information for initial evaluation",
  "schema": {
    "type": "object",
    "properties": {
      "companyName": {
        "type": "string",
        "description": "Legal company name"
      },
      "revenue": {
        "type": "string",
        "description": "Annual revenue (include currency and amount)"
      },
      "employeeCount": {
        "type": "number",
        "description": "Total number of employees"
      },
      "founded": {
        "type": "string",
        "description": "Year company was founded"
      }
    },
    "required": ["companyName"]
  },
  "renderTemplate": "# {{companyName}}\n\n**Revenue**: {{revenue}}\n**Employees**: {{employeeCount}}\n**Founded**: {{founded}}\n",
  "includedSourceTypes": []
}'
```

**What just happened?**

You created a reusable blueprint. Anytime you analyze a vendor document, Meridian now knows to look for:
- Company name (required)
- Revenue (optional)
- Employee count (optional)
- Year founded (optional)

**Save the response** - you'll need the `id` field (it looks like `019a0e6c-a470-7895-bb27-84292878a777`). This is your Analysis Type ID.

---

## Step 2: Create a Pipeline (The Workspace)

A **Pipeline** is like a project folder where you'll upload documents and get results. It's linked to your Analysis Type so Meridian knows what to extract.

```bash
curl -X POST http://localhost:5080/api/pipelines \
  -H "Content-Type: application/json" \
  -d '{
  "name": "Acme Corp Evaluation",
  "description": "Analyzing Acme Corp vendor documents",
  "analysisTypeId": "YOUR_ANALYSIS_TYPE_ID_HERE"
}'
```

**Replace `YOUR_ANALYSIS_TYPE_ID_HERE`** with the ID from Step 1.

**Save the response** - you'll need the `id` field. This is your Pipeline ID.

**What just happened?**

You created a workspace where you can:
- Upload multiple documents about Acme Corp
- Have them all analyzed using the same template
- Get a single consolidated report

---

## Step 3: Upload a Document

Now the fun part - let's give Meridian something to read.

### Option A: Create a Simple Test File

If you don't have a vendor document handy, create a file called `acme.txt`:

```
Acme Corporation Vendor Profile

Acme Corporation was founded in 2015 and has grown to become a leading
provider of enterprise software solutions. The company currently employs
175 people across offices in New York, London, and Singapore.

According to our FY2024 financial statement, Acme generated $52.3 million
in annual revenue, representing 23% year-over-year growth.

Key Certifications:
- ISO 27001:2022
- SOC 2 Type II
```

### Option B: Upload Your Own Document

Use any PDF or text file you have.

### Upload to Meridian

```bash
curl -X POST "http://localhost:5080/api/pipelines/YOUR_PIPELINE_ID/documents" \
  -F "file=@acme.txt" \
  -F "fileName=acme.txt"
```

**Replace `YOUR_PIPELINE_ID`** with the ID from Step 2.

**What just happened?**

Meridian immediately started:
1. **Extracting text** from your file
2. **Breaking it into passages** (small, searchable chunks)
3. **Embedding the passages** for semantic search
4. **Extracting your fields** using AI
5. **Building citations** linking each value back to the source

This all happens in the background. Let's check if it's done.

---

## Step 4: Check Processing Status

Processing usually takes 10-30 seconds for a simple document. Let's check:

```bash
curl "http://localhost:5080/api/pipelines/YOUR_PIPELINE_ID"
```

Look for the `status` field in the response:
- `"Pending"` - Still processing, wait a few seconds
- `"Ready"` - Done! You can get your results
- `"Failed"` - Something went wrong (check the error message)

If it's still pending, wait 10 seconds and try again.

---

## Step 5: Get Your Results

Once processing is complete, retrieve your structured data:

```bash
curl "http://localhost:5080/api/pipelines/YOUR_PIPELINE_ID/deliverables/latest"
```

**You'll get back**:

```json
{
  "renderedMarkdown": "# Acme Corporation\n\n**Revenue**: $52.3 million\n**Employees**: 175\n**Founded**: 2015\n",
  "dataJson": "{\"companyName\":\"Acme Corporation\",\"revenue\":\"$52.3 million\",\"employeeCount\":175,\"founded\":\"2015\"}",
  "quality": {
    "citationCoverage": 1.0,
    "highConfidence": 4,
    "mediumConfidence": 0,
    "lowConfidence": 0
  }
}
```

**What you're seeing**:

1. **renderedMarkdown** - Human-readable summary using your template
2. **dataJson** - Structured data (parse this for automation)
3. **quality** - Confidence metrics (all fields have high confidence!)

**Want to see the citations?** The deliverable includes evidence for each field showing exactly where in the document that value came from.

---

## Step 6: View in PDF Format (Bonus)

Want a professional PDF report with citations?

```bash
curl "http://localhost:5080/api/pipelines/YOUR_PIPELINE_ID/deliverables/pdf" \
  --output acme-report.pdf
```

Open `acme-report.pdf` - you'll see your data beautifully formatted with footnotes linking back to the source document.

---

## 🎉 You're Done!

**What you just accomplished**:

✅ Created a reusable Analysis Type for vendor evaluation
✅ Built a Pipeline workspace
✅ Uploaded a document
✅ Extracted structured data with AI
✅ Retrieved results in multiple formats (JSON, Markdown, PDF)
✅ Got citations for every field

**Total time**: ~5 minutes of your active time (plus 10-30 seconds of processing)

---

## What's Next?

Now that you've got the basics, here are some powerful next steps:

### Upload Multiple Documents

Upload more documents to the same pipeline:

```bash
curl -X POST "http://localhost:5080/api/pipelines/YOUR_PIPELINE_ID/documents" \
  -F "file=@financial-statement.pdf"

curl -X POST "http://localhost:5080/api/pipelines/YOUR_PIPELINE_ID/documents" \
  -F "file=@security-audit.pdf"
```

**Meridian automatically**:
- Extracts fields from each document
- Resolves conflicts (e.g., if revenue differs between documents)
- Ranks by confidence and source precedence
- Updates your deliverable with the best answers

### Add Authoritative Notes

Have a correction or update? Override any field with Authoritative Notes:

```bash
curl -X PUT "http://localhost:5080/api/pipelines/YOUR_PIPELINE_ID/notes" \
  -H "Content-Type: application/json" \
  -d '{
  "authoritativeNotes": "REVENUE: FY2024 revenue was exactly $52.3M USD (confirmed by CFO)\nEMPLOYEE COUNT: 175 total employees as of Oct 2024",
  "reProcess": true
}'
```

Notes always win over document extractions - perfect for user corrections.

### Check Quality Metrics

See how confident Meridian is in its extractions:

```bash
curl "http://localhost:5080/api/pipelines/YOUR_PIPELINE_ID/quality"
```

**Quality metrics include**:
- `highConfidence` - Fields with >90% confidence
- `mediumConfidence` - Fields with 70-90% confidence
- `lowConfidence` - Fields needing manual review
- `citationCoverage` - % of fields with source citations
- `notesSourced` - Fields overridden by Authoritative Notes

### Override Individual Fields

Need to manually correct a specific field?

```bash
curl -X PUT "http://localhost:5080/api/pipelines/YOUR_PIPELINE_ID/fields/employeeCount/override" \
  -H "Content-Type: application/json" \
  -d '{
  "value": 180,
  "reason": "Updated per LinkedIn profile as of Nov 2024"
}'
```

Manual overrides are tracked separately and always take precedence.

### Create More Analysis Types

Build templates for different use cases:

- **Financial Due Diligence** - Extract revenue, profit, debt, cash flow
- **Security Audit Review** - Pull certifications, findings, remediation plans
- **Contract Analysis** - Extract parties, dates, termination clauses, payment terms
- **Resume Screening** - Grab skills, experience, education, certifications

Each Analysis Type becomes a reusable tool in your document intelligence toolkit.

---

## Common Questions

### How do I know what Source Types are available?

```bash
curl http://localhost:5080/api/sourcetypes/all
```

Source Types help classify documents (Financial Statement, Vendor Questionnaire, etc.) for better extraction and precedence rules.

### Can I reuse the same Analysis Type?

**Yes!** That's the point. Create the Analysis Type once, then create multiple Pipelines using it:

```bash
# Pipeline for Acme Corp
curl -X POST http://localhost:5080/api/pipelines \
  -d '{"name": "Acme Corp", "analysisTypeId": "YOUR_ANALYSIS_TYPE_ID"}'

# Pipeline for Beta LLC
curl -X POST http://localhost:5080/api/pipelines \
  -d '{"name": "Beta LLC", "analysisTypeId": "YOUR_ANALYSIS_TYPE_ID"}'
```

Same extraction logic, different documents.

### What if processing fails?

Check the pipeline status for error details:

```bash
curl "http://localhost:5080/api/pipelines/YOUR_PIPELINE_ID"
```

Common issues:
- **File too large** - PDFs >50MB may time out
- **Scanned PDFs** - OCR takes longer; increase timeout
- **Unsupported format** - Stick to PDF, TXT, DOCX

### How do I delete a pipeline?

```bash
curl -X DELETE "http://localhost:5080/api/pipelines/YOUR_PIPELINE_ID"
```

This removes the pipeline and all associated documents, extractions, and deliverables.

---

## Understanding the Data Flow

Here's what happens under the hood when you upload a document:

1. **Text Extraction** → Document converted to text (OCR for scanned PDFs)
2. **Passage Chunking** → Text split into ~200-word semantic chunks
3. **Embedding** → Each passage converted to vector for search
4. **Classification** → Document matched to Source Type (Financial, Vendor, etc.)
5. **Field Extraction** → For each field in your schema:
   - Search for relevant passages
   - Send to AI with context
   - Validate and locate exact text span
   - Store with full citation
6. **Conflict Resolution** → If multiple documents have different values, merge using configured strategy (highest confidence, source precedence, latest, etc.)
7. **Deliverable Generation** → Render final output using your template

**Time breakdown** (typical):
- Text extraction: 2-5 seconds
- Passage processing: 5-10 seconds
- Field extraction: 10-30 seconds (scales with # of fields)
- Total: 20-45 seconds for a 10-page PDF with 5-10 fields

---

## API Quick Reference

### Core Workflow

```bash
# 1. Create Analysis Type (one-time setup)
POST /api/analysistypes

# 2. Create Pipeline (per project/vendor)
POST /api/pipelines

# 3. Upload documents (repeat as needed)
POST /api/pipelines/{pipelineId}/documents

# 4. Check status
GET /api/pipelines/{pipelineId}

# 5. Get results
GET /api/pipelines/{pipelineId}/deliverables/latest
GET /api/pipelines/{pipelineId}/deliverables/json
GET /api/pipelines/{pipelineId}/deliverables/pdf
```

### Advanced Features

```bash
# Add/update Authoritative Notes
PUT /api/pipelines/{pipelineId}/notes

# Override specific field
PUT /api/pipelines/{pipelineId}/fields/{fieldPath}/override

# Check quality metrics
GET /api/pipelines/{pipelineId}/quality

# View processing jobs
GET /api/pipelines/{pipelineId}/jobs/{jobId}

# Refresh/reprocess
POST /api/pipelines/{pipelineId}/refresh
```

### Discovery

```bash
# List all Analysis Types
GET /api/analysistypes/all

# List all Source Types
GET /api/sourcetypes/all

# List all Pipelines
GET /api/pipelines/all

# Search Pipelines
POST /api/pipelines/query
```

---

## Tips for Success

### 1. Start Simple

Don't try to extract 50 fields on your first attempt. Start with 3-5 critical fields, verify they work, then expand.

### 2. Write Good Descriptions

The `description` field in your schema helps the AI understand what you want:

**❌ Bad**: `"revenue": { "type": "number" }`
**✅ Good**: `"revenue": { "type": "string", "description": "Annual revenue including currency (e.g., $47.2M USD)" }`

### 3. Use Source Types

If you consistently process certain document types (financial statements, contracts, etc.), create Source Types. This improves classification and enables smart merge policies.

### 4. Check Quality Metrics

Before trusting the data, look at `quality.highConfidence` vs `quality.lowConfidence`. Low-confidence fields may need manual review.

### 5. Leverage Authoritative Notes

When you have ground truth (from a phone call, verified source, etc.), add it to Notes. This overrides any document extractions and improves accuracy.

---

## Need Help?

- **Swagger UI**: Visit `http://localhost:5080/swagger` for interactive API documentation
- **Health Check**: `http://localhost:5080/health`
- **Logs**: `docker compose -f docker/compose.yml logs meridian-api`

**Happy extracting!** 🚀
