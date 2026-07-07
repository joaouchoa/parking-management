using MediatR;

namespace ParkingManagement.Application.Common.Mediator;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
}
