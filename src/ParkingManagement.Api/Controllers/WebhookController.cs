using MediatR;
using Microsoft.AspNetCore.Mvc;
using ParkingManagement.Api.Contracts.Webhook;
using ParkingManagement.Application.Features.Parking.Commands.RegisterEntry;
using ParkingManagement.Application.Features.Parking.Commands.RegisterExit;
using ParkingManagement.Application.Features.Parking.Commands.RegisterParked;

namespace ParkingManagement.Api.Controllers;

[ApiController]
[Route("webhook")]
public sealed class WebhookController(ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive([FromBody] VehicleEventEnvelope envelope, CancellationToken cancellationToken)
    {
        return envelope.EventType.ToUpperInvariant() switch
        {
            VehicleEventTypes.Entry => await HandleEntryAsync(envelope, cancellationToken),
            VehicleEventTypes.Parked => await HandleParkedAsync(envelope, cancellationToken),
            VehicleEventTypes.Exit => await HandleExitAsync(envelope, cancellationToken),
            _ => Problem(
                title: "event_type desconhecido",
                detail: $"O evento '{envelope.EventType}' não é suportado. Use ENTRY, PARKED ou EXIT.",
                statusCode: StatusCodes.Status400BadRequest)
        };
    }

    private async Task<IActionResult> HandleEntryAsync(VehicleEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var request = new RegisterEntryRequest(envelope.LicensePlate, envelope.EntryTime ?? default);
        var result = await sender.Send(request, cancellationToken);
        return result.IsSuccess ? Ok() : this.ToProblem(result.Error);
    }

    private async Task<IActionResult> HandleParkedAsync(VehicleEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var request = new RegisterParkedRequest(envelope.LicensePlate, envelope.Lat ?? default, envelope.Lng ?? default);
        var result = await sender.Send(request, cancellationToken);
        return result.IsSuccess ? Ok() : this.ToProblem(result.Error);
    }

    private async Task<IActionResult> HandleExitAsync(VehicleEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var request = new RegisterExitRequest(envelope.LicensePlate, envelope.ExitTime ?? default);
        var result = await sender.Send(request, cancellationToken);
        return result.IsSuccess ? Ok() : this.ToProblem(result.Error);
    }
}
