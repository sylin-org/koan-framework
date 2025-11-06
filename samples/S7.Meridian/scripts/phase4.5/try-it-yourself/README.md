# Try It Yourself - File-Only Pipeline API

This folder contains everything you need to try Meridian's file-only pipeline creation API with automatic document classification.

## Folder Structure

```
try-it-yourself/
├── TryItYourself.ps1    # The script
├── docs/                # Your documents go here
├── output/              # Analysis deliverables are saved here
└── README.md           # You are here
```

## Quick Start

Simply run the script from this folder:

```powershell
.\TryItYourself.ps1
```

The script will:
1. ✅ Discover available analysis types from the API
2. ✅ Create sample documents in `docs/` folder if needed
3. ✅ Generate a pipeline configuration (no manifest = auto-classification)
4. ✅ Upload all files to create the pipeline
5. ✅ Show AI-powered classification results with confidence scores
6. ✅ Wait for analysis to complete
7. ✅ Retrieve and save deliverable to `output/` folder

## What Gets Auto-Classified?

All documents in the `docs/` folder are automatically classified using AI with confidence scores:

- **Meeting Summary/Notes (MEET)** - Meeting minutes, agendas, action items
- **Technical Report (RPT)** - Technical documentation, requirements, specifications
- **Contract/Agreement (CONT)** - Contracts, agreements, legal documents
- **Invoice (INV)** - Invoices, billing documents, financial records
- **Email Communication (EMAIL)** - Email threads, correspondence

## Using Your Own Documents

### Option 1: Use the docs Folder

After first run, replace or add your documents to:
```
try-it-yourself/docs/
```

Then run the script again:
```powershell
.\TryItYourself.ps1
```

### Option 2: Specify Your Own Folder

Point to your own documents folder:
```powershell
.\TryItYourself.ps1 -DocumentsFolder "C:\MyDocuments\ProjectFiles"
```

Output will be saved to a parallel folder: `C:\MyDocuments\ProjectFiles-output`

## How It Works

### 1. Configuration File
The script creates an `analysis-config.json` in the docs folder with:
- **Pipeline metadata** (name, description, notes)
- **Analysis type** (EAR = Enterprise Architecture Review)
- **Empty manifest** - triggers auto-classification for all documents

```json
{
  "pipeline": {
    "name": "Enterprise Architecture Review 20251024-154132",
    "description": "Try It Yourself - Auto-classified documents",
    "notes": "Sample analysis using AI-powered document classification"
  },
  "analysis": {
    "type": "EAR"
  },
  "manifest": {}
}
```

### 2. File Upload
All files in the docs folder (including `analysis-config.json`) are uploaded via multipart/form-data to:
```
POST /api/pipelines/create
```

### 3. Auto-Classification
For each document:
- AI analyzes content to determine document type
- Assigns source type with confidence score (0-100%)
- Uses LLM-based classification method

### 4. Analysis Execution
- Pipeline is created with classified documents
- Processing job runs automatically
- Analysis deliverable is generated
- Results are saved to `output/` folder

## Sample Output

```
============================================
 Document Classification Results
============================================

📄 budget-summary.txt
   Source Type:   Technical Report (RPT)
   Method:        AI Auto-Classification (Llm)
   Confidence:    95%

📄 meeting-notes.txt
   Source Type:   Meeting Summary/Notes (MEET)
   Method:        AI Auto-Classification (Llm)
   Confidence:    95%

📄 security-requirements.txt
   Source Type:   Technical Report (RPT)
   Method:        AI Auto-Classification (Llm)
   Confidence:    99%

📄 vendor-proposal.txt
   Source Type:   Invoice (INV)
   Method:        AI Auto-Classification (Llm)
   Confidence:    90%

============================================
 Statistics
============================================

Total Documents:        4
Manifest-Specified:     0
Auto-Classified:        4
```

## Parameters

| Parameter | Description | Default |
|-----------|-------------|---------|
| `-BaseUrl` | API base URL | `http://localhost:5080` |
| `-DocumentsFolder` | Path to your documents | `./docs` |
| `-OutputDirectory` | Where to save deliverables | `./output` |
| `-SkipCertificateCheck` | Skip SSL validation | `false` |

## Examples

### Use default folders
```powershell
.\TryItYourself.ps1
```

### Use remote API
```powershell
.\TryItYourself.ps1 -BaseUrl "https://meridian.example.com"
```

### Use your documents
```powershell
.\TryItYourself.ps1 -DocumentsFolder "C:\Projects\ProjectX\Documents"
```

### Custom output location
```powershell
.\TryItYourself.ps1 -OutputDirectory "C:\Reports\Meridian"
```

## Comparison: With vs Without Manifest

### Without Manifest (This Script)
```json
{
  "manifest": {}
}
```
**Result:** All documents auto-classified by AI

### With Manifest (../ScenarioA-FileOnly.ps1)
```json
{
  "manifest": {
    "meeting-notes.txt": {
      "type": "MEET",
      "notes": "Steering committee meeting"
    },
    "vendor-prescreen.txt": {
      "type": "CONT",
      "notes": "Vendor questionnaire responses"
    }
  }
}
```
**Result:** Manifest files use specified types, others auto-classified

## Troubleshooting

### Script fails with "Connection refused"
Ensure Meridian API is running:
```powershell
docker ps --filter "name=meridian-api"
```

If not running, start it from the Meridian root:
```powershell
cd F:\Replica\NAS\Files\repo\github\koan-framework\samples\S7.Meridian
.\start.bat
```

### No documents found
Check that your docs folder exists and contains files:
```powershell
Get-ChildItem -Path .\docs
```

### Low confidence scores
- Ensure documents have clear, descriptive content
- Consider using manifest for critical document types
- Review classification in API response and adjust as needed

## Next Steps

1. **Review the generated analysis** in `output/` folder
2. **Try your own documents** by adding them to `docs/` folder
3. **Experiment with different analysis types** (see available codes)
4. **Compare with manifest-based approach** (see `../ScenarioA-FileOnly.ps1`)

## Related Scripts

- **../ScenarioA-FileOnly.ps1** - Enterprise review with mixed manifest/auto classification
- **../phase4.5-common.ps1** - Shared utility functions

## API Documentation

For full API documentation, visit:
```
http://localhost:5080/swagger/index.html
```

Key endpoints:
- `GET /api/analysistypes/codes` - Available analysis types
- `GET /api/sourcetypes/codes` - Available source types
- `POST /api/pipelines/create` - Create pipeline from files
- `GET /api/pipelines/{id}/deliverable` - Get analysis results
