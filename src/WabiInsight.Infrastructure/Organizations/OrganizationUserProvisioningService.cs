using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Security;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Authentication;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Organizations;

/// <summary>
/// Creates organization users and emails their temporary password (owner decision H). Adds the rows to the
/// current unit of work; the caller commits. Writes run in system scope because onboarding is performed by a
/// platform administrator who has no tenant context.
/// </summary>
public sealed class OrganizationUserProvisioningService : IOrganizationUserProvisioningService
{
    private readonly ConfigurationDbContext _context;
    private readonly IPasswordHashingService _hasher;
    private readonly IEmailOutbox _outbox;
    private readonly IAuditWriter _audit;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly EmailOptions _email;
    private readonly PlatformUrlOptions _urls;

    public OrganizationUserProvisioningService(
        ConfigurationDbContext context,
        IPasswordHashingService hasher,
        IEmailOutbox outbox,
        IAuditWriter audit,
        ITenantContext tenantContext,
        IClock clock,
        IOptions<EmailOptions> email,
        IOptions<PlatformUrlOptions> urls)
    {
        _context = context;
        _hasher = hasher;
        _outbox = outbox;
        _audit = audit;
        _tenantContext = tenantContext;
        _clock = clock;
        _email = email.Value;
        _urls = urls.Value;
    }

    public async Task<ProvisionedUser> ProvisionAsync(Guid organizationId, string email, string fullName, string preferredLanguage, IReadOnlyCollection<string> roleCodes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleCodes);
        if (!EmailNormalizer.IsPlausible(email))
        {
            throw new OrganizationValidationException(OrganizationOperationOutcome.ValidationFailed, ["administrator_email_invalid"]);
        }

        var normalized = EmailNormalizer.Normalize(email);
        if (await _context.LoginIdentities.AnyAsync(l => l.NormalizedEmail == normalized, cancellationToken))
        {
            throw new OrganizationValidationException(OrganizationOperationOutcome.EmailAlreadyRegistered, ["email_already_registered"]);
        }

        var roles = await _context.Roles.IgnoreQueryFilters()
            .Where(r => r.OrganizationId == organizationId && roleCodes.Contains(r.RoleCode) && r.IsActive)
            .ToListAsync(cancellationToken);
        if (roles.Count != roleCodes.Distinct(StringComparer.Ordinal).Count())
        {
            throw new OrganizationValidationException(OrganizationOperationOutcome.ValidationFailed, ["role_unknown"]);
        }

        var language = preferredLanguage == "ar" ? "ar" : "en";
        var temporary = TemporaryPasswordGenerator.Generate();
        var now = _clock.UtcNow;
        var user = new OrganizationUser
        {
            UserId = Ids.New(),
            OrganizationId = organizationId,
            Email = email.Trim(),
            NormalizedEmail = normalized,
            FullName = fullName.Trim(),
            PreferredLanguage = language,
            Status = OrganizationUserStatus.Active,
            MustChangePassword = true,
            PasswordHash = _hasher.Hash(temporary),
            SecurityStamp = TemporaryPasswordGenerator.NewSecurityStamp(),
            CreatedByUserId = _tenantContext.Principal.UserId,
            CreatedByAdministratorId = _tenantContext.Principal.AdministratorId,
            CreatedAt = now,
        };

        _context.OrganizationUsers.Add(user);
        _context.LoginIdentities.Add(new LoginIdentity
        {
            NormalizedEmail = normalized,
            PrincipalType = PrincipalType.OrganizationUser,
            PrincipalId = user.UserId,
            OrganizationId = organizationId,
            CreatedAt = now,
        });
        foreach (var role in roles)
        {
            _context.UserRoles.Add(new UserRole
            {
                OrganizationId = organizationId,
                UserId = user.UserId,
                RoleId = role.RoleId,
                AssignedAt = now,
                AssignedByUserId = _tenantContext.Principal.UserId,
                AssignedByAdministratorId = _tenantContext.Principal.AdministratorId,
            });
        }

        _audit.Append(AuditEvents.OrganizationUserCreated, "OrganizationUser", user.UserId.ToString(), new { email = normalized, roles = roles.Select(r => r.RoleCode) }, organizationId);

        using (_context.BeginSystemScope())
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        var messageId = await QueueAsync(user, temporary, isNewAccount: true, cancellationToken);
        return new ProvisionedUser(user.UserId, user.Email, AdministratorAccountService.Mask(user.Email), messageId);
    }

    public async Task<ProvisionedUser> ReissueTemporaryPasswordAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _context.OrganizationUsers.IgnoreQueryFilters()
            .SingleOrDefaultAsync(u => u.OrganizationId == organizationId && u.UserId == userId, cancellationToken)
            ?? throw new OrganizationValidationException(OrganizationOperationOutcome.NotFound, ["user_not_found"]);

        var temporary = TemporaryPasswordGenerator.Generate();
        user.PasswordHash = _hasher.Hash(temporary);
        user.SecurityStamp = TemporaryPasswordGenerator.NewSecurityStamp();
        user.MustChangePassword = true;
        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        user.PasswordChangedAt = _clock.UtcNow;
        _audit.Append(AuditEvents.OrganizationUserPasswordReissued, "OrganizationUser", userId.ToString(), null, organizationId);

        using (_context.BeginSystemScope())
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        var messageId = await QueueAsync(user, temporary, isNewAccount: false, cancellationToken);
        return new ProvisionedUser(user.UserId, user.Email, AdministratorAccountService.Mask(user.Email), messageId);
    }

    private Task<Guid> QueueAsync(OrganizationUser user, string temporaryPassword, bool isNewAccount, CancellationToken cancellationToken)
    {
        var message = EmailTemplates.TemporaryPassword(user.Email, user.FullName, temporaryPassword, user.PreferredLanguage, isNewAccount, _urls.SignInUrl, _email.ReplyTo);
        return _outbox.EnqueueAsync(message, isNewAccount ? EmailTemplateCodes.AccountCreated : EmailTemplateCodes.PasswordReset, user.OrganizationId, cancellationToken);
    }
}
