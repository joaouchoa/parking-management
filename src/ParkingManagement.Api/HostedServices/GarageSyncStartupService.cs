using MediatR;
using ParkingManagement.Application.Features.Garage.Commands.SyncGarage;

namespace ParkingManagement.Api.HostedServices;

public sealed class GarageSyncStartupService(
    IServiceProvider serviceProvider,
    ILogger<GarageSyncStartupService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        try
        {
            var result = await sender.Send(new SyncGarageRequest(), cancellationToken);

            if (result.IsFailure)
            {
                LogSyncIndisponivel(result.Error.Message);
                return;
            }

            logger.LogInformation(
                "Garagem sincronizada: {Sectors} setores e {Spots} vagas.",
                result.Value.SectorsSynced,
                result.Value.SpotsSynced);
        }
        catch (Exception ex)
        {
            // O simulador é um sistema externo fora do nosso controle — não deixamos uma
            // falha dele (indisponibilidade, timeout, DNS) derrubar a API inteira no startup.
            // A API sobe normalmente; use POST /garage/sync para tentar novamente quando o
            // simulador estiver disponível.
            LogSyncIndisponivel(ex.Message);
        }
    }

    private void LogSyncIndisponivel(string motivo) =>
        logger.LogWarning(
            "Não foi possível sincronizar a configuração da garagem com o simulador ({Motivo}). " +
            "A API vai subir mesmo assim, mas /webhook e /revenue vão falhar até a sincronização " +
            "ser refeita manualmente via POST /garage/sync.",
            motivo);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
