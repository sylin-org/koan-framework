Add-Type -Path 'C:/Windows/Microsoft.NET/assembly/GAC_MSIL/System.Data.SQLite/v4.0_1.0.118.0__db937bc2d44ff139/System.Data.SQLite.dll'

$dbPath = '.koan/data/Koan.sqlite'
if (-not (Test-Path $dbPath)) {
    Write-Error "Database file not found at $dbPath"
    exit 1
}

$jobId = '019a714a-9e62-76a0-8c86-f1bcc344a023'

$conn = [System.Data.SQLite.SQLiteConnection]::new("Data Source=$dbPath")
$conn.Open()

try {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = 'SELECT Status, VectorsSynced, ChunksCreated, ProcessedFiles FROM Job WHERE Id = @job'
    $cmd.Parameters.AddWithValue('@job', $jobId) | Out-Null
    $reader = $cmd.ExecuteReader()
    if ($reader.Read()) {
        Write-Host "Job: $jobId"
        Write-Host (' Status: {0}' -f $reader[0])
        Write-Host (' VectorsSynced: {0}' -f $reader[1])
        Write-Host (' ChunksCreated: {0}' -f $reader[2])
        Write-Host (' ProcessedFiles: {0}' -f $reader[3])
    }
    else {
        Write-Host "Job $jobId not found"
    }
    $reader.Close()

    $cmd = $conn.CreateCommand()
    $cmd.CommandText = 'SELECT COUNT(1) FROM SyncOperation WHERE JobId = @job'
    $cmd.Parameters.AddWithValue('@job', $jobId) | Out-Null
    $total = $cmd.ExecuteScalar()
    Write-Host (' SyncOperations total: {0}' -f $total)

    foreach ($status in 0..2) {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = 'SELECT COUNT(1) FROM SyncOperation WHERE JobId = @job AND Status = @status'
        $cmd.Parameters.AddWithValue('@job', $jobId) | Out-Null
        $cmd.Parameters.AddWithValue('@status', $status) | Out-Null
        $count = $cmd.ExecuteScalar()
        switch ($status) {
            0 { $label = 'Pending' }
            1 { $label = 'Completed' }
            2 { $label = 'DeadLetter' }
        }
        Write-Host (' {0}: {1}' -f $label, $count)
    }
}
finally {
    $conn.Close()
}
