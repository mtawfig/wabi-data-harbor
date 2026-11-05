using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Authentication;
using WabiInsight.Core.Common;
using WabiInsight.Core.Security;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Authentication;

public sealed class AdministratorPasswordService : IAdministratorPasswordService
{
    private readonly ConfigurationDbContext _context;
    private readonly IPasswordHashingService _hasher;
    private readonly IAuditWriter _audit;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public AdministratorPasswordService(ConfigurationDbContext context, IPasswordHashingService hasher, IAuditWriter audit, ITenantContext tenantContext, IClock clock)
    {
        _context = context;
        _hasher = hasher;
        _audit = audit;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<string?> ResetPasswordAsync(Guid targetAdministratorId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsPlatformAdministrator)
        {
            throw new TenantContextException("Only platform administrators can reset administrator passwords.");
        }

        if (_tenantContext.Principal.AdministratorId == targetAdministratorId)
        {
            // Self-service goes through change-password; a reset of one's own account is refused.
            return null;
        }

        var target = await _context.Administrators.SingleOrDefaultAsync(a => a.AdministratorId == targetAdministratorId, cancellationToken);
        if (target is null)
        {
            return null;
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

        return temporary;
    }
}
