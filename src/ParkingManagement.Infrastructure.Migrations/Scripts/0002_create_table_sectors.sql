IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sectors' AND schema_id = SCHEMA_ID('parking'))
BEGIN
    CREATE TABLE parking.Sectors
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        Code NVARCHAR(10) NOT NULL,
        BasePrice DECIMAL(18,2) NOT NULL,
        MaxCapacity INT NOT NULL
    );
END
