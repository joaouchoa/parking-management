using FluentAssertions;
using ParkingManagement.Domain.Common;
using ParkingManagement.Domain.Garage;
using ParkingManagement.Domain.Garage.Errors;
using Xunit;

namespace ParkingManagement.Domain.Tests.Garage;

public class SectorTests
{
    [Fact]
    public void Criar_DeveCriarSetor_QuandoDadosValidos()
    {
        // Arrange & Act
        var sector = Sector.Criar("A", 10m, 100);

        // Assert
        sector.Code.Should().Be("A");
        sector.BasePrice.Should().Be(10m);
        sector.MaxCapacity.Should().Be(100);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_DeveLancar_QuandoCodigoVazio(string code)
    {
        // Act
        Action act = () => Sector.Criar(code, 10m, 100);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage(GarageErrors.CodigoSetorObrigatorio);
    }

    [Fact]
    public void Criar_DeveLancar_QuandoBasePriceZero()
    {
        // Act
        Action act = () => Sector.Criar("A", 0m, 100);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage(GarageErrors.BasePriceInvalido);
    }

    [Fact]
    public void Criar_DeveLancar_QuandoBasePriceNegativo()
    {
        // Act
        Action act = () => Sector.Criar("A", -10m, 100);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage(GarageErrors.BasePriceInvalido);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Criar_DeveLancar_QuandoCapacidadeInvalida(int maxCapacity)
    {
        // Act
        Action act = () => Sector.Criar("A", 10m, maxCapacity);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage(GarageErrors.CapacidadeMaximaInvalida);
    }

    [Fact]
    public void AtualizarConfiguracao_DeveAtualizarValores_QuandoValidos()
    {
        // Arrange
        var sector = Sector.Criar("A", 10m, 100);

        // Act
        sector.AtualizarConfiguracao(15m, 150);

        // Assert
        sector.BasePrice.Should().Be(15m);
        sector.MaxCapacity.Should().Be(150);
    }

    [Fact]
    public void AtualizarConfiguracao_DeveLancar_QuandoBasePriceInvalido()
    {
        // Arrange
        var sector = Sector.Criar("A", 10m, 100);

        // Act
        Action act = () => sector.AtualizarConfiguracao(0m, 100);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage(GarageErrors.BasePriceInvalido);
    }

    [Fact]
    public void AtualizarConfiguracao_DeveLancar_QuandoCapacidadeInvalida()
    {
        // Arrange
        var sector = Sector.Criar("A", 10m, 100);

        // Act
        Action act = () => sector.AtualizarConfiguracao(10m, 0);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage(GarageErrors.CapacidadeMaximaInvalida);
    }

    [Fact]
    public void AtualizarConfiguracao_NaoDeveAlterarValores_QuandoLancaExcecao()
    {
        // Arrange
        var sector = Sector.Criar("A", 10m, 100);

        // Act
        Action act = () => sector.AtualizarConfiguracao(-5m, 200);

        // Assert
        act.Should().Throw<DomainException>();
        sector.BasePrice.Should().Be(10m);
        sector.MaxCapacity.Should().Be(100);
    }
}
