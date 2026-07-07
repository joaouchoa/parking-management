using Microsoft.EntityFrameworkCore;
using ParkingManagement.Domain.Common.ValueObjects;
using ParkingManagement.Domain.Garage;
using ParkingManagement.Domain.Garage.Repositories;
using ParkingManagement.Infrastructure.Persistence;

namespace ParkingManagement.Infrastructure.Repositories;

public sealed class SpotRepository(ParkingDbContext dbContext) : ISpotRepository
{
    public Task<Spot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Spots.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<Spot?> GetByExternalIdAsync(long externalId, CancellationToken cancellationToken = default) =>
        dbContext.Spots.FirstOrDefaultAsync(s => s.ExternalId == externalId, cancellationToken);

    public Task<Spot?> FindByCoordinateAsync(GeoCoordinate coordinate, CancellationToken cancellationToken = default) =>
        dbContext.Spots.FirstOrDefaultAsync(
            s => s.Coordinate.Latitude == coordinate.Latitude && s.Coordinate.Longitude == coordinate.Longitude,
            cancellationToken);

    public async Task AddAsync(Spot spot, CancellationToken cancellationToken = default) =>
        await dbContext.Spots.AddAsync(spot, cancellationToken);

    public Task<int> CountOccupiedAsync(CancellationToken cancellationToken = default) =>
        dbContext.Spots.CountAsync(s => s.Status == SpotStatus.Ocupada, cancellationToken);

    public Task<int> CountOccupiedBySectorAsync(string sectorCode, CancellationToken cancellationToken = default) =>
        dbContext.Spots.CountAsync(s => s.SectorCode == sectorCode && s.Status == SpotStatus.Ocupada, cancellationToken);

    public void Update(Spot spot) => dbContext.Spots.Update(spot);
}
