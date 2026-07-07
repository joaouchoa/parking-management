IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Sectors_Code')
    CREATE UNIQUE INDEX IX_Sectors_Code ON parking.Sectors (Code);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Spots_ExternalId')
    CREATE UNIQUE INDEX IX_Spots_ExternalId ON parking.Spots (ExternalId);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ParkingSessions_LicensePlate')
    CREATE INDEX IX_ParkingSessions_LicensePlate ON parking.ParkingSessions (LicensePlate);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ParkingSessions_Status')
    CREATE INDEX IX_ParkingSessions_Status ON parking.ParkingSessions (Status);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ParkingSessions_SectorCode_ExitTime')
    CREATE INDEX IX_ParkingSessions_SectorCode_ExitTime ON parking.ParkingSessions (SectorCode, ExitTime);
