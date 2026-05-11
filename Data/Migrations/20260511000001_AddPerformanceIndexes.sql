-- =============================================
-- Performance Optimization Indexes Migration
-- Created: 2026-05-11
-- Purpose: Add indexes for frequently queried columns to optimize query performance
-- =============================================

USE [OrderProcessingDb];
GO

-- =============================================
-- Orders Table Indexes
-- =============================================

PRINT 'Creating performance indexes for Orders table...';

-- Index for OrderNumber lookups (unique, frequently used)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_OrderNumber' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX IX_Orders_OrderNumber
        ON Orders(OrderNumber)
        INCLUDE (Id, CustomerId, Status, TotalAmount, CreatedAt)
        WHERE IsDeleted = 0;
    PRINT '✓ Created index IX_Orders_OrderNumber';
END
ELSE
    PRINT '- Index IX_Orders_OrderNumber already exists';

-- Index for CustomerId filtering (frequently filtered)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_CustomerId' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Orders_CustomerId
        ON Orders(CustomerId, CreatedAt DESC)
        INCLUDE (Id, OrderNumber, Status, TotalAmount, ProductName)
        WHERE IsDeleted = 0;
    PRINT '✓ Created index IX_Orders_CustomerId';
END
ELSE
    PRINT '- Index IX_Orders_CustomerId already exists';

-- Index for Status filtering (frequently used in queries)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_Status' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Orders_Status
        ON Orders(Status, CreatedAt DESC)
        INCLUDE (Id, OrderNumber, CustomerId, TotalAmount)
        WHERE IsDeleted = 0;
    PRINT '✓ Created index IX_Orders_Status';
END
ELSE
    PRINT '- Index IX_Orders_Status already exists';

-- Index for CreatedAt date range filtering (frequently used for date filters)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_CreatedAt' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Orders_CreatedAt
        ON Orders(CreatedAt DESC, IsDeleted)
        INCLUDE (Id, OrderNumber, CustomerId, Status, TotalAmount);
    PRINT '✓ Created index IX_Orders_CreatedAt';
END
ELSE
    PRINT '- Index IX_Orders_CreatedAt already exists';

-- Index for TotalAmount range filtering (used in amount filters)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_TotalAmount' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Orders_TotalAmount
        ON Orders(TotalAmount, CreatedAt DESC)
        INCLUDE (Id, OrderNumber, CustomerId, Status)
        WHERE IsDeleted = 0;
    PRINT '✓ Created index IX_Orders_TotalAmount';
END
ELSE
    PRINT '- Index IX_Orders_TotalAmount already exists';

-- Composite index for common filter combinations (Status + CreatedAt + CustomerId)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_Status_CreatedAt_CustomerId' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Orders_Status_CreatedAt_CustomerId
        ON Orders(Status, CreatedAt DESC, CustomerId)
        INCLUDE (Id, OrderNumber, TotalAmount, ProductName)
        WHERE IsDeleted = 0;
    PRINT '✓ Created composite index IX_Orders_Status_CreatedAt_CustomerId';
END
ELSE
    PRINT '- Index IX_Orders_Status_CreatedAt_CustomerId already exists';

-- Index for soft-delete filtering (IsDeleted is frequently checked)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_IsDeleted' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Orders_IsDeleted
        ON Orders(IsDeleted, CreatedAt DESC)
        INCLUDE (Id, OrderNumber, CustomerId, Status);
    PRINT '✓ Created index IX_Orders_IsDeleted';
END
ELSE
    PRINT '- Index IX_Orders_IsDeleted already exists';

-- Index for ProductName search (used in LIKE queries)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_ProductName' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Orders_ProductName
        ON Orders(ProductName)
        INCLUDE (Id, OrderNumber, CustomerId, Status, TotalAmount, CreatedAt)
        WHERE IsDeleted = 0;
    PRINT '✓ Created index IX_Orders_ProductName';
END
ELSE
    PRINT '- Index IX_Orders_ProductName already exists';

-- =============================================
-- IdempotencyKeys Table Indexes
-- =============================================

PRINT 'Creating performance indexes for IdempotencyKeys table...';

-- Index for Key lookups (primary lookup field)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IdempotencyKeys_Key' AND object_id = OBJECT_ID('IdempotencyKeys'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX IX_IdempotencyKeys_Key
        ON IdempotencyKeys([Key])
        INCLUDE (OrderId, Status, ResponseData, ExpiresAt);
    PRINT '✓ Created index IX_IdempotencyKeys_Key';
END
ELSE
    PRINT '- Index IX_IdempotencyKeys_Key already exists';

-- Index for ExpiresAt (used in cleanup operations)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IdempotencyKeys_ExpiresAt' AND object_id = OBJECT_ID('IdempotencyKeys'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_IdempotencyKeys_ExpiresAt
        ON IdempotencyKeys(ExpiresAt)
        INCLUDE (Id, [Key]);
    PRINT '✓ Created index IX_IdempotencyKeys_ExpiresAt';
END
ELSE
    PRINT '- Index IX_IdempotencyKeys_ExpiresAt already exists';

-- Index for OrderId lookups (foreign key optimization)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IdempotencyKeys_OrderId' AND object_id = OBJECT_ID('IdempotencyKeys'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_IdempotencyKeys_OrderId
        ON IdempotencyKeys(OrderId)
        INCLUDE ([Key], Status, CreatedAt);
    PRINT '✓ Created index IX_IdempotencyKeys_OrderId';
END
ELSE
    PRINT '- Index IX_IdempotencyKeys_OrderId already exists';

-- =============================================
-- Update Statistics
-- =============================================

PRINT 'Updating statistics...';

UPDATE STATISTICS Orders WITH FULLSCAN;
PRINT '✓ Updated statistics for Orders table';

IF OBJECT_ID('IdempotencyKeys', 'U') IS NOT NULL
BEGIN
    UPDATE STATISTICS IdempotencyKeys WITH FULLSCAN;
    PRINT '✓ Updated statistics for IdempotencyKeys table';
END

-- =============================================
-- Index Usage Query (for monitoring)
-- =============================================

PRINT '';
PRINT '=============================================';
PRINT 'Index optimization complete!';
PRINT '=============================================';
PRINT '';
PRINT 'To monitor index usage, run:';
PRINT 'SELECT * FROM sys.dm_db_index_usage_stats WHERE database_id = DB_ID(''OrderProcessingDb'')';
PRINT '';

GO
