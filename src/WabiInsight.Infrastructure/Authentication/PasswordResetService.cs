using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Authentication;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Security;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Authentication;

/// <summary>
/// Self-service password reset (P13.32). The rules that keep it safe:
/// the answer to a request never says whether the address has an account; the link's token is random, kept only as a
/// hash, good once and for a short time; the current password keeps working until the link is used, so asking for a
/// reset in someone's name locks nobody out; a newer request, or any other change of the password, ends earlier
/// links; and using a link ends every open session of the account.
/// </summary>
public sealed class PasswordResetService : IPasswordResetService
{
    /// <summary>Requests are kept this long after they expire (for the audit trail of who asked, from where), then deleted.</summary>
    private static readonly TimeSpan KeptAfterExpiry = TimeSpan.FromDays(7);

    private readonly ConfigurationDbContext _context;
    private readonly IPasswordHashingService _hasher;
    private readonly IAuditWriter _audit;
    private readonly IUsageEventWriter _usage;
    private readonly IEmailOutbox _outbox;
    private readonly IClock _clock;
    private readonly SecurityOptions _security;
    private readonly PlatformUrlOptions _urls;
    private readonly EmailOptions _email;

    public PasswordResetService(
        ConfigurationDbContext context,
        IPasswordHashingService hasher,
        IAuditWriter audit,
        IUsageEventWriter usage,
        IEmailOutbox outbox,
        IClock clock,
        IOptions<SecurityOptions> security,
        IOptions<PlatformUrlOptions> urls,
        IOptions<EmailOptions> email)
    {
        _context = context;
        _hasher = hasher;
        _audit = audit;
        _usage = usage;
        _outbox = outbox;
        _clock = clock;
        _security = security.Value;
        _urls = urls.Value;
        _email = email.Value;
    }

    /// <summary>The account behind a request, read without a signed-in principal (by primary key, like sign-in).</summary>
    private sealed record Account(PrincipalType Type, Guid Id, Guid? OrganizationId, string Email, string FullName, string Language, DateTimeOffset? PasswordChangedAt, string? CannotSignIn)
    {
        public string EntityType => Type == PrincipalType.PlatformAdministrator ? "Administrator" : "OrganizationUser";
    }

    public async Task RequestAsync(string email, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        // Housekeeping on every request, whoever asks: long-expired requests go.
        var old = now - KeptAfterExpiry;
        await _context.PasswordResetRequests.Where(r => r.ExpiresAt < old).ExecuteDeleteAsync(cancellationToken);

        if (!EmailNormalizer.IsPlausible(email))
        {
            return;
        }

        var normalized = EmailNormalizer.Normalize(email);
        var identity = await _context.LoginIdentities.AsNoTracking().SingleOrDefaultAsync(l => l.NormalizedEmail == normalized, cancellationToken);
        var account = identity is null ? null : await FindAccountAsync(identity.PrincipalType, identity.PrincipalId, cancellationToken);
        if (account is null)
        {
            return;
        }

        if (account.CannotSignIn is { } reason)
        {
            // Someone who may not sign in gets no link; the attempt is on record for the organization's administrators.
            await IgnoreAsync(account, reason, ipAddress, cancellationToken);
            return;
        }

        var hourAgo = now.AddHours(-1);
        var recent = await _context.PasswordResetRequests.CountAsync(r => r.PrincipalType == account.Type && r.PrincipalId == account.Id && r.CreatedAt > hourAgo, cancellationToken);
        if (recent >= _security.PasswordResetRequestsPerHour)
        {
            await IgnoreAsync(account, "too_many_requests", ipAddress, cancellationToken);
            return;
        }

        // Only the newest link of an account works.
        var open = await _context.PasswordResetRequests
            .Where(r => r.PrincipalType == account.Type && r.PrincipalId == account.Id && r.UsedAt == null && r.CancelledAt == null && r.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        foreach (var earlier in open)
        {
            earlier.CancelledAt = now;
        }

        var token = NewToken();
        var request = new PasswordResetRequest
        {
            PasswordResetRequestId = Ids.New(),
            PrincipalType = account.Type,
            PrincipalId = account.Id,
            OrganizationId = account.OrganizationId,
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_security.PasswordResetLinkMinutes),
            RequestedFromIp = Truncate(ipAddress, 64),
        };
        _context.PasswordResetRequests.Add(request);
        _audit.Append(AuditEvents.PasswordResetRequested, account.EntityType, account.Id.ToString(), new { requestId = request.PasswordResetRequestId, expiresAt = request.ExpiresAt, ip = request.RequestedFromIp }, account.OrganizationId);

        // The token goes after "#": browsers never send that part to a server, so it reaches no access log or proxy.
        var link = $"{_urls.BaseUrl.TrimEnd('/')}/reset-password#token={token}";
        var message = EmailTemplates.PasswordResetLink(account.Email, account.FullName, link, _security.PasswordResetLinkMinutes, account.Language, _email.ReplyTo);
        using (_context.BeginSystemScope())
        {
            await _context.SaveChangesAsync(cancellationToken);
            await _outbox.EnqueueAsync(message, EmailTemplateCodes.PasswordResetLink, account.OrganizationId, cancellationToken);
        }
    }

    public async Task<bool> IsValidAsync(string token, CancellationToken cancellationToken = default)
        => await FindUsableAsync(token, cancellationToken) is not null;

    public async Task<PasswordResetResult> CompleteAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        var found = await FindUsableAsync(token, cancellationToken);
        if (found is null)
        {
            return PasswordResetResult.Invalid;
        }

        var (request, account) = found.Value;

        // The rules first: a password that breaks them does not spend the link.
        var errors = PasswordPolicy.Validate(newPassword, account.Email, _security, allowSamplePassword: false);
        if (errors.Count > 0)
        {
            return new PasswordResetResult(PasswordResetOutcome.PolicyViolation, errors);
        }

        var now = _clock.UtcNow;
        var hash = _hasher.Hash(newPassword);
        var stamp = TemporaryPasswordGenerator.NewSecurityStamp();
        var strategy = _context.Database.CreateExecutionStrategy();
        var spent = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            // Claimed in the database: of two people using the same link at the same moment, one wins.
            var claimed = await _context.PasswordResetRequests
                .Where(r => r.PasswordResetRequestId == request.PasswordResetRequestId && r.UsedAt == null && r.CancelledAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.UsedAt, now), cancellationToken);
            if (claimed != 1)
            {
                return false;
            }

            if (account.Type == PrincipalType.PlatformAdministrator)
            {
                var admin = await _context.Administrators.SingleAsync(a => a.AdministratorId == account.Id, cancellationToken);
                admin.PasswordHash = hash;
                admin.SecurityStamp = stamp;
                admin.MustChangePassword = false;
                admin.FailedLoginCount = 0;
                admin.LockoutEnd = null;
                admin.PasswordChangedAt = now;
            }
            else
            {
                var user = await _context.OrganizationUsers.IgnoreQueryFilters().SingleAsync(u => u.UserId == account.Id, cancellationToken);
                user.PasswordHash = hash;
                user.SecurityStamp = stamp;
                user.MustChangePassword = false;
                user.FailedLoginCount = 0;
                user.LockoutEnd = null;
                user.PasswordChangedAt = now;
            }

            _audit.Append(AuditEvents.PasswordResetCompleted, account.EntityType, account.Id.ToString(), new { requestId = request.PasswordResetRequestId }, account.OrganizationId);
            _usage.Append(UsageEventTypes.PasswordChanged, account.EntityType, account.Id.ToString(), metadata: new { by = "reset_link" }, organizationId: account.OrganizationId, actorType: account.Type, actorId: account.Id);
            using (_context.BeginSystemScope())
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        });

        if (!spent)
        {
            return PasswordResetResult.Invalid;
        }

        // The owner is told, in case it was not them.
        var notice = EmailTemplates.PasswordChangedNotice(account.Email, account.FullName, _urls.SignInUrl, account.Language, account.Type == PrincipalType.PlatformAdministrator, _email.ReplyTo);
        using (_context.BeginSystemScope())
        {
            await _outbox.EnqueueAsync(notice, EmailTemplateCodes.PasswordChangedNotice, account.OrganizationId, cancellationToken);
        }

        return PasswordResetResult.Succeeded;
    }

    /// <summary>The request of this token and its account, when the link can still be used; null otherwise, without saying why.</summary>
    private async Task<(PasswordResetRequest Request, Account Account)?> FindUsableAsync(string? token, CancellationToken cancellationToken)
    {
        // A token is 43 characters; anything far from that is not looked up.
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 20 or > 128)
        {
            return null;
        }

        var hash = Hash(token.Trim());
        var now = _clock.UtcNow;
        var request = await _context.PasswordResetRequests.AsNoTracking()
            .SingleOrDefaultAsync(r => r.TokenHash == hash && r.UsedAt == null && r.CancelledAt == null && r.ExpiresAt > now, cancellationToken);
        if (request is null)
        {
            return null;
        }

        var account = await FindAccountAsync(request.PrincipalType, request.PrincipalId, cancellationToken);
        if (account is null || account.CannotSignIn is not null)
        {
            return null;
        }

        // The password changed after the link was sent (the person remembered it, or an administrator reset it): the link is over.
        if (account.PasswordChangedAt is { } changed && changed > request.CreatedAt)
        {
            return null;
        }

        return (request, account);
    }

    private async Task<Account?> FindAccountAsync(PrincipalType type, Guid id, CancellationToken cancellationToken)
    {
        if (type == PrincipalType.PlatformAdministrator)
        {
            var admin = await _context.Administrators.AsNoTracking().SingleOrDefaultAsync(a => a.AdministratorId == id, cancellationToken);
            return admin is null
                ? null
                : new Account(type, id, null, admin.Email, admin.FullName, admin.PreferredLanguage, admin.PasswordChangedAt, admin.IsActive ? null : "inactive");
        }

        if (type != PrincipalType.OrganizationUser)
        {
            return null;
        }

        // Before sign-in there is no tenant context: an explicit read by primary key, as the sign-in itself does.
        var user = await _context.OrganizationUsers.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(u => u.UserId == id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var organizationActive = await _context.Organizations.AsNoTracking()
            .AnyAsync(o => o.OrganizationId == user.OrganizationId && o.Status == OrganizationStatus.Active, cancellationToken);
        var cannot = user.Status != OrganizationUserStatus.Active ? "inactive" : organizationActive ? null : "organization_suspended";
        return new Account(type, id, user.OrganizationId, user.Email, user.FullName, user.PreferredLanguage, user.PasswordChangedAt, cannot);
    }

    private async Task IgnoreAsync(Account account, string reason, string? ipAddress, CancellationToken cancellationToken)
    {
        _audit.Append(AuditEvents.PasswordResetIgnored, account.EntityType, account.Id.ToString(), new { reason, ip = Truncate(ipAddress, 64) }, account.OrganizationId);
        using (_context.BeginSystemScope())
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>256 random bits, written with letters, digits, "-" and "_" only, so the link needs no escaping.</summary>
    internal static string NewToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string? Truncate(string? value, int length)
        => value is null || value.Length <= length ? value : value[..length];
}
