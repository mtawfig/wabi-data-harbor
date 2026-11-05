using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Security;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Organizations;

/// <summary>
/// Organization Administrators manage the users of their own organization. The tenant filter scopes every
/// read, departments/sections/roles are validated against the organization, temporary passwords are
/// emailed, and the organization can never lose its last active administrator.
/// </summary>
public sealed class OrganizationUserService : IOrganizationUserService
{
    private readonly ConfigurationDbContext _context;
    private readonly IOrganizationUserProvisioningService _provisioning;
    private readonly IAuditWriter _audit;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public OrganizationUserService(ConfigurationDbContext context, IOrganizationUserProvisioningService provisioning, IAuditWriter audit, ITenantContext tenantContext, IClock clock)
    {
        _context = context;
        _provisioning = provisioning;
        _audit = audit;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<PagedResult<OrganizationUserDto>> ListAsync(OrganizationUserListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        RequireAdmin();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var users = _context.OrganizationUsers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            users = users.Where(u => u.FullName.Contains(term) || u.Email.Contains(term) || (u.EmployeeNumber != null && u.EmployeeNumber.Contains(term)));
        }

        if (query.Status is { } status)
        {
            users = users.Where(u => u.Status == status);
        }

        if (query.DepartmentId is { } departmentId)
        {
            users = users.Where(u => u.DepartmentId == departmentId);
        }

        if (!string.IsNullOrWhiteSpace(query.RoleCode))
        {
            var code = query.RoleCode.Trim();
            users = users.Where(u => _context.UserRoles.Any(ur => ur.UserId == u.UserId && _context.Roles.Any(r => r.RoleId == ur.RoleId && r.RoleCode == code)));
        }

        var total = await users.CountAsync(cancellationToken);
        var items = await Project(users.OrderBy(u => u.FullName).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return new PagedResult<OrganizationUserDto>(items, page, pageSize, total);
    }

    public async Task<OrganizationUserDto?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        return await Project(_context.OrganizationUsers.AsNoTracking().Where(u => u.UserId == userId)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<(OrganizationUserDto User, string DeliveredTo)> CreateAsync(CreateOrganizationUserCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireAdmin();
        ValidateProfile(command.FullName, command.EmployeeNumber, command.PhoneNumber, command.PreferredLanguage);
        await ValidateStructureAsync(command.DepartmentId, command.SectionId, cancellationToken);
        await ValidateEmployeeNumberAsync(command.EmployeeNumber, null, cancellationToken);

        ProvisionedUser provisioned;
        try
        {
            provisioned = await _provisioning.ProvisionAsync(organizationId, command.Email, command.FullName, command.PreferredLanguage, command.RoleCodes, cancellationToken);
        }
        catch (OrganizationValidationException ex)
        {
            throw new DomainException(ex.Outcome, [.. ex.Errors]);
        }

        var user = await _context.OrganizationUsers.SingleAsync(u => u.UserId == provisioned.UserId, cancellationToken);
        user.EmployeeNumber = Clean(command.EmployeeNumber);
        user.PhoneNumber = Clean(command.PhoneNumber);
        user.DepartmentId = command.DepartmentId;
        user.SectionId = command.SectionId;
        await _context.SaveChangesAsync(cancellationToken);

        return ((await GetAsync(user.UserId, cancellationToken))!, provisioned.DeliveredTo);
    }

    public async Task<OrganizationUserDto> UpdateAsync(Guid userId, UpdateOrganizationUserCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireAdmin();
        ValidateProfile(command.FullName, command.EmployeeNumber, command.PhoneNumber, command.PreferredLanguage);
        await ValidateStructureAsync(command.DepartmentId, command.SectionId, cancellationToken);
        await ValidateEmployeeNumberAsync(command.EmployeeNumber, userId, cancellationToken);

        var user = await _context.OrganizationUsers.SingleOrDefaultAsync(u => u.UserId == userId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "user_not_found");

        var requestedRoles = command.RoleCodes.Distinct(StringComparer.Ordinal).ToList();
        var roles = await _context.Roles.Where(r => requestedRoles.Contains(r.RoleCode) && r.IsActive).ToListAsync(cancellationToken);
        if (roles.Count != requestedRoles.Count)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "role_unknown");
        }

        var currentAssignments = await _context.UserRoles.Where(ur => ur.UserId == userId).ToListAsync(cancellationToken);
        var currentCodes = await _context.Roles.Where(r => currentAssignments.Select(a => a.RoleId).Contains(r.RoleId)).Select(r => r.RoleCode).ToListAsync(cancellationToken);
        var losesAdmin = currentCodes.Contains(SystemRoles.OrganizationAdmin) && !requestedRoles.Contains(SystemRoles.OrganizationAdmin);
        if (losesAdmin)
        {
            if (_tenantContext.Principal.UserId == userId)
            {
                throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "cannot_remove_own_admin_role");
            }

            await EnsureAnotherActiveAdminAsync(userId, cancellationToken);
        }

        user.FullName = command.FullName.Trim();
        user.EmployeeNumber = Clean(command.EmployeeNumber);
        user.PhoneNumber = Clean(command.PhoneNumber);
        user.DepartmentId = command.DepartmentId;
        user.SectionId = command.SectionId;
        user.PreferredLanguage = command.PreferredLanguage;
        user.UpdatedByUserId = _tenantContext.Principal.UserId;

        var desiredRoleIds = roles.Select(r => r.RoleId).ToHashSet();
        foreach (var assignment in currentAssignments.Where(a => !desiredRoleIds.Contains(a.RoleId)))
        {
            _context.UserRoles.Remove(assignment);
        }

        foreach (var role in roles.Where(r => currentAssignments.All(a => a.RoleId != r.RoleId)))
        {
            _context.UserRoles.Add(new UserRole { OrganizationId = organizationId, UserId = userId, RoleId = role.RoleId, AssignedAt = _clock.UtcNow, AssignedByUserId = _tenantContext.Principal.UserId });
        }

        if (!currentCodes.Order().SequenceEqual(requestedRoles.Order()))
        {
            user.SecurityStamp = TemporaryPasswordGenerator.NewSecurityStamp(); // sessions pick up the new roles on revalidation
            _audit.Append(AuditEvents.OrganizationUserRolesChanged, "OrganizationUser", userId.ToString(), new { before = currentCodes, after = requestedRoles });
        }

        _audit.Append(AuditEvents.OrganizationUserUpdated, "OrganizationUser", userId.ToString(), new { user.FullName, user.EmployeeNumber, user.DepartmentId, user.SectionId, user.PreferredLanguage });
        await _context.SaveChangesAsync(cancellationToken);
        return (await GetAsync(userId, cancellationToken))!;
    }

    public async Task<OrganizationUserDto> SetStatusAsync(Guid userId, OrganizationUserStatus status, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        if (status is not (OrganizationUserStatus.Active or OrganizationUserStatus.Inactive))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "status_invalid");
        }

        if (_tenantContext.Principal.UserId == userId)
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "cannot_change_own_status");
        }

        var user = await _context.OrganizationUsers.SingleOrDefaultAsync(u => u.UserId == userId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "user_not_found");

        if (status == OrganizationUserStatus.Inactive && await IsAdminAsync(userId, cancellationToken))
        {
            await EnsureAnotherActiveAdminAsync(userId, cancellationToken);
        }

        var previous = user.Status;
        user.Status = status;
        user.SecurityStamp = TemporaryPasswordGenerator.NewSecurityStamp();
        user.UpdatedByUserId = _tenantContext.Principal.UserId;
        _audit.Append(AuditEvents.OrganizationUserStatusChanged, "OrganizationUser", userId.ToString(), new { previous = previous.ToString(), status = status.ToString() });
        await _context.SaveChangesAsync(cancellationToken);
        return (await GetAsync(userId, cancellationToken))!;
    }

    public async Task<string> ResetPasswordAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireAdmin();
        if (_tenantContext.Principal.UserId == userId)
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "use_change_password");
        }

        if (!await _context.OrganizationUsers.AnyAsync(u => u.UserId == userId, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.NotFound, "user_not_found");
        }

        try
        {
            return (await _provisioning.ReissueTemporaryPasswordAsync(organizationId, userId, cancellationToken)).DeliveredTo;
        }
        catch (OrganizationValidationException ex)
        {
            throw new DomainException(ex.Outcome, [.. ex.Errors]);
        }
    }

    private IQueryable<OrganizationUserDto> Project(IQueryable<OrganizationUser> users)
    {
        return users.Select(u => new OrganizationUserDto(
            u.UserId, u.Email, u.FullName, u.EmployeeNumber, u.PhoneNumber, u.DepartmentId, u.SectionId, u.PreferredLanguage, u.Status, u.MustChangePassword, u.LastLoginAt, u.CreatedAt,
            _context.UserRoles.Where(ur => ur.UserId == u.UserId).Join(_context.Roles, ur => ur.RoleId, r => r.RoleId, (ur, r) => r.RoleCode).OrderBy(c => c).ToList()));
    }

    private Guid RequireAdmin()
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        if (!_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            throw new TenantContextException("Only organization administrators can manage users.");
        }

        return organizationId;
    }

    private async Task ValidateStructureAsync(Guid? departmentId, Guid? sectionId, CancellationToken cancellationToken)
    {
        if (sectionId is not null && departmentId is null)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "section_requires_department");
        }

        if (departmentId is { } d && !await _context.Departments.AnyAsync(x => x.DepartmentId == d && x.IsActive, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "department_not_found");
        }

        if (sectionId is { } s && !await _context.Sections.AnyAsync(x => x.SectionId == s && x.DepartmentId == departmentId && x.IsActive, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "section_not_in_department");
        }
    }

    private async Task ValidateEmployeeNumberAsync(string? employeeNumber, Guid? exceptUserId, CancellationToken cancellationToken)
    {
        var number = Clean(employeeNumber);
        if (number is not null && await _context.OrganizationUsers.AnyAsync(u => u.EmployeeNumber == number && u.UserId != exceptUserId, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "employee_number_exists");
        }
    }

    private async Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.UserRoles.AnyAsync(ur => ur.UserId == userId && _context.Roles.Any(r => r.RoleId == ur.RoleId && r.RoleCode == SystemRoles.OrganizationAdmin), cancellationToken);
    }

    private async Task EnsureAnotherActiveAdminAsync(Guid exceptUserId, CancellationToken cancellationToken)
    {
        var others = await _context.UserRoles
            .Where(ur => ur.UserId != exceptUserId && _context.Roles.Any(r => r.RoleId == ur.RoleId && r.RoleCode == SystemRoles.OrganizationAdmin))
            .Join(_context.OrganizationUsers.Where(u => u.Status == OrganizationUserStatus.Active), ur => ur.UserId, u => u.UserId, (ur, u) => u.UserId)
            .AnyAsync(cancellationToken);
        if (!others)
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "last_admin_protected");
        }
    }

    private static void ValidateProfile(string fullName, string? employeeNumber, string? phoneNumber, string preferredLanguage)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 200)
        {
            errors.Add("full_name_invalid");
        }

        if (employeeNumber is { Length: > 50 })
        {
            errors.Add("employee_number_invalid");
        }

        if (phoneNumber is { Length: > 50 })
        {
            errors.Add("phone_invalid");
        }

        if (preferredLanguage is not ("en" or "ar"))
        {
            errors.Add("language_invalid");
        }

        if (errors.Count > 0)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, [.. errors]);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
