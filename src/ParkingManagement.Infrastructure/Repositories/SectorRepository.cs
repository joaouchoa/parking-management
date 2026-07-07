using Microsoft.EntityFrameworkCore;
using ParkingManagement.Domain.Garage;
using ParkingManagement.Domain.Garage.Repositories;
using ParkingManagement.Infrastructure.Persistence;

namespace ParkingManagement.Infrastructure.Repositories;

public sealed class SectorRepository(ParkingDbContext dbContext) : ISectorRepository
{
    public Task<Sector?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        dbContext.Sectors.FirstOrDefaultAsync(s => s.Code == code, cancellationToken);

    public async Task<IReadOnlyCollection<Sector>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Sectors.ToListAsync(cancellationToken);

    public async Task AddAsync(Sector sector, CancellationToken cancellationToken = default) =>
        await dbContext.Sectors.AddAsync(sector, cancellationToken);

    public Task<int> GetTotalCapacityAsync(CancellationToken cancellationToken = default) =>
        dbContext.Sectors.SumAsync(s => s.MaxCapacity, cancellationToken);
}
