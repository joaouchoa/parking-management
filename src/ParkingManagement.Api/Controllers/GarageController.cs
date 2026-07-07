using MediatR;
using Microsoft.AspNetCore.Mvc;
using ParkingManagement.Application.Features.Garage.Commands.SeedGarage;
using ParkingManagement.Application.Features.Garage.Commands.SyncGarage;

namespace ParkingManagement.Api.Controllers;

/// <summary>
/// Concentra as operações relacionadas à configuração da garagem (setores e vagas).
/// </summary>
[ApiController]
[Route("garage")]
public sealed class GarageController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Repete a sincronização com o simulador externo (GET /garage), a mesma
    /// que roda automaticamente no startup via GarageSyncStartupService — sem
    /// precisar reiniciar a API.
    /// </summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SyncGarageRequest(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error);
    }

    /// <summary>
    /// Configura setores e vagas diretamente, sem depender do simulador externo —
    /// útil para testes manuais quando o simulador da Estapar não está disponível.
    /// </summary>
    [HttpPost("seed")]
    public async Task<IActionResult> Seed(SeedGarageRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error);
    }
}
