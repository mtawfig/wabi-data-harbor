using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WabiInsight.Core.Applications;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Metadata;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Security;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Metadata;

/// <summary>
/// Metadata catalog (specification §17): discovery through the dialect adapter, merge with diff (new objects and
/// columns stay unapproved; removed ones are flagged, never deleted), EN/AR display names, business descriptions
/// and approval. Organization Administrators act through the tenant filter; the support executor calls
/// <see cref="RefreshForOrganizationAsync"/> under an approved grant with explicit organization scope.
/// </summary>
public sealed partial class MetadataService : IMetadataService
{
    private readonly ILogger<MetadataService> _logger;
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IConnectionStringProtector _protector;
    private readonly IDatabaseAdapterFactory _adapters;
    private readonly IAuditWriter _audit;
    private readonly IUsageEventWriter _usage;
    private readonly IClock _clock;

    public MetadataService(ConfigurationDbContext context, ITenantContext tenantContext, IConnectionStringProtector protector, IDatabaseAdapterFactory adapters, IAuditWriter audit, IUsageEventWriter usage, IClock clock, ILogger<MetadataService> logger)
    {
        _logger = logger;
        _context = context;
        _tenantContext = tenantContext;
        _protector = protector;
        _adapters = adapters;
        _audit = audit;
        _usage = usage;
        _clock = clock;
    }

    public async Task<MetadataRefreshResult> RefreshAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireAdmin();
        return await RefreshForOrganizationAsync(organizationId, applicationId, cancellationToken);
    }

    /// <summary>Explicitly scoped refresh used by both the administrator path and the support executor (platform scope).</summary>
    public async Task<MetadataRefreshResult> RefreshForOrganizationAsync(Guid organizationId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        var applications = _tenantContext.IsPlatformAdministrator ? _context.Applications.AsPlatformScope(_tenantContext) : _context.Applications;
        var application = await applications.SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.ApplicationId == applicationId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "application_not_found");
        if (application.IsBuiltIn || application.EncryptedConnectionString is null)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "application_connection_missing");
        }

        DiscoveredSchema discovered;
        try
        {
            discovered = await _adapters.Resolve(application.DatabaseType).DiscoverAsync(_protector.Unprotect(application.EncryptedConnectionString), cancellationToken);
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or Oracle.ManagedDataAccess.Client.OracleException or InvalidOperationException or TimeoutException or ArgumentException or InvalidCastException)
        {
            // The message may name the server; it goes to the server log only, never to the client.
            LogDiscoveryFailed(_logger, applicationId, ex.GetType().Name, ex.Message);
            application.LastConnectionTestAt = _clock.UtcNow;
            application.LastConnectionTestSucceeded = false;
            await _context.SaveChangesAsync(cancellationToken);
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "metadata_discovery_failed");
        }

        var now = _clock.UtcNow;
        var objectsQuery = Scoped(_context.DatabaseObjects).Where(o => o.ApplicationId == applicationId);
        var existingObjects = await objectsQuery.ToListAsync(cancellationToken);
        var existingColumns = await Scoped(_context.DatabaseColumns).Where(c => objectsQuery.Any(o => o.ObjectId == c.ObjectId)).ToListAsync(cancellationToken);
        var existingRelationships = await Scoped(_context.DatabaseRelationships).Where(r => r.ApplicationId == applicationId).ToListAsync(cancellationToken);

        var result = new RefreshCounters();
        var objectIndex = existingObjects.ToDictionary(o => (o.SchemaName, o.ObjectName), StringTupleComparer.Instance);
        var columnIndex = existingColumns.ToDictionary(c => (c.ObjectId, c.ColumnName), ObjectColumnComparer.Instance);
        var seenObjects = new HashSet<Guid>();
        var seenColumns = new HashSet<Guid>();

        foreach (var found in discovered.Objects)
        {
            if (!objectIndex.TryGetValue((found.SchemaName, found.ObjectName), out var entity))
            {
                entity = new DatabaseObject { ObjectId = Ids.New(), OrganizationId = organizationId, ApplicationId = applicationId, SchemaName = found.SchemaName, ObjectName = found.ObjectName, ObjectType = found.ObjectType, IsApproved = false, IsPresent = true, DiscoveredAt = now, LastSeenAt = now };
                _context.DatabaseObjects.Add(entity);
                objectIndex[(found.SchemaName, found.ObjectName)] = entity;
                result.ObjectsAdded++;
            }
            else
            {
                var changed = !entity.IsPresent || entity.ObjectType != found.ObjectType;
                entity.ObjectType = found.ObjectType;
                entity.LastSeenAt = now;
                if (!entity.IsPresent)
                {
                    entity.IsPresent = true;
                    entity.RemovedAt = null;
                }

                if (changed)
                {
                    result.ObjectsUpdated++;
                }
            }

            seenObjects.Add(entity.ObjectId);
            foreach (var column in found.Columns)
            {
                if (!columnIndex.TryGetValue((entity.ObjectId, column.ColumnName), out var columnEntity))
                {
                    columnEntity = new DatabaseColumn { ColumnId = Ids.New(), OrganizationId = organizationId, ObjectId = entity.ObjectId, ColumnName = column.ColumnName, IsApproved = false, IsPresent = true, DiscoveredAt = now, LastSeenAt = now };
                    _context.DatabaseColumns.Add(columnEntity);
                    columnIndex[(entity.ObjectId, column.ColumnName)] = columnEntity;
                    result.ColumnsAdded++;
                }
                else if (!columnEntity.IsPresent)
                {
                    columnEntity.IsPresent = true;
                    columnEntity.RemovedAt = null;
                    result.ColumnsAdded++;
                }

                columnEntity.DataType = column.DataType;
                columnEntity.MaxLength = column.MaxLength;
                columnEntity.Precision = column.Precision;
                columnEntity.Scale = column.Scale;
                columnEntity.IsNullable = column.IsNullable;
                columnEntity.IsPrimaryKey = column.IsPrimaryKey;
                columnEntity.OrdinalPosition = column.OrdinalPosition;
                columnEntity.LastSeenAt = now;
                seenColumns.Add(columnEntity.ColumnId);
            }
        }

        foreach (var missing in existingObjects.Where(o => o.IsPresent && !seenObjects.Contains(o.ObjectId)))
        {
            missing.IsPresent = false;
            missing.RemovedAt = now;
            result.ObjectsRemoved++;
        }

        foreach (var missing in existingColumns.Where(c => c.IsPresent && !seenColumns.Contains(c.ColumnId) && seenObjects.Contains(c.ObjectId)))
        {
            missing.IsPresent = false;
            missing.RemovedAt = now;
            result.ColumnsRemoved++;
        }

        var seenRelationships = new HashSet<Guid>();
        foreach (var found in discovered.Relationships)
        {
            if (!objectIndex.TryGetValue((found.FromSchema, found.FromObject), out var fromObject) || !objectIndex.TryGetValue((found.ToSchema, found.ToObject), out var toObject)
                || !columnIndex.TryGetValue((fromObject.ObjectId, found.FromColumn), out var fromColumn) || !columnIndex.TryGetValue((toObject.ObjectId, found.ToColumn), out var toColumn))
            {
                continue;
            }

            var relationship = existingRelationships.FirstOrDefault(r => string.Equals(r.ConstraintName, found.ConstraintName, StringComparison.OrdinalIgnoreCase) && r.FromColumnId == fromColumn.ColumnId);
            if (relationship is null)
            {
                relationship = new DatabaseRelationship { RelationshipId = Ids.New(), OrganizationId = organizationId, ApplicationId = applicationId, ConstraintName = found.ConstraintName, FromObjectId = fromObject.ObjectId, FromColumnId = fromColumn.ColumnId, ToObjectId = toObject.ObjectId, ToColumnId = toColumn.ColumnId, IsPresent = true, DiscoveredAt = now, LastSeenAt = now };
                _context.DatabaseRelationships.Add(relationship);
                existingRelationships.Add(relationship);
                result.RelationshipsAdded++;
            }
            else
            {
                if (!relationship.IsPresent)
                {
                    relationship.IsPresent = true;
                    result.RelationshipsAdded++;
                }

                relationship.LastSeenAt = now;
            }

            seenRelationships.Add(relationship.RelationshipId);
        }

        foreach (var missing in existingRelationships.Where(r => r.IsPresent && !seenRelationships.Contains(r.RelationshipId)))
        {
            missing.IsPresent = false;
            result.RelationshipsRemoved++;
        }

        application.LastMetadataRefreshAt = now;
        application.LastConnectionTestAt = now;
        application.LastConnectionTestSucceeded = true;
        var summary = new MetadataRefreshResult(result.ObjectsAdded, result.ObjectsUpdated, result.ObjectsRemoved, result.ColumnsAdded, result.ColumnsRemoved, result.RelationshipsAdded, result.RelationshipsRemoved, now);
        _audit.Append(AuditEvents.MetadataRefreshed, "Application", applicationId.ToString(), summary, organizationId);
        _usage.Append(UsageEventTypes.MetadataRefreshed, "Application", applicationId.ToString(), quantity: discovered.Objects.Count, organizationId: organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return summary;
    }

    public async Task<PagedResult<ObjectSummaryDto>> ListObjectsAsync(ObjectListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        RequireAdmin();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 500);
        var objects = _context.DatabaseObjects.AsNoTracking().Where(o => o.ApplicationId == query.ApplicationId);
        if (!query.IncludeRemoved)
        {
            objects = objects.Where(o => o.IsPresent);
        }

        if (query.Approved is { } approved)
        {
            objects = objects.Where(o => o.IsApproved == approved);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            objects = objects.Where(o => o.ObjectName.Contains(term) || o.SchemaName.Contains(term) || (o.DisplayNameEn != null && o.DisplayNameEn.Contains(term)) || (o.DisplayNameAr != null && o.DisplayNameAr.Contains(term)));
        }

        var total = await objects.CountAsync(cancellationToken);
        var items = await ProjectSummary(objects.OrderBy(o => o.SchemaName).ThenBy(o => o.ObjectName).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return new PagedResult<ObjectSummaryDto>(items, page, pageSize, total);
    }

    public async Task<ObjectDetailDto?> GetObjectAsync(Guid objectId, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        var summary = await ProjectSummary(_context.DatabaseObjects.AsNoTracking().Where(o => o.ObjectId == objectId)).SingleOrDefaultAsync(cancellationToken);
        if (summary is null)
        {
            return null;
        }

        return await DetailAsync(summary, cancellationToken);
    }

    public async Task<ObjectDetailDto> UpdateObjectAsync(Guid objectId, UpdateObjectCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireAdmin();
        ValidateText(command.DisplayNameEn, command.DisplayNameAr, command.BusinessDescription);
        var entity = await _context.DatabaseObjects.SingleOrDefaultAsync(o => o.ObjectId == objectId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "object_not_found");
        if (command.IsApproved && !entity.IsPresent)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "object_removed_from_source");
        }

        entity.DisplayNameEn = Clean(command.DisplayNameEn);
        entity.DisplayNameAr = Clean(command.DisplayNameAr);
        entity.BusinessDescription = Clean(command.BusinessDescription);
        SetApproval(entity, command.IsApproved);
        _audit.Append(AuditEvents.MetadataObjectUpdated, "DatabaseObject", objectId.ToString(), new { entity.SchemaName, entity.ObjectName, entity.IsApproved }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return (await GetObjectAsync(objectId, cancellationToken))!;
    }

    public async Task<ColumnDto> UpdateColumnAsync(Guid columnId, UpdateColumnCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireAdmin();
        ValidateText(command.DisplayNameEn, command.DisplayNameAr, command.BusinessDescription);
        var column = await _context.DatabaseColumns.SingleOrDefaultAsync(c => c.ColumnId == columnId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "column_not_found");
        if (command.IsApproved && !column.IsPresent)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "column_removed_from_source");
        }

        column.DisplayNameEn = Clean(command.DisplayNameEn);
        column.DisplayNameAr = Clean(command.DisplayNameAr);
        column.BusinessDescription = Clean(command.BusinessDescription);
        column.Classification = command.Classification;
        column.IsApproved = command.IsApproved;
        if (command.SemanticRole is { } role)
        {
            column.SemanticRole = role == ColumnSemanticRole.Unspecified ? null : role;
        }

        _audit.Append(AuditEvents.MetadataColumnUpdated, "DatabaseColumn", columnId.ToString(), new { column.ColumnName, column.IsApproved, column.Classification, column.SemanticRole }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(column);
    }

    public async Task<ObjectDetailDto> ApproveObjectWithColumnsAsync(Guid objectId, bool approve, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireAdmin();
        var entity = await _context.DatabaseObjects.SingleOrDefaultAsync(o => o.ObjectId == objectId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "object_not_found");
        if (approve && !entity.IsPresent)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "object_removed_from_source");
        }

        SetApproval(entity, approve);
        var columns = await _context.DatabaseColumns.Where(c => c.ObjectId == objectId && c.IsPresent).ToListAsync(cancellationToken);
        foreach (var column in columns)
        {
            column.IsApproved = approve;
        }

        _audit.Append(AuditEvents.MetadataObjectUpdated, "DatabaseObject", objectId.ToString(), new { entity.SchemaName, entity.ObjectName, entity.IsApproved, columns = columns.Count }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return (await GetObjectAsync(objectId, cancellationToken))!;
    }

    private void SetApproval(DatabaseObject entity, bool approve)
    {
        if (approve && !entity.IsApproved)
        {
            entity.ApprovedAt = _clock.UtcNow;
            entity.ApprovedByUserId = _tenantContext.Principal.UserId;
        }
        else if (!approve)
        {
            entity.ApprovedAt = null;
            entity.ApprovedByUserId = null;
        }

        entity.IsApproved = approve;
    }

    private async Task<ObjectDetailDto> DetailAsync(ObjectSummaryDto summary, CancellationToken cancellationToken)
    {
        var description = await _context.DatabaseObjects.AsNoTracking().Where(o => o.ObjectId == summary.ObjectId).Select(o => o.BusinessDescription).SingleAsync(cancellationToken);
        var columns = await _context.DatabaseColumns.AsNoTracking().Where(c => c.ObjectId == summary.ObjectId).OrderBy(c => c.OrdinalPosition).ToListAsync(cancellationToken);
        var relationships = await _context.DatabaseRelationships.AsNoTracking()
            .Where(r => r.FromObjectId == summary.ObjectId || r.ToObjectId == summary.ObjectId)
            .Select(r => new RelationshipDto(
                r.RelationshipId, r.ConstraintName,
                r.FromObjectId, _context.DatabaseObjects.Where(o => o.ObjectId == r.FromObjectId).Select(o => o.SchemaName + "." + o.ObjectName).First(), _context.DatabaseColumns.Where(c => c.ColumnId == r.FromColumnId).Select(c => c.ColumnName).First(),
                r.ToObjectId, _context.DatabaseObjects.Where(o => o.ObjectId == r.ToObjectId).Select(o => o.SchemaName + "." + o.ObjectName).First(), _context.DatabaseColumns.Where(c => c.ColumnId == r.ToColumnId).Select(c => c.ColumnName).First(),
                r.IsPresent))
            .ToListAsync(cancellationToken);
        return new ObjectDetailDto(summary, description, columns.Select(ToDto).ToList(), relationships);
    }

    private IQueryable<ObjectSummaryDto> ProjectSummary(IQueryable<DatabaseObject> objects)
        => objects.Select(o => new ObjectSummaryDto(
            o.ObjectId, o.ApplicationId, o.SchemaName, o.ObjectName, o.ObjectType, o.DisplayNameEn, o.DisplayNameAr, o.IsApproved, o.IsPresent,
            _context.DatabaseColumns.Count(c => c.ObjectId == o.ObjectId && c.IsPresent),
            _context.DatabaseColumns.Count(c => c.ObjectId == o.ObjectId && c.IsPresent && c.IsApproved),
            o.DiscoveredAt, o.LastSeenAt));

    private static ColumnDto ToDto(DatabaseColumn c)
        => new(c.ColumnId, c.ColumnName, c.DataType, c.MaxLength, c.Precision, c.Scale, c.IsNullable, c.IsPrimaryKey, c.OrdinalPosition, c.DisplayNameEn, c.DisplayNameAr, c.BusinessDescription, c.Classification, c.IsApproved, c.IsPresent,
            c.SemanticRole, c.SemanticRole ?? Core.Monitoring.ColumnRoleSuggester.Suggest(c.ColumnName, c.DataType, c.IsPrimaryKey));

    private IQueryable<T> Scoped<T>(IQueryable<T> query)
        where T : class, ITenantOwned
        => _tenantContext.IsPlatformAdministrator ? query.AsPlatformScope(_tenantContext) : query;

    private Guid RequireAdmin()
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        if (!_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            throw new TenantContextException("Only organization administrators can manage metadata.");
        }

        return organizationId;
    }

    private static void ValidateText(string? en, string? ar, string? description)
    {
        if (en is { Length: > 200 } || ar is { Length: > 200 } || description is { Length: > 2000 })
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "metadata_text_too_long");
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Metadata discovery failed for application {ApplicationId}: {ExceptionType}: {Message}")]
    private static partial void LogDiscoveryFailed(ILogger logger, Guid applicationId, string exceptionType, string message);

    private sealed class RefreshCounters
    {
        public int ObjectsAdded;
        public int ObjectsUpdated;
        public int ObjectsRemoved;
        public int ColumnsAdded;
        public int ColumnsRemoved;
        public int RelationshipsAdded;
        public int RelationshipsRemoved;
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string, string)>
    {
        public static readonly StringTupleComparer Instance = new();

        public bool Equals((string, string) x, (string, string) y) => string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string, string) obj) => HashCode.Combine(obj.Item1.ToUpperInvariant(), obj.Item2.ToUpperInvariant());
    }

    private sealed class ObjectColumnComparer : IEqualityComparer<(Guid, string)>
    {
        public static readonly ObjectColumnComparer Instance = new();

        public bool Equals((Guid, string) x, (Guid, string) y) => x.Item1 == y.Item1 && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((Guid, string) obj) => HashCode.Combine(obj.Item1, obj.Item2.ToUpperInvariant());
    }
}
