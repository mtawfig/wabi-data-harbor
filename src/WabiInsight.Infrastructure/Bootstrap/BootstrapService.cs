using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Bootstrap;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Audit;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Security;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Bootstrap;

/// <summary>
/// Creates the first Platform Administrator exactly once (specification §7.2). The singleton
/// SystemInitialization row and the unique bootstrap-administrator index make concurrent attempts fail.
/// </summary>
public sealed class BootstrapService : IBootstrapService
{
    private readonly ConfigurationDbContext _context;
    private readonly IPasswordHashingService _hasher;
    private readonly IHostEnvironment _environment;
    private readonly IClock _clock;
    private readonly BootstrapOptions _bootstrap;
    private readonly SecurityOptions _security;

    public BootstrapService(
        ConfigurationDbContext context,
        IPasswordHashingService hasher,
        IHostEnvironment environment,
        IClock clock,
        IOptions<BootstrapOptions> bootstrap,
        IOptions<SecurityOptions> security)
    {
        _context = context;
        _hasher = hasher;
        _environment = environment;
        _clock = clock;
        _bootstrap = bootstrap.Value;
        _security = security.Value;
    }

    public async Task<BootstrapStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var state = await _context.SystemInitialization.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == SystemInitialization.SingletonId, cancellationToken);
        string? adminEmail = null;
        if (state?.BootstrapAdministratorId is { } adminId)
        {
            adminEmail = await _context.Administrators.AsNoTracking()
                .Where(a => a.AdministratorId == adminId)
                .Select(a => a.Email)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var pending = (await _context.Database.GetPendingMigrationsAsync(cancellationToken)).Any();

        return new BootstrapStatus(
            state?.BootstrapCompleted ?? false,
            state?.BootstrapCompletedAt,
            adminEmail,
            state?.SchemaVersion,
            pending,
            _bootstrap.Enabled);
    }

    public async Task<BootstrapResult> InitializeAsync(string? providedSecret, CancellationToken cancellationToken = default)
    {
        if (!_bootstrap.Enabled)
        {
            return new BootstrapResult(BootstrapOutcome.Disabled, ["bootstrap_disabled"]);
        }

        if (string.IsNullOrWhiteSpace(_bootstrap.InitializationSecret) || _bootstrap.InitializationSecret.Length < 16)
        {
            return new BootstrapResult(BootstrapOutcome.InvalidConfiguration, ["initialization_secret_missing"]);
        }

        if (providedSecret is null || !FixedTimeEquals(providedSecret, _bootstrap.InitializationSecret))
        {
            return new BootstrapResult(BootstrapOutcome.Unauthorized, ["initialization_secret_invalid"]);
        }

        var errors = new List<string>();
        if (!EmailNormalizer.IsPlausible(_bootstrap.AdministratorEmail))
        {
            errors.Add("administrator_email_invalid");
        }

        if (string.IsNullOrWhiteSpace(_bootstrap.AdministratorFullName))
        {
            errors.Add("administrator_full_name_required");
        }

        errors.AddRange(PasswordPolicy.Validate(
            _bootstrap.AdministratorPassword,
            _bootstrap.AdministratorEmail,
            _security,
            allowSamplePassword: _environment.IsDevelopment()));

        if (errors.Count > 0)
        {
            return new BootstrapResult(BootstrapOutcome.InvalidConfiguration, errors);
        }

        if ((await _context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
        {
            return new BootstrapResult(BootstrapOutcome.SchemaNotCurrent, ["schema_not_current"]);
        }

        var existing = await _context.SystemInitialization.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == SystemInitialization.SingletonId, cancellationToken);
        if (existing?.BootstrapCompleted == true || await _context.Administrators.AnyAsync(cancellationToken))
        {
            return new BootstrapResult(BootstrapOutcome.AlreadyCompleted, ["bootstrap_already_completed"]);
        }

        var now = _clock.UtcNow;
        var administrator = new Administrator
        {
            AdministratorId = Ids.New(),
            Email = _bootstrap.AdministratorEmail.Trim(),
            NormalizedEmail = EmailNormalizer.Normalize(_bootstrap.AdministratorEmail),
            FullName = _bootstrap.AdministratorFullName.Trim(),
            PasswordHash = _hasher.Hash(_bootstrap.AdministratorPassword!),
            IsActive = true,
            MustChangePassword = true,
            IsBootstrapAdministrator = true,
            SecurityStamp = TemporaryPasswordGenerator.NewSecurityStamp(),
            CreatedAt = now,
        };

        var schemaVersion = (await _context.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault();

        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                _context.Administrators.Add(administrator);
                _context.LoginIdentities.Add(new LoginIdentity
                {
                    NormalizedEmail = administrator.NormalizedEmail,
                    PrincipalType = PrincipalType.PlatformAdministrator,
                    PrincipalId = administrator.AdministratorId,
                    CreatedAt = now,
                });
                _context.SystemInitialization.Add(new SystemInitialization
                {
                    Id = SystemInitialization.SingletonId,
                    BootstrapCompleted = true,
                    BootstrapCompletedAt = now,
                    BootstrapAdministratorId = administrator.AdministratorId,
                    SchemaVersion = schemaVersion,
                });
                _context.AuditLogs.Add(new AuditLog
                {
                    AuditLogId = Ids.New(),
                    ActorType = PrincipalType.PlatformAdministrator,
                    ActorId = administrator.AdministratorId,
                    ActorDisplayName = administrator.FullName,
                    EventType = AuditEvents.BootstrapCompleted,
                    EntityType = "Administrator",
                    EntityId = administrator.AdministratorId.ToString(),
                    DetailsJson = $"{{\"email\":\"{administrator.NormalizedEmail}\",\"schemaVersion\":\"{schemaVersion}\"}}",
                    CreatedAt = now,
                });

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch (DbUpdateException)
        {
            // A concurrent bootstrap won the race: the singleton row or the bootstrap index already exists.
            return new BootstrapResult(BootstrapOutcome.AlreadyCompleted, ["bootstrap_already_completed"]);
        }

        return new BootstrapResult(BootstrapOutcome.Completed, [], administrator.AdministratorId);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var left = System.Text.Encoding.UTF8.GetBytes(a);
        var right = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
    }
}
