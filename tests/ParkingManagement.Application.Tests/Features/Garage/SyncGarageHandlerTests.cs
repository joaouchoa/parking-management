using FluentAssertions;
using NSubstitute;
using ParkingManagement.Application.Common.Integrations;
using ParkingManagement.Application.Common.Persistence;
using ParkingManagement.Application.Features.Garage.Commands.SyncGarage;
using ParkingManagement.Domain.Garage;
using ParkingManagement.Domain.Garage.Repositories;

namespace ParkingManagement.Application.Tests.Features.Garage;

public class SyncGarageHandlerTests
{
    private readonly IGarageSimulatorClient _simulatorClient = Substitute.For<IGarageSimulatorClient>();
    private readonly ISectorRepository _sectorRepository = Substitute.For<ISectorRepository>();
    private readonly ISpotRepository _spotRepository = Substitute.For<ISpotRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private SyncGarageHandler CreateHandler() => new(_simulatorClient, _sectorRepository, _spotRepository, _unitOfWork);

    [Fact]
    public async Task Handle_DeveCriarSetoresEVagasNovos_QuandoNaoExistemAinda()
    {
        // Arrange
        var configuration = new GarageConfigurationDto(
            Garage: [new GarageSectorDto("A", 10m, 100)],
            Spots: [new GarageSpotDto(1, "A", -23.561684, -46.655981)]);

        _simulatorClient.GetGarageConfigurationAsync(Arg.Any<CancellationToken>()).Returns(configuration);
        _sectorRepository.GetByCodeAsync("A", Arg.Any<CancellationToken>()).Returns((Sector?)null);
        _spotRepository.GetByExternalIdAsync(1, Arg.Any<CancellationToken>()).Returns((Spot?)null);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new SyncGarageRequest(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SectorsSynced.Should().Be(1);
        result.Value.SpotsSynced.Should().Be(1);
        await _sectorRepository.Received(1).AddAsync(Arg.Any<Sector>(), Arg.Any<CancellationToken>());
        await _spotRepository.Received(1).AddAsync(Arg.Any<Spot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DeveAtualizarSetorExistente_SemDuplicar()
    {
        // Arrange
        var sectorExistente = Sector.Criar("A", 8m, 50);
        var configuration = new GarageConfigurationDto(
            Garage: [new GarageSectorDto("A", 10m, 100)],
            Spots: []);

        _simulatorClient.GetGarageConfigurationAsync(Arg.Any<CancellationToken>()).Returns(configuration);
        _sectorRepository.GetByCodeAsync("A", Arg.Any<CancellationToken>()).Returns(sectorExistente);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new SyncGarageRequest(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        sectorExistente.BasePrice.Should().Be(10m);
        sectorExistente.MaxCapacity.Should().Be(100);
        await _sectorRepository.DidNotReceive().AddAsync(Arg.Any<Sector>(), Arg.Any<CancellationToken>());
    }
}
