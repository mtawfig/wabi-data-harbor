using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Organizations;

/// <summary>Departments and sections of the current organization. Tenant filters scope every read; writes require OrganizationAdmin.</summary>
public sealed partial class OrganizationStructureService : IOrganizationStructureService
{
    private readonly ConfigurationDbContext _context;
    private readonly IAuditWriter _audit;
    private readonly ITenantContext _tenantContext;

    public OrganizationStructureService(ConfigurationDbContext context, IAuditWriter audit, ITenantContext tenantContext)
    {
        _context = context;
        _audit = audit;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<DepartmentDto>> ListDepartmentsAsync(bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        _tenantContext.RequireOrganizationId();
        var query = _context.Departments.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(d => d.IsActive);
        }

        return await query
            .OrderBy(d => d.DepartmentCode)
            .Select(d => new DepartmentDto(
                d.DepartmentId, d.DepartmentCode, d.DepartmentNameEn, d.DepartmentNameAr, d.IsActive,
                _context.Sections.Count(s => s.DepartmentId == d.DepartmentId),
                _context.OrganizationUsers.Count(u => u.DepartmentId == d.DepartmentId)))
            .ToListAsync(cancellationToken);
    }

    public async Task<DepartmentDto> CreateDepartmentAsync(SaveDepartmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireAdmin();
        Validate(command.DepartmentCode, command.DepartmentNameEn, command.DepartmentNameAr);
        var code = command.DepartmentCode.Trim().ToUpperInvariant();
        if (await _context.Departments.AnyAsync(d => d.DepartmentCode == code, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "department_code_exists");
        }

        var department = new Department
        {
            DepartmentId = Ids.New(),
            OrganizationId = organizationId,
            DepartmentCode = code,
            DepartmentNameEn = command.DepartmentNameEn.Trim(),
            DepartmentNameAr = command.DepartmentNameAr.Trim(),
            IsActive = command.IsActive,
        };
        _context.Departments.Add(department);
        _audit.Append(AuditEvents.DepartmentSaved, "Department", department.DepartmentId.ToString(), new { code, created = true });
        await _context.SaveChangesAsync(cancellationToken);
        return (await ListDepartmentsAsync(true, cancellationToken)).Single(d => d.DepartmentId == department.DepartmentId);
    }

    public async Task<DepartmentDto> UpdateDepartmentAsync(Guid departmentId, SaveDepartmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireAdmin();
        Validate(command.DepartmentCode, command.DepartmentNameEn, command.DepartmentNameAr);
        var department = await _context.Departments.SingleOrDefaultAsync(d => d.DepartmentId == departmentId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "department_not_found");

        var code = command.DepartmentCode.Trim().ToUpperInvariant();
        if (await _context.Departments.AnyAsync(d => d.DepartmentCode == code && d.DepartmentId != departmentId, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "department_code_exists");
        }

        department.DepartmentCode = code;
        department.DepartmentNameEn = command.DepartmentNameEn.Trim();
        department.DepartmentNameAr = command.DepartmentNameAr.Trim();
        department.IsActive = command.IsActive;
        _audit.Append(AuditEvents.DepartmentSaved, "Department", departmentId.ToString(), new { code, isActive = command.IsActive });
        await _context.SaveChangesAsync(cancellationToken);
        return (await ListDepartmentsAsync(true, cancellationToken)).Single(d => d.DepartmentId == departmentId);
    }

    public async Task<IReadOnlyList<SectionDto>> ListSectionsAsync(Guid? departmentId = null, bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        _tenantContext.RequireOrganizationId();
        var query = _context.Sections.AsNoTracking();
        if (departmentId is { } id)
        {
            query = query.Where(s => s.DepartmentId == id);
        }

        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        return await query
            .OrderBy(s => s.DepartmentId).ThenBy(s => s.SectionCode)
            .Select(s => new SectionDto(s.SectionId, s.DepartmentId, s.SectionCode, s.SectionNameEn, s.SectionNameAr, s.IsActive, _context.OrganizationUsers.Count(u => u.SectionId == s.SectionId)))
            .ToListAsync(cancellationToken);
    }

    public async Task<SectionDto> CreateSectionAsync(SaveSectionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireAdmin();
        Validate(command.SectionCode, command.SectionNameEn, command.SectionNameAr);
        if (!await _context.Departments.AnyAsync(d => d.DepartmentId == command.DepartmentId, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "department_not_found");
        }

        var code = command.SectionCode.Trim().ToUpperInvariant();
        if (await _context.Sections.AnyAsync(s => s.DepartmentId == command.DepartmentId && s.SectionCode == code, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "section_code_exists");
        }

        var section = new Section
        {
            SectionId = Ids.New(),
            OrganizationId = organizationId,
            DepartmentId = command.DepartmentId,
            SectionCode = code,
            SectionNameEn = command.SectionNameEn.Trim(),
            SectionNameAr = command.SectionNameAr.Trim(),
            IsActive = command.IsActive,
        };
        _context.Sections.Add(section);
        _audit.Append(AuditEvents.SectionSaved, "Section", section.SectionId.ToString(), new { code, departmentId = command.DepartmentId, created = true });
        await _context.SaveChangesAsync(cancellationToken);
        return (await ListSectionsAsync(null, true, cancellationToken)).Single(s => s.SectionId == section.SectionId);
    }

    public async Task<SectionDto> UpdateSectionAsync(Guid sectionId, SaveSectionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireAdmin();
        Validate(command.SectionCode, command.SectionNameEn, command.SectionNameAr);
        var section = await _context.Sections.SingleOrDefaultAsync(s => s.SectionId == sectionId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "section_not_found");

        if (section.DepartmentId != command.DepartmentId)
        {
            // Moving a section would orphan its users' department/section pairing; create a new section instead.
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "section_department_immutable");
        }

        var code = command.SectionCode.Trim().ToUpperInvariant();
        if (await _context.Sections.AnyAsync(s => s.DepartmentId == section.DepartmentId && s.SectionCode == code && s.SectionId != sectionId, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "section_code_exists");
        }

        section.SectionCode = code;
        section.SectionNameEn = command.SectionNameEn.Trim();
        section.SectionNameAr = command.SectionNameAr.Trim();
        section.IsActive = command.IsActive;
        _audit.Append(AuditEvents.SectionSaved, "Section", sectionId.ToString(), new { code, isActive = command.IsActive });
        await _context.SaveChangesAsync(cancellationToken);
        return (await ListSectionsAsync(null, true, cancellationToken)).Single(s => s.SectionId == sectionId);
    }

    private Guid RequireAdmin()
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        if (!_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            throw new TenantContextException("Only organization administrators can manage the organization structure.");
        }

        return organizationId;
    }

    private static void Validate(string code, string nameEn, string nameAr)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(code) || !CodePattern().IsMatch(code.Trim()))
        {
            errors.Add("code_invalid");
        }

        if (string.IsNullOrWhiteSpace(nameEn) || nameEn.Length > 200)
        {
            errors.Add("name_en_invalid");
        }

        if (string.IsNullOrWhiteSpace(nameAr) || nameAr.Length > 200)
        {
            errors.Add("name_ar_invalid");
        }

        if (errors.Count > 0)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, [.. errors]);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,31}$")]
    private static partial Regex CodePattern();
}
