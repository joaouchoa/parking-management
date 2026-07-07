using ParkingManagement.Application.Common.Mediator;
using ParkingManagement.Application.Common.Persistence;
using ParkingManagement.Application.Common.Results;
using ParkingManagement.Domain.Garage.Repositories;
using ParkingManagement.Domain.Parking;
using ParkingManagement.Domain.Parking.Repositories;
using ParkingManagement.Domain.Parking.ValueObjects;

namespace ParkingManagement.Application.Features.Parking.Commands.RegisterEntry;

public sealed class RegisterEntryHandler(
    IParkingSessionRepository sessionRepository,
    ISectorRepository sectorRepository,
    ISpotRepository spotRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RegisterEntryRequest, Result<RegisterEntryResponse>>
{
    public async Task<Result<RegisterEntryResponse>> Handle(RegisterEntryRequest request, CancellationToken cancellationToken)
    {
        var totalCapacity = await sectorRepository.GetTotalCapacityAsync(cancellationToken);
        var totalOccupied = await spotRepository.CountOccupiedAsync(cancellationToken);

        var occupancyPercentage = totalCapacity == 0
            ? 100m
            : totalOccupied * 100m / totalCapacity;

        var licensePlate = LicensePlate.Criar(request.LicensePlate);

        var session = ParkingSession.IniciarEntrada(licensePlate, request.EntryTime, occupancyPercentage);

        await sessionRepository.AddAsync(session, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new RegisterEntryResponse(
            session.Id,
            session.LicensePlate.Value,
            session.EntryTime,
            session.PricingSnapshot.Multiplier));
    }
}
