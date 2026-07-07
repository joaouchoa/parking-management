using ParkingManagement.Domain.Common;

namespace ParkingManagement.Domain.Parking.Events;

public sealed record VehicleExitedEvent(Guid SessionId, decimal AmountCharged) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
