using System.Text.Json.Serialization;

namespace ParkingManagement.Application.Common.Integrations;

public interface IGarageSimulatorClient
{
    Task<GarageConfigurationDto> GetGarageConfigurationAsync(CancellationToken cancellationToken = default);
}

public sealed record GarageConfigurationDto(
    IReadOnlyCollection<GarageSectorDto> Garage,
    IReadOnlyCollection<GarageSpotDto> Spots);

public sealed record GarageSectorDto(
    string Sector,
    decimal BasePrice,
    [property: JsonPropertyName("max_capacity")] int MaxCapacity);

public sealed record GarageSpotDto(long Id, string Sector, double Lat, double Lng);
