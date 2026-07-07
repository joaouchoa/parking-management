using ParkingManagement.Domain.Common;

namespace ParkingManagement.Domain.Parking.Events;

public sealed record VehicleParkedEvent(Guid SessionId, Guid SpotId, string SectorCode) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
