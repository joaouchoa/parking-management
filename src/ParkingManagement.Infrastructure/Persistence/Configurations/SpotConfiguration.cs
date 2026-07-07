using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingManagement.Domain.Garage;

namespace ParkingManagement.Infrastructure.Persistence.Configurations;

public sealed class SpotConfiguration : IEntityTypeConfiguration<Spot>
{
    public void Configure(EntityTypeBuilder<Spot> builder)
    {
        builder.ToTable("Spots", "parking");

        builder.HasKey(s => s.Id);

        builder.HasIndex(s => s.ExternalId).IsUnique();

        builder.Property(s => s.SectorCode)
            .HasMaxLength(10)
            .IsRequired();

        builder.OwnsOne(s => s.Coordinate, coordinate =>
        {
            coordinate.Property(c => c.Latitude).HasColumnName("Latitude");
            coordinate.Property(c => c.Longitude).HasColumnName("Longitude");
        });

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(20);
    }
}
