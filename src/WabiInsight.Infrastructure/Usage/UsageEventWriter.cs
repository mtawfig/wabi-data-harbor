using System.Text.Json;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Usage;

public sealed class UsageEventWriter : IUsageEventWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IRequestContext _request;
    private readonly IClock _clock;

    public UsageEventWriter(ConfigurationDbContext context, ITenantContext tenantContext, IRequestContext request, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _request = request;
        _clock = clock;
    }

    public void Append(string eventType, string? entityType = null, string? entityId = null, long quantity = 1, bool succeeded = true, object? metadata = null, Guid? organizationId = null, PrincipalType? actorType = null, Guid? actorId = null, Guid? departmentId = null)
    {
        var principal = _tenantContext.Principal;
        _context.UsageEvents.Add(new UsageEvent
        {
            UsageEventId = Ids.New(),
            OrganizationId = organizationId ?? principal.OrganizationId,
            ActorType = actorType ?? principal.PrincipalType,
            ActorId = actorId ?? principal.AdministratorId ?? principal.UserId,
            DepartmentId = departmentId ?? principal.DepartmentId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            Quantity = quantity,
            Succeeded = succeeded,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata, JsonOptions),
            CorrelationId = _request.CorrelationId,
            CreatedAt = _clock.UtcNow,
        });
    }

    public async Task AppendAndSaveAsync(string eventType, string? entityType = null, string? entityId = null, long quantity = 1, bool succeeded = true, object? metadata = null, Guid? organizationId = null, PrincipalType? actorType = null, Guid? actorId = null, Guid? departmentId = null, CancellationToken cancellationToken = default)
    {
        Append(eventType, entityType, entityId, quantity, succeeded, metadata, organizationId, actorType, actorId, departmentId);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
