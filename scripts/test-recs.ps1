# Smoke test for S5.Recs API endpoints
param(
    [string]$BaseUrl = 'http://localhost:5007',
    [string]$UserId = 'demo-user'
)

function Invoke-JsonPost {
    param(
        [Parameter(Mandatory=$true)][string]$Url,
        [Parameter(Mandatory=$true)][string]$Json
    )
    try {
        return Invoke-RestMethod -Method Post -Uri $Url -ContentType 'application/json' -Body $Json
    }
    catch {
        Write-Host "POST $Url failed:" -ForegroundColor Red
        if ($_.Exception.Response) {
            $resp = $_.Exception.Response
            $code = [int]$resp.StatusCode
            $sr = New-Object System.IO.StreamReader($resp.GetResponseStream())
            $content = $sr.ReadToEnd()
            Write-Host ("StatusCode: {0}" -f $code)
            Write-Host ("Body: {0}" -f $content)
        }
        else {
            Write-Host $_.Exception.ToString()
        }
        throw
    }
}

Write-Host "== Providers ==" -ForegroundColor Cyan
Invoke-RestMethod -Method Get -Uri "$BaseUrl/admin/providers" | ConvertTo-Json -Depth 5

Write-Host "== Stats ==" -ForegroundColor Cyan
Invoke-RestMethod -Method Get -Uri "$BaseUrl/admin/stats" | ConvertTo-Json -Depth 5

Write-Host "== Query (text only) ==" -ForegroundColor Cyan
$q1 = @{ text = 'space opera with mecha and politics'; topK = 6 } | ConvertTo-Json
$r1 = Invoke-JsonPost -Url "$BaseUrl/api/recs/query" -Json $q1
$r1 | ConvertTo-Json -Depth 6

if ($r1.items -and $r1.items.Count -gt 0) {
    $animeId = $r1.items[0].anime.id
    Write-Host "== Rate top result and re-query with userId ==" -ForegroundColor Cyan
    $rate = @{ userId = $UserId; animeId = $animeId; rating = 4 } | ConvertTo-Json
    Invoke-JsonPost -Url "$BaseUrl/api/recs/rate" -Json $rate | Out-Null

    $q2 = @{ text = 'space opera with mecha and politics'; topK = 6; userId = $UserId } | ConvertTo-Json
    $r2 = Invoke-JsonPost -Url "$BaseUrl/api/recs/query" -Json $q2
    $r2 | ConvertTo-Json -Depth 6
}
else {
    Write-Host "No items returned from initial query; skipping rate test." -ForegroundColor Yellow
}
