using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using ParkingManagement.Integration.Tests.Fakes;

namespace ParkingManagement.Integration.Tests.Parking;

[Collection(IntegrationTestCollection.Name)]
public sealed class ParkingFlowTests(ParkingApiFactory factory)
{
    private sealed record RevenueResponseDto(
        [property: JsonPropertyName("amount")] decimal Amount,
        [property: JsonPropertyName("currency")] string Currency);

    [Fact]
    public async Task FluxoCompleto_EntradaEstacionamentoSaida_DeveRefletirNaReceita()
    {
        // Arrange
        var client = factory.CreateClient();
        var licensePlate = $"E2E{Random.Shared.Next(1000, 9999)}";
        var entryTime = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var exitTime = entryTime.AddMinutes(91);

        // Act — ENTRY
        var entryResponse = await client.PostAsJsonAsync("/webhook", new
        {
            license_plate = licensePlate,
            entry_time = entryTime,
            event_type = "ENTRY"
        });

        // Act — PARKED
        var parkedResponse = await client.PostAsJsonAsync("/webhook", new
        {
            license_plate = licensePlate,
            lat = FakeGarageSimulatorClient.SpotA1.Lat,
            lng = FakeGarageSimulatorClient.SpotA1.Lng,
            event_type = "PARKED"
        });

        // Act — EXIT
        var exitResponse = await client.PostAsJsonAsync("/webhook", new
        {
            license_plate = licensePlate,
            exit_time = exitTime,
            event_type = "EXIT"
        });

        // Act — REVENUE
        var revenueResponse = await client.GetAsync($"/revenue?sector=A&date={exitTime:yyyy-MM-dd}");
        var revenue = await revenueResponse.Content.ReadFromJsonAsync<RevenueResponseDto>();

        // Assert
        entryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        parkedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        exitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        revenueResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        revenue.Should().NotBeNull();
        revenue!.Amount.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task Webhook_DeveRetornarBadRequest_QuandoEventTypeDesconhecido()
    {
        // Arrange
        var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/webhook", new
        {
            license_plate = "XXX0000",
            event_type = "UNKNOWN"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
