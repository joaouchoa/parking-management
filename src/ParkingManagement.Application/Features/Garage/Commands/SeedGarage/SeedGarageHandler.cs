using ParkingManagement.Application.Common.Integrations;
using ParkingManagement.Application.Common.Mediator;
using ParkingManagement.Application.Common.Persistence;
using ParkingManagement.Application.Common.Results;
using ParkingManagement.Domain.Garage.Repositories;

namespace ParkingManagement.Application.Features.Garage.Commands.SeedGarage;

public sealed class SeedGarageHandler(
    ISectorRepository sectorRepository,
    ISpotRepository spotRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<SeedGarageRequest, Result<SeedGarageResponse>>
{
    public async Task<Result<SeedGarageResponse>> Handle(SeedGarageRequest request, CancellationToken cancellationToken)
    {
        var configuration = new GarageConfigurationDto(request.Garage, request.Spots);

        var (sectorsUpserted, spotsUpserted) = await GarageUpsert.ApplyAsync(
            configuration, sectorRepository, spotRepository, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new SeedGarageResponse(sectorsUpserted, spotsUpserted));
    }
}
