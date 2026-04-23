-- =============================================
-- Order Processing System - Initial Migration Script
-- Migration: 20260423000001_InitialCreate
-- Description: Creates Orders, IdempotencyKeys, and OrderAuditLogs tables
-- with all required indexes and constraints for production use
-- =============================================

USE [OrderProcessingDB]
GO

-- Ensure the database exists
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'OrderProcessingDB')
BEGIN
    CREATE DATABASE [OrderProcessingDB]
END
GO

USE [OrderProcessingDB]
GO

-- Create EF Core migrations history table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[__EFMigrationsHistory]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    )
END
GO

-- =============================================
-- Create Orders Table
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Orders]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Orders] (
        [Id] int IDENTITY(1,1) NOT NULL,
        [OrderNumber] nvarchar(50) NOT NULL,
        [CustomerId] nvarchar(100) NOT NULL,
        [ProductName] nvarchar(200) NOT NULL,
        [Quantity] int NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [Status] nvarchar(max) NOT NULL DEFAULT 'Pending',
        [IdempotencyKey] nvarchar(100) NOT NULL,
        [RetryCount] int NOT NULL DEFAULT 0,
        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] datetime2 NULL,
        [ProcessedAt] datetime2 NULL,
        [ErrorMessage] nvarchar(500) NULL,
        [IsDeleted] bit NOT NULL DEFAULT 0,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Orders] PRIMARY KEY CLUSTERED ([Id] ASC)
    )
    
    PRINT 'Orders table created successfully'
END
ELSE
BEGIN
    PRINT 'Orders table already exists'
END
GO

-- =============================================
-- Create IdempotencyKeys Table
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[IdempotencyKeys]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[IdempotencyKeys] (
        [Id] int IDENTITY(1,1) NOT NULL,
        [Key] nvarchar(100) NOT NULL,
        [OrderId] int NOT NULL,
        [Status] nvarchar(50) NOT NULL DEFAULT 'Processed',
        [CreatedAt] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        [ExpiresAt] datetime2 NOT NULL,
        [ResponseData] nvarchar(1000) NULL,
        CONSTRAINT [PK_IdempotencyKeys] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_IdempotencyKeys_Orders_OrderId] FOREIGN KEY ([OrderId]) 
            REFERENCES [dbo].[Orders] ([Id]) ON DELETE CASCADE
    )
    
    PRINT 'IdempotencyKeys table created successfully'
END
ELSE
BEGIN
    PRINT 'IdempotencyKeys table already exists'
END
GO

-- =============================================
-- Create OrderAuditLogs Table
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[OrderAuditLogs]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[OrderAuditLogs] (
        [Id] int IDENTITY(1,1) NOT NULL,
        [OrderId] int NOT NULL,
        [Action] nvarchar(50) NOT NULL,
        [OldStatus] nvarchar(50) NOT NULL,
        [NewStatus] nvarchar(50) NOT NULL,
        [Details] nvarchar(500) NULL,
        [PerformedBy] nvarchar(100) NOT NULL DEFAULT 'System',
        [Timestamp] datetime2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_OrderAuditLogs] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_OrderAuditLogs_Orders_OrderId] FOREIGN KEY ([OrderId]) 
            REFERENCES [dbo].[Orders] ([Id]) ON DELETE CASCADE
    )
    
    PRINT 'OrderAuditLogs table created successfully'
END
ELSE
BEGIN
    PRINT 'OrderAuditLogs table already exists'
END
GO

-- =============================================
-- Create Indexes on Orders Table
-- =============================================

-- Unique indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_OrderNumber' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_Orders_OrderNumber] ON [dbo].[Orders]([OrderNumber] ASC)
    PRINT 'Index IX_Orders_OrderNumber created'
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_IdempotencyKey' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_Orders_IdempotencyKey] ON [dbo].[Orders]([IdempotencyKey] ASC)
    PRINT 'Index IX_Orders_IdempotencyKey created'
END

-- Performance indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_Status' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Orders_Status] ON [dbo].[Orders]([Status] ASC)
    PRINT 'Index IX_Orders_Status created'
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_CreatedAt' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Orders_CreatedAt] ON [dbo].[Orders]([CreatedAt] ASC)
    PRINT 'Index IX_Orders_CreatedAt created'
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_CustomerId' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId] ON [dbo].[Orders]([CustomerId] ASC)
    PRINT 'Index IX_Orders_CustomerId created'
END

-- Composite indexes for common queries
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_Status_CreatedAt' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Orders_Status_CreatedAt] ON [dbo].[Orders]([Status] ASC, [CreatedAt] ASC)
    PRINT 'Index IX_Orders_Status_CreatedAt created'
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Orders_CustomerId_CreatedAt' AND object_id = OBJECT_ID('Orders'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId_CreatedAt] ON [dbo].[Orders]([CustomerId] ASC, [CreatedAt] ASC)
    PRINT 'Index IX_Orders_CustomerId_CreatedAt created'
END
GO

-- =============================================
-- Create Indexes on IdempotencyKeys Table
-- =============================================

-- Unique index on Key
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IdempotencyKeys_Key' AND object_id = OBJECT_ID('IdempotencyKeys'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_IdempotencyKeys_Key] ON [dbo].[IdempotencyKeys]([Key] ASC)
    PRINT 'Index IX_IdempotencyKeys_Key created'
END

-- Index for cleanup queries
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IdempotencyKeys_ExpiresAt' AND object_id = OBJECT_ID('IdempotencyKeys'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_IdempotencyKeys_ExpiresAt] ON [dbo].[IdempotencyKeys]([ExpiresAt] ASC)
    PRINT 'Index IX_IdempotencyKeys_ExpiresAt created'
END

-- Foreign key index
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_IdempotencyKeys_OrderId' AND object_id = OBJECT_ID('IdempotencyKeys'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_IdempotencyKeys_OrderId] ON [dbo].[IdempotencyKeys]([OrderId] ASC)
    PRINT 'Index IX_IdempotencyKeys_OrderId created'
END
GO

-- =============================================
-- Create Indexes on OrderAuditLogs Table
-- =============================================

-- Performance indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_OrderAuditLogs_OrderId' AND object_id = OBJECT_ID('OrderAuditLogs'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_OrderAuditLogs_OrderId] ON [dbo].[OrderAuditLogs]([OrderId] ASC)
    PRINT 'Index IX_OrderAuditLogs_OrderId created'
END

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_OrderAuditLogs_Timestamp' AND object_id = OBJECT_ID('OrderAuditLogs'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_OrderAuditLogs_Timestamp] ON [dbo].[OrderAuditLogs]([Timestamp] ASC)
    PRINT 'Index IX_OrderAuditLogs_Timestamp created'
END

-- Composite index for common audit queries
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_OrderAuditLogs_OrderId_Timestamp' AND object_id = OBJECT_ID('OrderAuditLogs'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_OrderAuditLogs_OrderId_Timestamp] ON [dbo].[OrderAuditLogs]([OrderId] ASC, [Timestamp] ASC)
    PRINT 'Index IX_OrderAuditLogs_OrderId_Timestamp created'
END
GO

-- =============================================
-- Record Migration in History Table
-- =============================================
IF NOT EXISTS (SELECT * FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260423000001_InitialCreate')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260423000001_InitialCreate', N'7.0.14')
    
    PRINT 'Migration 20260423000001_InitialCreate recorded in history'
END
ELSE
BEGIN
    PRINT 'Migration 20260423000001_InitialCreate already applied'
END
GO

-- =============================================
-- Verification Script
-- =============================================
PRINT ''
PRINT '========================================='
PRINT 'Migration Application Complete!'
PRINT '========================================='
PRINT ''

-- List all tables created
SELECT 
    'Tables Created' as [Status],
    t.name AS [Table Name],
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id = t.object_id AND index_id > 0) AS [Index Count]
FROM sys.tables t
WHERE t.name IN ('Orders', 'IdempotencyKeys', 'OrderAuditLogs', '__EFMigrationsHistory')
ORDER BY t.name

PRINT ''
PRINT 'Database schema is ready for production use!'
GO
