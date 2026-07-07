using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using ParkingManagement.Domain.Common;

namespace ParkingManagement.Api.Middlewares;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "Falha de validação",
                string.Join(" ", ex.Errors.Select(e => e.ErrorMessage)));
        }
        catch (DomainException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status422UnprocessableEntity, "Regra de negócio violada", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro não tratado ao processar a requisição {Path}", context.Request.Path);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "Erro interno",
                "Ocorreu um erro inesperado. Tente novamente mais tarde.");
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail
        };

        await context.Response.WriteAsJsonAsync(problemDetails);
    }
}
