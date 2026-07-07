using System.Net.Http.Json;
using ParkingManagement.Application.Common.Integrations;

namespace ParkingManagement.Infrastructure.ExternalServices;

public sealed class GarageSimulatorClient(HttpClient httpClient) : IGarageSimulatorClient
{
    public async Task<GarageConfigurationDto> GetGarageConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<GarageConfigurationDto>("/garage", cancellationToken);

        return response ?? throw new InvalidOperationException(
            "O simulador retornou uma resposta vazia para GET /garage.");
    }
}
