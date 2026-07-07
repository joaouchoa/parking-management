using System.Linq;
using FluentAssertions;
using ParkingManagement.Domain.Common;
using ParkingManagement.Domain.Common.ValueObjects;
using ParkingManagement.Domain.Parking;
using ParkingManagement.Domain.Parking.Errors;
using ParkingManagement.Domain.Parking.Events;
using ParkingManagement.Domain.Parking.ValueObjects;
using ParkingManagement.Domain.Tests.Parking.Builders;
using Xunit;

namespace ParkingManagement.Domain.Tests.Parking;

public class ParkingSessionTests
{
    [Fact]
    public void IniciarEntrada_DeveAplicarDesconto10Porcento_QuandoLotacaoAbaixoDe25()
    {
        // Arrange
        var licensePlate = LicensePlate.Criar("ZUL0001");

        // Act
        var session = ParkingSession.IniciarEntrada(licensePlate, DateTime.UtcNow, 20m);

        // Assert
        session.PricingSnapshot.Multiplier.Should().Be(0.90m);
    }

    [Fact]
    public void IniciarEntrada_DeveAplicarPrecoNormal_QuandoLotacaoAte50()
    {
        // Arrange
        var licensePlate = LicensePlate.Criar("ZUL0002");

        // Act
        var session = ParkingSession.IniciarEntrada(licensePlate, DateTime.UtcNow, 50m);

        // Assert
        session.PricingSnapshot.Multiplier.Should().Be(1.00m);
    }

    [Fact]
    public void IniciarEntrada_DeveAplicarAcrescimo10Porcento_QuandoLotacaoAte75()
    {
        // Arrange
        var licensePlate = LicensePlate.Criar("ZUL0003");

        // Act
        var session = ParkingSession.IniciarEntrada(licensePlate, DateTime.UtcNow, 75m);

        // Assert
        session.PricingSnapshot.Multiplier.Should().Be(1.10m);
    }

    [Fact]
    public void IniciarEntrada_DeveAplicarAcrescimo25Porcento_QuandoLotacaoAte100()
    {
        // Arrange
        var licensePlate = LicensePlate.Criar("ZUL0004");

        // Act
        var session = ParkingSession.IniciarEntrada(licensePlate, DateTime.UtcNow, 99m);

        // Assert
        session.PricingSnapshot.Multiplier.Should().Be(1.25m);
    }

    [Fact]
    public void IniciarEntrada_DeveLancar_QuandoLotacao100Porcento()
    {
        // Arrange
        var licensePlate = LicensePlate.Criar("ZUL0005");

        // Act
        Action act = () => ParkingSession.IniciarEntrada(licensePlate, DateTime.UtcNow, 100m);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage(ParkingSessionErrors.GaragemCheia);
    }

    [Fact]
    public void RegistrarEstacionamento_DeveAssociarVagaESetor()
    {
        // Arrange
        var session = ParkingSessionFaker.CriarEntrada();
        var spotId = Guid.NewGuid();
        var coordinate = GeoCoordinate.Criar(-23.561684, -46.655981);

        // Act
        session.RegistrarEstacionamento(spotId, "A", coordinate, DateTime.UtcNow);

        // Assert
        session.SpotId.Should().Be(spotId);
        session.SectorCode.Should().Be("A");
        session.Status.Should().Be(ParkingSessionStatus.Estacionado);
    }

    [Fact]
    public void RegistrarEstacionamento_DeveLancar_QuandoSessaoJaEstacionada()
    {
        // Arrange
        var session = ParkingSessionFaker.CriarEstacionada();
        var coordinate = GeoCoordinate.Criar(-23.561684, -46.655981);

        // Act
        Action act = () => session.RegistrarEstacionamento(Guid.NewGuid(), "A", coordinate, DateTime.UtcNow);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage(ParkingSessionErrors.SessaoJaEstacionada);
    }

    [Fact]
    public void RegistrarSaida_DeveCobrarZero_QuandoPermanenciaMenorQue30Min()
    {
        // Arrange
        var entryTime = DateTime.UtcNow;
        var session = ParkingSessionFaker.CriarEstacionada(entryTime: entryTime);

        // Act
        session.RegistrarSaida(entryTime.AddMinutes(20), sectorBasePrice: 10m);

        // Assert
        session.AmountCharged.Should().Be(0m);
    }

    [Fact]
    public void RegistrarSaida_DeveArredondarHoraCheiaParaCima()
    {
        // Arrange
        var entryTime = DateTime.UtcNow;
        var session = ParkingSessionFaker.CriarEstacionada(occupancyPercentage: 50m, entryTime: entryTime);

        // Act — 30 min grátis + 61 min cobráveis => arredonda para 2 horas
        session.RegistrarSaida(entryTime.AddMinutes(91), sectorBasePrice: 10m);

        // Assert
        session.AmountCharged.Should().Be(20m);
    }

    [Fact]
    public void RegistrarSaida_DeveUsarMultiplicadorDaEntrada_NaoODaSaida()
    {
        // Arrange
        var entryTime = DateTime.UtcNow;
        var session = ParkingSessionFaker.CriarEstacionada(occupancyPercentage: 20m, entryTime: entryTime);

        // Act — 30 min grátis + 30 min cobráveis => 1 hora, base 10, multiplicador 0.90 travado na entrada
        session.RegistrarSaida(entryTime.AddMinutes(61), sectorBasePrice: 10m);

        // Assert
        session.AmountCharged.Should().Be(9m);
    }

    [Fact]
    public void RegistrarSaida_DeveLancar_QuandoSessaoNaoEstacionada()
    {
        // Arrange
        var session = ParkingSessionFaker.CriarEntrada();

        // Act
        Action act = () => session.RegistrarSaida(DateTime.UtcNow, sectorBasePrice: 10m);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage(ParkingSessionErrors.SessaoNaoEstacionada);
    }

    [Fact]
    public void IniciarEntrada_DeveRegistrarVehicleEnteredEvent()
    {
        // Arrange
        var licensePlate = LicensePlate.Criar("ZUL0006");
        var entryTime = DateTime.UtcNow;

        // Act
        var session = ParkingSession.IniciarEntrada(licensePlate, entryTime, 30m);

        // Assert
        var domainEvent = session.DomainEvents.Should().ContainSingle().Subject
            .Should().BeOfType<VehicleEnteredEvent>().Subject;

        domainEvent.SessionId.Should().Be(session.Id);
        domainEvent.LicensePlate.Should().Be("ZUL0006");
        domainEvent.EntryTime.Should().Be(entryTime);
    }

    [Fact]
    public void RegistrarEstacionamento_DeveRegistrarVehicleParkedEvent()
    {
        // Arrange
        var session = ParkingSessionFaker.CriarEntrada();
        var spotId = Guid.NewGuid();
        var coordinate = GeoCoordinate.Criar(-23.561684, -46.655981);

        // Act
        session.RegistrarEstacionamento(spotId, "A", coordinate, DateTime.UtcNow);

        // Assert
        var domainEvent = session.DomainEvents.OfType<VehicleParkedEvent>().Should().ContainSingle().Subject;

        domainEvent.SessionId.Should().Be(session.Id);
        domainEvent.SpotId.Should().Be(spotId);
        domainEvent.SectorCode.Should().Be("A");
    }

    [Fact]
    public void RegistrarSaida_DeveRegistrarVehicleExitedEvent()
    {
        // Arrange
        var entryTime = DateTime.UtcNow;
        var session = ParkingSessionFaker.CriarEstacionada(occupancyPercentage: 50m, entryTime: entryTime);

        // Act
        session.RegistrarSaida(entryTime.AddMinutes(91), sectorBasePrice: 10m);

        // Assert
        var domainEvent = session.DomainEvents.OfType<VehicleExitedEvent>().Should().ContainSingle().Subject;

        domainEvent.SessionId.Should().Be(session.Id);
        domainEvent.AmountCharged.Should().Be(20m);
    }

    [Fact]
    public void ClearDomainEvents_DeveEsvaziarEventosAcumulados()
    {
        // Arrange
        var session = ParkingSessionFaker.CriarEntrada();
        session.DomainEvents.Should().NotBeEmpty();

        // Act
        session.ClearDomainEvents();

        // Assert
        session.DomainEvents.Should().BeEmpty();
    }
}
