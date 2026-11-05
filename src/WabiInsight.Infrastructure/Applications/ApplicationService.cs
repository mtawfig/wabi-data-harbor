using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Applications;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Security;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Applications;

/// <summary>
/// Organization applications (specification §11). Connection strings are normalized by the dialect adapter,
/// fingerprinted (globally unique: review A7), encrypted with Data Protection and never returned.
/// Reads go through the tenant filter; Organization Administrators only.
/// </summary>
public sealed partial class ApplicationService : IApplicationService
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IConnectionStringProtector _protector;
    private readonly IDatabaseAdapterFactory _adapters;
    private readonly IAuditWriter _audit;
    private readonly IUsageEventWriter _usage;
    private readonly IClock _clock;

    public ApplicationService(ConfigurationDbContext context, ITenantContext tenantContext, IConnectionStringProtector protector, IDatabaseAdapterFactory adapters, IAuditWriter audit, IUsageEventWriter usage, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _protector = protector;
        _adapters = adapters;
        _audit = audit;
        _usage = usage;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ApplicationDto>> ListAsync(bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        _tenantContext.RequireOrganizationId();
        var query = _context.Applications.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(a => a.IsActive);
        }

        return await Project(query.OrderByDescending(a => a.IsBuiltIn).ThenBy(a => a.ApplicationName)).ToListAsync(cancellationToken);
    }

    public async Task<ApplicationDto?> GetAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        _tenantContext.RequireOrganizationId();
        return await Project(_context.Applications.AsNoTracking().Where(a => a.ApplicationId == applicationId)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ApplicationDto> CreateAsync(SaveApplicationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireAdmin();
        Validate(command.ApplicationCode, command.ApplicationName, command.ApplicationNameAr, command.Description);
        if (command.DatabaseType == DatabaseType.PlatformInsights)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "application_type_reserved");
        }

        var code = command.ApplicationCode.Trim().ToUpperInvariant();
        if (await _context.Applications.AnyAsync(a => a.ApplicationCode == code, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "application_code_exists");
        }

        var application = new Application
        {
            ApplicationId = Ids.New(),
            OrganizationId = organizationId,
            ApplicationCode = code,
            ApplicationName = command.ApplicationName.Trim(),
            ApplicationNameAr = command.ApplicationNameAr.Trim(),
            Description = Clean(command.Description),
            DatabaseType = command.DatabaseType,
            IsBuiltIn = false,
            IsActive = true,
            IsApproved = false,
            CreatedByUserId = _tenantContext.Principal.UserId,
        };

        if (!string.IsNullOrWhiteSpace(command.ConnectionString))
        {
            await ApplyConnectionAsync(application, command.ConnectionString, command.ConnectionHint, cancellationToken);
        }

        _context.Applications.Add(application);
        _audit.Append(AuditEvents.ApplicationCreated, "Application", application.ApplicationId.ToString(), new { code, application.DatabaseType, connectionConfigured = application.EncryptedConnectionString != null }, organizationId);
        _usage.Append(UsageEventTypes.ApplicationCreated, "Application", application.ApplicationId.ToString(), organizationId: organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return (await GetAsync(application.ApplicationId, cancellationToken))!;
    }

    public async Task<ApplicationDto> UpdateAsync(Guid applicationId, UpdateApplicationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireAdmin();
        Validate("X", command.ApplicationName, command.ApplicationNameAr, command.Description);
        var application = await LoadAsync(applicationId, cancellationToken);
        if (application.IsBuiltIn && !command.IsActive)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "application_builtin_protected");
        }

        var before = new { application.ApplicationName, application.ApplicationNameAr, application.Description, application.IsActive };
        application.ApplicationName = command.ApplicationName.Trim();
        application.ApplicationNameAr = command.ApplicationNameAr.Trim();
        application.Description = Clean(command.Description);
        application.IsActive = command.IsActive;
        application.UpdatedByUserId = _tenantContext.Principal.UserId;
        _audit.Append(AuditEvents.ApplicationUpdated, "Application", applicationId.ToString(), new { before, after = new { application.ApplicationName, application.ApplicationNameAr, application.Description, application.IsActive } }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return (await GetAsync(applicationId, cancellationToken))!;
    }

    public async Task<ApplicationDto> ReplaceConnectionAsync(Guid applicationId, string connectionString, string? connectionHint, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireAdmin();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "connection_string_required");
        }

        var application = await LoadAsync(applicationId, cancellationToken);
        if (application.IsBuiltIn)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "application_builtin_protected");
        }

        await ApplyConnectionAsync(application, connectionString, connectionHint, cancellationToken);
        application.LastConnectionTestAt = null;
        application.LastConnectionTestSucceeded = null;
        application.UpdatedByUserId = _tenantContext.Principal.UserId;
        _audit.Append(AuditEvents.ApplicationConnectionReplaced, "Application", applicationId.ToString(), new { hint = application.ConnectionHint }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return (await GetAsync(applicationId, cancellationToken))!;
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(DatabaseType databaseType, string connectionString, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireAdmin();
        if (databaseType == DatabaseType.PlatformInsights || string.IsNullOrWhiteSpace(connectionString))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "connection_string_required");
        }

        var result = await _adapters.Resolve(databaseType).TestAsync(connectionString, cancellationToken);
        _audit.Append(AuditEvents.ApplicationConnectionTested, "Application", null, new { databaseType, result.Succeeded, result.ReadOnlyVerified, result.Error, stored = false }, organizationId);
        _usage.Append(UsageEventTypes.ConnectionTested, "Application", null, succeeded: result.Succeeded, organizationId: organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<ConnectionTestResult> TestStoredConnectionAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireAdmin();
        var application = await LoadAsync(applicationId, cancellationToken);
        if (application.IsBuiltIn || application.EncryptedConnectionString is null)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "application_connection_missing");
        }

        var result = await _adapters.Resolve(application.DatabaseType).TestAsync(_protector.Unprotect(application.EncryptedConnectionString), cancellationToken);
        application.LastConnectionTestAt = _clock.UtcNow;
        application.LastConnectionTestSucceeded = result.Succeeded;
        _audit.Append(AuditEvents.ApplicationConnectionTested, "Application", applicationId.ToString(), new { application.DatabaseType, result.Succeeded, result.ReadOnlyVerified, result.Error, stored = true }, organizationId);
        _usage.Append(UsageEventTypes.ConnectionTested, "Application", applicationId.ToString(), succeeded: result.Succeeded, organizationId: organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<ApplicationDto> SetApprovalAsync(Guid applicationId, bool isApproved, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireAdmin();
        var application = await LoadAsync(applicationId, cancellationToken);
        if (application.IsBuiltIn)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "application_builtin_protected");
        }

        if (isApproved && application.EncryptedConnectionString is null)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "application_connection_missing");
        }

        application.IsApproved = isApproved;
        application.UpdatedByUserId = _tenantContext.Principal.UserId;
        _audit.Append(AuditEvents.ApplicationApprovalChanged, "Application", applicationId.ToString(), new { isApproved }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return (await GetAsync(applicationId, cancellationToken))!;
    }

    /// <summary>Normalizes, fingerprints (unique across all organizations) and encrypts. Plain text never leaves this method.</summary>
    private async Task ApplyConnectionAsync(Application application, string connectionString, string? hint, CancellationToken cancellationToken)
    {
        var adapter = _adapters.Resolve(application.DatabaseType);
        string normalized;
        try
        {
            normalized = adapter.Normalize(connectionString);
        }
        catch (ArgumentException ex)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, ex.Message);
        }

        var fingerprint = await _protector.FingerprintAsync(normalized, cancellationToken);
        if (await _context.Applications.IgnoreQueryFilters().AnyAsync(a => a.ConnectionFingerprint == fingerprint && a.ApplicationId != application.ApplicationId, cancellationToken))
        {
            // Review A7: a source already registered (by any organization) is rejected; nothing about the other registration is revealed.
            throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "connection_already_registered");
        }

        application.EncryptedConnectionString = _protector.Protect(normalized);
        application.ConnectionFingerprint = fingerprint;
        application.ConnectionHint = Clean(hint) ?? adapter.Hint(normalized);
    }

    private IQueryable<ApplicationDto> Project(IQueryable<Application> applications)
        => applications.Select(a => new ApplicationDto(
            a.ApplicationId, a.ApplicationCode, a.ApplicationName, a.ApplicationNameAr, a.Description, a.DatabaseType, a.IsBuiltIn, a.IsActive, a.IsApproved,
            a.EncryptedConnectionString != null, a.ConnectionHint, a.LastConnectionTestAt, a.LastConnectionTestSucceeded, a.LastMetadataRefreshAt,
            _context.DatabaseObjects.Count(o => o.ApplicationId == a.ApplicationId && o.IsPresent),
            _context.DatabaseObjects.Count(o => o.ApplicationId == a.ApplicationId && o.IsPresent && o.IsApproved),
            a.CreatedAt, a.UpdatedAt));

    private async Task<Application> LoadAsync(Guid applicationId, CancellationToken cancellationToken)
        => await _context.Applications.SingleOrDefaultAsync(a => a.ApplicationId == applicationId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "application_not_found");

    private Guid RequireAdmin()
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        if (!_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            throw new TenantContextException("Only organization administrators can manage applications.");
        }

        return organizationId;
    }

    private static void Validate(string code, string nameEn, string nameAr, string? description)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(code) || !CodePattern().IsMatch(code.Trim()))
        {
            errors.Add("application_code_invalid");
        }

        if (string.IsNullOrWhiteSpace(nameEn) || nameEn.Length > 200)
        {
            errors.Add("name_en_invalid");
        }

        if (string.IsNullOrWhiteSpace(nameAr) || nameAr.Length > 200)
        {
            errors.Add("name_ar_invalid");
        }

        if (description is { Length: > 1000 })
        {
            errors.Add("description_too_long");
        }

        if (errors.Count > 0)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, [.. errors]);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{1,63}$")]
    private static partial Regex CodePattern();
}
