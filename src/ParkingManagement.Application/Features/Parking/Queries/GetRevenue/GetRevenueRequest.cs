using ParkingManagement.Application.Common.Mediator;
using ParkingManagement.Application.Common.Results;

namespace ParkingManagement.Application.Features.Parking.Queries.GetRevenue;

public sealed record GetRevenueRequest(
    string Sector,
    DateOnly Date
) : IQuery<Result<GetRevenueResponse>>;
