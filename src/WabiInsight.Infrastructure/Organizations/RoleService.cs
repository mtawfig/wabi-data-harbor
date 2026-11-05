using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Organizations;

/// <summary>Organization-scoped roles. System roles (OrganizationAdmin, Management) keep their code and cannot be deleted or deactivated.</summary>
public sealed partial class RoleService : IRoleService
{
    private readonly ConfigurationDbContext _context;
    private readonly IAuditWriter _audit;
    private readonly ITenantContext _tenantContext;

    public RoleService(ConfigurationDbContext context, IAuditWriter audit, ITenantContext tenantContext)
    {
        _context = context;
        _audit = audit;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        _tenantContext.RequireOrganizationId();
        return await _context.Roles.AsNoTracking()
            .OrderByDescending(r => r.IsSystem).ThenBy(r => r.RoleCode)
            .Select(r => new RoleDto(r.RoleId, r.RoleCode, r.NameEn, r.NameAr, r.Description, r.IsSystem, r.IsActive, r.TemplateVersion, _context.UserRoles.Count(ur => ur.RoleId == r.RoleId)))
            .ToListAsync(cancellationToken);
    }

    public async Task<RoleDto> CreateAsync(SaveRoleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireAdmin();
        var organizationId = _tenantContext.RequireOrganizationId();
        Validate(command);
        var code = command.RoleCode.Trim();
        if (await _context.Roles.AnyAsync(r => r.RoleCode == code, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "role_code_exists");
        }

        var role = new Role
        {
            RoleId = Ids.New(),
            OrganizationId = organizationId,
            RoleCode = code,
            NameEn = command.NameEn.Trim(),
            NameAr = command.NameAr.Trim(),
            Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),
            IsSystem = false,
            IsActive = command.IsActive,
        };
        _context.Roles.Add(role);
        _audit.Append(AuditEvents.RoleSaved, "Role", role.RoleId.ToString(), new { code, created = true });
        await _context.SaveChangesAsync(cancellationToken);
        return (await ListAsync(cancellationToken)).Single(r => r.RoleId == role.RoleId);
    }

    public async Task<RoleDto> UpdateAsync(Guid roleId, SaveRoleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireAdmin();
        Validate(command);
        var role = await _context.Roles.SingleOrDefaultAsync(r => r.RoleId == roleId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "role_not_found");

        var code = command.RoleCode.Trim();
        if (role.IsSystem && (!string.Equals(code, role.RoleCode, StringComparison.Ordinal) || !command.IsActive))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "system_role_protected");
        }

        if (await _context.Roles.AnyAsync(r => r.RoleCode == code && r.RoleId != roleId, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "role_code_exists");
        }

        role.RoleCode = code;
        role.NameEn = command.NameEn.Trim();
        role.NameAr = command.NameAr.Trim();
        role.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
        role.IsActive = command.IsActive;
        _audit.Append(AuditEvents.RoleSaved, "Role", roleId.ToString(), new { code, isActive = command.IsActive });
        await _context.SaveChangesAsync(cancellationToken);
        return (await ListAsync(cancellationToken)).Single(r => r.RoleId == roleId);
    }

    public async Task DeleteAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        var role = await _context.Roles.SingleOrDefaultAsync(r => r.RoleId == roleId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "role_not_found");
        if (role.IsSystem)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "system_role_protected");
        }

        if (await _context.UserRoles.AnyAsync(ur => ur.RoleId == roleId, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "role_in_use");
        }

        _context.Roles.Remove(role);
        _audit.Append(AuditEvents.RoleDeleted, "Role", roleId.ToString(), new { role.RoleCode });
        await _context.SaveChangesAsync(cancellationToken);
    }

    private void RequireAdmin()
    {
        _tenantContext.RequireOrganizationId();
        if (!_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            throw new TenantContextException("Only organization administrators can manage roles.");
        }
    }

    private static void Validate(SaveRoleCommand command)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(command.RoleCode) || !CodePattern().IsMatch(command.RoleCode.Trim()))
        {
            errors.Add("role_code_invalid");
        }

        if (string.IsNullOrWhiteSpace(command.NameEn) || command.NameEn.Length > 200)
        {
            errors.Add("name_en_invalid");
        }

        if (string.IsNullOrWhiteSpace(command.NameAr) || command.NameAr.Length > 200)
        {
            errors.Add("name_ar_invalid");
        }

        if (errors.Count > 0)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, [.. errors]);
        }
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{1,63}$")]
    private static partial Regex CodePattern();
}

/// <summary>Platform-managed default role templates. Edits never touch existing organizations' roles.</summary>
public sealed class RoleTemplateService : IRoleTemplateService
{
    private readonly ConfigurationDbContext _context;
    private readonly IAuditWriter _audit;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public RoleTemplateService(ConfigurationDbContext context, IAuditWriter audit, ITenantContext tenantContext, IClock clock)
    {
        _context = context;
        _audit = audit;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<IReadOnlyList<RoleTemplateDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        return await _context.DefaultRoleTemplates.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.RoleCode).Select(t => ToDto(t)).ToListAsync(cancellationToken);
    }

    public async Task<RoleTemplateDto> CreateAsync(SaveRoleTemplateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequirePlatformAdministrator();
        Validate(command);
        var code = command.RoleCode.Trim();
        if (await _context.DefaultRoleTemplates.AnyAsync(t => t.RoleCode == code, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "template_code_exists");
        }

        var template = new DefaultRoleTemplate
        {
            TemplateId = Ids.New(),
            RoleCode = code,
            NameEn = command.NameEn.Trim(),
            NameAr = command.NameAr.Trim(),
            Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),
            IsSystem = false,
            IsActive = command.IsActive,
            SortOrder = command.SortOrder,
            Version = 1,
            UpdatedAt = _clock.UtcNow,
            UpdatedByAdministratorId = _tenantContext.Principal.AdministratorId,
        };
        _context.DefaultRoleTemplates.Add(template);
        _audit.Append(AuditEvents.RoleTemplateSaved, "DefaultRoleTemplate", template.TemplateId.ToString(), new { code, created = true });
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(template);
    }

    public async Task<RoleTemplateDto> UpdateAsync(Guid templateId, SaveRoleTemplateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequirePlatformAdministrator();
        Validate(command);
        var template = await _context.DefaultRoleTemplates.SingleOrDefaultAsync(t => t.TemplateId == templateId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "template_not_found");

        var code = command.RoleCode.Trim();
        if (template.IsSystem && (!string.Equals(code, template.RoleCode, StringComparison.Ordinal) || !command.IsActive))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "system_template_protected");
        }

        if (await _context.DefaultRoleTemplates.AnyAsync(t => t.RoleCode == code && t.TemplateId != templateId, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "template_code_exists");
        }

        template.RoleCode = code;
        template.NameEn = command.NameEn.Trim();
        template.NameAr = command.NameAr.Trim();
        template.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
        template.IsActive = command.IsActive;
        template.SortOrder = command.SortOrder;
        template.Version += 1;
        template.UpdatedAt = _clock.UtcNow;
        template.UpdatedByAdministratorId = _tenantContext.Principal.AdministratorId;
        _audit.Append(AuditEvents.RoleTemplateSaved, "DefaultRoleTemplate", templateId.ToString(), new { code, version = template.Version });
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(template);
    }

    public async Task<IReadOnlyList<RoleTemplateDto>> GetProvisioningTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var templates = await _context.DefaultRoleTemplates.AsNoTracking()
            .Where(t => t.IsActive || t.IsSystem)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.RoleCode)
            .Select(t => ToDto(t))
            .ToListAsync(cancellationToken);

        if (templates.Count == 0)
        {
            templates = SystemRoles.Templates.Select((t, i) => new RoleTemplateDto(Guid.Empty, t.Code, t.NameEn, t.NameAr, t.Description, t.IsSystem, true, i, SystemRoles.TemplateVersion, DateTimeOffset.MinValue)).ToList();
        }

        return templates;
    }

    private static RoleTemplateDto ToDto(DefaultRoleTemplate t) =>
        new(t.TemplateId, t.RoleCode, t.NameEn, t.NameAr, t.Description, t.IsSystem, t.IsActive, t.SortOrder, t.Version, t.UpdatedAt);

    private void RequirePlatformAdministrator()
    {
        if (!_tenantContext.IsPlatformAdministrator)
        {
            throw new TenantContextException("Only platform administrators can manage role templates.");
        }
    }

    private static void Validate(SaveRoleTemplateCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.RoleCode) || command.RoleCode.Length > 64 || string.IsNullOrWhiteSpace(command.NameEn) || string.IsNullOrWhiteSpace(command.NameAr))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "template_invalid");
        }
    }
}
