using Microsoft.EntityFrameworkCore;
using ParkingManagement.Application.Common.Persistence;
using ParkingManagement.Domain.Garage;
using ParkingManagement.Domain.Parking;

namespace ParkingManagement.Infrastructure.Persistence;

public sealed class ParkingDbContext(DbContextOptions<ParkingDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Sector> Sectors => Set<Sector>();
    public DbSet<Spot> Spots => Set<Spot>();
    public DbSet<ParkingSession> ParkingSessions => Set<ParkingSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ParkingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
