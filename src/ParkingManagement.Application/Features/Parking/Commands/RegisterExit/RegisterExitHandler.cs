using ParkingManagement.Application.Common.Errors;
using ParkingManagement.Application.Common.Mediator;
using ParkingManagement.Application.Common.Persistence;
using ParkingManagement.Application.Common.Results;
using ParkingManagement.Domain.Garage.Repositories;
using ParkingManagement.Domain.Parking.Repositories;

namespace ParkingManagement.Application.Features.Parking.Commands.RegisterExit;

public sealed class RegisterExitHandler(
    IParkingSessionRepository sessionRepository,
    ISectorRepository sectorRepository,
    ISpotRepository spotRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RegisterExitRequest, Result<RegisterExitResponse>>
{
    public async Task<Result<RegisterExitResponse>> Handle(RegisterExitRequest request, CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetActiveByLicensePlateAsync(request.LicensePlate, cancellationToken);
        if (session is null)
        {
            return Result.Failure<RegisterExitResponse>(
                Error.NotFound("Parking.SessaoNaoEncontrada", ApplicationErrorMessages.Parking.SessaoAtivaNaoEncontrada));
        }

        var sector = await sectorRepository.GetByCodeAsync(session.SectorCode!, cancellationToken);
        if (sector is null)
        {
            return Result.Failure<RegisterExitResponse>(
                Error.NotFound("Parking.SetorNaoEncontrado", ApplicationErrorMessages.Parking.SetorDaSessaoNaoEncontrado));
        }

        session.RegistrarSaida(request.ExitTime, sector.BasePrice);
        sessionRepository.Update(session);

        await ReleaseSpotAsync(session.SpotId!.Value, spotRepository, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new RegisterExitResponse(session.Id, session.ExitTime!.Value, session.AmountCharged!.Value));
    }

    private static async Task ReleaseSpotAsync(Guid spotId, ISpotRepository spotRepository, CancellationToken cancellationToken)
    {
        var spot = await spotRepository.GetByIdAsync(spotId, cancellationToken);
        if (spot is null)
            return;

        spot.Liberar();
        spotRepository.Update(spot);
    }
}
