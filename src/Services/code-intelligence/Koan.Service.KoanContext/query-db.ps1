# Query SQLite database for partitions
$dbPath = ".koan/data/Koan.sqlite"
$targetPartition = "019a658430757076ae694ced4e2799f5"
$projectId = "019a6584-3075-7076-ae69-4ced4e2799f5"

# Load System.Data.SQLite
Add-Type -Path "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Data.SQLite\v4.0_1.0.118.0__db937bc2d44ff139\System.Data.SQLite.dll" -ErrorAction SilentlyContinue

try {
    $conn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$dbPath")
    $conn.Open()

    # List all tables
    Write-Host "`n=== All Tables in Database ===" -ForegroundColor Cyan
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;"
    $reader = $cmd.ExecuteReader()
    while ($reader.Read()) {
        $tableName = $reader.GetString(0)
        Write-Host $tableName

        # Check if this table name matches our target partition
        if ($tableName -like "*$targetPartition*") {
            Write-Host "  ^^^ FOUND TARGET PARTITION ^^^" -ForegroundColor Green
        }
    }
    $reader.Close()

    # Check if Project with that ID exists
    Write-Host "`n=== Checking for Project $projectId ===" -ForegroundColor Cyan
    $cmd.CommandText = "SELECT Id, Name, RootPath FROM Project WHERE Id = @id;"
    $cmd.Parameters.AddWithValue("@id", $projectId) | Out-Null
    $reader = $cmd.ExecuteReader()
    if ($reader.Read()) {
        Write-Host "Project found!" -ForegroundColor Green
        Write-Host "  ID: $($reader.GetString(0))"
        Write-Host "  Name: $($reader.GetString(1))"
        Write-Host "  RootPath: $($reader.GetString(2))"
    } else {
        Write-Host "Project NOT found in database" -ForegroundColor Red
    }
    $reader.Close()

    # List all projects
    Write-Host "`n=== All Projects ===" -ForegroundColor Cyan
    $cmd.CommandText = "SELECT Id, Name FROM Project;"
    $cmd.Parameters.Clear()
    $reader = $cmd.ExecuteReader()
    while ($reader.Read()) {
        $pid = $reader.GetString(0)
        $pname = $reader.GetString(1)
    $partitionId = $pid.Replace("-", "")
    Write-Host "$pid ($pname) -> partition token: $partitionId"

        if ($partitionId -eq $targetPartition) {
            Write-Host "  ^^^ THIS IS THE TARGET PARTITION ^^^" -ForegroundColor Green
        }
    }
    $reader.Close()

    $conn.Close()
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host "Trying with System.Data.SQLite from NuGet..."

    # Alternative: use dotnet tool
    Write-Host "`n=== Using direct file check ===" -ForegroundColor Yellow
    if (Test-Path $dbPath) {
        Write-Host "Database file exists at: $dbPath" -ForegroundColor Green
        Write-Host "File size: $((Get-Item $dbPath).Length) bytes"
    } else {
        Write-Host "Database file NOT found at: $dbPath" -ForegroundColor Red
    }
}
