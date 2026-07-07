namespace ParkingManagement.Application.Features.Parking.Commands.RegisterParked;

public sealed record RegisterParkedResponse(
    Guid SessionId,
    string SectorCode,
    Guid SpotId
);
