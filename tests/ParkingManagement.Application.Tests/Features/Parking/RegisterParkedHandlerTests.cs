using FluentAssertions;
using NSubstitute;
using ParkingManagement.Application.Common.Errors;
using ParkingManagement.Application.Common.Persistence;
using ParkingManagement.Application.Features.Parking.Commands.RegisterParked;
using ParkingManagement.Domain.Common.ValueObjects;
using ParkingManagement.Domain.Garage;
using ParkingManagement.Domain.Garage.Repositories;
using ParkingManagement.Domain.Parking;
using ParkingManagement.Domain.Parking.Repositories;
using ParkingManagement.Domain.Parking.ValueObjects;

namespace ParkingManagement.Application.Tests.Features.Parking;

public class RegisterParkedHandlerTests
{
    private readonly IParkingSessionRepository _sessionRepository = Substitute.For<IParkingSessionRepository>();
    private readonly ISpotRepository _spotRepository = Substitute.For<ISpotRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private RegisterParkedHandler CreateHandler() => new(_sessionRepository, _spotRepository, _unitOfWork);

    [Fact]
    public async Task Handle_DeveAssociarVagaESetor_QuandoSessaoEVagaExistem()
    {
        // Arrange
        var session = ParkingSession.IniciarEntrada(LicensePlate.Criar("ZUL0001"), DateTime.UtcNow, 30m);
        var spot = Spot.Criar(1, "A", GeoCoordinate.Criar(-23.561684, -46.655981));

        _sessionRepository.GetActiveByLicensePlateAsync("ZUL0001", Arg.Any<CancellationToken>()).Returns(session);
        _spotRepository.FindByCoordinateAsync(Arg.Any<GeoCoordinate>(), Arg.Any<CancellationToken>()).Returns(spot);

        var handler = CreateHandler();
        var request = new RegisterParkedRequest("ZUL0001", -23.561684, -46.655981);

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SectorCode.Should().Be("A");
        spot.Status.Should().Be(SpotStatus.Ocupada);
    }

    [Fact]
    public async Task Handle_DeveRetornarNotFound_QuandoSessaoAtivaNaoExiste()
    {
        // Arrange
        _sessionRepository.GetActiveByLicensePlateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ParkingSession?)null);

        var handler = CreateHandler();
        var request = new RegisterParkedRequest("ZUL0002", -23.561684, -46.655981);

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Be(ApplicationErrorMessages.Parking.SessaoAtivaNaoEncontrada);
    }
}
