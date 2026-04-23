# =============================================
# Apply Database Migration Script
# Order Processing System - Initial Migration
# =============================================

param(
    [string]$ServerName = "localhost,1433",
    [string]$DatabaseName = "OrderProcessingDB",
    [string]$Username = "sa",
    [string]$Password = "YourStrong@Passw0rd",
    [string]$MigrationScriptPath = ".\Data\Migrations\20260423000001_InitialCreate.sql"
)

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Order Processing System - Database Migration" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Check if the migration script exists
if (-not (Test-Path $MigrationScriptPath)) {
    Write-Host "Error: Migration script not found at: $MigrationScriptPath" -ForegroundColor Red
    exit 1
}

Write-Host "Migration Script: $MigrationScriptPath" -ForegroundColor Green
Write-Host "Server: $ServerName" -ForegroundColor Green
Write-Host "Database: $DatabaseName" -ForegroundColor Green
Write-Host ""

try {
    # Load the SQL script
    Write-Host "Loading migration script..." -ForegroundColor Yellow
    $sqlScript = Get-Content -Path $MigrationScriptPath -Raw
    
    # Create connection string
    $connectionString = "Server=$ServerName;User Id=$Username;Password=$Password;TrustServerCertificate=True;MultipleActiveResultSets=true"
    
    Write-Host "Connecting to SQL Server..." -ForegroundColor Yellow
    
    # Create SQL connection
    $connection = New-Object System.Data.SqlClient.SqlConnection
    $connection.ConnectionString = $connectionString
    $connection.Open()
    
    Write-Host "Connected successfully!" -ForegroundColor Green
    Write-Host ""
    
    # Execute the migration script
    Write-Host "Executing migration script..." -ForegroundColor Yellow
    Write-Host "This may take a few moments..." -ForegroundColor Yellow
    Write-Host ""
    
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 300 # 5 minutes timeout
    $command.CommandText = $sqlScript
    
    # Execute and capture messages
    $command.ExecuteNonQuery() | Out-Null
    
    Write-Host ""
    Write-Host "=========================================" -ForegroundColor Green
    Write-Host "Migration Applied Successfully!" -ForegroundColor Green
    Write-Host "=========================================" -ForegroundColor Green
    Write-Host ""
    
    # Verify the migration
    Write-Host "Verifying migration..." -ForegroundColor Yellow
    
    $verifyCommand = $connection.CreateCommand()
    $verifyCommand.CommandText = @"
SELECT 
    t.name AS TableName,
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id = t.object_id AND index_id > 0) AS IndexCount
FROM sys.tables t
WHERE t.name IN ('Orders', 'IdempotencyKeys', 'OrderAuditLogs', '__EFMigrationsHistory')
ORDER BY t.name
"@
    
    $reader = $verifyCommand.ExecuteReader()
    
    Write-Host ""
    Write-Host "Tables Created:" -ForegroundColor Cyan
    Write-Host "----------------------------------------"
    
    $tableCount = 0
    while ($reader.Read()) {
        $tableName = $reader["TableName"]
        $indexCount = $reader["IndexCount"]
        Write-Host "  - $tableName (Indexes: $indexCount)" -ForegroundColor White
        $tableCount++
    }
    $reader.Close()
    
    Write-Host "----------------------------------------"
    Write-Host "Total Tables: $tableCount" -ForegroundColor Green
    Write-Host ""
    
    # Check migration history
    $historyCommand = $connection.CreateCommand()
    $historyCommand.CommandText = "SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory"
    $historyReader = $historyCommand.ExecuteReader()
    
    Write-Host "Migration History:" -ForegroundColor Cyan
    Write-Host "----------------------------------------"
    
    while ($historyReader.Read()) {
        $migrationId = $historyReader["MigrationId"]
        $productVersion = $historyReader["ProductVersion"]
        Write-Host "  - $migrationId (EF Core $productVersion)" -ForegroundColor White
    }
    $historyReader.Close()
    
    Write-Host "----------------------------------------"
    Write-Host ""
    Write-Host "Database is ready for production use! ✓" -ForegroundColor Green
    Write-Host ""
    
    $connection.Close()
    exit 0
}
catch {
    Write-Host ""
    Write-Host "=========================================" -ForegroundColor Red
    Write-Host "Error Applying Migration" -ForegroundColor Red
    Write-Host "=========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error Message:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Stack Trace:" -ForegroundColor Red
    Write-Host $_.Exception.StackTrace -ForegroundColor Yellow
    Write-Host ""
    
    if ($connection.State -eq 'Open') {
        $connection.Close()
    }
    
    exit 1
}
