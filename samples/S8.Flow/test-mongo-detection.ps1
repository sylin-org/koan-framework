# Test MongoDB Auto-Detection Script
Write-Host "Testing MongoDB Auto-Detection..." -ForegroundColor Green

Write-Host "`n=== Test 1: Default behavior (no MongoDB running) ===" -ForegroundColor Yellow
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DOTNET_RUNNING_IN_CONTAINER = ""
$env:Koan_DATA_MONGO_URLS = ""

Write-Host "Starting application with Development environment..."
Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "S8.Flow.Api", "--no-build" -Wait -WindowStyle Hidden

Write-Host "`n=== Test 2: With container environment simulation ===" -ForegroundColor Yellow
$env:DOTNET_RUNNING_IN_CONTAINER = "true"
Write-Host "Setting DOTNET_RUNNING_IN_CONTAINER=true"
Write-Host "Starting application simulating container environment..."
Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "S8.Flow.Api", "--no-build" -Wait -WindowStyle Hidden

Write-Host "`n=== Test 3: With explicit MongoDB URLs ===" -ForegroundColor Yellow
$env:Koan_DATA_MONGO_URLS = "mongodb://localhost:27017,mongodb://mongo:27017"
Write-Host "Setting Koan_DATA_MONGO_URLS=mongodb://localhost:27017,mongodb://mongo:27017"
Write-Host "Starting application with explicit URL list..."
Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "S8.Flow.Api", "--no-build" -Wait -WindowStyle Hidden

Write-Host "`n=== Test 4: Production environment ===" -ForegroundColor Yellow
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:DOTNET_RUNNING_IN_CONTAINER = ""
$env:Koan_DATA_MONGO_URLS = ""
Write-Host "Setting environment to Production"
Write-Host "Starting application in production mode..."
Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "S8.Flow.Api", "--no-build" -Wait -WindowStyle Hidden

Write-Host "`nMongoDB Auto-Detection tests completed!" -ForegroundColor Green