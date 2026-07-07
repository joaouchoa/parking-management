using FluentAssertions;
using ParkingManagement.Domain.Common.ValueObjects;
using Xunit;

namespace ParkingManagement.Domain.Tests.Common.ValueObjects;

public class GeoCoordinateTests
{
    [Fact]
    public void Equals_DeveSerVerdadeiro_QuandoMesmosValores()
    {
        // Arrange
        var a = GeoCoordinate.Criar(-23.561684, -46.655981);
        var b = GeoCoordinate.Criar(-23.561684, -46.655981);

        // Act & Assert
        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equals_DeveSerFalso_QuandoValoresDiferentes()
    {
        // Arrange
        var a = GeoCoordinate.Criar(-23.561684, -46.655981);
        var b = GeoCoordinate.Criar(-23.561700, -46.656000);

        // Act & Assert
        a.Should().NotBe(b);
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void OperadorIgualdade_DeveSerFalso_QuandoComparadoComNull()
    {
        // Arrange
        var a = GeoCoordinate.Criar(-23.561684, -46.655981);

        // Act & Assert
        (a == null).Should().BeFalse();
        (null == a).Should().BeFalse();
    }
}
