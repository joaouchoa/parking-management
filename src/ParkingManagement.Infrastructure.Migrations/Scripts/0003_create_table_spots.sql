IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Spots' AND schema_id = SCHEMA_ID('parking'))
BEGIN
    CREATE TABLE parking.Spots
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        ExternalId BIGINT NOT NULL,
        SectorCode NVARCHAR(10) NOT NULL,
        Latitude FLOAT NOT NULL,
        Longitude FLOAT NOT NULL,
        Status NVARCHAR(20) NOT NULL
    );
END
