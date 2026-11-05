using System.Reflection;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Audit;
using WabiInsight.Core.Entities.Email;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Infrastructure.Persistence;

/// <summary>
/// The single Wabi Insight configuration database.
/// Every <see cref="ITenantOwned"/> entity has a global query filter bound to the current
/// organization and a write-time ownership guard. Append-only entities reject updates and deletes.
/// </summary>
public class ConfigurationDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private bool _systemScope;

    public ConfigurationDbContext(
        DbContextOptions<ConfigurationDbContext> options,
        ITenantContext tenantContext,
        IClock clock)
        : base(options)
    {
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Administrator> Administrators => Set<Administrator>();

    public DbSet<OrganizationUser> OrganizationUsers => Set<OrganizationUser>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<Application> Applications => Set<Application>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Section> Sections => Set<Section>();

    public DbSet<DefaultRoleTemplate> DefaultRoleTemplates => Set<DefaultRoleTemplate>();

    public DbSet<LoginIdentity> LoginIdentities => Set<LoginIdentity>();

    public DbSet<SystemInitialization> SystemInitialization => Set<SystemInitialization>();

    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();

    public DbSet<EmailMessageRecord> EmailMessages => Set<EmailMessageRecord>();

    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();

    public DbSet<TicketSequence> TicketSequences => Set<TicketSequence>();

    public DbSet<SupportTicketEvent> SupportTicketEvents => Set<SupportTicketEvent>();

    public DbSet<SupportTicketComment> SupportTicketComments => Set<SupportTicketComment>();

    public DbSet<SupportTicketAttachment> SupportTicketAttachments => Set<SupportTicketAttachment>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<SupportAccessGrant> SupportAccessGrants => Set<SupportAccessGrant>();

    public DbSet<SupportSession> SupportSessions => Set<SupportSession>();

    public DbSet<DatabaseObject> DatabaseObjects => Set<DatabaseObject>();

    public DbSet<DatabaseColumn> DatabaseColumns => Set<DatabaseColumn>();

    public DbSet<DatabaseRelationship> DatabaseRelationships => Set<DatabaseRelationship>();

    /// <summary>Evaluated per query as a parameter; null (anonymous or platform administrator) matches no tenant rows.</summary>
    private Guid? CurrentOrganizationId => _tenantContext.OrganizationId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                typeof(ConfigurationDbContext)
                    .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, [modelBuilder]);
            }
        }
    }

    /// <summary>
    /// Allows pre-authentication and system operations (login counters, bootstrap, onboarding) to write
    /// tenant-owned rows without a tenant principal. Scoped and explicit; never used by request handlers
    /// acting for an organization user.
    /// </summary>
    public IDisposable BeginSystemScope()
    {
        _systemScope = true;
        return new SystemScope(this);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceInvariants();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        EnforceInvariants();
        return base.SaveChanges();
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => entity.OrganizationId == CurrentOrganizationId);
    }

    private void EnforceInvariants()
    {
        var now = _clock.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog or UsageEvent or SupportTicketEvent && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} rows are append-only.");
            }

            if (entry.Entity is ITenantOwned owned && entry.State is EntityState.Added or EntityState.Modified)
            {
                GuardTenantOwnership(owned);
            }

            if (entry.Entity is AuditableEntity auditable)
            {
                if (entry.State == EntityState.Added && auditable.CreatedAt == default)
                {
                    auditable.CreatedAt = now;
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditable.UpdatedAt = now;
                }
            }
        }
    }

    private void GuardTenantOwnership(ITenantOwned owned)
    {
        if (owned.OrganizationId == Guid.Empty)
        {
            throw new TenantContextException("A tenant-owned entity must have an OrganizationId.");
        }

        // Organization users may only write inside their own organization. Platform administrators
        // write tenant rows only through explicitly audited paths (onboarding, approved support grants),
        // which run with a platform principal; anonymous scopes never write tenant data.
        switch (_tenantContext.Principal.PrincipalType)
        {
            case PrincipalType.OrganizationUser when owned.OrganizationId != _tenantContext.OrganizationId:
                throw new TenantContextException("Cross-organization write rejected.");
            case PrincipalType.Anonymous when !_systemScope:
                throw new TenantContextException("Anonymous scopes cannot write tenant-owned data.");
        }
    }

    private sealed class SystemScope : IDisposable
    {
        private readonly ConfigurationDbContext _context;

        public SystemScope(ConfigurationDbContext context)
        {
            _context = context;
        }

        public void Dispose()
        {
            _context._systemScope = false;
        }
    }
}
