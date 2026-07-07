using ParkingManagement.Application.Common.Errors;
using ParkingManagement.Application.Common.Mediator;
using ParkingManagement.Application.Common.Persistence;
using ParkingManagement.Application.Common.Results;
using ParkingManagement.Domain.Common.ValueObjects;
using ParkingManagement.Domain.Garage.Repositories;
using ParkingManagement.Domain.Parking.Repositories;

namespace ParkingManagement.Application.Features.Parking.Commands.RegisterParked;

public sealed class RegisterParkedHandler(
    IParkingSessionRepository sessionRepository,
    ISpotRepository spotRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<RegisterParkedRequest, Result<RegisterParkedResponse>>
{
    public async Task<Result<RegisterParkedResponse>> Handle(RegisterParkedRequest request, CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetActiveByLicensePlateAsync(request.LicensePlate, cancellationToken);
        if (session is null)
        {
            return Result.Failure<RegisterParkedResponse>(
                Error.NotFound("Parking.SessaoNaoEncontrada", ApplicationErrorMessages.Parking.SessaoAtivaNaoEncontrada));
        }

        var coordinate = GeoCoordinate.Criar(request.Lat, request.Lng);

        var spot = await spotRepository.FindByCoordinateAsync(coordinate, cancellationToken);
        if (spot is null)
        {
            return Result.Failure<RegisterParkedResponse>(
                Error.NotFound("Parking.VagaNaoEncontrada", ApplicationErrorMessages.Parking.VagaNaoEncontradaPorCoordenada));
        }

        spot.Ocupar();
        spotRepository.Update(spot);

        session.RegistrarEstacionamento(spot.Id, spot.SectorCode, coordinate, DateTime.UtcNow);
        sessionRepository.Update(session);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new RegisterParkedResponse(session.Id, spot.SectorCode, spot.Id));
    }
}
