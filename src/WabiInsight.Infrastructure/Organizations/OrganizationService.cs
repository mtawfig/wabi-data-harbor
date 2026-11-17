using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Security;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Storage;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Organizations;

/// <summary>
/// Organization lifecycle (platform administrators) and the organization's own profile (organization users).
/// Platform reads go through explicit platform-scope queries and expose counts, never application names.
/// </summary>
public sealed partial class OrganizationService : IOrganizationService
{
    private readonly ConfigurationDbContext _context;
    private readonly IOrganizationUserProvisioningService _provisioning;
    private readonly IRoleTemplateService _templates;
    private readonly IAuditWriter _audit;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IUsageEventWriter _usage;
    private readonly IArtifactStorage _storage;

    public OrganizationService(ConfigurationDbContext context, IOrganizationUserProvisioningService provisioning, IRoleTemplateService templates, IAuditWriter audit, ITenantContext tenantContext, IClock clock, IUsageEventWriter usage, IArtifactStorage storage)
    {
        _storage = storage;
        _context = context;
        _provisioning = provisioning;
        _templates = templates;
        _audit = audit;
        _tenantContext = tenantContext;
        _clock = clock;
        _usage = usage;
    }

    public async Task<OrganizationOperationResult> CreateAsync(CreateOrganizationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequirePlatformAdministrator();

        var errors = ValidateCore(command.OrganizationName, command.OrganizationNameAr, command.ContactEmail, command.CountryCode, command.TimeZone, command.DefaultLanguage);
        var code = command.OrganizationCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!CodePattern().IsMatch(code))
        {
            errors.Add("organization_code_invalid");
        }

        if (!EmailNormalizer.IsPlausible(command.InitialAdministratorEmail))
        {
            errors.Add("administrator_email_invalid");
        }

        if (string.IsNullOrWhiteSpace(command.InitialAdministratorFullName))
        {
            errors.Add("administrator_full_name_required");
        }

        if (errors.Count > 0)
        {
            return new OrganizationOperationResult(OrganizationOperationOutcome.ValidationFailed, errors);
        }

        if (await _context.Organizations.AnyAsync(o => o.OrganizationCode == code, cancellationToken))
        {
            return new OrganizationOperationResult(OrganizationOperationOutcome.CodeAlreadyExists, ["organization_code_exists"]);
        }

        var normalizedAdminEmail = EmailNormalizer.Normalize(command.InitialAdministratorEmail);
        if (await _context.LoginIdentities.AnyAsync(l => l.NormalizedEmail == normalizedAdminEmail, cancellationToken))
        {
            return new OrganizationOperationResult(OrganizationOperationOutcome.EmailAlreadyRegistered, ["email_already_registered"]);
        }

        var now = _clock.UtcNow;
        var organization = new Organization
        {
            OrganizationId = Ids.New(),
            OrganizationCode = code,
            OrganizationName = command.OrganizationName.Trim(),
            OrganizationNameAr = command.OrganizationNameAr.Trim(),
            Description = Clean(command.Description),
            ContactEmail = Clean(command.ContactEmail),
            ContactPhone = Clean(command.ContactPhone),
            CountryCode = Clean(command.CountryCode)?.ToUpperInvariant(),
            TimeZone = command.TimeZone.Trim(),
            DefaultLanguage = command.DefaultLanguage,
            Status = OrganizationStatus.Active,
            CreatedByAdministratorId = _tenantContext.Principal.AdministratorId,
            CreatedAt = now,
        };

        string deliveredTo;
        var templates = await _templates.GetProvisioningTemplatesAsync(cancellationToken);
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            deliveredTo = await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                _context.Organizations.Add(organization);
                foreach (var template in templates)
                {
                    _context.Roles.Add(new Role
                    {
                        RoleId = Ids.New(),
                        OrganizationId = organization.OrganizationId,
                        RoleCode = template.RoleCode,
                        NameEn = template.NameEn,
                        NameAr = template.NameAr,
                        Description = template.Description,
                        IsSystem = template.IsSystem,
                        TemplateVersion = template.Version,
                        CreatedAt = now,
                    });
                }

                _context.Applications.Add(new Application
                {
                    ApplicationId = Ids.New(),
                    OrganizationId = organization.OrganizationId,
                    ApplicationName = "Platform Insights",
                    ApplicationNameAr = "تحليلات المنصة",
                    ApplicationCode = Application.PlatformInsightsCode,
                    Description = "Built-in, tenant-scoped usage analytics of Wabi Insight for this organization.",
                    DatabaseType = DatabaseType.PlatformInsights,
                    IsBuiltIn = true,
                    BuiltInCode = Application.PlatformInsightsCode,
                    IsActive = true,
                    IsApproved = true,
                    CreatedAt = now,
                });

                _audit.Append(AuditEvents.OrganizationCreated, "Organization", organization.OrganizationId.ToString(), new { code, name = organization.OrganizationName }, organization.OrganizationId);
                _usage.Append(UsageEventTypes.OrganizationCreated, "Organization", organization.OrganizationId.ToString(), organizationId: organization.OrganizationId);

                using (_context.BeginSystemScope())
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }

                var provisioned = await _provisioning.ProvisionAsync(
                    organization.OrganizationId,
                    command.InitialAdministratorEmail,
                    command.InitialAdministratorFullName,
                    organization.DefaultLanguage,
                    [SystemRoles.OrganizationAdmin],
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                return provisioned.DeliveredTo;
            });
        }
        catch (OrganizationValidationException ex)
        {
            return new OrganizationOperationResult(ex.Outcome, ex.Errors);
        }
        catch (DbUpdateException)
        {
            return new OrganizationOperationResult(OrganizationOperationOutcome.CodeAlreadyExists, ["organization_code_exists"]);
        }

        var detail = await GetAsync(organization.OrganizationId, cancellationToken);
        return new OrganizationOperationResult(OrganizationOperationOutcome.Success, [], detail, deliveredTo);
    }

    public async Task<OrganizationOperationResult> UpdateAsync(Guid organizationId, UpdateOrganizationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequirePlatformAdministrator();

        var errors = ValidateCore(command.OrganizationName, command.OrganizationNameAr, command.ContactEmail, command.CountryCode, command.TimeZone, command.DefaultLanguage);
        if (errors.Count > 0)
        {
            return new OrganizationOperationResult(OrganizationOperationOutcome.ValidationFailed, errors);
        }

        var organization = await _context.Organizations.SingleOrDefaultAsync(o => o.OrganizationId == organizationId, cancellationToken);
        if (organization is null)
        {
            return new OrganizationOperationResult(OrganizationOperationOutcome.NotFound, ["organization_not_found"]);
        }

        var before = Snapshot(organization);
        organization.OrganizationName = command.OrganizationName.Trim();
        organization.OrganizationNameAr = command.OrganizationNameAr.Trim();
        organization.Description = Clean(command.Description);
        organization.ContactEmail = Clean(command.ContactEmail);
        organization.ContactPhone = Clean(command.ContactPhone);
        organization.CountryCode = Clean(command.CountryCode)?.ToUpperInvariant();
        organization.TimeZone = command.TimeZone.Trim();
        organization.DefaultLanguage = command.DefaultLanguage;
        organization.UpdatedByAdministratorId = _tenantContext.Principal.AdministratorId;
        _audit.Append(AuditEvents.OrganizationUpdated, "Organization", organizationId.ToString(), new { before, after = Snapshot(organization) }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);

        return new OrganizationOperationResult(OrganizationOperationOutcome.Success, [], await GetAsync(organizationId, cancellationToken));
    }

    public async Task<OrganizationOperationResult> SetStatusAsync(Guid organizationId, OrganizationStatus status, string? reason, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var organization = await _context.Organizations.SingleOrDefaultAsync(o => o.OrganizationId == organizationId, cancellationToken);
        if (organization is null)
        {
            return new OrganizationOperationResult(OrganizationOperationOutcome.NotFound, ["organization_not_found"]);
        }

        if (!IsAllowedTransition(organization.Status, status))
        {
            return new OrganizationOperationResult(OrganizationOperationOutcome.InvalidStatusTransition, ["organization_status_transition_invalid"]);
        }

        var previous = organization.Status;
        organization.Status = status;
        organization.UpdatedByAdministratorId = _tenantContext.Principal.AdministratorId;
        _audit.Append(AuditEvents.OrganizationStatusChanged, "Organization", organizationId.ToString(), new { previous = previous.ToString(), status = status.ToString(), reason = Clean(reason) }, organizationId);
        _usage.Append(UsageEventTypes.OrganizationStatusChanged, "Organization", organizationId.ToString(), metadata: new { status = status.ToString() }, organizationId: organizationId);
        await _context.SaveChangesAsync(cancellationToken);

        return new OrganizationOperationResult(OrganizationOperationOutcome.Success, [], await GetAsync(organizationId, cancellationToken));
    }

    public async Task<OrganizationOperationResult> ResendInitialAdministratorAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var organization = await _context.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.OrganizationId == organizationId, cancellationToken);
        if (organization is null)
        {
            return new OrganizationOperationResult(OrganizationOperationOutcome.NotFound, ["organization_not_found"]);
        }

        var initial = await InitialAdministratorQuery(organizationId).FirstOrDefaultAsync(cancellationToken);
        if (initial is null)
        {
            return new OrganizationOperationResult(OrganizationOperationOutcome.NotFound, ["initial_administrator_not_found"]);
        }

        if (!initial.MustChangePassword || initial.LastLoginAt is not null)
        {
            // The account completed its first sign-in: later resets belong to the organization administrator (P2.2) or a support grant (P5).
            return new OrganizationOperationResult(OrganizationOperationOutcome.InvalidStatusTransition, ["initial_administrator_already_activated"]);
        }

        try
        {
            var provisioned = await _provisioning.ReissueTemporaryPasswordAsync(organizationId, initial.UserId, cancellationToken);
            return new OrganizationOperationResult(OrganizationOperationOutcome.Success, [], await GetAsync(organizationId, cancellationToken), provisioned.DeliveredTo);
        }
        catch (OrganizationValidationException ex)
        {
            return new OrganizationOperationResult(ex.Outcome, ex.Errors);
        }
    }

    public async Task<OrganizationDetail?> GetAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        return await DetailQuery(_context.Organizations.AsNoTracking().Where(o => o.OrganizationId == organizationId)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<PagedResult<OrganizationDetail>> ListAsync(OrganizationListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        RequirePlatformAdministrator();

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var organizations = _context.Organizations.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            organizations = organizations.Where(o => o.OrganizationCode.Contains(term) || o.OrganizationName.Contains(term) || o.OrganizationNameAr.Contains(term));
        }

        if (query.Status is { } status)
        {
            organizations = organizations.Where(o => o.Status == status);
        }

        var total = await organizations.CountAsync(cancellationToken);
        var items = await DetailQuery(organizations.OrderBy(o => o.OrganizationName).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return new PagedResult<OrganizationDetail>(items, page, pageSize, total);
    }

    public async Task<OrganizationProfile> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        return await _context.Organizations.AsNoTracking()
            .Where(o => o.OrganizationId == organizationId)
            .Select(o => new OrganizationProfile(o.OrganizationId, o.OrganizationCode, o.OrganizationName, o.OrganizationNameAr, o.Description, o.ContactEmail, o.ContactPhone, o.CountryCode, o.TimeZone, o.DefaultLanguage, o.Status, o.CreatedAt, o.RequireSecondAdminApprovalForSupportAccess, o.LogoArtifactKey != null, o.AskClarifyingQuestions))
            .SingleAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MyRoleDto>> GetMyRolesAsync(CancellationToken cancellationToken = default)
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        var userId = _tenantContext.Principal.UserId;
        return await (from ur in _context.UserRoles.AsNoTracking()
                      join r in _context.Roles.AsNoTracking() on ur.RoleId equals r.RoleId
                      where ur.OrganizationId == organizationId && r.OrganizationId == organizationId && ur.UserId == userId && r.IsActive
                      orderby r.NameEn
                      select new MyRoleDto(r.RoleCode, r.NameEn, r.NameAr))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// PNG and JPEG only (P13.25). SVG is a document that can carry script, and no text filter over it is reliable;
    /// a logo uploaded as SVG before this rule is still served, as a download that cannot run (see the controller).
    /// </summary>
    private static readonly string[] LogoContentTypes = ["image/png", "image/jpeg"];
    public const long MaxLogoBytes = 1024 * 1024;

    public async Task SetLogoAsync(LogoUpload upload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        var organizationId = _tenantContext.RequireOrganizationId();
        if (!_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            throw new TenantContextException("Only organization administrators can change the logo.");
        }

        var contentType = upload.ContentType.Split(';')[0].Trim().ToLowerInvariant();
        if (!LogoContentTypes.Contains(contentType))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "logo_type_invalid");
        }

        if (upload.Length <= 0 || upload.Length > MaxLogoBytes)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "logo_too_large");
        }

        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxLogoBytes || !LooksLikeImage(buffer, contentType))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "logo_type_invalid");
        }

        buffer.Position = 0;
        var organization = await _context.Organizations.SingleAsync(o => o.OrganizationId == organizationId, cancellationToken);
        var previous = organization.LogoArtifactKey;
        organization.LogoArtifactKey = await _storage.SaveAsync("organization-logos", buffer, cancellationToken);
        organization.LogoContentType = contentType;
        _audit.Append(AuditEvents.OrganizationUpdated, "Organization", organizationId.ToString(), new { logo = "replaced", contentType, bytes = buffer.Length }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        if (previous is not null)
        {
            await _storage.DeleteAsync(previous, cancellationToken);
        }
    }

    public async Task RemoveLogoAsync(CancellationToken cancellationToken = default)
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        if (!_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            throw new TenantContextException("Only organization administrators can change the logo.");
        }

        var organization = await _context.Organizations.SingleAsync(o => o.OrganizationId == organizationId, cancellationToken);
        var previous = organization.LogoArtifactKey;
        organization.LogoArtifactKey = null;
        organization.LogoContentType = null;
        _audit.Append(AuditEvents.OrganizationUpdated, "Organization", organizationId.ToString(), new { logo = "removed" }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        if (previous is not null)
        {
            await _storage.DeleteAsync(previous, cancellationToken);
        }
    }

    public async Task<LogoDownload?> OpenLogoAsync(CancellationToken cancellationToken = default)
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        var organization = await _context.Organizations.AsNoTracking().SingleAsync(o => o.OrganizationId == organizationId, cancellationToken);
        if (organization.LogoArtifactKey is null)
        {
            return null;
        }

        var stream = await _storage.OpenReadAsync(organization.LogoArtifactKey, cancellationToken);
        return stream is null ? null : new LogoDownload(stream, organization.LogoContentType ?? "application/octet-stream");
    }

    /// <summary>Magic bytes for PNG and JPEG: the declared type has to be what the file really is.</summary>
    private static bool LooksLikeImage(MemoryStream buffer, string contentType)
    {
        var bytes = buffer.GetBuffer();
        var length = (int)buffer.Length;
        return contentType switch
        {
            "image/png" => length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47,
            "image/jpeg" => length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            _ => false,
        };
    }

    public async Task<OrganizationOperationResult> UpdateCurrentAsync(UpdateOrganizationProfileCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = _tenantContext.RequireOrganizationId();
        if (!_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            throw new TenantContextException("Only organization administrators can update the organization profile.");
        }

        var errors = ValidateCore(command.OrganizationName, command.OrganizationNameAr, command.ContactEmail, null, command.TimeZone, command.DefaultLanguage);
        if (errors.Count > 0)
        {
            return new OrganizationOperationResult(OrganizationOperationOutcome.ValidationFailed, errors);
        }

        var organization = await _context.Organizations.SingleAsync(o => o.OrganizationId == organizationId, cancellationToken);
        var before = Snapshot(organization);
        organization.OrganizationName = command.OrganizationName.Trim();
        organization.OrganizationNameAr = command.OrganizationNameAr.Trim();
        organization.Description = Clean(command.Description);
        organization.ContactEmail = Clean(command.ContactEmail);
        organization.ContactPhone = Clean(command.ContactPhone);
        organization.TimeZone = command.TimeZone.Trim();
        organization.DefaultLanguage = command.DefaultLanguage;
        organization.RequireSecondAdminApprovalForSupportAccess = command.RequireSecondAdminApprovalForSupportAccess;
        organization.AskClarifyingQuestions = command.AskClarifyingQuestions ?? organization.AskClarifyingQuestions;
        _audit.Append(AuditEvents.OrganizationUpdated, "Organization", organizationId.ToString(), new { before, after = Snapshot(organization), requireSecondAdminApproval = command.RequireSecondAdminApprovalForSupportAccess, askClarifyingQuestions = organization.AskClarifyingQuestions }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);

        return new OrganizationOperationResult(OrganizationOperationOutcome.Success, []);
    }

    private IQueryable<OrganizationDetail> DetailQuery(IQueryable<Organization> organizations)
    {
        // Aggregates only: application names and connection details are organization-private.
        return organizations.Select(o => new OrganizationDetail(
            o.OrganizationId,
            o.OrganizationCode,
            o.OrganizationName,
            o.OrganizationNameAr,
            o.Description,
            o.ContactEmail,
            o.ContactPhone,
            o.CountryCode,
            o.TimeZone,
            o.DefaultLanguage,
            o.Status,
            o.CreatedAt,
            o.UpdatedAt,
            _context.OrganizationUsers.IgnoreQueryFilters().Count(u => u.OrganizationId == o.OrganizationId),
            _context.OrganizationUsers.IgnoreQueryFilters().Count(u => u.OrganizationId == o.OrganizationId && u.Status == OrganizationUserStatus.Active),
            _context.Applications.IgnoreQueryFilters().Count(a => a.OrganizationId == o.OrganizationId),
            _context.OrganizationUsers.IgnoreQueryFilters()
                .Where(u => u.OrganizationId == o.OrganizationId && u.CreatedByAdministratorId != null)
                .OrderBy(u => u.CreatedAt)
                .Select(u => u.Email)
                .FirstOrDefault(),
            _context.OrganizationUsers.IgnoreQueryFilters()
                .Where(u => u.OrganizationId == o.OrganizationId && u.CreatedByAdministratorId != null)
                .OrderBy(u => u.CreatedAt)
                .Select(u => !u.MustChangePassword)
                .FirstOrDefault()));
    }

    private IQueryable<OrganizationUser> InitialAdministratorQuery(Guid organizationId)
    {
        return _context.OrganizationUsers.IgnoreQueryFilters()
            .Where(u => u.OrganizationId == organizationId && u.CreatedByAdministratorId != null)
            .OrderBy(u => u.CreatedAt);
    }

    private void RequirePlatformAdministrator()
    {
        if (!_tenantContext.IsPlatformAdministrator)
        {
            throw new TenantContextException("Only platform administrators can manage organizations.");
        }
    }

    private static List<string> ValidateCore(string? name, string? nameAr, string? contactEmail, string? countryCode, string? timeZone, string? defaultLanguage)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            errors.Add("organization_name_invalid");
        }

        if (string.IsNullOrWhiteSpace(nameAr) || nameAr.Length > 200)
        {
            errors.Add("organization_name_ar_invalid");
        }

        if (!string.IsNullOrWhiteSpace(contactEmail) && !EmailNormalizer.IsPlausible(contactEmail))
        {
            errors.Add("contact_email_invalid");
        }

        if (!string.IsNullOrWhiteSpace(countryCode) && !CountryPattern().IsMatch(countryCode.Trim()))
        {
            errors.Add("country_code_invalid");
        }

        if (!TimeZones.TryFind(timeZone, out _))
        {
            errors.Add("time_zone_invalid");
        }

        if (defaultLanguage is not ("en" or "ar"))
        {
            errors.Add("default_language_invalid");
        }

        return errors;
    }

    private static bool IsAllowedTransition(OrganizationStatus from, OrganizationStatus to) => (from, to) switch
    {
        (OrganizationStatus.Active, OrganizationStatus.Suspended) => true,
        (OrganizationStatus.Active, OrganizationStatus.Deactivated) => true,
        (OrganizationStatus.Suspended, OrganizationStatus.Active) => true,
        (OrganizationStatus.Suspended, OrganizationStatus.Deactivated) => true,
        (OrganizationStatus.Deactivated, OrganizationStatus.Active) => true,
        _ => false,
    };

    private static object Snapshot(Organization o) => new { o.OrganizationName, o.OrganizationNameAr, o.Description, o.ContactEmail, o.ContactPhone, o.CountryCode, o.TimeZone, o.DefaultLanguage };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{1,30}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[A-Za-z]{2}$")]
    private static partial Regex CountryPattern();
}
