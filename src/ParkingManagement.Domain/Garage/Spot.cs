using ParkingManagement.Domain.Common;
using ParkingManagement.Domain.Common.ValueObjects;
using ParkingManagement.Domain.Garage.Errors;

namespace ParkingManagement.Domain.Garage;

public sealed class Spot : Entity
{
    public long ExternalId { get; private set; }
    public string SectorCode { get; private set; } = null!;
    public GeoCoordinate Coordinate { get; private set; } = null!;
    public SpotStatus Status { get; private set; } = SpotStatus.Livre;

    private Spot()
    {
    }

    public static Spot Criar(long externalId, string sectorCode, GeoCoordinate coordinate)
    {
        if (string.IsNullOrWhiteSpace(sectorCode))
            throw new DomainException(GarageErrors.CodigoSetorObrigatorio);

        return new Spot
        {
            ExternalId = externalId,
            SectorCode = sectorCode,
            Coordinate = coordinate,
            Status = SpotStatus.Livre
        };
    }

    public void Ocupar()
    {
        if (Status == SpotStatus.Ocupada)
            throw new DomainException(GarageErrors.VagaJaOcupada);

        Status = SpotStatus.Ocupada;
    }

    public void Liberar()
    {
        if (Status == SpotStatus.Livre)
            throw new DomainException(GarageErrors.VagaJaLivre);

        Status = SpotStatus.Livre;
    }
}
