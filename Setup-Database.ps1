# Order Processing System - Quick Database Setup
Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Order Processing System - Quick Start" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Check Docker
Write-Host "[1/4] Checking Docker..." -ForegroundColor Yellow
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: Docker not installed!" -ForegroundColor Red
    exit 1
}
Write-Host "Docker OK" -ForegroundColor Green
Write-Host ""

# Start containers
Write-Host "[2/4] Starting containers..." -ForegroundColor Yellow
Push-Location Docker
docker-compose up -d
Pop-Location
Write-Host "Containers started" -ForegroundColor Green
Write-Host ""

# Wait for SQL Server
Write-Host "[3/4] Waiting for SQL Server..." -ForegroundColor Yellow
Start-Sleep -Seconds 20
$ready = $false
for ($i = 1; $i -le 20; $i++) {
    Write-Host "  Checking... attempt $i" -ForegroundColor Gray
    $result = docker exec order-processing-sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "YourStrong@Passw0rd" -Q "SELECT 1" 2>&1
    if ($LASTEXITCODE -eq 0) {
        $ready = $true
        break
    }
    Start-Sleep -Seconds 3
}

if ($ready) {
    Write-Host "SQL Server ready!" -ForegroundColor Green
} else {
    Write-Host "WARNING: SQL Server may need more time" -ForegroundColor Yellow
}
Write-Host ""

# Apply migration
Write-Host "[4/4] Applying migration..." -ForegroundColor Yellow
if (Test-Path "Data/Migrations/20260423000001_InitialCreate.sql") {
    docker cp "Data/Migrations/20260423000001_InitialCreate.sql" order-processing-sqlserver:/tmp/migration.sql
    docker exec order-processing-sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "YourStrong@Passw0rd" -i /tmp/migration.sql
    Write-Host "Migration applied!" -ForegroundColor Green
}
Write-Host ""

# Done
Write-Host "=========================================" -ForegroundColor Green
Write-Host "Setup Complete!" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Services:" -ForegroundColor Cyan
Write-Host "  SQL Server: localhost:1433" -ForegroundColor White
Write-Host "  Redis:      localhost:6379" -ForegroundColor White
Write-Host ""
Write-Host "Next: dotnet run" -ForegroundColor Yellow
Write-Host ""
