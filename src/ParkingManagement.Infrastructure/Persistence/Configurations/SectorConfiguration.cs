using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkingManagement.Domain.Garage;

namespace ParkingManagement.Infrastructure.Persistence.Configurations;

public sealed class SectorConfiguration : IEntityTypeConfiguration<Sector>
{
    public void Configure(EntityTypeBuilder<Sector> builder)
    {
        builder.ToTable("Sectors", "parking");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code)
            .HasMaxLength(10)
            .IsRequired();

        builder.HasIndex(s => s.Code).IsUnique();

        builder.Property(s => s.BasePrice)
            .HasColumnType("decimal(18,2)");

        builder.Property(s => s.MaxCapacity)
            .IsRequired();
    }
}
