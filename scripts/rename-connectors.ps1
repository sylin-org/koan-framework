[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot ".." )).Path

$mappings = @(
    @{ OldId = "Koan.AI.Connector.Ollama"; NewId = "Koan.AI.Connector.Ollama"; NewSrcPath = "src\\Connectors\\AI\\Ollama" },
    @{ OldId = "Koan.Canon.Connector.RabbitMq"; NewId = "Koan.Canon.Connector.RabbitMq"; NewSrcPath = "src\\Connectors\\Canon\\RabbitMq" },
    @{ OldId = "Koan.Canon.Runtime.Connector.Dapr"; NewId = "Koan.Canon.Runtime.Connector.Dapr"; NewSrcPath = "src\\Connectors\\Canon\\Runtime\\Dapr" },
    @{ OldId = "Koan.Data.Connector.Couchbase"; NewId = "Koan.Data.Connector.Couchbase"; NewSrcPath = "src\\Connectors\\Data\\Couchbase" },
    @{ OldId = "Koan.Data.Connector.ElasticSearch"; NewId = "Koan.Data.Connector.ElasticSearch"; NewSrcPath = "src\\Connectors\\Data\\ElasticSearch" },
    @{ OldId = "Koan.Data.Connector.Json"; NewId = "Koan.Data.Connector.Json"; NewSrcPath = "src\\Connectors\\Data\\Json" },
    @{ OldId = "Koan.Data.Connector.Mongo"; NewId = "Koan.Data.Connector.Mongo"; NewSrcPath = "src\\Connectors\\Data\\Mongo" },
    @{ OldId = "Koan.Data.Connector.OpenSearch"; NewId = "Koan.Data.Connector.OpenSearch"; NewSrcPath = "src\\Connectors\\Data\\OpenSearch" },
    @{ OldId = "Koan.Data.Connector.Postgres"; NewId = "Koan.Data.Connector.Postgres"; NewSrcPath = "src\\Connectors\\Data\\Postgres" },
    @{ OldId = "Koan.Data.Connector.Redis"; NewId = "Koan.Data.Connector.Redis"; NewSrcPath = "src\\Connectors\\Data\\Redis" },
    @{ OldId = "Koan.Data.Connector.Sqlite"; NewId = "Koan.Data.Connector.Sqlite"; NewSrcPath = "src\\Connectors\\Data\\Sqlite" },
    @{ OldId = "Koan.Data.Connector.SqlServer"; NewId = "Koan.Data.Connector.SqlServer"; NewSrcPath = "src\\Connectors\\Data\\SqlServer" },
    @{ OldId = "Koan.Data.Cqrs.Outbox.Connector.Mongo"; NewId = "Koan.Data.Cqrs.Outbox.Connector.Mongo"; NewSrcPath = "src\\Connectors\\Data\\Cqrs\\Outbox\\Mongo" },
    @{ OldId = "Koan.Data.Vector.Connector.Milvus"; NewId = "Koan.Data.Vector.Connector.Milvus"; NewSrcPath = "src\\Connectors\\Data\\Vector\\Milvus" },
    @{ OldId = "Koan.Data.Vector.Connector.Weaviate"; NewId = "Koan.Data.Vector.Connector.Weaviate"; NewSrcPath = "src\\Connectors\\Data\\Vector\\Weaviate" },
    @{ OldId = "Koan.Messaging.Connector.RabbitMq"; NewId = "Koan.Messaging.Connector.RabbitMq"; NewSrcPath = "src\\Connectors\\Messaging\\RabbitMq" },
    @{ OldId = "Koan.Messaging.Inbox.Connector.Http"; NewId = "Koan.Messaging.Inbox.Connector.Http"; NewSrcPath = "src\\Connectors\\Messaging\\Inbox\\Http" },
    @{ OldId = "Koan.Messaging.Inbox.Connector.InMemory"; NewId = "Koan.Messaging.Inbox.Connector.InMemory"; NewSrcPath = "src\\Connectors\\Messaging\\Inbox\\InMemory" },
    @{ OldId = "Koan.Orchestration.Connector.Docker"; NewId = "Koan.Orchestration.Connector.Docker"; NewSrcPath = "src\\Connectors\\Orchestration\\Docker" },
    @{ OldId = "Koan.Orchestration.Connector.Podman"; NewId = "Koan.Orchestration.Connector.Podman"; NewSrcPath = "src\\Connectors\\Orchestration\\Podman" },
    @{ OldId = "Koan.Orchestration.Renderers.Connector.Compose"; NewId = "Koan.Orchestration.Renderers.Connector.Compose"; NewSrcPath = "src\\Connectors\\Orchestration\\Renderers\\Compose" },
    @{ OldId = "Koan.Secrets.Connector.Vault"; NewId = "Koan.Secrets.Connector.Vault"; NewSrcPath = "src\\Connectors\\Secrets\\Vault" },
    @{ OldId = "Koan.Service.Inbox.Connector.Redis"; NewId = "Koan.Service.Inbox.Connector.Redis"; NewSrcPath = "src\\Connectors\\Service\\Inbox\\Redis" },
    @{ OldId = "Koan.Storage.Connector.Local"; NewId = "Koan.Storage.Connector.Local"; NewSrcPath = "src\\Connectors\\Storage\\Local" },
    @{ OldId = "Koan.Web.Auth.Connector.Discord"; NewId = "Koan.Web.Auth.Connector.Discord"; NewSrcPath = "src\\Connectors\\Web\\Auth\\Discord" },
    @{ OldId = "Koan.Web.Auth.Connector.Google"; NewId = "Koan.Web.Auth.Connector.Google"; NewSrcPath = "src\\Connectors\\Web\\Auth\\Google" },
    @{ OldId = "Koan.Web.Auth.Connector.Microsoft"; NewId = "Koan.Web.Auth.Connector.Microsoft"; NewSrcPath = "src\\Connectors\\Web\\Auth\\Microsoft" },
    @{ OldId = "Koan.Web.Auth.Connector.Oidc"; NewId = "Koan.Web.Auth.Connector.Oidc"; NewSrcPath = "src\\Connectors\\Web\\Auth\\Oidc" },
    @{ OldId = "Koan.Web.Auth.Connector.Test"; NewId = "Koan.Web.Auth.Connector.Test"; NewSrcPath = "src\\Connectors\\Web\\Auth\\Test" },
    @{ OldId = "Koan.Web.Connector.GraphQl"; NewId = "Koan.Web.Connector.GraphQl"; NewSrcPath = "src\\Connectors\\Web\\GraphQl" },
    @{ OldId = "Koan.Web.Connector.Swagger"; NewId = "Koan.Web.Connector.Swagger"; NewSrcPath = "src\\Connectors\\Web\\Swagger" }
)

$testSuffixes = @("Tests", "IntegrationTests", "E2ETests", "E2E.Tests", "FunctionalTests", "EndToEndTests")

foreach ($map in $mappings) {
    $oldSrcPath = Join-Path $repoRoot ("src\" + $map.OldId)
    if (Test-Path $oldSrcPath) {
        $newSrcFull = Join-Path $repoRoot $map.NewSrcPath
        $parentDir = Split-Path $newSrcFull -Parent
        if (-not (Test-Path $parentDir)) {
            New-Item -ItemType Directory -Path $parentDir -Force | Out-Null
        }
        if (Test-Path $newSrcFull) {
            Remove-Item -Path $newSrcFull -Recurse -Force
        }
        Move-Item -Path $oldSrcPath -Destination $newSrcFull
        $oldProjPath = Join-Path $newSrcFull ("{0}.csproj" -f $map.OldId)
        if (Test-Path $oldProjPath) {
            Rename-Item -Path $oldProjPath -NewName ("{0}.csproj" -f $map.NewId)
        }
    }

    foreach ($suffix in $testSuffixes) {
        $oldTestPath = Join-Path $repoRoot ("tests\{0}.{1}" -f $map.OldId, $suffix)
        if (Test-Path $oldTestPath) {
            $newTestName = "{0}.{1}" -f $map.NewId, $suffix
            Rename-Item -Path $oldTestPath -NewName $newTestName
        }
    }
}

$pathReplacements = New-Object System.Collections.Generic.List[pscustomobject]
foreach ($map in $mappings) {
    $oldSrcRel = "src\" + $map.OldId
    $newSrcRel = $map.NewSrcPath
    $pathReplacements.Add([pscustomobject]@{ Old = $oldSrcRel; New = $newSrcRel })
    $pathReplacements.Add([pscustomobject]@{ Old = $oldSrcRel.Replace('\\', '/'); New = $newSrcRel.Replace('\\', '/') })

    foreach ($suffix in $testSuffixes) {
        $oldTestRel = "tests\" + ("{0}.{1}" -f $map.OldId, $suffix)
        $newTestRel = "tests\" + ("{0}.{1}" -f $map.NewId, $suffix)
        $pathReplacements.Add([pscustomobject]@{ Old = $oldTestRel; New = $newTestRel })
        $pathReplacements.Add([pscustomobject]@{ Old = $oldTestRel.Replace('\\', '/'); New = $newTestRel.Replace('\\', '/') })
    }
}

$idReplacements = New-Object System.Collections.Generic.List[pscustomobject]
foreach ($map in $mappings) {
    $idReplacements.Add([pscustomobject]@{ Old = $map.OldId; New = $map.NewId })
    $idReplacements.Add([pscustomobject]@{ Old = $map.OldId + ".Tests"; New = $map.NewId + ".Tests" })
    $idReplacements.Add([pscustomobject]@{ Old = $map.OldId + ".IntegrationTests"; New = $map.NewId + ".IntegrationTests" })
    $idReplacements.Add([pscustomobject]@{ Old = $map.OldId + ".E2ETests"; New = $map.NewId + ".E2ETests" })
    $idReplacements.Add([pscustomobject]@{ Old = $map.OldId + ".E2E.Tests"; New = $map.NewId + ".E2E.Tests" })
    $idReplacements.Add([pscustomobject]@{ Old = $map.OldId + ".FunctionalTests"; New = $map.NewId + ".FunctionalTests" })
    $idReplacements.Add([pscustomobject]@{ Old = $map.OldId + ".EndToEndTests"; New = $map.NewId + ".EndToEndTests" })
}

$targetExtensions = @(
    ".cs", ".csproj", ".sln", ".props", ".targets", ".ps1", ".psm1", ".json", ".yml", ".yaml", ".md", ".cshtml", ".razor", ".config", ".xml", ".nuspec", ".tt", ".proj", ".fsproj", ".vbproj", ".sh", ".bat", ".cmd", ".psd1"
)

$files = Get-ChildItem -Path $repoRoot -Recurse -File | Where-Object {
    $_.FullName -notmatch "\\(bin|obj|artifacts|TestResults|packages|.git|.vs|node_modules)\\"
}

foreach ($file in $files) {
    if ($targetExtensions.Count -gt 0) {
        $ext = $file.Extension
        if ($ext) {
            $ext = $ext.ToLower()
            if (-not $targetExtensions.Contains($ext)) {
                continue
            }
        }
    }

    try {
        $fullPath = $file.FullName
        if ($fullPath -match "\\samples\\S5\.Recs\\data\\" -or $fullPath -match "\\\.mongodb\\") {
            continue
        }

        $content = Get-Content -Path $file.FullName -Raw
        if ($null -eq $content) {
            continue
        }

        $updated = $content

        foreach ($replacement in $pathReplacements) {
            $updated = $updated.Replace($replacement.Old, $replacement.New)
        }

        foreach ($replacement in $idReplacements) {
            $updated = $updated.Replace($replacement.Old, $replacement.New)
        }

        if ($updated -ne $content) {
            Set-Content -Path $file.FullName -Value $updated -Encoding UTF8
        }
    }
    catch {
        Write-Error "Failed processing $($file.FullName): $($_.Exception.Message)"
        throw
    }
}

Write-Host "Connector rename completed."
