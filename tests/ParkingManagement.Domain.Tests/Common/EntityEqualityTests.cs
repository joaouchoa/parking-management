using FluentAssertions;
using ParkingManagement.Domain.Common.ValueObjects;
using ParkingManagement.Domain.Garage;
using Xunit;

namespace ParkingManagement.Domain.Tests.Common;

/// <summary>
/// Testa Entity.Equals/GetHashCode usando Spot como sujeito concreto,
/// já que Entity é abstrata e não pode ser instanciada diretamente.
/// </summary>
public class EntityEqualityTests
{
    private static Spot CriarSpot(long externalId = 1) =>
        Spot.Criar(externalId, "A", GeoCoordinate.Criar(-23.561684, -46.655981));

    [Fact]
    public void Equals_DeveSerVerdadeiro_QuandoMesmaInstancia()
    {
        // Arrange
        var spot = CriarSpot();

        // Act & Assert
        spot.Equals(spot).Should().BeTrue();
        spot.GetHashCode().Should().Be(spot.Id.GetHashCode());
    }

    [Fact]
    public void Equals_DeveSerFalso_QuandoIdsDiferentes()
    {
        // Arrange
        var spotA = CriarSpot(1);
        var spotB = CriarSpot(2);

        // Act & Assert
        spotA.Equals(spotB).Should().BeFalse();
    }

    [Fact]
    public void Equals_DeveSerFalso_QuandoComparadoComOutroTipo()
    {
        // Arrange
        var spot = CriarSpot();

        // Act & Assert
        spot.Equals("qualquer coisa").Should().BeFalse();
    }

    [Fact]
    public void Equals_DeveSerFalso_QuandoComparadoComNull()
    {
        // Arrange
        var spot = CriarSpot();

        // Act & Assert
        spot.Equals(null).Should().BeFalse();
    }
}
