using FluentAssertions;
using NSubstitute;
using ParkingManagement.Application.Common.Integrations;
using ParkingManagement.Application.Common.Persistence;
using ParkingManagement.Application.Features.Garage.Commands.SeedGarage;
using ParkingManagement.Domain.Garage;
using ParkingManagement.Domain.Garage.Repositories;

namespace ParkingManagement.Application.Tests.Features.Garage;

public class SeedGarageHandlerTests
{
    private readonly ISectorRepository _sectorRepository = Substitute.For<ISectorRepository>();
    private readonly ISpotRepository _spotRepository = Substitute.For<ISpotRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private SeedGarageHandler CreateHandler() => new(_sectorRepository, _spotRepository, _unitOfWork);

    [Fact]
    public async Task Handle_DeveCriarSetoresEVagasNovos_QuandoNaoExistemAinda()
    {
        // Arrange
        var request = new SeedGarageRequest(
            Garage: [new GarageSectorDto("A", 10m, 100)],
            Spots: [new GarageSpotDto(1, "A", -23.561684, -46.655981)]);

        _sectorRepository.GetByCodeAsync("A", Arg.Any<CancellationToken>()).Returns((Sector?)null);
        _spotRepository.GetByExternalIdAsync(1, Arg.Any<CancellationToken>()).Returns((Spot?)null);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SectorsUpserted.Should().Be(1);
        result.Value.SpotsUpserted.Should().Be(1);
        await _sectorRepository.Received(1).AddAsync(Arg.Any<Sector>(), Arg.Any<CancellationToken>());
        await _spotRepository.Received(1).AddAsync(Arg.Any<Spot>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DeveAtualizarSetorExistente_SemDuplicar()
    {
        // Arrange
        var sectorExistente = Sector.Criar("A", 8m, 50);
        var request = new SeedGarageRequest(
            Garage: [new GarageSectorDto("A", 10m, 100)],
            Spots: []);

        _sectorRepository.GetByCodeAsync("A", Arg.Any<CancellationToken>()).Returns(sectorExistente);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        sectorExistente.BasePrice.Should().Be(10m);
        sectorExistente.MaxCapacity.Should().Be(100);
        await _sectorRepository.DidNotReceive().AddAsync(Arg.Any<Sector>(), Arg.Any<CancellationToken>());
    }
}
