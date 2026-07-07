using FluentAssertions;
using NSubstitute;
using ParkingManagement.Application.Common.Persistence;
using ParkingManagement.Application.Features.Parking.Commands.RegisterEntry;
using ParkingManagement.Domain.Common;
using ParkingManagement.Domain.Garage.Repositories;
using ParkingManagement.Domain.Parking.Repositories;

namespace ParkingManagement.Application.Tests.Features.Parking;

public class RegisterEntryHandlerTests
{
    private readonly IParkingSessionRepository _sessionRepository = Substitute.For<IParkingSessionRepository>();
    private readonly ISectorRepository _sectorRepository = Substitute.For<ISectorRepository>();
    private readonly ISpotRepository _spotRepository = Substitute.For<ISpotRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private RegisterEntryHandler CreateHandler() =>
        new(_sessionRepository, _sectorRepository, _spotRepository, _unitOfWork);

    [Fact]
    public async Task Handle_DeveRegistrarEntrada_QuandoGaragemNaoEstaCheia()
    {
        // Arrange
        _sectorRepository.GetTotalCapacityAsync(Arg.Any<CancellationToken>()).Returns(100);
        _spotRepository.CountOccupiedAsync(Arg.Any<CancellationToken>()).Returns(40);

        var handler = CreateHandler();
        var request = new RegisterEntryRequest("ZUL0001", DateTime.UtcNow);

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.LicensePlate.Should().Be("ZUL0001");
        await _sessionRepository.Received(1).AddAsync(Arg.Any<Domain.Parking.ParkingSession>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DeveLancarDomainException_QuandoGaragemCheia()
    {
        // Arrange
        _sectorRepository.GetTotalCapacityAsync(Arg.Any<CancellationToken>()).Returns(100);
        _spotRepository.CountOccupiedAsync(Arg.Any<CancellationToken>()).Returns(100);

        var handler = CreateHandler();
        var request = new RegisterEntryRequest("ZUL0002", DateTime.UtcNow);

        // Act
        Func<Task> act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DomainException>();
    }
}
