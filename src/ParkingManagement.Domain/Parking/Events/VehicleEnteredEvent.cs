using ParkingManagement.Domain.Common;

namespace ParkingManagement.Domain.Parking.Events;

public sealed record VehicleEnteredEvent(Guid SessionId, string LicensePlate, DateTime EntryTime) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
