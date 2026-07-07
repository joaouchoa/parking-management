using ParkingManagement.Application.Common.Mediator;
using ParkingManagement.Application.Common.Results;

namespace ParkingManagement.Application.Features.Parking.Commands.RegisterParked;

public sealed record RegisterParkedRequest(
    string LicensePlate,
    double Lat,
    double Lng
) : ICommand<Result<RegisterParkedResponse>>;
