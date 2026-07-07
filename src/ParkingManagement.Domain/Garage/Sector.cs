using ParkingManagement.Domain.Common;
using ParkingManagement.Domain.Garage.Errors;

namespace ParkingManagement.Domain.Garage;

public sealed class Sector : Entity
{
    public string Code { get; private set; } = null!;
    public decimal BasePrice { get; private set; }
    public int MaxCapacity { get; private set; }

    private Sector()
    {
    }

    public static Sector Criar(string code, decimal basePrice, int maxCapacity)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException(GarageErrors.CodigoSetorObrigatorio);

        if (basePrice <= 0)
            throw new DomainException(GarageErrors.BasePriceInvalido);

        if (maxCapacity <= 0)
            throw new DomainException(GarageErrors.CapacidadeMaximaInvalida);

        return new Sector
        {
            Code = code,
            BasePrice = basePrice,
            MaxCapacity = maxCapacity
        };
    }

    public void AtualizarConfiguracao(decimal basePrice, int maxCapacity)
    {
        if (basePrice <= 0)
            throw new DomainException(GarageErrors.BasePriceInvalido);

        if (maxCapacity <= 0)
            throw new DomainException(GarageErrors.CapacidadeMaximaInvalida);

        BasePrice = basePrice;
        MaxCapacity = maxCapacity;
    }
}
