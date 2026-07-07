using MediatR;

namespace ParkingManagement.Application.Common.Mediator;

public interface ICommand<out TResponse> : IRequest<TResponse>
{
}
