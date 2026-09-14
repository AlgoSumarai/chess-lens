using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ChessLens.Api;

public sealed class ApiErrors(IProblemDetailsService problems, ILogger<ApiErrors> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var status = exception is ArgumentException or BadHttpRequestException ? 400 : 500;
        if (status == 500) log.LogError(exception, "Request failed {CorrelationId}", context.TraceIdentifier);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = status == 400 ? "Check your input" : "Request failed",
                Detail = status == 400 ? exception.Message : "Something went wrong. Please retry.", Extensions = { ["correlationId"] = context.TraceIdentifier } }
        });
    }
}
