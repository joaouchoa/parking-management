IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'parking')
BEGIN
    EXEC('CREATE SCHEMA parking');
END
