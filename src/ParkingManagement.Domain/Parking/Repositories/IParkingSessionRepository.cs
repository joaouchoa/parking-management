namespace ParkingManagement.Domain.Parking.Repositories;

public interface IParkingSessionRepository
{
    Task<ParkingSession?> GetActiveByLicensePlateAsync(string licensePlate, CancellationToken cancellationToken = default);
    Task<ParkingSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(ParkingSession session, CancellationToken cancellationToken = default);
    void Update(ParkingSession session);

    Task<decimal> GetRevenueAsync(string sectorCode, DateOnly date, CancellationToken cancellationToken = default);
}
