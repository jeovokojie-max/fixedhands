USE [fixhands];
GO

IF OBJECT_ID(N'dbo.Skilledreg', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Skilledreg
    (
        FNames NVARCHAR(100) NULL,
        phone NVARCHAR(50) NULL,
        Services NVARCHAR(100) NULL,
        Area NVARCHAR(200) NULL,
        Address NVARCHAR(200) NULL,
        Available NVARCHAR(100) NULL,
        Workabout NVARCHAR(300) NULL
    );
END;
GO

-- Keep phone values unique without treating a phone number as an IDENTITY.
IF EXISTS
(
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Skilledreg')
      AND name = N'phone'
      AND is_identity = 1
)
    THROW 50003, 'The phone column cannot be an IDENTITY column because the form inserts the submitted phone number. Make it NVARCHAR(50) and use it as a unique key.', 1;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS i
    INNER JOIN sys.index_columns AS ic
        ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
    INNER JOIN sys.columns AS c
        ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE i.object_id = OBJECT_ID(N'dbo.Skilledreg')
      AND i.is_unique = 1
      AND c.name = N'phone'
    GROUP BY i.object_id, i.index_id
    HAVING COUNT(*) = 1
)
BEGIN
    IF EXISTS
    (
        SELECT phone
        FROM dbo.Skilledreg
        WHERE phone IS NOT NULL
        GROUP BY phone
        HAVING COUNT_BIG(*) > 1
    )
        THROW 50002, 'Duplicate phone values exist in dbo.Skilledreg. Resolve them before adding the unique phone key.', 1;

    CREATE UNIQUE INDEX UX_Skilledreg_Phone ON dbo.Skilledreg (phone) WHERE phone IS NOT NULL;
END;
GO
