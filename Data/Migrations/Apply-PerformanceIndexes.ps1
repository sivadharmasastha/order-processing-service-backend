# =============================================
# Apply Performance Indexes Migration Script
# Created: 2026-05-11
# Purpose: Apply database indexes for query optimization
# =============================================

param(
    [string]$Server = "localhost",
    [string]$Database = "OrderProcessingDb",
    [string]$Username = "",
    [string]$Password = "",
    [switch]$UseIntegratedSecurity = $true,
    [switch]$Verbose = $false
)

# Import SQL Server module if available
if (Get-Module -ListAvailable -Name SqlServer) {
    Import-Module SqlServer -ErrorAction SilentlyContinue
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Performance Indexes Migration Tool" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Configuration
$scriptPath = Join-Path $PSScriptRoot "20260511000001_AddPerformanceIndexes.sql"

# Validate script file exists
if (-not (Test-Path $scriptPath)) {
    Write-Host "ERROR: Migration script not found at: $scriptPath" -ForegroundColor Red
    Write-Host "Please ensure the SQL script is in the same directory as this PowerShell script." -ForegroundColor Yellow
    exit 1
}

Write-Host "Migration Script: $scriptPath" -ForegroundColor Gray
Write-Host "Database Server: $Server" -ForegroundColor Gray
Write-Host "Database Name: $Database" -ForegroundColor Gray
Write-Host ""

# Build connection string
if ($UseIntegratedSecurity) {
    $connectionString = "Server=$Server;Database=$Database;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=30;"
    Write-Host "Using Windows Integrated Authentication" -ForegroundColor Gray
} else {
    if ([string]::IsNullOrWhiteSpace($Username) -or [string]::IsNullOrWhiteSpace($Password)) {
        Write-Host "ERROR: Username and Password are required when not using Integrated Security" -ForegroundColor Red
        exit 1
    }
    $connectionString = "Server=$Server;Database=$Database;User Id=$Username;Password=$Password;TrustServerCertificate=True;Connection Timeout=30;"
    Write-Host "Using SQL Server Authentication" -ForegroundColor Gray
}

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Testing Database Connection..." -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Test connection
try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "✓ Database connection successful!" -ForegroundColor Green
    $connection.Close()
} catch {
    Write-Host "✗ Failed to connect to database!" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Troubleshooting Tips:" -ForegroundColor Yellow
    Write-Host "1. Verify the server name is correct: $Server" -ForegroundColor Yellow
    Write-Host "2. Ensure the database exists: $Database" -ForegroundColor Yellow
    Write-Host "3. Check your authentication credentials" -ForegroundColor Yellow
    Write-Host "4. Verify SQL Server is running and accepting connections" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Applying Performance Indexes..." -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Read and execute the SQL script
try {
    $sqlScript = Get-Content $scriptPath -Raw
    
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    $command = $connection.CreateCommand()
    $command.CommandText = $sqlScript
    $command.CommandTimeout = 300 # 5 minutes timeout for index creation
    
    if ($Verbose) {
        Write-Host "Executing SQL script..." -ForegroundColor Gray
    }
    
    # Execute and capture messages
    $connection.FireInfoMessageEventOnUserErrors = $true
    $connection.add_InfoMessage({
        param($sender, $event)
        Write-Host $event.Message -ForegroundColor Cyan
    })
    
    $result = $command.ExecuteNonQuery()
    
    $connection.Close()
    
    Write-Host ""
    Write-Host "=========================================" -ForegroundColor Green
    Write-Host "✓ Performance Indexes Applied Successfully!" -ForegroundColor Green
    Write-Host "=========================================" -ForegroundColor Green
    Write-Host ""
    
    Write-Host "Summary:" -ForegroundColor White
    Write-Host "- Orders table indexes created/verified" -ForegroundColor White
    Write-Host "- IdempotencyKeys table indexes created/verified" -ForegroundColor White
    Write-Host "- Statistics updated for optimal query performance" -ForegroundColor White
    Write-Host ""
    
    Write-Host "Next Steps:" -ForegroundColor Yellow
    Write-Host "1. Monitor index usage with SQL Server DMVs" -ForegroundColor Yellow
    Write-Host "2. Run DBCC SHOW_STATISTICS to verify statistics" -ForegroundColor Yellow
    Write-Host "3. Test query performance improvements" -ForegroundColor Yellow
    Write-Host "4. Consider query store for performance monitoring" -ForegroundColor Yellow
    Write-Host ""
    
} catch {
    Write-Host ""
    Write-Host "=========================================" -ForegroundColor Red
    Write-Host "✗ Migration Failed!" -ForegroundColor Red
    Write-Host "=========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Stack Trace:" -ForegroundColor Gray
    Write-Host $_.Exception.StackTrace -ForegroundColor Gray
    Write-Host ""
    exit 1
}

# Optional: Display index information
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Querying Created Indexes..." -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    $query = @"
SELECT 
    OBJECT_NAME(i.object_id) AS TableName,
    i.name AS IndexName,
    i.type_desc AS IndexType,
    CASE WHEN i.is_unique = 1 THEN 'Yes' ELSE 'No' END AS IsUnique,
    STUFF((
        SELECT ', ' + c.name
        FROM sys.index_columns ic
        INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
        WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
        ORDER BY ic.key_ordinal
        FOR XML PATH('')
    ), 1, 2, '') AS KeyColumns,
    STUFF((
        SELECT ', ' + c.name
        FROM sys.index_columns ic
        INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
        WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1
        FOR XML PATH('')
    ), 1, 2, '') AS IncludedColumns
FROM sys.indexes i
WHERE OBJECT_NAME(i.object_id) IN ('Orders', 'IdempotencyKeys')
    AND i.type > 0  -- Exclude heap
    AND i.name LIKE 'IX_%'
ORDER BY TableName, IndexName;
"@
    
    $command = $connection.CreateCommand()
    $command.CommandText = $query
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($command)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    
    if ($dataset.Tables[0].Rows.Count -gt 0) {
        $dataset.Tables[0] | Format-Table -AutoSize
    } else {
        Write-Host "No custom indexes found." -ForegroundColor Yellow
    }
    
    $connection.Close()
    
} catch {
    Write-Host "Warning: Could not query index information" -ForegroundColor Yellow
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "=========================================" -ForegroundColor Green
Write-Host "Migration Complete!" -ForegroundColor Green
Write-Host "=========================================" -ForegroundColor Green
Write-Host ""

# Usage examples
Write-Host "Usage Examples:" -ForegroundColor Cyan
Write-Host ""
Write-Host "# With Integrated Authentication (default):" -ForegroundColor Gray
Write-Host ".\Apply-PerformanceIndexes.ps1 -Server 'localhost' -Database 'OrderProcessingDb'" -ForegroundColor White
Write-Host ""
Write-Host "# With SQL Server Authentication:" -ForegroundColor Gray
Write-Host ".\Apply-PerformanceIndexes.ps1 -Server 'localhost' -Database 'OrderProcessingDb' -Username 'sa' -Password 'YourPassword' -UseIntegratedSecurity:`$false" -ForegroundColor White
Write-Host ""
Write-Host "# With verbose output:" -ForegroundColor Gray
Write-Host ".\Apply-PerformanceIndexes.ps1 -Verbose" -ForegroundColor White
Write-Host ""
