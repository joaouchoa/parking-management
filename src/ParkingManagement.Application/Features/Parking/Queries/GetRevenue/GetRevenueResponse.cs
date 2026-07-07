namespace ParkingManagement.Application.Features.Parking.Queries.GetRevenue;

public sealed record GetRevenueResponse(
    decimal Amount,
    string Currency,
    DateTime Timestamp
);
