using ParkingManagement.Domain.Common.ValueObjects;

namespace ParkingManagement.Domain.Garage.Repositories;

public interface ISpotRepository
{
    Task<Spot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Spot?> GetByExternalIdAsync(long externalId, CancellationToken cancellationToken = default);
    Task<Spot?> FindByCoordinateAsync(GeoCoordinate coordinate, CancellationToken cancellationToken = default);
    Task AddAsync(Spot spot, CancellationToken cancellationToken = default);
    Task<int> CountOccupiedAsync(CancellationToken cancellationToken = default);
    Task<int> CountOccupiedBySectorAsync(string sectorCode, CancellationToken cancellationToken = default);
    void Update(Spot spot);
}
