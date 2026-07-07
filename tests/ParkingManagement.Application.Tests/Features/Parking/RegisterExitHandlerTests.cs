using FluentAssertions;
using NSubstitute;
using ParkingManagement.Application.Common.Errors;
using ParkingManagement.Application.Common.Persistence;
using ParkingManagement.Application.Features.Parking.Commands.RegisterExit;
using ParkingManagement.Domain.Common.ValueObjects;
using ParkingManagement.Domain.Garage;
using ParkingManagement.Domain.Garage.Repositories;
using ParkingManagement.Domain.Parking;
using ParkingManagement.Domain.Parking.Repositories;
using ParkingManagement.Domain.Parking.ValueObjects;

namespace ParkingManagement.Application.Tests.Features.Parking;

public class RegisterExitHandlerTests
{
    private readonly IParkingSessionRepository _sessionRepository = Substitute.For<IParkingSessionRepository>();
    private readonly ISectorRepository _sectorRepository = Substitute.For<ISectorRepository>();
    private readonly ISpotRepository _spotRepository = Substitute.For<ISpotRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private RegisterExitHandler CreateHandler() => new(_sessionRepository, _sectorRepository, _spotRepository, _unitOfWork);

    private static ParkingSession CriarSessaoEstacionada(Guid spotId, DateTime entryTime)
    {
        var session = ParkingSession.IniciarEntrada(LicensePlate.Criar("ZUL0001"), entryTime, 30m);
        session.RegistrarEstacionamento(spotId, "A", GeoCoordinate.Criar(-23.561684, -46.655981), entryTime.AddMinutes(2));
        return session;
    }

    [Fact]
    public async Task Handle_DeveCalcularValorELiberarVaga_QuandoSessaoEstacionada()
    {
        // Arrange
        var entryTime = DateTime.UtcNow;
        var spotId = Guid.NewGuid();
        var session = CriarSessaoEstacionada(spotId, entryTime);
        var sector = Sector.Criar("A", 10m, 100);
        var spot = Spot.Criar(1, "A", GeoCoordinate.Criar(-23.561684, -46.655981));
        spot.Ocupar();

        _sessionRepository.GetActiveByLicensePlateAsync("ZUL0001", Arg.Any<CancellationToken>()).Returns(session);
        _sectorRepository.GetByCodeAsync("A", Arg.Any<CancellationToken>()).Returns(sector);
        _spotRepository.GetByIdAsync(spotId, Arg.Any<CancellationToken>()).Returns(spot);

        var handler = CreateHandler();
        var request = new RegisterExitRequest("ZUL0001", entryTime.AddMinutes(91));

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.AmountCharged.Should().Be(20m);
        spot.Status.Should().Be(SpotStatus.Livre);
    }

    [Fact]
    public async Task Handle_DeveRetornarNotFound_QuandoSessaoAtivaNaoExiste()
    {
        // Arrange
        _sessionRepository.GetActiveByLicensePlateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ParkingSession?)null);

        var handler = CreateHandler();
        var request = new RegisterExitRequest("ZUL0002", DateTime.UtcNow);

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Be(ApplicationErrorMessages.Parking.SessaoAtivaNaoEncontrada);
    }
}
