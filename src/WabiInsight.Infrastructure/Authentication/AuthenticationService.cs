using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Authentication;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Security;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Authentication;

/// <summary>
/// Local email/password authentication over the two account stores. Unknown email and wrong password
/// produce the same outcome; lockout, inactive accounts and suspended organizations are denied.
/// </summary>
public sealed class AuthenticationService : IAuthenticationService
{
    private readonly ConfigurationDbContext _context;
    private readonly IPasswordHashingService _hasher;
    private readonly IAuditWriter _audit;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly SecurityOptions _options;
    private readonly IUsageEventWriter _usage;

    public AuthenticationService(
        ConfigurationDbContext context,
        IPasswordHashingService hasher,
        IAuditWriter audit,
        ITenantContext tenantContext,
        IClock clock,
        IOptions<SecurityOptions> options,
        IUsageEventWriter usage)
    {
        _context = context;
        _hasher = hasher;
        _audit = audit;
        _tenantContext = tenantContext;
        _clock = clock;
        _options = options.Value;
        _usage = usage;
    }

    public async Task<LoginResult> LoginAsync(string email, string password, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (!EmailNormalizer.IsPlausible(email) || string.IsNullOrEmpty(password))
        {
            return LoginResult.Failed(LoginOutcome.InvalidCredentials);
        }

        var normalized = EmailNormalizer.Normalize(email);
        var identity = await _context.LoginIdentities.AsNoTracking()
            .SingleOrDefaultAsync(l => l.NormalizedEmail == normalized, cancellationToken);

        if (identity is null)
        {
            // Burn comparable time so unknown emails are not distinguishable by latency.
            _hasher.Verify(DummyHash, password);
            await _audit.AppendAndSaveAsync(AuditEvents.LoginFailed, details: new { email = normalized, reason = "unknown" }, cancellationToken: cancellationToken);
            return LoginResult.Failed(LoginOutcome.InvalidCredentials);
        }

        return identity.PrincipalType switch
        {
            PrincipalType.PlatformAdministrator => await LoginAdministratorAsync(identity.PrincipalId, normalized, password, cancellationToken),
            PrincipalType.OrganizationUser => await LoginOrganizationUserAsync(identity.PrincipalId, normalized, password, cancellationToken),
            _ => LoginResult.Failed(LoginOutcome.InvalidCredentials),
        };
    }

    public async Task<ChangePasswordResult> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var principal = _tenantContext.Principal;

        return principal.PrincipalType switch
        {
            PrincipalType.PlatformAdministrator => await ChangeAdministratorPasswordAsync(principal.AdministratorId!.Value, currentPassword, newPassword, cancellationToken),
            PrincipalType.OrganizationUser => await ChangeOrganizationUserPasswordAsync(principal.UserId!.Value, currentPassword, newPassword, cancellationToken),
            _ => throw new TenantContextException("No authenticated principal."),
        };
    }

    public Task RecordLogoutAsync(CancellationToken cancellationToken = default)
    {
        _audit.Append(AuditEvents.Logout);
        return _usage.AppendAndSaveAsync(UsageEventTypes.Logout, cancellationToken: cancellationToken);
    }

    private async Task<LoginResult> LoginAdministratorAsync(Guid id, string normalizedEmail, string password, CancellationToken cancellationToken)
    {
        var admin = await _context.Administrators.SingleOrDefaultAsync(a => a.AdministratorId == id, cancellationToken);
        if (admin is null)
        {
            return LoginResult.Failed(LoginOutcome.InvalidCredentials);
        }

        if (admin.LockoutEnd is { } end && end > _clock.UtcNow)
        {
            await _audit.AppendAndSaveAsync(AuditEvents.LoginDenied, "Administrator", id.ToString(), new { email = normalizedEmail, reason = "locked_out" }, cancellationToken: cancellationToken);
            return LoginResult.Failed(LoginOutcome.LockedOut, end);
        }

        var verification = _hasher.Verify(admin.PasswordHash, password);
        if (verification == PasswordVerification.Failed)
        {
            var count = admin.FailedLoginCount + 1;
            var locked = count >= _options.MaxFailedLoginAttempts;
            admin.FailedLoginCount = locked ? 0 : count;
            admin.LockoutEnd = locked ? _clock.UtcNow.AddMinutes(_options.LockoutMinutes) : null;
            _audit.Append(locked ? AuditEvents.AccountLockedOut : AuditEvents.LoginFailed, "Administrator", id.ToString(), new { email = normalizedEmail, failedAttempts = count });
            _usage.Append(UsageEventTypes.LoginFailed, "Administrator", id.ToString(), succeeded: false, metadata: new { locked }, actorType: PrincipalType.PlatformAdministrator, actorId: id);
            await _context.SaveChangesAsync(cancellationToken);
            return locked ? LoginResult.Failed(LoginOutcome.LockedOut, admin.LockoutEnd) : LoginResult.Failed(LoginOutcome.InvalidCredentials);
        }

        if (!admin.IsActive)
        {
            await _audit.AppendAndSaveAsync(AuditEvents.LoginDenied, "Administrator", id.ToString(), new { email = normalizedEmail, reason = "inactive" }, cancellationToken: cancellationToken);
            return LoginResult.Failed(LoginOutcome.AccountDisabled);
        }

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            admin.PasswordHash = _hasher.Hash(password);
        }

        admin.FailedLoginCount = 0;
        admin.LockoutEnd = null;
        admin.LastLoginAt = _clock.UtcNow;
        _audit.Append(AuditEvents.LoginSucceeded, "Administrator", id.ToString(), new { email = normalizedEmail });
        _usage.Append(UsageEventTypes.Login, "Administrator", id.ToString(), actorType: PrincipalType.PlatformAdministrator, actorId: id);
        await _context.SaveChangesAsync(cancellationToken);

        return new LoginResult(LoginOutcome.Success, AccountResolver.ToAccount(admin));
    }

    private async Task<LoginResult> LoginOrganizationUserAsync(Guid id, string normalizedEmail, string password, CancellationToken cancellationToken)
    {
        // Pre-authentication read: no tenant context exists yet, so this is an explicit platform-scope lookup by primary key.
        var user = await _context.OrganizationUsers.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.UserId == id, cancellationToken);
        if (user is null)
        {
            return LoginResult.Failed(LoginOutcome.InvalidCredentials);
        }

        if (user.LockoutEnd is { } end && end > _clock.UtcNow)
        {
            await _audit.AppendAndSaveAsync(AuditEvents.LoginDenied, "OrganizationUser", id.ToString(), new { email = normalizedEmail, reason = "locked_out" }, user.OrganizationId, cancellationToken);
            return LoginResult.Failed(LoginOutcome.LockedOut, end);
        }

        var verification = user.PasswordHash is null ? PasswordVerification.Failed : _hasher.Verify(user.PasswordHash, password);
        if (verification == PasswordVerification.Failed)
        {
            var count = user.FailedLoginCount + 1;
            var locked = count >= _options.MaxFailedLoginAttempts;
            user.FailedLoginCount = locked ? 0 : count;
            user.LockoutEnd = locked ? _clock.UtcNow.AddMinutes(_options.LockoutMinutes) : null;
            _audit.Append(locked ? AuditEvents.AccountLockedOut : AuditEvents.LoginFailed, "OrganizationUser", id.ToString(), new { email = normalizedEmail, failedAttempts = count }, user.OrganizationId);
            _usage.Append(UsageEventTypes.LoginFailed, "OrganizationUser", id.ToString(), succeeded: false, metadata: new { locked }, organizationId: user.OrganizationId, actorType: PrincipalType.OrganizationUser, actorId: id, departmentId: user.DepartmentId);
            await SaveWithSystemScopeAsync(cancellationToken);
            return locked ? LoginResult.Failed(LoginOutcome.LockedOut, user.LockoutEnd) : LoginResult.Failed(LoginOutcome.InvalidCredentials);
        }

        if (user.Status != OrganizationUserStatus.Active)
        {
            await _audit.AppendAndSaveAsync(AuditEvents.LoginDenied, "OrganizationUser", id.ToString(), new { email = normalizedEmail, reason = "inactive" }, user.OrganizationId, cancellationToken);
            return LoginResult.Failed(LoginOutcome.AccountDisabled);
        }

        var organizationStatus = await _context.Organizations.AsNoTracking()
            .Where(o => o.OrganizationId == user.OrganizationId)
            .Select(o => o.Status)
            .SingleAsync(cancellationToken);
        if (organizationStatus != OrganizationStatus.Active)
        {
            await _audit.AppendAndSaveAsync(AuditEvents.LoginDenied, "OrganizationUser", id.ToString(), new { email = normalizedEmail, reason = "organization_suspended" }, user.OrganizationId, cancellationToken);
            return LoginResult.Failed(LoginOutcome.OrganizationSuspended);
        }

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            user.PasswordHash = _hasher.Hash(password);
        }

        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = _clock.UtcNow;
        _audit.Append(AuditEvents.LoginSucceeded, "OrganizationUser", id.ToString(), new { email = normalizedEmail }, user.OrganizationId);
        _usage.Append(UsageEventTypes.Login, "OrganizationUser", id.ToString(), organizationId: user.OrganizationId, actorType: PrincipalType.OrganizationUser, actorId: id, departmentId: user.DepartmentId);
        await SaveWithSystemScopeAsync(cancellationToken);

        var roles = await AccountResolver.RoleCodesAsync(_context, user, cancellationToken);
        return new LoginResult(LoginOutcome.Success, AccountResolver.ToAccount(user, roles));
    }

    private async Task<ChangePasswordResult> ChangeAdministratorPasswordAsync(Guid id, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var admin = await _context.Administrators.SingleAsync(a => a.AdministratorId == id, cancellationToken);
        if (_hasher.Verify(admin.PasswordHash, currentPassword) == PasswordVerification.Failed)
        {
            return new ChangePasswordResult(ChangePasswordOutcome.CurrentPasswordInvalid, ["current_password_invalid"], null);
        }

        var errors = PasswordPolicy.Validate(newPassword, admin.Email, _options, allowSamplePassword: false);
        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            errors = [.. errors, "password_unchanged"];
        }

        if (errors.Count > 0)
        {
            return new ChangePasswordResult(ChangePasswordOutcome.PolicyViolation, errors, null);
        }

        admin.PasswordHash = _hasher.Hash(newPassword);
        admin.SecurityStamp = TemporaryPasswordGenerator.NewSecurityStamp();
        admin.MustChangePassword = false;
        admin.PasswordChangedAt = _clock.UtcNow;
        _audit.Append(AuditEvents.PasswordChanged, "Administrator", id.ToString());
        _usage.Append(UsageEventTypes.PasswordChanged, "Administrator", id.ToString());
        await _context.SaveChangesAsync(cancellationToken);

        return new ChangePasswordResult(ChangePasswordOutcome.Success, [], AccountResolver.ToAccount(admin));
    }

    private async Task<ChangePasswordResult> ChangeOrganizationUserPasswordAsync(Guid id, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var user = await _context.OrganizationUsers.SingleAsync(u => u.UserId == id, cancellationToken);
        if (user.PasswordHash is null || _hasher.Verify(user.PasswordHash, currentPassword) == PasswordVerification.Failed)
        {
            return new ChangePasswordResult(ChangePasswordOutcome.CurrentPasswordInvalid, ["current_password_invalid"], null);
        }

        var errors = PasswordPolicy.Validate(newPassword, user.Email, _options, allowSamplePassword: false);
        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            errors = [.. errors, "password_unchanged"];
        }

        if (errors.Count > 0)
        {
            return new ChangePasswordResult(ChangePasswordOutcome.PolicyViolation, errors, null);
        }

        user.PasswordHash = _hasher.Hash(newPassword);
        user.SecurityStamp = TemporaryPasswordGenerator.NewSecurityStamp();
        user.MustChangePassword = false;
        user.PasswordChangedAt = _clock.UtcNow;
        _audit.Append(AuditEvents.PasswordChanged, "OrganizationUser", id.ToString());
        _usage.Append(UsageEventTypes.PasswordChanged, "OrganizationUser", id.ToString());
        await _context.SaveChangesAsync(cancellationToken);

        var roles = await AccountResolver.RoleCodesAsync(_context, user, cancellationToken);
        return new ChangePasswordResult(ChangePasswordOutcome.Success, [], AccountResolver.ToAccount(user, roles));
    }

    /// <summary>Login runs before a tenant context exists; the pre-authentication counters are written in system scope.</summary>
    private async Task SaveWithSystemScopeAsync(CancellationToken cancellationToken)
    {
        using (_context.BeginSystemScope())
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    // A real hash of an unguessable value, computed once, used only to equalize timing for unknown emails.
    private static readonly Lazy<string> DummyHashValue = new(() =>
        new Security.IdentityPasswordHashingService().Hash(TemporaryPasswordGenerator.Generate(32)));

    private static string DummyHash => DummyHashValue.Value;
}
