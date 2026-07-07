using ParkingManagement.Application.Common.Errors;
using ParkingManagement.Application.Common.Mediator;
using ParkingManagement.Application.Common.Results;
using ParkingManagement.Domain.Garage.Repositories;
using ParkingManagement.Domain.Parking.Repositories;

namespace ParkingManagement.Application.Features.Parking.Queries.GetRevenue;

public sealed class GetRevenueHandler(
    IParkingSessionRepository sessionRepository,
    ISectorRepository sectorRepository)
    : IQueryHandler<GetRevenueRequest, Result<GetRevenueResponse>>
{
    public async Task<Result<GetRevenueResponse>> Handle(GetRevenueRequest request, CancellationToken cancellationToken)
    {
        var sector = await sectorRepository.GetByCodeAsync(request.Sector, cancellationToken);
        if (sector is null)
        {
            return Result.Failure<GetRevenueResponse>(
                Error.NotFound("Garage.SetorNaoEncontrado", ApplicationErrorMessages.Garage.SetorNaoEncontrado));
        }

        var amount = await sessionRepository.GetRevenueAsync(request.Sector, request.Date, cancellationToken);

        return Result.Success(new GetRevenueResponse(amount, "BRL", DateTime.UtcNow));
    }
}
