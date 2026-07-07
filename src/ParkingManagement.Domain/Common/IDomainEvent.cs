namespace ParkingManagement.Domain.Common;

public interface IDomainEvent
{
    DateTime OccurredOn { get; }
}
