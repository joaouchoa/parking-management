using System.Globalization;
using FluentAssertions;
using ParkingManagement.Domain.Parking.ValueObjects;
using Xunit;

namespace ParkingManagement.Domain.Tests.Parking.ValueObjects;

public class PricingSnapshotTests
{
    [Theory]
    [InlineData(0, "0.90")]
    [InlineData(24, "0.90")]
    [InlineData(25, "1.00")]
    [InlineData(50, "1.00")]
    [InlineData(51, "1.10")]
    [InlineData(75, "1.10")]
    [InlineData(76, "1.25")]
    [InlineData(100, "1.25")]
    public void CalcularPara_DeveAplicarMultiplicadorCorreto(int occupancyPercentage, string expectedMultiplier)
    {
        // Act
        var snapshot = PricingSnapshot.CalcularPara(occupancyPercentage);

        // Assert
        snapshot.Multiplier.Should().Be(decimal.Parse(expectedMultiplier, CultureInfo.InvariantCulture));
        snapshot.OccupancyPercentageAtEntry.Should().Be(occupancyPercentage);
    }

    [Fact]
    public void Equals_DeveSerVerdadeiro_QuandoMesmaLotacao()
    {
        // Arrange
        var a = PricingSnapshot.CalcularPara(20m);
        var b = PricingSnapshot.CalcularPara(20m);

        // Act & Assert
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equals_DeveSerFalso_QuandoLotacoesResultamEmMultiplicadoresDiferentes()
    {
        // Arrange
        var a = PricingSnapshot.CalcularPara(20m);
        var b = PricingSnapshot.CalcularPara(60m);

        // Act & Assert
        a.Should().NotBe(b);
    }
}
