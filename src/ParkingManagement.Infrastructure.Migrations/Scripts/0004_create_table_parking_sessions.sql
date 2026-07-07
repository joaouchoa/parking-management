IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ParkingSessions' AND schema_id = SCHEMA_ID('parking'))
BEGIN
    CREATE TABLE parking.ParkingSessions
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        LicensePlate NVARCHAR(8) NOT NULL,
        EntryTime DATETIME2 NOT NULL,
        OccupancyPercentageAtEntry DECIMAL(5,2) NOT NULL,
        PriceMultiplier DECIMAL(5,2) NOT NULL,
        ParkedAt DATETIME2 NULL,
        ParkedLatitude FLOAT NULL,
        ParkedLongitude FLOAT NULL,
        SpotId UNIQUEIDENTIFIER NULL,
        SectorCode NVARCHAR(10) NULL,
        ExitTime DATETIME2 NULL,
        AmountCharged DECIMAL(18,2) NULL,
        Status NVARCHAR(20) NOT NULL
    );
END
