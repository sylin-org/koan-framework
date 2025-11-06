#!/usr/bin/env pwsh
<#
.SYNOPSIS
Tests the enhanced AI authoring capability for rich analysis types.

.DESCRIPTION
Sends sample requests to the AI authoring endpoint to validate:
- Rich field generation (15-20 fields)
- Structured markdown templates with sections
- Role-based, contextual instructions
- Comprehensive analysis type specifications

.EXAMPLE
./test-rich-ai-generation.ps1
#>

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Configuration
$BaseUrl = "http://localhost:5080"
$ApiPath = "/api/analysistypes/ai-suggest"
$Endpoint = "$BaseUrl$ApiPath"

Write-Host "🧪 Testing Enhanced AI Analysis Type Generation" -ForegroundColor Cyan
Write-Host "=" * 60
Write-Host ""

# Test Case 1: Enterprise Architecture Review
Write-Host "📋 Test 1: Enterprise Architecture Review" -ForegroundColor Yellow
Write-Host "Goal: Generate comprehensive enterprise architecture analysis type"
Write-Host ""

$request1 = @{
    goal                = "Conduct a thorough enterprise architecture review of technology proposals to assess strategic alignment, identify risks and opportunities, and provide actionable recommendations for leadership decision-making"
    audience            = "CTO, Enterprise Architects, and Executive Leadership"
    additionalContext   = "Reviews should evaluate business alignment, technical feasibility, security implications, cost analysis, and implementation roadmap. Output should include executive summary, detailed findings across multiple dimensions, risk assessment, and strategic recommendations."
    includedSourceTypes = @("Technical Specification", "Business Case", "Architecture Diagram", "Requirements Document")
} | ConvertTo-Json -Depth 10

Write-Host "Sending request..." -ForegroundColor Gray
try {
    $response1 = Invoke-RestMethod -Uri $Endpoint -Method Post `
        -ContentType "application/json" `
        -Body $request1 `
        -TimeoutSec 60

    Write-Host "✅ Response received!" -ForegroundColor Green
    Write-Host ""
    
    $draft = $response1.draft
    
    Write-Host "📊 Generated Analysis Type:" -ForegroundColor Cyan
    Write-Host "  Name: $($draft.name)" -ForegroundColor White
    Write-Host "  Description: $($draft.description.Substring(0, [Math]::Min(100, $draft.description.Length)))..." -ForegroundColor Gray
    Write-Host ""
    
    # Count fields from schema
    $schema = $draft.outputSchemaJson | ConvertFrom-Json
    $fieldCount = $schema.properties.PSObject.Properties.Name.Count
    
    Write-Host "📝 Output Fields: $fieldCount" -ForegroundColor Cyan
    $schema.properties.PSObject.Properties | ForEach-Object {
        Write-Host "  - $($_.Name)" -ForegroundColor Gray
    }
    Write-Host ""
    
    Write-Host "📄 Instructions Preview (first 300 chars):" -ForegroundColor Cyan
    $instructionsPreview = $draft.instructions.Substring(0, [Math]::Min(300, $draft.instructions.Length))
    Write-Host $instructionsPreview -ForegroundColor Gray
    if ($draft.instructions.Length -gt 300) {
        Write-Host "..." -ForegroundColor Gray
    }
    Write-Host ""
    
    Write-Host "📋 Template Preview (first 500 chars):" -ForegroundColor Cyan
    $templatePreview = $draft.outputTemplate.Substring(0, [Math]::Min(500, $draft.outputTemplate.Length))
    Write-Host $templatePreview -ForegroundColor Gray
    if ($draft.outputTemplate.Length -gt 500) {
        Write-Host "..." -ForegroundColor Gray
    }
    Write-Host ""
    
    # Validation checks
    Write-Host "✔️ Validation Checks:" -ForegroundColor Green
    
    $checks = @(
        @{ Test = $fieldCount -ge 10; Message = "Field count >= 10: $fieldCount" }
        @{ Test = $draft.instructions.Length -ge 200; Message = "Instructions >= 200 chars: $($draft.instructions.Length)" }
        @{ Test = $draft.outputTemplate -match "##"; Message = "Template uses markdown sections: $(($draft.outputTemplate -match '##').ToString())" }
        @{ Test = $draft.outputTemplate -match "\{\{[a-z_]+\}\}"; Message = "Template uses Mustache syntax: $(($draft.outputTemplate -match '\{\{[a-z_]+\}\}').ToString())" }
        @{ Test = $draft.instructions -match "(As a|As an)"; Message = "Instructions have role context: $(($draft.instructions -match '(As a|As an)').ToString())" }
    )
    
    foreach ($check in $checks) {
        $icon = if ($check.Test) { "✅" } else { "❌" }
        $color = if ($check.Test) { "Green" } else { "Red" }
        Write-Host "  $icon $($check.Message)" -ForegroundColor $color
    }
    Write-Host ""
    
    if ($response1.warnings -and $response1.warnings.Count -gt 0) {
        Write-Host "⚠️  Warnings:" -ForegroundColor Yellow
        $response1.warnings | ForEach-Object {
            Write-Host "  - $_" -ForegroundColor Yellow
        }
        Write-Host ""
    }
    
    # Save full response for inspection
    $outputFile = "test-enterprise-arch-review-output.json"
    $response1 | ConvertTo-Json -Depth 10 | Out-File $outputFile
    Write-Host "💾 Full response saved to: $outputFile" -ForegroundColor Cyan
    Write-Host ""
    
}
catch {
    Write-Host "❌ Request failed: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

Write-Host "=" * 60
Write-Host ""

# Test Case 2: Security Assessment
Write-Host "📋 Test 2: Security & Compliance Assessment" -ForegroundColor Yellow
Write-Host "Goal: Generate security assessment analysis type"
Write-Host ""

$request2 = @{
    goal                = "Perform comprehensive security and compliance assessment of systems and applications to identify vulnerabilities, assess regulatory compliance, and recommend security improvements"
    audience            = "CISO, Security Engineers, Compliance Officers"
    additionalContext   = "Assessment should cover authentication/authorization, data protection, network security, vulnerability assessment, compliance gaps, and remediation priorities."
    includedSourceTypes = @("Security Scan Report", "Compliance Checklist", "Architecture Diagram", "Code Review")
} | ConvertTo-Json -Depth 10

Write-Host "Sending request..." -ForegroundColor Gray
try {
    $response2 = Invoke-RestMethod -Uri $Endpoint -Method Post `
        -ContentType "application/json" `
        -Body $request2 `
        -TimeoutSec 60

    Write-Host "✅ Response received!" -ForegroundColor Green
    Write-Host ""
    
    $draft2 = $response2.draft
    
    Write-Host "📊 Generated Analysis Type:" -ForegroundColor Cyan
    Write-Host "  Name: $($draft2.name)" -ForegroundColor White
    
    $schema2 = $draft2.outputSchemaJson | ConvertFrom-Json
    $fieldCount2 = $schema2.properties.PSObject.Properties.Name.Count
    
    Write-Host "📝 Output Fields: $fieldCount2" -ForegroundColor Cyan
    Write-Host "📄 Instructions Length: $($draft2.instructions.Length) chars" -ForegroundColor Cyan
    Write-Host "📋 Template Length: $($draft2.outputTemplate.Length) chars" -ForegroundColor Cyan
    Write-Host ""
    
    # Quick validation
    $allChecksPass = ($fieldCount2 -ge 10) -and 
    ($draft2.instructions.Length -ge 200) -and
    ($draft2.outputTemplate -match "##")
    
    if ($allChecksPass) {
        Write-Host "✅ All validation checks passed!" -ForegroundColor Green
    }
    else {
        Write-Host "⚠️  Some validation checks failed" -ForegroundColor Yellow
    }
    Write-Host ""
    
    $outputFile2 = "test-security-assessment-output.json"
    $response2 | ConvertTo-Json -Depth 10 | Out-File $outputFile2
    Write-Host "💾 Full response saved to: $outputFile2" -ForegroundColor Cyan
    Write-Host ""
    
}
catch {
    Write-Host "❌ Request failed: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
}

Write-Host "=" * 60
Write-Host ""

# Test Case 3: Simple Analysis (Boundary Test)
Write-Host "📋 Test 3: Simple Meeting Summary (Boundary Test)" -ForegroundColor Yellow
Write-Host "Goal: Ensure simple analysis types still work with new constraints"
Write-Host ""

$request3 = @{
    goal                = "Summarize meeting notes into key decisions, action items, and next steps"
    audience            = "Meeting participants"
    includedSourceTypes = @("Meeting Notes", "Recording Transcript")
} | ConvertTo-Json -Depth 10

Write-Host "Sending request..." -ForegroundColor Gray
try {
    $response3 = Invoke-RestMethod -Uri $Endpoint -Method Post `
        -ContentType "application/json" `
        -Body $request3 `
        -TimeoutSec 60

    Write-Host "✅ Response received!" -ForegroundColor Green
    Write-Host ""
    
    $draft3 = $response3.draft
    
    Write-Host "📊 Generated Analysis Type:" -ForegroundColor Cyan
    Write-Host "  Name: $($draft3.name)" -ForegroundColor White
    
    $schema3 = $draft3.outputSchemaJson | ConvertFrom-Json
    $fieldCount3 = $schema3.properties.PSObject.Properties.Name.Count
    
    Write-Host "📝 Output Fields: $fieldCount3" -ForegroundColor Cyan
    Write-Host ""
    
    if ($fieldCount3 -ge 5 -and $fieldCount3 -le 10) {
        Write-Host "✅ Simple analysis generated appropriate field count (5-10)" -ForegroundColor Green
    }
    else {
        Write-Host "⚠️  Field count outside expected range: $fieldCount3" -ForegroundColor Yellow
    }
    Write-Host ""
    
}
catch {
    Write-Host "❌ Request failed: $_" -ForegroundColor Red
}

Write-Host "=" * 60
Write-Host ""
Write-Host "🎉 Enhanced AI Generation Testing Complete!" -ForegroundColor Green
Write-Host ""
Write-Host "📁 Output files:" -ForegroundColor Cyan
Write-Host "  - test-enterprise-arch-review-output.json"
Write-Host "  - test-security-assessment-output.json"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Review the generated analysis types in the output files"
Write-Host "  2. Test creating types via the UI"
Write-Host "  3. Verify template rendering and field extraction"
Write-Host ""
