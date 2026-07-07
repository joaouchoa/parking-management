using ParkingManagement.Domain.Common;
using ParkingManagement.Domain.Common.ValueObjects;
using ParkingManagement.Domain.Parking.Errors;
using ParkingManagement.Domain.Parking.Events;
using ParkingManagement.Domain.Parking.ValueObjects;

namespace ParkingManagement.Domain.Parking;

public sealed class ParkingSession : AggregateRoot
{
    private const int MinutosGratis = 30;

    public LicensePlate LicensePlate { get; private set; } = null!;
    public DateTime EntryTime { get; private set; }
    public PricingSnapshot PricingSnapshot { get; private set; } = null!;

    public DateTime? ParkedAt { get; private set; }
    public GeoCoordinate? ParkedCoordinate { get; private set; }
    public Guid? SpotId { get; private set; }
    public string? SectorCode { get; private set; }

    public DateTime? ExitTime { get; private set; }
    public decimal? AmountCharged { get; private set; }

    public ParkingSessionStatus Status { get; private set; }

    private ParkingSession()
    {
    }

    public static ParkingSession IniciarEntrada(LicensePlate licensePlate, DateTime entryTime, decimal occupancyPercentage)
    {
        if (occupancyPercentage is < 0 or > 100)
            throw new DomainException(ParkingSessionErrors.OcupacaoPercentualInvalida);

        if (occupancyPercentage >= 100m)
            throw new DomainException(ParkingSessionErrors.GaragemCheia);

        var session = new ParkingSession
        {
            LicensePlate = licensePlate,
            EntryTime = entryTime,
            PricingSnapshot = PricingSnapshot.CalcularPara(occupancyPercentage),
            Status = ParkingSessionStatus.Entrou
        };

        session.RaiseDomainEvent(new VehicleEnteredEvent(session.Id, licensePlate.Value, entryTime));

        return session;
    }

    public void RegistrarEstacionamento(Guid spotId, string sectorCode, GeoCoordinate coordinate, DateTime parkedAt)
    {
        if (Status != ParkingSessionStatus.Entrou)
            throw new DomainException(ParkingSessionErrors.SessaoJaEstacionada);

        SpotId = spotId;
        SectorCode = sectorCode;
        ParkedCoordinate = coordinate;
        ParkedAt = parkedAt;
        Status = ParkingSessionStatus.Estacionado;

        RaiseDomainEvent(new VehicleParkedEvent(Id, spotId, sectorCode));
    }

    public void RegistrarSaida(DateTime exitTime, decimal sectorBasePrice)
    {
        if (Status != ParkingSessionStatus.Estacionado)
            throw new DomainException(ParkingSessionErrors.SessaoNaoEstacionada);

        if (sectorBasePrice <= 0)
            throw new DomainException(ParkingSessionErrors.BasePriceInvalido);

        if (exitTime < EntryTime)
            throw new DomainException(ParkingSessionErrors.ExitTimeAnteriorAEntrada);

        ExitTime = exitTime;
        AmountCharged = CalcularValorCobrado(exitTime, sectorBasePrice);
        Status = ParkingSessionStatus.Finalizado;

        RaiseDomainEvent(new VehicleExitedEvent(Id, AmountCharged.Value));
    }

    private decimal CalcularValorCobrado(DateTime exitTime, decimal sectorBasePrice)
    {
        var minutosDecorridos = (exitTime - EntryTime).TotalMinutes;

        if (minutosDecorridos <= MinutosGratis)
            return 0m;

        var minutosCobraveis = minutosDecorridos - MinutosGratis;
        var horasCobradas = (int)Math.Ceiling(minutosCobraveis / 60.0);

        return horasCobradas * sectorBasePrice * PricingSnapshot.Multiplier;
    }
}
