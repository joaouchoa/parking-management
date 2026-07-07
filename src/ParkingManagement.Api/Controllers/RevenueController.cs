using MediatR;
using Microsoft.AspNetCore.Mvc;
using ParkingManagement.Application.Features.Parking.Queries.GetRevenue;

namespace ParkingManagement.Api.Controllers;

[ApiController]
[Route("revenue")]
public sealed class RevenueController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string sector,
        [FromQuery] DateOnly date,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetRevenueRequest(sector, date), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error);
    }
}
