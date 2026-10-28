USE [fixhands];
GO

IF COL_LENGTH(N'dbo.Login', N'PasswordHash') IS NULL
BEGIN
    ALTER TABLE [dbo].[Login]
        ADD [PasswordHash] NVARCHAR(512) NULL;
END;
GO

IF COL_LENGTH(N'dbo.Login', N'IsAdmin') IS NULL
BEGIN
    ALTER TABLE [dbo].[Login]
        ADD [IsAdmin] BIT NOT NULL
            CONSTRAINT [DF_Login_IsAdmin] DEFAULT (0) WITH VALUES;
END;
GO

-- Promote only the intended administrator after confirming the account name.
-- UPDATE [dbo].[Login] SET [IsAdmin] = 1 WHERE [Usernames] = N'<admin username>';
GO
