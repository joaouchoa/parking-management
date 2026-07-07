using Microsoft.EntityFrameworkCore;
using ParkingManagement.Domain.Parking;
using ParkingManagement.Domain.Parking.Repositories;
using ParkingManagement.Infrastructure.Persistence;

namespace ParkingManagement.Infrastructure.Repositories;

public sealed class ParkingSessionRepository(ParkingDbContext dbContext) : IParkingSessionRepository
{
    public Task<ParkingSession?> GetActiveByLicensePlateAsync(string licensePlate, CancellationToken cancellationToken = default) =>
        dbContext.ParkingSessions
            .Where(s => s.LicensePlate.Value == licensePlate && s.Status != ParkingSessionStatus.Finalizado)
            .OrderByDescending(s => s.EntryTime)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<ParkingSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.ParkingSessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task AddAsync(ParkingSession session, CancellationToken cancellationToken = default) =>
        await dbContext.ParkingSessions.AddAsync(session, cancellationToken);

    public void Update(ParkingSession session) => dbContext.ParkingSessions.Update(session);

    public Task<decimal> GetRevenueAsync(string sectorCode, DateOnly date, CancellationToken cancellationToken = default)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = start.AddDays(1);

        return dbContext.ParkingSessions
            .Where(s => s.SectorCode == sectorCode
                        && s.Status == ParkingSessionStatus.Finalizado
                        && s.ExitTime >= start
                        && s.ExitTime < end)
            .SumAsync(s => s.AmountCharged ?? 0m, cancellationToken);
    }
}
