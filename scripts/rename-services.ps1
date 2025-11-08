# KOAN-CONTEXT-HARDENING.md Week 1 Day 1-2: Service Renaming Script
# This script renames all 8 remaining services following the naming simplification proposal

$ErrorActionPreference = "Stop"
$basePath = "F:\Replica\NAS\Files\repo\github\koan-framework\src\Koan.Context"

Write-Host "=== Koan.Context Service Renaming Script ===" -ForegroundColor Cyan
Write-Host "This will rename 8 services and delete 8 interface files" -ForegroundColor Yellow
Write-Host ""

# Define renaming map: Old Name -> New Name
$serviceRenames = @{
    "ContentExtractionService" = "Extraction"
    "ChunkingService" = "Chunker"
    "EmbeddingService" = "Embeddings"
    "IndexingService" = "Indexer"
    "RetrievalService" = "Search"
    "TokenCountingService" = "Tokens"
    "ContinuationTokenService" = "Pagination"
    "SourceUrlGenerator" = "UrlBuilder"
}

# Step 1: Rename service classes and files
Write-Host "[1/4] Renaming service classes and files..." -ForegroundColor Green

foreach ($oldName in $serviceRenames.Keys) {
    $newName = $serviceRenames[$oldName]
    $oldFile = Join-Path $basePath "Services\$oldName.cs"
    $newFile = Join-Path $basePath "Services\$newName.cs"

    if (Test-Path $oldFile) {
        Write-Host "  - $oldName -> $newName"

        # Read file content
        $content = Get-Content $oldFile -Raw

        # Replace class name
        $content = $content -replace "public class $oldName", "public class $newName"
        $content = $content -replace "public sealed class $oldName", "public sealed class $newName"

        # Replace constructor
        $content = $content -replace "public $oldName\(", "public $newName("

        # Replace logger references
        $content = $content -replace "ILogger<$oldName>", "ILogger<$newName>"

        # Replace interface implementation
        $interfaceName = "I$oldName"
        $content = $content -replace ": $interfaceName", ""
        $content = $content -replace ",$interfaceName", ""

        # Write updated content
        Set-Content $newFile -Value $content -NoNewline

        # Delete old file if different
        if ($oldFile -ne $newFile) {
            Remove-Item $oldFile -Force
        }
    }
}

# Step 2: Delete interface files
Write-Host "[2/4] Deleting interface files..." -ForegroundColor Green

$interfaceFiles = @(
    "IContentExtractionService.cs",
    "IChunkingService.cs",
    "IEmbeddingService.cs",
    "IIndexingService.cs",
    "IRetrievalService.cs"
)

foreach ($file in $interfaceFiles) {
    $filePath = Join-Path $basePath "Services\$file"
    if (Test-Path $filePath) {
        Write-Host "  - Deleting $file"
        Remove-Item $filePath -Force
    }
}

# Step 3: Update KoanAutoRegistrar.cs DI registrations
Write-Host "[3/4] Updating DI registrations..." -ForegroundColor Green

$registrarPath = Join-Path $basePath "Initialization\KoanAutoRegistrar.cs"
$registrarContent = Get-Content $registrarPath -Raw

# Replace DI registrations
$registrarContent = $registrarContent -replace "services\.AddScoped<IContentExtractionService, ContentExtractionService>\(\);", "services.AddScoped<Extraction>();"
$registrarContent = $registrarContent -replace "services\.AddScoped<IChunkingService, ChunkingService>\(\);", "services.AddScoped<Chunker>();"
$registrarContent = $registrarContent -replace "services\.AddScoped<IIndexingService, IndexingService>\(\);", "services.AddScoped<Indexer>();"
$registrarContent = $registrarContent -replace "services\.AddScoped<IRetrievalService, RetrievalService>\(\);", "services.AddScoped<Search>();"
$registrarContent = $registrarContent -replace "services\.AddSingleton<ITokenCountingService, TokenCountingService>\(\);", "services.AddSingleton<Tokens>();"
$registrarContent = $registrarContent -replace "services\.AddSingleton<IContinuationTokenService, ContinuationTokenService>\(\);", "services.AddSingleton<Pagination>();"
$registrarContent = $registrarContent -replace "services\.AddSingleton<ISourceUrlGenerator, SourceUrlGenerator>\(\);", "services.AddSingleton<UrlBuilder>();"

# Special case for IEmbeddingService (has factory)
$registrarContent = $registrarContent -replace "services\.AddScoped<IEmbeddingService>\(sp =>", "services.AddScoped<Embeddings>(sp =>"
$registrarContent = $registrarContent -replace "var logger = sp\.GetRequiredService<Microsoft\.Extensions\.Logging\.ILogger<EmbeddingService>>\(\);", "var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Embeddings>>();"
$registrarContent = $registrarContent -replace "return new EmbeddingService\(", "return new Embeddings("

Set-Content $registrarPath -Value $registrarContent -NoNewline

# Step 4: Update all file references
Write-Host "[4/4] Updating references in other files..." -ForegroundColor Green

# Get all C# files in Koan.Context
$csFiles = Get-ChildItem -Path $basePath -Filter "*.cs" -Recurse

foreach ($file in $csFiles) {
    $content = Get-Content $file.FullName -Raw
    $modified = $false

    foreach ($oldName in $serviceRenames.Keys) {
        $newName = $serviceRenames[$oldName]
        $interfaceName = "I$oldName"

        if ($content -match $interfaceName -or $content -match $oldName) {
            $content = $content -replace "I$oldName", $newName
            $content = $content -replace "$oldName", $newName
            $modified = $true
        }
    }

    if ($modified) {
        Set-Content $file.FullName -Value $content -NoNewline
    }
}

Write-Host ""
Write-Host "=== Renaming Complete! ===" -ForegroundColor Green
Write-Host "Next step: Run 'dotnet build src/Koan.Context/Koan.Context.csproj' to verify" -ForegroundColor Yellow
