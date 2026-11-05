using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Applications;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Notifications;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Security;
using WabiInsight.Core.SupportAccess;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Metadata;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.SupportAccess;

/// <summary>
/// Executes permitted operations inside the organization of the active grant. Every call is authorized by
/// <see cref="ISupportAccessAuthorizer"/> first; the outcome (success or failure code) goes to the ticket
/// timeline and the audit trail with administrator, organization, ticket, grant, operation and target (§16.4).
/// Business data is never read; secrets never appear in details.
/// </summary>
public sealed partial class SupportOperationExecutor : ISupportOperationExecutor
{
    private readonly ConfigurationDbContext _context;
    private readonly ISupportAccessAuthorizer _authorizer;
    private readonly IOrganizationUserProvisioningService _provisioning;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly IUsageEventWriter _usage;
    private readonly INotificationDispatcher _notifications;
    private readonly MetadataService _metadata;
    private readonly IConnectionStringProtector _protector;
    private readonly IDatabaseAdapterFactory _adapters;

    public SupportOperationExecutor(ConfigurationDbContext context, ISupportAccessAuthorizer authorizer, IOrganizationUserProvisioningService provisioning, ITenantContext tenantContext, IClock clock, IAuditWriter audit, IUsageEventWriter usage, INotificationDispatcher notifications, MetadataService metadata, IConnectionStringProtector protector, IDatabaseAdapterFactory adapters)
    {
        _metadata = metadata;
        _protector = protector;
        _adapters = adapters;
        _context = context;
        _authorizer = authorizer;
        _provisioning = provisioning;
        _tenantContext = tenantContext;
        _clock = clock;
        _audit = audit;
        _usage = usage;
        _notifications = notifications;
    }

    public async Task<SupportOperationResult> ExecuteAsync(ExecuteOperationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var context = await _authorizer.AuthorizeAsync(command.Operation, command.TargetReference, cancellationToken);
        try
        {
            var (result, deliveredTo, summary) = context.Operation switch
            {
                SupportOperations.CreateOrganizationUser => await CreateUserAsync(context, command.Payload, cancellationToken),
                SupportOperations.ResetOrganizationUserPassword => await ResetPasswordAsync(context, cancellationToken),
                SupportOperations.ConfigureRole => await ConfigureRoleAsync(context, command.Payload, cancellationToken),
                SupportOperations.UpdateOrganizationSettings => await UpdateSettingsAsync(context, command.Payload, cancellationToken),
                SupportOperations.RefreshMetadata => await RefreshMetadataAsync(context, cancellationToken),
                SupportOperations.DiagnoseApplicationConnection => await DiagnoseAsync(context, cancellationToken),
                _ => throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "support_operation_not_available"),
            };

            await RecordAsync(context, "Succeeded", summary, cancellationToken);
            return new SupportOperationResult(context.Operation, context.TargetReference, "Succeeded", result, deliveredTo);
        }
        catch (DomainException ex)
        {
            await RecordFailureAsync(context, ex.Message, cancellationToken);
            throw;
        }
        catch (OrganizationValidationException ex)
        {
            await RecordFailureAsync(context, ex.Message, cancellationToken);
            throw new DomainException(ex.Outcome, [.. ex.Errors]);
        }
    }

    private async Task<(object Result, string? DeliveredTo, object Summary)> CreateUserAsync(SupportExecutionContext context, JsonElement? payload, CancellationToken cancellationToken)
    {
        var p = Require(payload);
        var email = Text(p, "email") ?? throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "administrator_email_invalid");
        var fullName = Text(p, "fullName");
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 200)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "full_name_invalid");
        }

        var language = Text(p, "preferredLanguage") == "ar" ? "ar" : "en";
        var roles = p.TryGetProperty("roleCodes", out var r) && r.ValueKind == JsonValueKind.Array ? r.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToList() : [];
        var provisioned = await _provisioning.ProvisionAsync(context.OrganizationId, email, fullName, language, roles, cancellationToken);
        return (new { userId = provisioned.UserId, email = provisioned.Email, roles }, provisioned.DeliveredTo, new { userId = provisioned.UserId, roles });
    }

    private async Task<(object Result, string? DeliveredTo, object Summary)> ResetPasswordAsync(SupportExecutionContext context, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(context.TargetReference!);
        var provisioned = await _provisioning.ReissueTemporaryPasswordAsync(context.OrganizationId, userId, cancellationToken);
        return (new { userId = provisioned.UserId }, provisioned.DeliveredTo, new { userId = provisioned.UserId });
    }

    private async Task<(object Result, string? DeliveredTo, object Summary)> ConfigureRoleAsync(SupportExecutionContext context, JsonElement? payload, CancellationToken cancellationToken)
    {
        var p = Require(payload);
        var code = Text(p, "roleCode")?.Trim();
        var nameEn = Text(p, "nameEn")?.Trim();
        var nameAr = Text(p, "nameAr")?.Trim();
        var description = Text(p, "description");
        var isActive = !p.TryGetProperty("isActive", out var a) || a.ValueKind != JsonValueKind.False;
        if (code is null || !CodePattern().IsMatch(code))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "role_code_invalid");
        }

        if (string.IsNullOrWhiteSpace(nameEn) || nameEn.Length > 200 || string.IsNullOrWhiteSpace(nameAr) || nameAr.Length > 200)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "name_en_invalid");
        }

        var roles = _context.Roles.AsPlatformScope(_tenantContext).Where(x => x.OrganizationId == context.OrganizationId);
        Role role;
        object before;
        if (string.Equals(context.TargetReference, SupportAccessScope.NewRoleReference, StringComparison.OrdinalIgnoreCase))
        {
            if (await roles.AnyAsync(x => x.RoleCode == code, cancellationToken))
            {
                throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "role_code_exists");
            }

            role = new Role { RoleId = Ids.New(), OrganizationId = context.OrganizationId, RoleCode = code, NameEn = nameEn, NameAr = nameAr, Description = Clean(description), IsSystem = false, IsActive = isActive };
            _context.Roles.Add(role);
            before = new { created = true };
        }
        else
        {
            var roleId = Guid.Parse(context.TargetReference!);
            role = await roles.SingleOrDefaultAsync(x => x.RoleId == roleId, cancellationToken) ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "role_not_found");
            if (role.IsSystem && (!string.Equals(code, role.RoleCode, StringComparison.Ordinal) || !isActive))
            {
                throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "system_role_protected");
            }

            if (await roles.AnyAsync(x => x.RoleCode == code && x.RoleId != roleId, cancellationToken))
            {
                throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "role_code_exists");
            }

            before = new { role.RoleCode, role.NameEn, role.NameAr, role.Description, role.IsActive };
            role.RoleCode = code;
            role.NameEn = nameEn;
            role.NameAr = nameAr;
            role.Description = Clean(description);
            role.IsActive = isActive;
        }

        _audit.Append(AuditEvents.RoleSaved, "Role", role.RoleId.ToString(), new { code, viaSupportGrant = context.GrantId, before, after = new { role.RoleCode, role.NameEn, role.NameAr, role.Description, role.IsActive } }, context.OrganizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return (new { roleId = role.RoleId, role.RoleCode, role.IsActive }, null, new { roleId = role.RoleId, before, after = new { role.RoleCode, role.NameEn, role.IsActive } });
    }

    private async Task<(object Result, string? DeliveredTo, object Summary)> UpdateSettingsAsync(SupportExecutionContext context, JsonElement? payload, CancellationToken cancellationToken)
    {
        var p = Require(payload);
        var organization = await _context.Organizations.SingleAsync(o => o.OrganizationId == context.OrganizationId, cancellationToken);
        var before = new { organization.OrganizationName, organization.OrganizationNameAr, organization.Description, organization.ContactEmail, organization.ContactPhone, organization.TimeZone, organization.DefaultLanguage };
        var name = Text(p, "organizationName")?.Trim() ?? organization.OrganizationName;
        var nameAr = Text(p, "organizationNameAr")?.Trim() ?? organization.OrganizationNameAr;
        var timeZone = Text(p, "timeZone")?.Trim() ?? organization.TimeZone;
        var language = Text(p, "defaultLanguage") ?? organization.DefaultLanguage;
        var contactEmail = p.TryGetProperty("contactEmail", out _) ? Clean(Text(p, "contactEmail")) : organization.ContactEmail;
        if (name.Length is 0 or > 200 || nameAr.Length is 0 or > 200 || timeZone.Length is 0 or > 64 || language is not ("en" or "ar") || (contactEmail is not null && !EmailNormalizer.IsPlausible(contactEmail)))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "organization_profile_invalid");
        }

        organization.OrganizationName = name;
        organization.OrganizationNameAr = nameAr;
        organization.Description = p.TryGetProperty("description", out _) ? Clean(Text(p, "description")) : organization.Description;
        organization.ContactEmail = contactEmail;
        organization.ContactPhone = p.TryGetProperty("contactPhone", out _) ? Clean(Text(p, "contactPhone")) : organization.ContactPhone;
        organization.TimeZone = timeZone;
        organization.DefaultLanguage = language;
        organization.UpdatedByAdministratorId = context.AdministratorId;
        var after = new { organization.OrganizationName, organization.OrganizationNameAr, organization.Description, organization.ContactEmail, organization.ContactPhone, organization.TimeZone, organization.DefaultLanguage };
        _audit.Append(AuditEvents.OrganizationUpdated, "Organization", organization.OrganizationId.ToString(), new { viaSupportGrant = context.GrantId, before, after }, context.OrganizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return (after, null, new { before, after });
    }

    private async Task<(object Result, string? DeliveredTo, object Summary)> RefreshMetadataAsync(SupportExecutionContext context, CancellationToken cancellationToken)
    {
        var applicationId = Guid.Parse(context.TargetReference!);
        var result = await _metadata.RefreshForOrganizationAsync(context.OrganizationId, applicationId, cancellationToken);
        return (result, null, new { applicationId, result.ObjectsAdded, result.ObjectsRemoved, result.ColumnsAdded, result.ColumnsRemoved });
    }

    /// <summary>Narrowest diagnosis (specification §16.3): connectivity, version and read-only verification; never business rows.</summary>
    private async Task<(object Result, string? DeliveredTo, object Summary)> DiagnoseAsync(SupportExecutionContext context, CancellationToken cancellationToken)
    {
        var applicationId = Guid.Parse(context.TargetReference!);
        var application = await _context.Applications.AsPlatformScope(_tenantContext).AsNoTracking().SingleOrDefaultAsync(a => a.OrganizationId == context.OrganizationId && a.ApplicationId == applicationId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "application_not_found");
        if (application.IsBuiltIn || application.EncryptedConnectionString is null)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "application_connection_missing");
        }

        var result = await _adapters.Resolve(application.DatabaseType).TestAsync(_protector.Unprotect(application.EncryptedConnectionString), cancellationToken);
        var tracked = await _context.Applications.AsPlatformScope(_tenantContext).SingleAsync(a => a.ApplicationId == applicationId, cancellationToken);
        tracked.LastConnectionTestAt = _clock.UtcNow;
        tracked.LastConnectionTestSucceeded = result.Succeeded;
        await _context.SaveChangesAsync(cancellationToken);
        return (result, null, new { applicationId, result.Succeeded, result.ReadOnlyVerified, result.Error });
    }

    private async Task RecordAsync(SupportExecutionContext context, string outcome, object? summary, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var session = await _context.SupportSessions.AsPlatformScope(_tenantContext).SingleAsync(s => s.SessionId == context.SessionId, cancellationToken);
        session.OperationsExecuted++;
        var ticket = await _context.SupportTickets.AsPlatformScope(_tenantContext).SingleAsync(t => t.TicketId == context.TicketId, cancellationToken);
        ticket.LastActivityAt = now;
        var details = new { context.GrantId, context.SessionId, context.Operation, context.TargetReference, outcome, summary };
        _context.SupportTicketEvents.Add(new SupportTicketEvent
        {
            EventId = Ids.New(),
            TicketId = ticket.TicketId,
            OrganizationId = ticket.OrganizationId,
            ActorType = PrincipalType.PlatformAdministrator,
            ActorId = context.AdministratorId,
            ActorDisplayName = _tenantContext.Principal.DisplayName,
            EventType = TicketEventTypes.SupportOperationExecuted,
            DetailsJson = JsonSerializer.Serialize(details, SupportAccessService.JsonOptions),
            CreatedAt = now,
        });
        _audit.Append(AuditEvents.SupportOperationExecuted, "SupportAccessGrant", context.GrantId.ToString(), new { context.TicketId, ticket.ReferenceNumber, context.SessionId, context.Operation, context.TargetReference, outcome, summary }, context.OrganizationId);
        _usage.Append(UsageEventTypes.SupportOperationExecuted, "SupportAccessGrant", context.GrantId.ToString(), succeeded: outcome == "Succeeded", metadata: new { context.Operation }, organizationId: context.OrganizationId);

        var admins = await _context.OrganizationUsers.AsPlatformScope(_tenantContext).AsNoTracking()
            .Where(u => u.OrganizationId == context.OrganizationId && u.Status == OrganizationUserStatus.Active
                && _context.UserRoles.Any(ur => ur.UserId == u.UserId && _context.Roles.Any(r => r.RoleId == ur.RoleId && r.RoleCode == SystemRoles.OrganizationAdmin)))
            .Select(u => new NotificationRecipient(PrincipalType.OrganizationUser, u.UserId, u.OrganizationId, u.Email, u.FullName, u.PreferredLanguage))
            .ToListAsync(cancellationToken);
        var organization = await _context.Organizations.AsNoTracking().Where(o => o.OrganizationId == context.OrganizationId).Select(o => new { o.OrganizationName, o.OrganizationNameAr }).SingleAsync(cancellationToken);
        _notifications.Notify(admins, NotificationTypes.SupportOperationExecuted, "SupportAccessGrant", context.GrantId.ToString(),
            new { grantId = context.GrantId, ticketId = ticket.TicketId, reference = ticket.ReferenceNumber, subject = ticket.Subject, organizationName = organization.OrganizationName, organizationNameAr = organization.OrganizationNameAr, actorName = _tenantContext.Principal.DisplayName, operation = context.Operation, outcome },
            context.OrganizationId);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordFailureAsync(SupportExecutionContext context, string code, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();
        await RecordAsync(context, $"Failed: {code}", null, cancellationToken);
    }

    private static JsonElement Require(JsonElement? payload)
        => payload is { ValueKind: JsonValueKind.Object } p ? p : throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "support_payload_required");

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{1,63}$")]
    private static partial Regex CodePattern();
}
