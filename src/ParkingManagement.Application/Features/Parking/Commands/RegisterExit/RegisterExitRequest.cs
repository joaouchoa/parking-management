using ParkingManagement.Application.Common.Mediator;
using ParkingManagement.Application.Common.Results;

namespace ParkingManagement.Application.Features.Parking.Commands.RegisterExit;

public sealed record RegisterExitRequest(
    string LicensePlate,
    DateTime ExitTime
) : ICommand<Result<RegisterExitResponse>>;
