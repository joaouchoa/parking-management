using FluentAssertions;
using ParkingManagement.Domain.Common;
using ParkingManagement.Domain.Parking.Errors;
using ParkingManagement.Domain.Parking.ValueObjects;
using Xunit;

namespace ParkingManagement.Domain.Tests.Parking.ValueObjects;

public class LicensePlateTests
{
    [Fact]
    public void Criar_DeveNormalizarParaMaiusculoSemEspacos()
    {
        // Act
        var plate = LicensePlate.Criar("  zul0001  ");

        // Assert
        plate.Value.Should().Be("ZUL0001");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_DeveLancar_QuandoVazia(string value)
    {
        // Act
        Action act = () => LicensePlate.Criar(value);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage(ParkingSessionErrors.PlacaObrigatoria);
    }

    [Theory]
    [InlineData("AB12")]
    [InlineData("ABCDEFGHI")]
    [InlineData("ZUL-0001")]
    public void Criar_DeveLancar_QuandoFormatoInvalido(string value)
    {
        // Act
        Action act = () => LicensePlate.Criar(value);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage(ParkingSessionErrors.PlacaFormatoInvalido);
    }

    [Fact]
    public void Equals_DeveSerVerdadeiro_QuandoMesmaPlacaEmCaixaDiferente()
    {
        // Arrange
        var a = LicensePlate.Criar("zul0001");
        var b = LicensePlate.Criar("ZUL0001");

        // Act & Assert
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equals_DeveSerFalso_QuandoPlacasDiferentes()
    {
        // Arrange
        var a = LicensePlate.Criar("ZUL0001");
        var b = LicensePlate.Criar("ZUL0002");

        // Act & Assert
        a.Should().NotBe(b);
    }

    [Fact]
    public void ToString_DeveRetornarValorNormalizado()
    {
        // Arrange
        var plate = LicensePlate.Criar("zul0001");

        // Act & Assert
        plate.ToString().Should().Be("ZUL0001");
    }
}
