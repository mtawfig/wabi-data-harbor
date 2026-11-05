using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Authentication;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Security;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Authentication;

/// <summary>
/// Creates Platform Administrators and resets their passwords. The temporary password is generated,
/// hashed, and emailed; it is never returned to the caller, logged, or stored in plain text.
/// </summary>
public sealed class AdministratorAccountService : IAdministratorAccountService
{
    private readonly ConfigurationDbContext _context;
    private readonly IPasswordHashingService _hasher;
    private readonly IEmailOutbox _outbox;
    private readonly IAuditWriter _audit;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly EmailOptions _email;
    private readonly PlatformUrlOptions _urls;
    private readonly IUsageEventWriter _usage;

    public AdministratorAccountService(
        ConfigurationDbContext context,
        IPasswordHashingService hasher,
        IEmailOutbox outbox,
        IAuditWriter audit,
        ITenantContext tenantContext,
        IClock clock,
        IOptions<EmailOptions> email,
        IOptions<PlatformUrlOptions> urls,
        IUsageEventWriter usage)
    {
        _context = context;
        _hasher = hasher;
        _outbox = outbox;
        _audit = audit;
        _tenantContext = tenantContext;
        _clock = clock;
        _email = email.Value;
        _urls = urls.Value;
        _usage = usage;
    }

    public async Task<AdministratorAccountResult> CreateAsync(string email, string fullName, string preferredLanguage, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        if (!EmailNormalizer.IsPlausible(email))
        {
            return new AdministratorAccountResult(AdministratorAccountOutcome.InvalidEmail, null, null, null);
        }

        var normalized = EmailNormalizer.Normalize(email);
        if (await _context.LoginIdentities.AnyAsync(l => l.NormalizedEmail == normalized, cancellationToken))
        {
            return new AdministratorAccountResult(AdministratorAccountOutcome.EmailAlreadyRegistered, null, null, null);
        }

        var language = preferredLanguage == "ar" ? "ar" : "en";
        var temporary = TemporaryPasswordGenerator.Generate();
        var now = _clock.UtcNow;
        var administrator = new Administrator
        {
            AdministratorId = Ids.New(),
            Email = email.Trim(),
            NormalizedEmail = normalized,
            FullName = fullName.Trim(),
            PasswordHash = _hasher.Hash(temporary),
            IsActive = true,
            MustChangePassword = true,
            SecurityStamp = TemporaryPasswordGenerator.NewSecurityStamp(),
            PreferredLanguage = language,
            CreatedByAdministratorId = _tenantContext.Principal.AdministratorId,
            CreatedAt = now,
        };

        _context.Administrators.Add(administrator);
        _context.LoginIdentities.Add(new LoginIdentity
        {
            NormalizedEmail = normalized,
            PrincipalType = PrincipalType.PlatformAdministrator,
            PrincipalId = administrator.AdministratorId,
            CreatedAt = now,
        });
        _audit.Append(AuditEvents.AdministratorCreated, "Administrator", administrator.AdministratorId.ToString(), new { email = normalized, fullName = administrator.FullName });
        _usage.Append(UsageEventTypes.AdministratorCreated, "Administrator", administrator.AdministratorId.ToString());
        await _context.SaveChangesAsync(cancellationToken);

        var messageId = await QueueTemporaryPasswordAsync(administrator, temporary, isNewAccount: true, cancellationToken);
        return new AdministratorAccountResult(AdministratorAccountOutcome.Success, administrator.AdministratorId, Mask(administrator.Email), messageId);
    }

    public async Task<AdministratorAccountResult> ResetPasswordAsync(Guid targetAdministratorId, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        if (_tenantContext.Principal.AdministratorId == targetAdministratorId)
        {
            return new AdministratorAccountResult(AdministratorAccountOutcome.SelfResetNotAllowed, null, null, null);
        }

        var target = await _context.Administrators.SingleOrDefaultAsync(a => a.AdministratorId == targetAdministratorId, cancellationToken);
        if (target is null)
        {
            return new AdministratorAccountResult(AdministratorAccountOutcome.NotFound, null, null, null);
        }

        var temporary = TemporaryPasswordGenerator.Generate();
        target.PasswordHash = _hasher.Hash(temporary);
        target.SecurityStamp = TemporaryPasswordGenerator.NewSecurityStamp();
        target.MustChangePassword = true;
        target.FailedLoginCount = 0;
        target.LockoutEnd = null;
        target.PasswordChangedAt = _clock.UtcNow;
        _audit.Append(AuditEvents.AdministratorPasswordReset, "Administrator", targetAdministratorId.ToString());
        await _context.SaveChangesAsync(cancellationToken);

        var messageId = await QueueTemporaryPasswordAsync(target, temporary, isNewAccount: false, cancellationToken);
        return new AdministratorAccountResult(AdministratorAccountOutcome.Success, target.AdministratorId, Mask(target.Email), messageId);
    }

    private async Task<Guid> QueueTemporaryPasswordAsync(Administrator administrator, string temporaryPassword, bool isNewAccount, CancellationToken cancellationToken)
    {
        var message = EmailTemplates.TemporaryPassword(
            administrator.Email,
            administrator.FullName,
            temporaryPassword,
            administrator.PreferredLanguage,
            isNewAccount,
            _urls.SignInUrl,
            _email.ReplyTo);

        return await _outbox.EnqueueAsync(message, isNewAccount ? EmailTemplateCodes.AccountCreated : EmailTemplateCodes.PasswordReset, null, cancellationToken);
    }

    private void RequirePlatformAdministrator()
    {
        if (!_tenantContext.IsPlatformAdministrator)
        {
            throw new TenantContextException("Only platform administrators can manage administrator accounts.");
        }
    }

    /// <summary>a***e@domain — enough for the caller to confirm the recipient without exposing the address.</summary>
    internal static string Mask(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at <= 1)
        {
            return email;
        }

        return $"{email[0]}***{email[at - 1]}{email[at..]}";
    }
}

/// <summary>Public URLs used in emails, bound from the "Platform" section.</summary>
public sealed class PlatformUrlOptions
{
    public const string SectionName = "Platform";

    public string BaseUrl { get; set; } = "http://localhost:4200";

    public string SignInUrl => $"{BaseUrl.TrimEnd('/')}/login";
}
