#!/usr/bin/env pwsh
param(
    [switch]$Serve,
    [int]$Port = 8080,
    [switch]$Clean,
    [switch]$ValidateCode,
    [switch]$OpenBrowser,
    [ValidateSet('Verbose','Info','Warning','Error')]
    [string]$LogLevel = 'Info'
)

$ErrorActionPreference = 'Stop'

function Write-Heading($text) {
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Write-Success($text) {
    Write-Host "✓ $text" -ForegroundColor Green
}

Push-Location (Resolve-Path "$PSScriptRoot\..")
try {
    Write-Heading "Koan Documentation Local Build"

    # Validate code examples first if requested
    if ($ValidateCode) {
        Write-Heading "Validating Code Examples"
        & "$PSScriptRoot/validate-code-examples.ps1" -Verbose:$($LogLevel -eq 'Verbose')
        if ($LASTEXITCODE -ne 0) {
            throw "Code validation failed"
        }
        Write-Success "Code examples validated successfully"
    }

    # Build documentation
    Write-Heading "Building Documentation"
    $buildArgs = @(
        '-ConfigPath', 'docs/docfx.json'
        '-LogLevel', $LogLevel
    )

    if ($Clean) {
        $buildArgs += '-Clean'
    }

    if ($Serve) {
        $buildArgs += '-Serve'
        $buildArgs += '-Port', $Port
    }

    & "$PSScriptRoot/build-docs.ps1" @buildArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Documentation build failed"
    }

    $outputPath = Resolve-Path "artifacts/other/website/docs"
    Write-Success "Documentation built successfully"
    Write-Host "Output: $outputPath"

    if ($Serve) {
        $url = "http://localhost:$Port"
        Write-Success "Documentation server started at $url"

        if ($OpenBrowser) {
            if ($IsWindows) {
                Start-Process $url
            } elseif ($IsMacOS) {
                & open $url
            } elseif ($IsLinux) {
                & xdg-open $url
            }
        }

        Write-Host "Press Ctrl+C to stop the server"
    } else {
        Write-Host ""
        Write-Host "To serve locally:"
        Write-Host "  ./scripts/docs-local.ps1 -Serve"
        Write-Host ""
        Write-Host "To validate code examples:"
        Write-Host "  ./scripts/docs-local.ps1 -ValidateCode"
    }
}
catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}