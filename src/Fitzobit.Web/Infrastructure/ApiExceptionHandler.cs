using Fitzobit.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Fitzobit.Web.Infrastructure;

/// <summary>
/// Turns exceptions into RFC 7807 responses. Business errors carry their user-facing message; anything unexpected is
/// logged in full and the client only gets a generic message with a reference id — never a stack trace or internals.
/// </summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            NotFoundException e => (StatusCodes.Status404NotFound, "Not found", e.Message),
            BusinessRuleException e => (StatusCodes.Status409Conflict, "Request could not be completed", e.Message),
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } =>
                (StatusCodes.Status413PayloadTooLarge, "Too large", "The upload is larger than the allowed size."),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested =>
                (499, "Cancelled", "The request was cancelled."),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error",
                $"Something went wrong. The details have been logged (reference {httpContext.TraceIdentifier}).")
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled error for {Method} {Path} (reference {TraceId})",
                httpContext.Request.Method, httpContext.Request.Path, httpContext.TraceIdentifier);

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
            Exception = null
        });
    }
}
