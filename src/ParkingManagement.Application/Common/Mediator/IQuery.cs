using MediatR;

namespace ParkingManagement.Application.Common.Mediator;

public interface IQuery<out TResponse> : IRequest<TResponse>
{
}
