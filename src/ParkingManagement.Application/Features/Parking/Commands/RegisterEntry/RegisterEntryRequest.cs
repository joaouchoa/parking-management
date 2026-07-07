using ParkingManagement.Application.Common.Mediator;
using ParkingManagement.Application.Common.Results;

namespace ParkingManagement.Application.Features.Parking.Commands.RegisterEntry;

public sealed record RegisterEntryRequest(
    string LicensePlate,
    DateTime EntryTime
) : ICommand<Result<RegisterEntryResponse>>;
