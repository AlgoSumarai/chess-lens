using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ChessLens.Core.Persistence;

namespace ChessLens.Api;

public sealed class ApiErrors(IProblemDetailsService problems, ILogger<ApiErrors> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var status = exception is ResourceBudgetReached ? 429 : exception is ArgumentException or BadHttpRequestException ? 400 : 500;
        if (status == 500) log.LogError("Request failed {CorrelationId}: {ErrorType} {StackTrace}", context.TraceIdentifier, exception.GetType().Name, exception.StackTrace);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = status == 429 ? "Resource budget reached" : status == 400 ? "Check your input" : "Request failed",
                Detail = status is 400 or 429 ? exception.Message : "Something went wrong. Please retry.", Extensions = { ["correlationId"] = context.TraceIdentifier } }
        });
    }
}
