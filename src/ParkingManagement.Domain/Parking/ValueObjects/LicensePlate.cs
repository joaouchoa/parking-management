using System.Text.RegularExpressions;
using ParkingManagement.Domain.Common;
using ParkingManagement.Domain.Parking.Errors;

namespace ParkingManagement.Domain.Parking.ValueObjects;

public sealed partial class LicensePlate : ValueObject
{
    public string Value { get; }

    private LicensePlate(string value)
    {
        Value = value;
    }

    public static LicensePlate Criar(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException(ParkingSessionErrors.PlacaObrigatoria);

        var normalized = value.Trim().ToUpperInvariant();

        if (!PlateRegex().IsMatch(normalized))
            throw new DomainException(ParkingSessionErrors.PlacaFormatoInvalido);

        return new LicensePlate(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Z0-9]{5,8}$")]
    private static partial Regex PlateRegex();
}
