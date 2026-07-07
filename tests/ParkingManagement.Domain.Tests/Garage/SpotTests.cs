using FluentAssertions;
using ParkingManagement.Domain.Common;
using ParkingManagement.Domain.Common.ValueObjects;
using ParkingManagement.Domain.Garage;
using ParkingManagement.Domain.Garage.Errors;
using Xunit;

namespace ParkingManagement.Domain.Tests.Garage;

public class SpotTests
{
    [Fact]
    public void Ocupar_DeveMarcarVagaComoOcupada_QuandoLivre()
    {
        // Arrange
        var spot = Spot.Criar(1, "A", GeoCoordinate.Criar(-23.561684, -46.655981));

        // Act
        spot.Ocupar();

        // Assert
        spot.Status.Should().Be(SpotStatus.Ocupada);
    }

    [Fact]
    public void Ocupar_DeveLancar_QuandoJaOcupada()
    {
        // Arrange
        var spot = Spot.Criar(1, "A", GeoCoordinate.Criar(-23.561684, -46.655981));
        spot.Ocupar();

        // Act
        Action act = () => spot.Ocupar();

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage(GarageErrors.VagaJaOcupada);
    }

    [Fact]
    public void Liberar_DeveMarcarVagaComoLivre_QuandoOcupada()
    {
        // Arrange
        var spot = Spot.Criar(1, "A", GeoCoordinate.Criar(-23.561684, -46.655981));
        spot.Ocupar();

        // Act
        spot.Liberar();

        // Assert
        spot.Status.Should().Be(SpotStatus.Livre);
    }

    [Fact]
    public void Liberar_DeveLancar_QuandoJaLivre()
    {
        // Arrange
        var spot = Spot.Criar(1, "A", GeoCoordinate.Criar(-23.561684, -46.655981));

        // Act
        Action act = () => spot.Liberar();

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage(GarageErrors.VagaJaLivre);
    }
}
