using FluentAssertions;
using NSubstitute;
using ParkingManagement.Application.Common.Errors;
using ParkingManagement.Application.Features.Parking.Queries.GetRevenue;
using ParkingManagement.Domain.Garage;
using ParkingManagement.Domain.Garage.Repositories;
using ParkingManagement.Domain.Parking.Repositories;

namespace ParkingManagement.Application.Tests.Features.Parking;

public class GetRevenueHandlerTests
{
    private readonly IParkingSessionRepository _sessionRepository = Substitute.For<IParkingSessionRepository>();
    private readonly ISectorRepository _sectorRepository = Substitute.For<ISectorRepository>();

    private GetRevenueHandler CreateHandler() => new(_sessionRepository, _sectorRepository);

    [Fact]
    public async Task Handle_DeveRetornarReceita_QuandoSetorExiste()
    {
        // Arrange
        var sector = Sector.Criar("A", 10m, 100);
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        _sectorRepository.GetByCodeAsync("A", Arg.Any<CancellationToken>()).Returns(sector);
        _sessionRepository.GetRevenueAsync("A", date, Arg.Any<CancellationToken>()).Returns(150.00m);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new GetRevenueRequest("A", date), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(150.00m);
        result.Value.Currency.Should().Be("BRL");
    }

    [Fact]
    public async Task Handle_DeveRetornarNotFound_QuandoSetorNaoExiste()
    {
        // Arrange
        _sectorRepository.GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Sector?)null);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new GetRevenueRequest("Z", DateOnly.FromDateTime(DateTime.UtcNow)), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Be(ApplicationErrorMessages.Garage.SetorNaoEncontrado);
    }
}
