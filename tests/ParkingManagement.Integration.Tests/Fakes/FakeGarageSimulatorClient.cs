using ParkingManagement.Application.Common.Integrations;

namespace ParkingManagement.Integration.Tests.Fakes;

public sealed class FakeGarageSimulatorClient : IGarageSimulatorClient
{
    public static readonly GarageSpotDto SpotA1 = new(1, "A", -23.561684, -46.655981);
    public static readonly GarageSpotDto SpotA2 = new(2, "A", -23.561700, -46.656000);

    public Task<GarageConfigurationDto> GetGarageConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var configuration = new GarageConfigurationDto(
            Garage: [new GarageSectorDto("A", 10.0m, 2)],
            Spots: [SpotA1, SpotA2]);

        return Task.FromResult(configuration);
    }
}
