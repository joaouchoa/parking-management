using ParkingManagement.Application.Common.Mediator;
using ParkingManagement.Application.Common.Results;

namespace ParkingManagement.Application.Features.Garage.Commands.SyncGarage;

public sealed record SyncGarageRequest : ICommand<Result<SyncGarageResponse>>;
