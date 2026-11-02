using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Orders.Application.Orders;
using Orders.Domain;

namespace Orders.Api;

/// <summary>Maps application and domain exceptions to RFC 9457 ProblemDetails responses.</summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails? problem = exception switch
        {
            ValidationException ve => new ValidationProblemDetails(
                ve.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
            },
            UnknownMenuItemsException unknown => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Unknown or unavailable menu items",
                Detail = unknown.Message,
            },
            DomainException domain => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The request conflicts with the order's current state",
                Detail = domain.Message,
            },
            HttpRequestException => new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "A downstream service is unavailable, please retry",
            },
            _ => null,
        };

        if (problem is null) return false; // fall back to the default 500 handler

        context.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
