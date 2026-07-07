using Microsoft.AspNetCore.Mvc;
using ParkingManagement.Application.Common.Results;

namespace ParkingManagement.Api.Controllers;

public static class ControllerExtensions
{
    public static IActionResult ToProblem(this ControllerBase controller, Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return controller.Problem(
            title: error.Code,
            detail: error.Message,
            statusCode: statusCode);
    }
}
