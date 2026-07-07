namespace ParkingManagement.Application.Features.Parking.Commands.RegisterExit;

public sealed record RegisterExitResponse(
    Guid SessionId,
    DateTime ExitTime,
    decimal AmountCharged
);
