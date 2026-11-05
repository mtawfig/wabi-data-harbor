using System.Text.Json;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Audit;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Audit;

public sealed class AuditWriter : IAuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IRequestContext _request;

    public AuditWriter(ConfigurationDbContext context, ITenantContext tenantContext, IClock clock, IRequestContext request)
    {
        _context = context;
        _tenantContext = tenantContext;
        _clock = clock;
        _request = request;
    }

    public void Append(string eventType, string? entityType = null, string? entityId = null, object? details = null, Guid? organizationId = null)
    {
        _context.AuditLogs.Add(Build(eventType, entityType, entityId, details, organizationId));
    }

    public async Task AppendAndSaveAsync(string eventType, string? entityType = null, string? entityId = null, object? details = null, Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        Append(eventType, entityType, entityId, details, organizationId);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private AuditLog Build(string eventType, string? entityType, string? entityId, object? details, Guid? organizationId)
    {
        var principal = _tenantContext.Principal;
        return new AuditLog
        {
            AuditLogId = Ids.New(),
            OrganizationId = organizationId ?? principal.OrganizationId,
            ActorType = principal.PrincipalType,
            ActorId = principal.AdministratorId ?? principal.UserId,
            ActorDisplayName = string.IsNullOrEmpty(principal.DisplayName) ? "anonymous" : principal.DisplayName,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details, JsonOptions),
            CorrelationId = _request.CorrelationId,
            IpAddress = _request.IpAddress,
            CreatedAt = _clock.UtcNow,
        };
    }
}
