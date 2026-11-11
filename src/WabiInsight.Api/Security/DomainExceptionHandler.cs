using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.SupportAccess;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Api.Security;

/// <summary>
/// Maps domain outcomes to RFC 9457 problem responses so controllers stay thin:
/// NotFound → 404, conflicts (including a concurrent save and a duplicate key) → 409, validation → 400, tenant/role violations → 403.
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
            SupportAccessDeniedException s => (StatusCodes.Status403Forbidden, "Support access denied", "support-access-denied", s.Code),
            TenantContextException t => (StatusCodes.Status403Forbidden, "Forbidden", "forbidden", t.Message),
            Core.Ai.AiProviderException a => (StatusCodes.Status502BadGateway, "AI provider error", "ai-provider", a.Code),
            // Two people saving the same record: the second is told to reload instead of getting a server error (E3).
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Changed by someone else", "concurrency-conflict", "concurrency_conflict"),
            // A unique key taken by a request that ran at the same time (a double click, two administrators at once).
            Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } } => (StatusCodes.Status409Conflict, "Already exists", "already-exists", "duplicate_record"),
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
