param()

function Invoke-MeridianRequest {
    param(
        [Parameter(Mandatory)] [string]$BaseUrl,
        [Parameter(Mandatory)] [string]$Path,
        [ValidateSet('GET','POST','PUT','PATCH','DELETE')] [string]$Method = 'GET',
        $Body = $null,
        [switch]$SkipCertificateCheck
    )

    $uri = if ($Path.StartsWith('http')) { $Path } else { ($BaseUrl.TrimEnd('/') + $Path) }
    $invokeParams = @{
        Uri     = $uri
        Method  = $Method
        Headers = @{ 'Accept' = 'application/json' }
    }

    if ($SkipCertificateCheck) {
        $invokeParams.SkipCertificateCheck = $true
    }

    if ($Body -ne $null) {
        $invokeParams.ContentType = 'application/json'
        $invokeParams.Body = $Body | ConvertTo-Json -Depth 10
    }

    try {
        return Invoke-RestMethod @invokeParams
    }
    catch {
        throw "Request to $uri failed: $($_.Exception.Message)"
    }
}

function Get-MeridianCollection {
    param(
        [string]$BaseUrl,
        [string]$Path,
        [switch]$SkipCertificateCheck
    )

    $response = Invoke-MeridianRequest -BaseUrl $BaseUrl -Path $Path -SkipCertificateCheck:$SkipCertificateCheck
    if ($response -eq $null) {
        return @()
    }

    if ($response.PSObject.Properties.Name -contains 'items') {
        return $response.items
    }

    if ($response.PSObject.Properties.Name -contains 'value') {
        return $response.value
    }

    if ($response -is [System.Collections.IEnumerable] -and -not ($response -is [string])) {
        return $response
    }

    return @($response)
}

function Get-Property {
    param(
        $Object,
        [string[]]$Names
    )

    foreach ($name in $Names) {
        if ($Object -and $Object.PSObject.Properties.Name -contains $name) {
            return $Object.$name
        }
    }

    return $null
}

function ConvertTo-Slug {
    param(
        [string]$Value,
        [string]$Separator = '-'
    )

    $slug = $Value.ToLowerInvariant()
    $slug = $slug -replace '[^a-z0-9]+', $Separator
    $slug = $slug.Trim($Separator.ToCharArray())
    if ([string]::IsNullOrWhiteSpace($slug)) {
        $slug = 'item'
    }

    return $slug
}

function ConvertTo-TitleCaseSafe {
    param(
        [string]$Value,
        [int]$MaxLength = 80,
        [string]$EmptyFallback = "Generated Item"
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $EmptyFallback
    }

    $normalized = ($Value -replace '[^a-zA-Z0-9 ]', ' ').Trim()
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        return $EmptyFallback
    }

    $title = [System.Globalization.CultureInfo]::InvariantCulture.TextInfo.ToTitleCase($normalized.ToLowerInvariant())
    if ($title.Length -gt $MaxLength) {
        $title = $title.Substring(0, $MaxLength).Trim()
    }

    return $title
}

function Normalize-List {
    param(
        [Parameter()] $Value,
        [switch]$AllowSplit
    )

    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [System.Collections.IEnumerable] -and -not ($Value -is [string])) {
        return @(
            $Value |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.ToString().Trim() } |
            Where-Object { $_ }
        )
    }

    $text = $Value.ToString().Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return @()
    }

    if ($AllowSplit -and $text.Contains('|')) {
        return @(
            $text.Split('|') |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ }
        )
    }

    return @($text)
}

function Ensure-MeridianSourceTypeAi {
    param(
        [string]$BaseUrl,
        [string]$Prompt,
        [string[]]$TargetFields = @(),
        [string[]]$DesiredTags = @(),
        [switch]$SkipCertificateCheck
    )

    $existing = Get-MeridianCollection -BaseUrl $BaseUrl -Path '/api/sourcetypes?size=100' -SkipCertificateCheck:$SkipCertificateCheck
    $match = $existing | Where-Object { $_.description -eq $Prompt }
    if ($match) {
        return $match
    }

    $request = @{ seedText = $Prompt }
    if ($TargetFields.Count -gt 0) { $request.targetFields = $TargetFields }
    if ($DesiredTags.Count -gt 0) { $request.desiredTags = $DesiredTags }

    $response = Invoke-MeridianRequest -BaseUrl $BaseUrl -Path '/api/sourcetypes/ai-suggest' -Method 'POST' -Body $request -SkipCertificateCheck:$SkipCertificateCheck
    $draft = $response.draft
    if (-not $draft) {
        throw "AI did not return a source type draft."
    }

    $tags = Normalize-List $draft.Tags
    if ($tags.Count -eq 0 -and $DesiredTags.Count -gt 0) {
        $tags = @($DesiredTags | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }

    $descriptors = Normalize-List $draft.Descriptors
    $filenamePatterns = Normalize-List -Value $draft.FilenamePatterns -AllowSplit
    $keywords = Normalize-List $draft.Keywords
    $mimeTypes = Normalize-List $draft.MimeTypes
    if ($mimeTypes.Count -eq 0) {
        $mimeTypes = @('text/plain')
    }

    $fieldQueries = @{}
    if ($draft.FieldQueries -is [System.Collections.IDictionary]) {
        foreach ($key in $draft.FieldQueries.Keys) {
            $cleanKey = $key.ToString().Trim()
            $cleanValue = $draft.FieldQueries[$key]
            if ([string]::IsNullOrWhiteSpace($cleanKey) -or [string]::IsNullOrWhiteSpace($cleanValue)) {
                continue
            }

            $fieldQueries[$cleanKey] = $cleanValue.ToString().Trim()
        }
    }

    $payload = [ordered]@{
        Name            = $draft.Name
        Description     = if ($draft.Description) { $draft.Description } else { $Prompt }
        Version         = 1
        Tags            = $tags
        Descriptors     = $descriptors
        FilenamePatterns= $filenamePatterns
        Keywords        = $keywords
        MimeTypes       = $mimeTypes
        FieldQueries    = $fieldQueries
        Instructions    = if ($draft.Instructions) { $draft.Instructions } else { "Summarize this document." }
        OutputTemplate  = if ($draft.OutputTemplate) { $draft.OutputTemplate } else { "{{SUMMARY}}" }
    }

    if ([string]::IsNullOrWhiteSpace($payload.Name)) {
        $payload.Name = ConvertTo-TitleCaseSafe -Value $Prompt -EmptyFallback "Generated Source Type"
    }

    if ($payload.FilenamePatterns.Count -eq 0) {
        $payload.FilenamePatterns = @((ConvertTo-Slug $payload.Name).Replace('-', ''))
    }

    if ($payload.Tags.Count -eq 0 -and $DesiredTags.Count -gt 0) {
        $payload.Tags = @($DesiredTags | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }

    return Invoke-MeridianRequest -BaseUrl $BaseUrl -Path '/api/sourcetypes' -Method 'POST' -Body $payload -SkipCertificateCheck:$SkipCertificateCheck
}

function Ensure-MeridianAnalysisTypeAi {
    param(
        [string]$BaseUrl,
        [string]$Goal,
        [string]$Audience = "",
        [string[]]$IncludedSourceTypes = @(),
        [string]$AdditionalContext = "",
        [switch]$SkipCertificateCheck
    )

    $existing = Get-MeridianCollection -BaseUrl $BaseUrl -Path '/api/analysistypes?size=100' -SkipCertificateCheck:$SkipCertificateCheck
    $match = $existing | Where-Object { $_.description -eq $Goal }
    if ($match) {
        return $match
    }

    $request = @{ goal = $Goal }
    if (-not [string]::IsNullOrWhiteSpace($Audience)) { $request.audience = $Audience }
    if ($IncludedSourceTypes.Count -gt 0) { $request.includedSourceTypes = $IncludedSourceTypes }
    if (-not [string]::IsNullOrWhiteSpace($AdditionalContext)) { $request.additionalContext = $AdditionalContext }

    $response = Invoke-MeridianRequest -BaseUrl $BaseUrl -Path '/api/analysistypes/ai-suggest' -Method 'POST' -Body $request -SkipCertificateCheck:$SkipCertificateCheck
    $draft = $response.draft
    if (-not $draft) {
        throw "AI did not return an analysis type draft."
    }

    $tags = Normalize-List $draft.Tags
    $descriptors = Normalize-List $draft.Descriptors
    $requiredSources = if ($draft.RequiredSourceTypes) { Normalize-List $draft.RequiredSourceTypes } else { @($IncludedSourceTypes | Where-Object { $_ }) }

    $payload = [ordered]@{
        Name                = $draft.Name
        Description         = if ($draft.Description) { $draft.Description } else { $Goal }
        Instructions        = if ($draft.Instructions) { $draft.Instructions } else { $Goal }
        OutputTemplate      = if ($draft.OutputTemplate) { $draft.OutputTemplate } else { "# Analysis`n{{SUMMARY}}" }
        Tags                = $tags
        Descriptors         = $descriptors
        RequiredSourceTypes = $requiredSources
    }

    if ([string]::IsNullOrWhiteSpace($payload.Name)) {
        $payload.Name = ConvertTo-TitleCaseSafe -Value $Goal -EmptyFallback "Generated Analysis Type"
    }

    if ($payload.RequiredSourceTypes.Count -eq 0 -and $IncludedSourceTypes.Count -gt 0) {
        $payload.RequiredSourceTypes = @($IncludedSourceTypes | Where-Object { $_ })
    }

    return Invoke-MeridianRequest -BaseUrl $BaseUrl -Path '/api/analysistypes' -Method 'POST' -Body $payload -SkipCertificateCheck:$SkipCertificateCheck
}

function Ensure-MeridianDeliverableType {
    param(
        [string]$BaseUrl,
        [hashtable]$Definition,
        [switch]$SkipCertificateCheck
    )

    $existing = Get-MeridianCollection -BaseUrl $BaseUrl -Path '/api/deliverabletypes?size=100' -SkipCertificateCheck:$SkipCertificateCheck
    $match = $existing | Where-Object { $_.Name -eq $Definition.Name }
    if ($null -ne $match) {
        return $match
    }

    return Invoke-MeridianRequest -BaseUrl $BaseUrl -Path '/api/deliverabletypes' -Method 'POST' -Body $Definition -SkipCertificateCheck:$SkipCertificateCheck
}

function New-MeridianPipeline {
    param(
        [string]$BaseUrl,
        [hashtable]$Definition,
        [switch]$SkipCertificateCheck
    )

    return Invoke-MeridianRequest -BaseUrl $BaseUrl -Path '/api/pipelines' -Method 'POST' -Body $Definition -SkipCertificateCheck:$SkipCertificateCheck
}

function New-MeridianDocumentFile {
    param(
        [string]$Content,
        [string]$FileName = "document.txt"
    )

    $tempPath = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString('N') + '-' + $FileName)
    Set-Content -Path $tempPath -Value $Content -Encoding UTF8
    return $tempPath
}

function Upload-MeridianDocument {
    param(
        [string]$BaseUrl,
        [string]$PipelineId,
        [string]$FilePath,
        [switch]$SkipCertificateCheck
    )

    $form = @{ files = Get-Item -Path $FilePath }
    $uri = "$BaseUrl/api/pipelines/$PipelineId/documents"
    $invokeParams = @{ Uri = $uri; Method = 'POST'; Form = $form }
    if ($SkipCertificateCheck) { $invokeParams.SkipCertificateCheck = $true }

    $response = Invoke-WebRequest @invokeParams
    return $response.Content | ConvertFrom-Json
}

function Upload-MeridianDocumentContent {
    param(
        [string]$BaseUrl,
        [string]$PipelineId,
        [string]$FileName,
        [string]$Content,
        [switch]$SkipCertificateCheck
    )

    $file = New-MeridianDocumentFile -Content $Content -FileName $FileName
    return Upload-MeridianDocument -BaseUrl $BaseUrl -PipelineId $PipelineId -FilePath $file -SkipCertificateCheck:$SkipCertificateCheck
}

function Wait-MeridianJob {
    param(
        [string]$BaseUrl,
        [string]$PipelineId,
        [string]$JobId,
        [int]$TimeoutSeconds = 180,
        [switch]$SkipCertificateCheck
    )

    $start = Get-Date
    do {
        Start-Sleep -Seconds 2
        try {
            $job = Invoke-MeridianRequest -BaseUrl $BaseUrl -Path "/api/pipelines/$PipelineId/jobs/$JobId" -SkipCertificateCheck:$SkipCertificateCheck
        }
        catch {
            continue
        }

        if ($job.Status -in @('Completed','Failed')) {
            return $job
        }
    }
    while ((Get-Date) - $start -lt [TimeSpan]::FromSeconds($TimeoutSeconds))

    throw "Job $JobId did not finish within $TimeoutSeconds seconds."
}

function Get-MeridianDeliverable {
    param(
        [string]$BaseUrl,
        [string]$PipelineId,
        [switch]$SkipCertificateCheck
    )

    try {
        return Invoke-MeridianRequest -BaseUrl $BaseUrl -Path "/api/pipelines/$PipelineId/deliverables/latest" -SkipCertificateCheck:$SkipCertificateCheck
    }
    catch {
        return $null
    }
}

