using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingManagement.Domain.Parking;

namespace ParkingManagement.Infrastructure.Persistence.Configurations;

public sealed class ParkingSessionConfiguration : IEntityTypeConfiguration<ParkingSession>
{
    public void Configure(EntityTypeBuilder<ParkingSession> builder)
    {
        builder.ToTable("ParkingSessions", "parking");

        builder.HasKey(s => s.Id);

        builder.OwnsOne(s => s.LicensePlate, licensePlate =>
        {
            licensePlate.Property(l => l.Value)
                .HasColumnName("LicensePlate")
                .HasMaxLength(8)
                .IsRequired();

            licensePlate.HasIndex(l => l.Value);
        });

        builder.OwnsOne(s => s.PricingSnapshot, pricing =>
        {
            pricing.Property(p => p.OccupancyPercentageAtEntry).HasColumnName("OccupancyPercentageAtEntry").HasColumnType("decimal(5,2)");
            pricing.Property(p => p.Multiplier).HasColumnName("PriceMultiplier").HasColumnType("decimal(5,2)");
        });

        builder.OwnsOne(s => s.ParkedCoordinate, coordinate =>
        {
            coordinate.Property(c => c.Latitude).HasColumnName("ParkedLatitude");
            coordinate.Property(c => c.Longitude).HasColumnName("ParkedLongitude");
        });

        builder.Property(s => s.SectorCode).HasMaxLength(10);

        builder.Property(s => s.AmountCharged).HasColumnType("decimal(18,2)");

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasIndex(s => new { s.SectorCode, s.ExitTime });
        builder.HasIndex(s => s.Status);

        builder.Ignore(s => s.DomainEvents);
    }
}
