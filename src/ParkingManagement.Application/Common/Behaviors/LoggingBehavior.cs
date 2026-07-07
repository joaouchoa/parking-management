using MediatR;
using Microsoft.Extensions.Logging;

namespace ParkingManagement.Application.Common.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        logger.LogInformation("Processando {RequestName}", requestName);

        var response = await next();

        logger.LogInformation("Concluído {RequestName}", requestName);

        return response;
    }
}
