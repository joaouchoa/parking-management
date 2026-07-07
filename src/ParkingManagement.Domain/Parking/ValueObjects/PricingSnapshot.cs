using ParkingManagement.Domain.Common;

namespace ParkingManagement.Domain.Parking.ValueObjects;

/// <summary>
/// Multiplicador de preço dinâmico travado no momento da entrada (RN-4),
/// aplicado sobre o basePrice do setor somente descoberto na saída.
/// </summary>
public sealed class PricingSnapshot : ValueObject
{
    public decimal OccupancyPercentageAtEntry { get; }
    public decimal Multiplier { get; }

    private PricingSnapshot(decimal occupancyPercentageAtEntry, decimal multiplier)
    {
        OccupancyPercentageAtEntry = occupancyPercentageAtEntry;
        Multiplier = multiplier;
    }

    public static PricingSnapshot CalcularPara(decimal occupancyPercentage)
    {
        var multiplier = occupancyPercentage switch
        {
            < 25m => 0.90m,
            <= 50m => 1.00m,
            <= 75m => 1.10m,
            <= 100m => 1.25m,
            _ => 1.25m
        };

        return new PricingSnapshot(occupancyPercentage, multiplier);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return OccupancyPercentageAtEntry;
        yield return Multiplier;
    }
}
