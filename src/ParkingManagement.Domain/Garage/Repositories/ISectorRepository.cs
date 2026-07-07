namespace ParkingManagement.Domain.Garage.Repositories;

public interface ISectorRepository
{
    Task<Sector?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Sector>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Sector sector, CancellationToken cancellationToken = default);
    Task<int> GetTotalCapacityAsync(CancellationToken cancellationToken = default);
}
