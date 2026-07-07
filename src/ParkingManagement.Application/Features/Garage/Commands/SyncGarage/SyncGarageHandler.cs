using ParkingManagement.Application.Common.Integrations;
using ParkingManagement.Application.Common.Mediator;
using ParkingManagement.Application.Common.Persistence;
using ParkingManagement.Application.Common.Results;
using ParkingManagement.Domain.Garage.Repositories;

namespace ParkingManagement.Application.Features.Garage.Commands.SyncGarage;

public sealed class SyncGarageHandler(
    IGarageSimulatorClient simulatorClient,
    ISectorRepository sectorRepository,
    ISpotRepository spotRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<SyncGarageRequest, Result<SyncGarageResponse>>
{
    public async Task<Result<SyncGarageResponse>> Handle(SyncGarageRequest request, CancellationToken cancellationToken)
    {
        var configuration = await simulatorClient.GetGarageConfigurationAsync(cancellationToken);

        var (sectorsSynced, spotsSynced) = await GarageUpsert.ApplyAsync(
            configuration, sectorRepository, spotRepository, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new SyncGarageResponse(sectorsSynced, spotsSynced));
    }
}
