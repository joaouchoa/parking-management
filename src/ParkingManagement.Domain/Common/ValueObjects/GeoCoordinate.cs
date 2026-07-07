namespace ParkingManagement.Domain.Common.ValueObjects;

public sealed class GeoCoordinate : ValueObject
{
    public double Latitude { get; }
    public double Longitude { get; }

    private GeoCoordinate(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public static GeoCoordinate Criar(double latitude, double longitude) => new(latitude, longitude);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }
}
