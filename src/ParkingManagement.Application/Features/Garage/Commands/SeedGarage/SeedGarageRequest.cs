using ParkingManagement.Application.Common.Integrations;
using ParkingManagement.Application.Common.Mediator;
using ParkingManagement.Application.Common.Results;

namespace ParkingManagement.Application.Features.Garage.Commands.SeedGarage;

public sealed record SeedGarageRequest(
    IReadOnlyCollection<GarageSectorDto> Garage,
    IReadOnlyCollection<GarageSpotDto> Spots
) : ICommand<Result<SeedGarageResponse>>;
