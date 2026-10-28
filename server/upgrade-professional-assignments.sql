/* Run once against the existing Fixhands database after backing it up. */
USE [fixhands];
GO

IF COL_LENGTH(N'dbo.Skilledreg', N'ProfessionalId') IS NULL
    ALTER TABLE dbo.Skilledreg ADD ProfessionalId UNIQUEIDENTIFIER NULL;
GO

UPDATE dbo.Skilledreg SET ProfessionalId = NEWID() WHERE ProfessionalId IS NULL;
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Skilledreg') AND name = N'ProfessionalId' AND is_nullable = 1)
    ALTER TABLE dbo.Skilledreg ALTER COLUMN ProfessionalId UNIQUEIDENTIFIER NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.Skilledreg') AND name = N'UQ_Skilledreg_ProfessionalId')
    ALTER TABLE dbo.Skilledreg ADD CONSTRAINT UQ_Skilledreg_ProfessionalId UNIQUE (ProfessionalId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.Skilledreg') AND name = N'DF_Skilledreg_ProfessionalId')
    ALTER TABLE dbo.Skilledreg ADD CONSTRAINT DF_Skilledreg_ProfessionalId DEFAULT (NEWID()) FOR ProfessionalId;
GO

IF OBJECT_ID(N'dbo.ServiceRequestAssignments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ServiceRequestAssignments
    (
        RequestId BIGINT NOT NULL,
        ProfessionalId UNIQUEIDENTIFIER NOT NULL,
        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_ServiceRequestAssignments_Status DEFAULT (N'pending'),
        AssignedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_ServiceRequestAssignments_AssignedAtUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_ServiceRequestAssignments PRIMARY KEY (RequestId, ProfessionalId),
        CONSTRAINT FK_ServiceRequestAssignments_Request FOREIGN KEY (RequestId) REFERENCES dbo.ServiceRequests(RequestId),
        CONSTRAINT FK_ServiceRequestAssignments_Professional FOREIGN KEY (ProfessionalId) REFERENCES dbo.Skilledreg(ProfessionalId)
    );
END;
GO
