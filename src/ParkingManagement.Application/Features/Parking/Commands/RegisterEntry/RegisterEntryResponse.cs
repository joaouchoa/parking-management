namespace ParkingManagement.Application.Features.Parking.Commands.RegisterEntry;

public sealed record RegisterEntryResponse(
    Guid SessionId,
    string LicensePlate,
    DateTime EntryTime,
    decimal PriceMultiplierApplied
);
