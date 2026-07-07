namespace ParkingManagement.Integration.Tests;

[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<ParkingApiFactory>
{
    public const string Name = "Integration";
}
