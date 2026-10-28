/* Run against the existing Fixhands database. This creates the shared service-request table. */
USE [fixhands];
GO

IF OBJECT_ID(N'dbo.ServiceRequests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ServiceRequests
    (
        RequestId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ServiceRequests PRIMARY KEY,
        CustomerName NVARCHAR(100) NOT NULL,
        PhoneNumber NVARCHAR(50) NOT NULL,
        Service NVARCHAR(100) NOT NULL,
        Area NVARCHAR(200) NOT NULL,
        JobAddress NVARCHAR(300) NOT NULL,
        Availability NVARCHAR(100) NOT NULL,
        Details NVARCHAR(1000) NOT NULL CONSTRAINT DF_ServiceRequests_Details DEFAULT (N''),
        SubmittedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_ServiceRequests_SubmittedAtUtc DEFAULT (SYSUTCDATETIME())
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ServiceRequests') AND name = N'IX_ServiceRequests_SubmittedAtUtc')
BEGIN
    CREATE INDEX IX_ServiceRequests_SubmittedAtUtc ON dbo.ServiceRequests (SubmittedAtUtc DESC);
END;
GO
