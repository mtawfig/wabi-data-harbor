using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Api.Security;

/// <summary>
/// Maps domain outcomes to RFC 9457 problem responses so controllers stay thin:
/// NotFound → 404, conflicts → 409, validation → 400, tenant/role violations → 403.
/// </summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, code, detail) = exception switch
        {
            DomainException d => d.Outcome switch
            {
                OrganizationOperationOutcome.NotFound => (StatusCodes.Status404NotFound, "Not found", "not-found", d.Message),
                OrganizationOperationOutcome.CodeAlreadyExists => (StatusCodes.Status409Conflict, "Already exists", "already-exists", d.Message),
                OrganizationOperationOutcome.EmailAlreadyRegistered => (StatusCodes.Status409Conflict, "Email already registered", "email-already-registered", d.Message),
                OrganizationOperationOutcome.InvalidStatusTransition => (StatusCodes.Status409Conflict, "Operation not allowed in the current state", "invalid-transition", d.Message),
                _ => (StatusCodes.Status400BadRequest, "Validation failed", "validation", d.Message),
            },
            OrganizationValidationException v => v.Outcome switch
            {
                OrganizationOperationOutcome.NotFound => (StatusCodes.Status404NotFound, "Not found", "not-found", v.Message),
                OrganizationOperationOutcome.EmailAlreadyRegistered => (StatusCodes.Status409Conflict, "Email already registered", "email-already-registered", v.Message),
                _ => (StatusCodes.Status400BadRequest, "Validation failed", "validation", v.Message),
            },
            TenantContextException t => (StatusCodes.Status403Forbidden, "Forbidden", "forbidden", t.Message),
            _ => (0, string.Empty, string.Empty, string.Empty),
        };

        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Type = $"https://wabi-insight.dev/problems/{code}",
            Title = title,
            Status = status,
            Detail = detail,
            Instance = httpContext.Request.Path,
        }, cancellationToken);
        return true;
    }
}
