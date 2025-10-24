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
        $exception = $_.Exception
        $statusDescription = $exception.Message
        $responseBody = $null

        $response = $null
        if ($exception.PSObject.Properties.Name -contains 'Response') {
            $response = $exception.Response
        }
        elseif ($exception.InnerException -and $exception.InnerException.PSObject.Properties.Name -contains 'Response') {
            $response = $exception.InnerException.Response
        }

        if ($response -is [System.Net.Http.HttpResponseMessage]) {
            $statusDescription = "{0} ({1})" -f [int]$response.StatusCode, $response.ReasonPhrase
            try {
                $responseBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            }
            catch { }
        }
        elseif ($response -is [System.Net.WebResponse]) {
            try {
                $statusDescription = "{0} ({1})" -f [int]$response.StatusCode, $response.StatusDescription
            }
            catch { }

            try {
                $stream = $response.GetResponseStream()
                if ($stream) {
                    $reader = New-Object System.IO.StreamReader($stream)
                    $responseBody = $reader.ReadToEnd()
                }
            }
            catch { }
        }

        if (-not [string]::IsNullOrWhiteSpace($responseBody)) {
            Write-Warning "Request to $uri failed with response: $responseBody"
            throw "Request to $uri failed: $statusDescription. Response body: $responseBody"
        }

        throw "Request to $uri failed: $statusDescription"
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

function Get-MeridianContentType {
    param(
        [string]$FilePath
    )

    if ([string]::IsNullOrWhiteSpace($FilePath)) {
        return 'text/plain'
    }

    $extension = [System.IO.Path]::GetExtension($FilePath)
    switch ($extension.ToLowerInvariant()) {
        '.txt' { return 'text/plain' }
        '.md' { return 'text/markdown' }
        '.pdf' { return 'application/pdf' }
        '.docx' { return 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' }
        default { return 'text/plain' }
    }
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
        [string[]]$FallbackDescriptorHints = @(),
        [string[]]$FallbackSignalPhrases = @(),
        [Nullable[bool]]$SupportsManualSelection = $null,
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
    if ($null -eq $tags) { $tags = @() }
    if ($tags.Count -eq 0 -and $DesiredTags.Count -gt 0) {
        $tags = @($DesiredTags | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }

    $descriptorHints = Normalize-List $draft.DescriptorHints
    if ($null -eq $descriptorHints) { $descriptorHints = @() }
    if ($descriptorHints.Count -eq 0 -and $FallbackDescriptorHints.Count -gt 0) {
        $descriptorHints = @($FallbackDescriptorHints | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }

    $signalPhrases = Normalize-List $draft.SignalPhrases
    if ($null -eq $signalPhrases) { $signalPhrases = @() }
    if ($signalPhrases.Count -eq 0 -and $FallbackSignalPhrases.Count -gt 0) {
        $signalPhrases = @($FallbackSignalPhrases | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }
    $mimeTypes = Normalize-List $draft.MimeTypes
    if ($null -eq $mimeTypes) { $mimeTypes = @() }
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
        Tags            = @($tags)
        DescriptorHints = @($descriptorHints)
        SignalPhrases   = @($signalPhrases)
        MimeTypes       = @($mimeTypes)
        FieldQueries    = $fieldQueries
        Instructions    = if ($draft.Instructions) { $draft.Instructions } else { "Summarize this document." }
        OutputTemplate  = if ($draft.OutputTemplate) { $draft.OutputTemplate } else { "{{SUMMARY}}" }
    }

    if ($SupportsManualSelection -ne $null) {
        $payload.SupportsManualSelection = [bool]$SupportsManualSelection
    }
    elseif ($draft.PSObject.Properties.Name -contains 'SupportsManualSelection') {
        $payload.SupportsManualSelection = [bool]$draft.SupportsManualSelection
    }
    else {
        $payload.SupportsManualSelection = $true
    }

    if ([string]::IsNullOrWhiteSpace($payload.Name)) {
        $payload.Name = ConvertTo-TitleCaseSafe -Value $Prompt -EmptyFallback "Generated Source Type"
    }

    if ($payload.Tags.Count -eq 0 -and $DesiredTags.Count -gt 0) {
        $payload.Tags = @($DesiredTags | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }

    Write-Host "Payload:" ($payload | ConvertTo-Json -Depth 10)
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

    # Build a single prompt from the various parameters
    $promptParts = @($Goal)
    if (-not [string]::IsNullOrWhiteSpace($Audience)) {
        $promptParts += "Target audience: $Audience."
    }
    if (-not [string]::IsNullOrWhiteSpace($AdditionalContext)) {
        $promptParts += $AdditionalContext
    }
    $fullPrompt = $promptParts -join " "

    $existing = Get-MeridianCollection -BaseUrl $BaseUrl -Path '/api/analysistypes?size=100' -SkipCertificateCheck:$SkipCertificateCheck
    $match = $existing | Where-Object { $_.description -eq $fullPrompt -or $_.description -eq $Goal }
    if ($match) {
        return $match
    }

    $request = @{ prompt = $fullPrompt }

    $response = Invoke-MeridianRequest -BaseUrl $BaseUrl -Path '/api/analysistypes/ai-suggest' -Method 'POST' -Body $request -SkipCertificateCheck:$SkipCertificateCheck
    $draft = $response.draft
    if (-not $draft) {
        throw "AI did not return an analysis type draft."
    }

    $tags = Normalize-List $draft.Tags
    if ($null -eq $tags) { $tags = @() }
    $descriptors = Normalize-List $draft.Descriptors
    if ($null -eq $descriptors) { $descriptors = @() }

    $payload = [ordered]@{
        Name                = $draft.Name
        Description         = if ($draft.Description) { $draft.Description } else { $fullPrompt }
        Instructions        = if ($draft.Instructions) { $draft.Instructions } else { $fullPrompt }
        OutputTemplate      = if ($draft.OutputTemplate) { $draft.OutputTemplate } else { "# Analysis`n{{SUMMARY}}" }
        JsonSchema          = if ($draft.JsonSchema) { $draft.JsonSchema } else { "{}" }
        Tags                = $tags
        Descriptors         = $descriptors
    }

    if ([string]::IsNullOrWhiteSpace($payload.Name)) {
        $payload.Name = ConvertTo-TitleCaseSafe -Value $fullPrompt -EmptyFallback "Generated Analysis Type"
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
        [string]$ContentType = "",
        [switch]$SkipCertificateCheck
    )

    if (-not (Test-Path -Path $FilePath -PathType Leaf)) {
        throw "File '$FilePath' was not found."
    }

    $uri = "$BaseUrl/api/pipelines/$PipelineId/documents"
    $resolvedContentType = if ([string]::IsNullOrWhiteSpace($ContentType)) { Get-MeridianContentType -FilePath $FilePath } else { $ContentType }

    $handler = New-Object System.Net.Http.HttpClientHandler
    if ($SkipCertificateCheck) {
        $handler.ServerCertificateCustomValidationCallback = { $true }
    }

    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [System.TimeSpan]::FromMinutes(5)

    try {
        $stream = [System.IO.File]::OpenRead($FilePath)
        try {
            $fileContent = New-Object System.Net.Http.StreamContent($stream)
            $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($resolvedContentType)

            $form = New-Object System.Net.Http.MultipartFormDataContent
            $form.Add($fileContent, 'files', [System.IO.Path]::GetFileName($FilePath))

            $response = $client.PostAsync($uri, $form).GetAwaiter().GetResult()
            $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

            if (-not $response.IsSuccessStatusCode) {
                $status = "{0} ({1})" -f [int]$response.StatusCode, $response.ReasonPhrase
                if (-not [string]::IsNullOrWhiteSpace($body)) {
                    throw "Upload to $uri failed: $status. Response body: $body"
                }

                throw "Upload to $uri failed: $status"
            }

            if ([string]::IsNullOrWhiteSpace($body)) {
                return $null
            }

            return $body | ConvertFrom-Json
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $client.Dispose()
        $handler.Dispose()
    }
}

function Upload-MeridianDocumentContent {
    param(
        [string]$BaseUrl,
        [string]$PipelineId,
        [string]$FileName,
        [string]$Content,
        [string]$ContentType = "",
        [switch]$SkipCertificateCheck
    )

    $file = New-MeridianDocumentFile -Content $Content -FileName $FileName
    return Upload-MeridianDocument -BaseUrl $BaseUrl -PipelineId $PipelineId -FilePath $file -ContentType $ContentType -SkipCertificateCheck:$SkipCertificateCheck
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
    $previousStatus = $null
    $lastTransition = $start
    do {
        Start-Sleep -Seconds 2
        try {
            $job = Invoke-MeridianRequest -BaseUrl $BaseUrl -Path "/api/pipelines/$PipelineId/jobs/$JobId" -SkipCertificateCheck:$SkipCertificateCheck
        }
        catch {
            continue
        }

        if ($job.Status -in @('Completed','Failed','Cancelled')) {
            return $job
        }

        if ($job.Status -ne $previousStatus) {
            $previousStatus = $job.Status
            $lastTransition = Get-Date
        }

        $heartbeat = $null
        if ($job.PSObject.Properties.Name -contains 'HeartbeatAt' -and $job.HeartbeatAt) {
            try {
                $heartbeat = [datetime]::Parse($job.HeartbeatAt)
            } catch {
                $heartbeat = $null
            }
        }

        $staleHeartbeat = $false
        if ($heartbeat) {
            $staleHeartbeat = (Get-Date) - $heartbeat -gt [TimeSpan]::FromMinutes(3)
        }

        $agedState = (Get-Date) - $lastTransition -gt [TimeSpan]::FromSeconds($TimeoutSeconds / 3)

        if ($job.Status -eq 'Pending' -and ($staleHeartbeat -or $agedState)) {
            $cancelled = Stop-MeridianJob -BaseUrl $BaseUrl -PipelineId $PipelineId -JobId $JobId -Reason "stale" -SkipCertificateCheck:$SkipCertificateCheck
            if ($cancelled) {
                return $cancelled
            }
        }
    }
    while ((Get-Date) - $start -lt [TimeSpan]::FromSeconds($TimeoutSeconds))

    $cancelled = Stop-MeridianJob -BaseUrl $BaseUrl -PipelineId $PipelineId -JobId $JobId -Reason "timeout" -SkipCertificateCheck:$SkipCertificateCheck
    if ($cancelled) {
        return $cancelled
    }

    throw "Job $JobId did not finish within $TimeoutSeconds seconds."
}

function Stop-MeridianJob {
    param(
        [string]$BaseUrl,
        [string]$PipelineId,
        [string]$JobId,
        [string]$Reason = "cancelled",
        [switch]$SkipCertificateCheck
    )

    try {
        $response = Invoke-MeridianRequest -BaseUrl $BaseUrl -Path "/api/pipelines/$PipelineId/jobs/$JobId/cancel" -Method 'POST' -Body @{ reason = $Reason } -SkipCertificateCheck:$SkipCertificateCheck
        if ($response) {
            return $response
        }
    }
    catch {
        Write-Warning "Failed to cancel job ${JobId}: $_"
    }

    return $null
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

