param(
    [string]$BaseUrl = "http://localhost:5080",
    [switch]$SkipCertificateCheck,
    [string]$OutputDirectory = "",
    [string]$DocumentsFolder = "",
    [switch]$NoWait,
    [int]$WaitTimeoutSeconds = 0,
    [switch]$ShowProgress
)

# Load required .NET assemblies for HTTP operations
Add-Type -AssemblyName System.Net.Http

$scriptRoot = Split-Path -Parent $PSCommandPath
$parentFolder = Split-Path -Parent $scriptRoot

# Load common functions from parent folder
. (Join-Path $parentFolder 'phase4.5-common.ps1')

Write-Host "============================================" -ForegroundColor Cyan
Write-Host " Try It Yourself - File-Only Pipeline API" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "This script demonstrates the file-only pipeline creation API." -ForegroundColor Gray
Write-Host "All documents will be auto-classified (no manifest)." -ForegroundColor Gray
Write-Host ""
Write-Host "Base URL: $BaseUrl" -ForegroundColor DarkGray

# Step 1: Discover available type codes
Write-Host "[1/6] Discovering available analysis types..." -ForegroundColor Yellow
$analysisTypes = Invoke-MeridianRequest -BaseUrl $BaseUrl -Path '/api/analysistypes/codes' -Method 'GET' -SkipCertificateCheck:$SkipCertificateCheck
$analysisTypesList = Get-Property -Object $analysisTypes -Names 'types','Types'

Write-Host "Available analysis types:" -ForegroundColor Green
foreach ($type in $analysisTypesList) {
    $code = Get-Property -Object $type -Names 'code','Code'
    $name = Get-Property -Object $type -Names 'name','Name'
    Write-Host "  ✓ $code - $name" -ForegroundColor Cyan
}
Write-Host ""

# Step 2: Set up folders relative to script location
if ([string]::IsNullOrWhiteSpace($DocumentsFolder)) {
    $DocumentsFolder = Join-Path $scriptRoot 'docs'
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    # If using default docs folder, use default output folder
    if ($DocumentsFolder -eq (Join-Path $scriptRoot 'docs')) {
        $OutputDirectory = Join-Path $scriptRoot 'output'
    } else {
        # For custom folders, create parallel output folder
        $documentsParent = Split-Path -Parent $DocumentsFolder
        $documentsFolderName = Split-Path -Leaf $DocumentsFolder
        $outputFolderName = $documentsFolderName + '-output'
        $OutputDirectory = Join-Path $documentsParent $outputFolderName
    }
}

New-Item -ItemType Directory -Path $DocumentsFolder -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

Write-Host ""
Write-Host "Folders:" -ForegroundColor DarkGray
Write-Host "  Documents: $DocumentsFolder" -ForegroundColor DarkGray
Write-Host "  Output:    $OutputDirectory" -ForegroundColor DarkGray
Write-Host ""

# Step 2: Check if we're using default folder and create sample docs if needed
$usingDefaultFolder = ($DocumentsFolder -eq (Join-Path $scriptRoot 'docs'))

# Create sample documents if using default folder and it's empty
$existingFiles = @()
if (Test-Path $DocumentsFolder) {
    $existingFiles = Get-ChildItem -Path $DocumentsFolder -File | Where-Object { $_.Name -ne 'analysis-config.json' }
}

if ($usingDefaultFolder -and $existingFiles.Count -eq 0) {
    Write-Host "[2/6] Creating sample documents..." -ForegroundColor Yellow

    # Create sample documents
    $sampleDocs = @(
        @{
            FileName = "meeting-notes.txt"
            Content = @"
Enterprise Architecture Review - Kickoff Meeting
Date: 2025-10-24

Attendees:
- Sarah Chen (CTO)
- Michael Torres (Lead Architect)
- Jennifer Park (Security Director)

Agenda:
1. Review current system architecture
2. Identify modernization opportunities
3. Discuss cloud migration strategy

Key Points:
- Current monolithic architecture shows performance bottlenecks
- Microservices transition planned for Q1 2026
- Security audit required before production deployment
- Integration with third-party vendors needs evaluation

Action Items:
- Michael: Prepare architecture diagrams by next week
- Jennifer: Schedule security assessment
- Sarah: Review vendor proposals
"@.Trim()
        },
        @{
            FileName = "vendor-proposal.txt"
            Content = @"
Vendor Proposal - CloudTech Solutions
Enterprise Architecture Modernization Services

Company Overview:
CloudTech Solutions has 15+ years of experience in enterprise architecture transformation.
Our team has successfully delivered 200+ cloud migration projects across various industries.

Proposed Services:
1. Architecture Assessment and Roadmap Development
2. Cloud Migration Strategy and Planning
3. Microservices Design and Implementation
4. Security and Compliance Review
5. Performance Optimization and Monitoring

Timeline: 6 months
Budget: $450,000
Team: 8 senior architects and engineers

References:
- TechCorp Inc. (Fortune 500 retail)
- MedHealth Systems (Healthcare provider)
- FinServe Group (Financial services)

Contact: David Wilson, Account Executive
Email: david.wilson@cloudtech.example
Phone: +1 (555) 123-4567
"@.Trim()
        },
        @{
            FileName = "security-requirements.txt"
            Content = @"
Security Requirements Document
Enterprise Architecture Modernization Project

1. Authentication and Authorization
   - Multi-factor authentication required for all access
   - Role-based access control (RBAC) implementation
   - Single sign-on (SSO) integration with corporate directory

2. Data Protection
   - Encryption at rest using AES-256
   - TLS 1.3 for all network communications
   - PII data masking in non-production environments

3. Compliance Requirements
   - SOC 2 Type II certification required
   - GDPR compliance for EU customer data
   - HIPAA compliance for healthcare integrations

4. Security Monitoring
   - 24/7 security operations center (SOC)
   - Real-time threat detection and alerting
   - Quarterly penetration testing
   - Annual security audits

5. Incident Response
   - Documented incident response plan
   - Maximum 4-hour response time for critical incidents
   - Post-incident review and remediation tracking

Approved by: Jennifer Park, Security Director
Date: 2025-10-24
"@.Trim()
        },
        @{
            FileName = "budget-summary.txt"
            Content = @"
Budget Summary Report
Enterprise Architecture Modernization Initiative
Fiscal Year 2026

Total Approved Budget: $1,250,000

Breakdown by Category:

1. Professional Services: $600,000
   - External consultants and architects
   - Vendor implementation services
   - Training and knowledge transfer

2. Infrastructure: $400,000
   - Cloud infrastructure costs
   - Licensing and subscriptions
   - Hardware for hybrid components

3. Internal Resources: $150,000
   - Internal team allocation
   - Overtime and extended hours
   - Cross-training programs

4. Contingency: $100,000
   - Risk mitigation reserve
   - Scope change buffer
   - Emergency support allocation

Current Expenditure: $0
Remaining Budget: $1,250,000

Payment Schedule:
- Q1 2026: $312,500 (25%)
- Q2 2026: $312,500 (25%)
- Q3 2026: $312,500 (25%)
- Q4 2026: $312,500 (25%)

CFO Approval: Required by 2025-11-15
Finance Contact: Amanda Martinez, amartinez@company.example
"@.Trim()
        }
    )

    foreach ($doc in $sampleDocs) {
        $docPath = Join-Path $DocumentsFolder $doc.FileName
        [IO.File]::WriteAllText($docPath, $doc.Content)
        Write-Host "  ✓ Created: $($doc.FileName)" -ForegroundColor Green
    }
    Write-Host ""
} elseif ($usingDefaultFolder) {
    Write-Host "[2/6] Using existing sample documents..." -ForegroundColor Yellow
    Write-Host ""
} else {
    Write-Host "[2/6] Using custom documents folder..." -ForegroundColor Yellow
    Write-Host ""
}

# Validate documents folder exists
if (-not (Test-Path $DocumentsFolder)) {
    Write-Host "Error: Documents folder not found: $DocumentsFolder" -ForegroundColor Red
    exit 1
}

# Step 3: Create configuration file
Write-Host "[3/6] Creating pipeline configuration..." -ForegroundColor Yellow

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$configData = @{
    pipeline = @{
        name = "Enterprise Architecture Review $timestamp"
        description = "Try It Yourself - Auto-classified documents"
        notes = @"
This is a sample Enterprise Architecture Review created via the file-only API.
All documents are auto-classified using AI-powered document classification.
"@.Trim()
        bias = "Focus on technical feasibility and cost-effectiveness"
    }
    analysis = @{
        type = "EAR"  # Enterprise Architecture Review
    }
    manifest = @{}  # Empty manifest - all documents will be auto-classified
}

$configJson = $configData | ConvertTo-Json -Depth 10
$configPath = Join-Path $DocumentsFolder "analysis-config.json"
[IO.File]::WriteAllText($configPath, $configJson)
Write-Host "  ✓ Created: analysis-config.json" -ForegroundColor Green
Write-Host ""

# Step 4: Collect all files from folder
Write-Host "[4/6] Collecting files for upload..." -ForegroundColor Yellow
$allFiles = Get-ChildItem -Path $DocumentsFolder -File
Write-Host "Found $($allFiles.Count) files:" -ForegroundColor Green
foreach ($file in $allFiles) {
    $sizeKB = [math]::Round($file.Length / 1024, 2)
    Write-Host "  • $($file.Name) ($sizeKB KB)" -ForegroundColor Cyan
}
Write-Host ""

# Step 5: Upload all files via multipart form-data
Write-Host "[5/6] Uploading files to pipeline creation API..." -ForegroundColor Yellow

try {
    $form = New-Object System.Net.Http.MultipartFormDataContent

    foreach ($file in $allFiles) {
        $fileContent = [System.IO.File]::ReadAllBytes($file.FullName)
        $fileStream = New-Object System.IO.MemoryStream @(,$fileContent)
        $streamContent = New-Object System.Net.Http.StreamContent($fileStream)
        $streamContent.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue("text/plain")
        $form.Add($streamContent, "files", $file.Name)
    }

    # Create HttpClient
    $handler = New-Object System.Net.Http.HttpClientHandler
    if ($SkipCertificateCheck) {
        $handler.ServerCertificateCustomValidationCallback = { $true }
    }
    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(300)

    # POST request
    $uri = "$BaseUrl/api/pipelines/create"
    $response = $client.PostAsync($uri, $form).Result
    $responseContent = $response.Content.ReadAsStringAsync().Result

    if (-not $response.IsSuccessStatusCode) {
        Write-Host "Error creating pipeline: $($response.StatusCode)" -ForegroundColor Red
        Write-Host $responseContent -ForegroundColor Red
        exit 1
    }

    $result = $responseContent | ConvertFrom-Json
    Write-Host "  ✓ Pipeline created successfully!" -ForegroundColor Green
    Write-Host ""

    # Display results
    $pipelineId = Get-Property -Object $result -Names 'pipelineId','PipelineId'
    $pipelineName = Get-Property -Object $result -Names 'pipelineName','PipelineName'
    $jobId = Get-Property -Object $result -Names 'jobId','JobId'
    $analysisType = Get-Property -Object $result -Names 'analysisType','AnalysisType'
    $analysisTypeName = Get-Property -Object $result -Names 'analysisTypeName','AnalysisTypeName'

    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host " Pipeline Created" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Pipeline ID:      $pipelineId" -ForegroundColor White
    Write-Host "Pipeline Name:    $pipelineName" -ForegroundColor White
    Write-Host "Analysis Type:    $analysisTypeName ($analysisType)" -ForegroundColor White
    Write-Host "Job ID:           $jobId" -ForegroundColor White
    Write-Host ""

    # Display document classification results
    $docs = Get-Property -Object $result -Names 'documents','Documents'
    if ($docs) {
        Write-Host "============================================" -ForegroundColor Cyan
        Write-Host " Document Classification Results" -ForegroundColor Cyan
        Write-Host "============================================" -ForegroundColor Cyan
        Write-Host ""

        foreach ($doc in $docs) {
            $docId = Get-Property -Object $doc -Names 'documentId','DocumentId'
            $fileName = Get-Property -Object $doc -Names 'fileName','FileName'
            $sourceType = Get-Property -Object $doc -Names 'sourceType','SourceType'
            $sourceTypeName = Get-Property -Object $doc -Names 'sourceTypeName','SourceTypeName'
            $method = Get-Property -Object $doc -Names 'method','Method'
            $confidence = Get-Property -Object $doc -Names 'confidence','Confidence'
            $inManifest = Get-Property -Object $doc -Names 'inManifest','InManifest'

            if ($inManifest) {
                Write-Host "📄 $fileName" -ForegroundColor Yellow
                Write-Host "   Source Type:   $sourceTypeName ($sourceType)" -ForegroundColor White
                Write-Host "   Method:        Manual (from manifest)" -ForegroundColor White
                Write-Host "   Confidence:    100%" -ForegroundColor White
            } else {
                $isDeferred = [string]::Equals($method, 'Deferred', [System.StringComparison]::OrdinalIgnoreCase)

                Write-Host "📄 $fileName" -ForegroundColor Cyan
                Write-Host "   Source Type:   $sourceTypeName ($sourceType)" -ForegroundColor White

                if ($isDeferred) {
                    Write-Host "   Method:        Deferred (background processing)" -ForegroundColor White
                    Write-Host "   Confidence:    Pending" -ForegroundColor DarkGray
                } else {
                    $confidencePercent = [Math]::Round($confidence * 100, 1)
                    $confidenceColor = if ($confidencePercent -ge 90) { "Green" } elseif ($confidencePercent -ge 70) { "Yellow" } else { "Red" }

                    Write-Host "   Method:        AI Auto-Classification ($method)" -ForegroundColor White
                    Write-Host "   Confidence:    $confidencePercent%" -ForegroundColor $confidenceColor
                }
            }
            Write-Host ""
        }
    }

    # Display statistics
    $stats = Get-Property -Object $result -Names 'statistics','Statistics'
    if ($stats) {
        $total = Get-Property -Object $stats -Names 'totalDocuments','TotalDocuments'
        $manifest = Get-Property -Object $stats -Names 'manifestSpecified','ManifestSpecified'
        $auto = Get-Property -Object $stats -Names 'autoClassified','AutoClassified'

        Write-Host "============================================" -ForegroundColor Cyan
        Write-Host " Statistics" -ForegroundColor Cyan
        Write-Host "============================================" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Total Documents:        $total" -ForegroundColor White
        Write-Host "Manifest-Specified:     $manifest" -ForegroundColor White
        Write-Host "Auto-Classified:        $auto" -ForegroundColor White
        Write-Host ""
    }

    if ($NoWait) {
        Write-Host "[6/6] Background processing queued (skipping wait as requested)." -ForegroundColor Yellow
        Write-Host "  • Job ID: $jobId" -ForegroundColor White
        Write-Host "  • Poll with: Wait-MeridianJob -PipelineId '$pipelineId' -JobId '$jobId'" -ForegroundColor DarkGray
        Write-Host ""
    }
    else {
        Write-Host "[6/6] Waiting for analysis to complete..." -ForegroundColor Yellow
        $job = Wait-MeridianJob -BaseUrl $BaseUrl -PipelineId $pipelineId -JobId $jobId -TimeoutSeconds $WaitTimeoutSeconds -PollSeconds 2 -SkipCertificateCheck:$SkipCertificateCheck -ShowProgress:$ShowProgress
        Write-Host "  ✓ Analysis completed!" -ForegroundColor Green
        Write-Host ""

        # Get deliverable
        Write-Host "Retrieving analysis deliverable..." -ForegroundColor Yellow
        $deliverable = Get-MeridianDeliverable -BaseUrl $BaseUrl -PipelineId $pipelineId -SkipCertificateCheck:$SkipCertificateCheck

        if ($deliverable) {
            $outputPath = Join-Path $OutputDirectory ("deliverable-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + ".md")
            $markdown = Get-Property -Object $deliverable -Names 'RenderedMarkdown','renderedMarkdown','Markdown','markdown'
            if ($markdown) {
                [IO.File]::WriteAllText($outputPath, $markdown)
                Write-Host ""
                Write-Host "============================================" -ForegroundColor Cyan
                Write-Host " Analysis Complete" -ForegroundColor Cyan
                Write-Host "============================================" -ForegroundColor Cyan
                Write-Host ""
                Write-Host "Deliverable saved to:" -ForegroundColor Green
                Write-Host "  $outputPath" -ForegroundColor White
                Write-Host ""
                Write-Host "Preview:" -ForegroundColor Yellow
                Write-Host "----------------------------------------" -ForegroundColor DarkGray

                # Show first 30 lines of deliverable
                $lines = $markdown -split "`n"
                $previewLines = $lines | Select-Object -First 30
                foreach ($line in $previewLines) {
                    Write-Host $line -ForegroundColor Gray
                }

                if ($lines.Count -gt 30) {
                    Write-Host "... ($($lines.Count - 30) more lines)" -ForegroundColor DarkGray
                }
                Write-Host "----------------------------------------" -ForegroundColor DarkGray
            }
        }
    }

} finally {
    if ($client) { $client.Dispose() }
    if ($handler) { $handler.Dispose() }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host " Success!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Folders:" -ForegroundColor Yellow
Write-Host "  Documents: $DocumentsFolder" -ForegroundColor Cyan
Write-Host "  Output:    $OutputDirectory" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
if ($NoWait) {
    Write-Host "  1. Monitor progress: Wait-MeridianJob -PipelineId '$pipelineId' -JobId '$jobId' -ShowProgress" -ForegroundColor White
    Write-Host "  2. Download deliverables after completion via Get-MeridianDeliverable" -ForegroundColor White
    Write-Host "  3. Add your own documents to docs folder" -ForegroundColor White
}
else {
    Write-Host "  1. Review the analysis deliverable in output folder" -ForegroundColor White
    Write-Host "  2. Add your own documents to docs folder" -ForegroundColor White
    Write-Host "  3. Run this script again to see auto-classification in action" -ForegroundColor White
}
Write-Host ""
Write-Host "To use your own documents:" -ForegroundColor Yellow
Write-Host "  .\TryItYourself.ps1 -DocumentsFolder 'C:\path\to\your\documents'" -ForegroundColor Cyan
Write-Host "  (Output will be saved to parallel folder)" -ForegroundColor DarkGray
Write-Host ""
