using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Insights;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Insights;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Insights;

/// <summary>Global Platform Insights catalog (specification §12): Platform Administrators only. Never reads organization rows.</summary>
public sealed partial class InsightsCatalogService : IInsightsCatalogService
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IInsightsConnectionFactory _connections;
    private readonly PlatformInsightsAdapter _adapter;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public InsightsCatalogService(ConfigurationDbContext context, ITenantContext tenantContext, IInsightsConnectionFactory connections, IAuditWriter audit, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _connections = connections;
        _adapter = new PlatformInsightsAdapter(connections);
        _audit = audit;
        _clock = clock;
    }

    public async Task<InsightsCatalogDto> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var objects = await LoadObjectsAsync(includeUnapproved: true, cancellationToken);
        var metrics = await LoadMetricsAsync(includeUnapproved: true, cancellationToken);
        return new InsightsCatalogDto(await CheckSourceAsync(cancellationToken), objects, metrics);
    }

    public async Task<InsightsSourceStatus> CheckSourceAsync(CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var test = await _connections.TestAsync(cancellationToken);
        var viewCount = 0;
        if (test.Succeeded)
        {
            viewCount = (await _adapter.DiscoverAsync(string.Empty, cancellationToken)).Objects.Count;
        }

        return new InsightsSourceStatus(_connections.DedicatedLoginConfigured, test.Succeeded, test.ReadOnlyVerified, viewCount, test.Warnings, test.Error, _clock.UtcNow);
    }

    public async Task<InsightsSyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var discovered = await _adapter.DiscoverAsync(string.Empty, cancellationToken);
        var objects = await _context.InsightsObjects.ToListAsync(cancellationToken);
        var columns = await _context.InsightsColumns.ToListAsync(cancellationToken);
        var now = _clock.UtcNow;
        int objectsAdded = 0, objectsRemoved = 0, columnsAdded = 0, columnsRemoved = 0, columnsChanged = 0;

        foreach (var view in discovered.Objects)
        {
            var entity = objects.FirstOrDefault(o => string.Equals(o.ViewName, view.ObjectName, StringComparison.OrdinalIgnoreCase));
            if (entity is null)
            {
                entity = new InsightsObject { InsightsObjectId = Ids.New(), ViewName = view.ObjectName, DisplayNameEn = view.ObjectName, DisplayNameAr = view.ObjectName, IsApproved = false, IsPresent = true, SortOrder = 1000, UpdatedAt = now, UpdatedByAdministratorId = _tenantContext.Principal.AdministratorId };
                _context.InsightsObjects.Add(entity);
                objects.Add(entity);
                objectsAdded++;
            }
            else if (!entity.IsPresent)
            {
                entity.IsPresent = true;
                entity.UpdatedAt = now;
            }

            var existing = columns.Where(c => c.InsightsObjectId == entity.InsightsObjectId).ToList();
            foreach (var column in view.Columns)
            {
                var row = existing.FirstOrDefault(c => string.Equals(c.ColumnName, column.ColumnName, StringComparison.OrdinalIgnoreCase));
                if (row is null)
                {
                    var isOrganization = string.Equals(column.ColumnName, InsightsSqlBuilder.OrganizationColumn, StringComparison.OrdinalIgnoreCase);
                    row = new InsightsColumn { InsightsColumnId = Ids.New(), InsightsObjectId = entity.InsightsObjectId, ColumnName = column.ColumnName, DataType = column.DataType, IsNullable = column.IsNullable, OrdinalPosition = column.OrdinalPosition, DisplayNameEn = column.ColumnName, DisplayNameAr = column.ColumnName, IsApproved = false, IsPresent = !isOrganization };
                    if (isOrganization)
                    {
                        continue; // the organization column is the predicate, never a catalog column
                    }

                    _context.InsightsColumns.Add(row);
                    columns.Add(row);
                    columnsAdded++;
                }
                else
                {
                    if (!row.IsPresent || row.DataType != column.DataType || row.IsNullable != column.IsNullable || row.OrdinalPosition != column.OrdinalPosition)
                    {
                        row.IsPresent = true;
                        row.DataType = column.DataType;
                        row.IsNullable = column.IsNullable;
                        row.OrdinalPosition = column.OrdinalPosition;
                        columnsChanged++;
                    }
                }
            }

            foreach (var gone in existing.Where(c => c.IsPresent && !view.Columns.Any(v => string.Equals(v.ColumnName, c.ColumnName, StringComparison.OrdinalIgnoreCase))))
            {
                gone.IsPresent = false;
                columnsRemoved++;
            }
        }

        foreach (var missing in objects.Where(o => o.IsPresent && !discovered.Objects.Any(v => string.Equals(v.ObjectName, o.ViewName, StringComparison.OrdinalIgnoreCase))))
        {
            missing.IsPresent = false;
            missing.UpdatedAt = now;
            objectsRemoved++;
        }

        var result = new InsightsSyncResult(objectsAdded, objectsRemoved, columnsAdded, columnsRemoved, columnsChanged);
        _audit.Append(AuditEvents.InsightsCatalogSynced, "InsightsCatalog", InsightsSchema.SchemaName, result);
        await _context.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<InsightsObjectDto> UpdateObjectAsync(Guid objectId, UpdateInsightsObjectCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequirePlatformAdministrator();
        var entity = await _context.InsightsObjects.SingleOrDefaultAsync(o => o.InsightsObjectId == objectId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "insights_object_not_found");
        if (command.IsApproved && !entity.IsPresent)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "insights_object_removed");
        }

        entity.DisplayNameEn = Required(command.DisplayNameEn, "name_en_invalid");
        entity.DisplayNameAr = Required(command.DisplayNameAr, "name_ar_invalid");
        entity.Description = Trim(command.Description);
        entity.IsApproved = command.IsApproved;
        entity.SortOrder = command.SortOrder;
        entity.UpdatedAt = _clock.UtcNow;
        entity.UpdatedByAdministratorId = _tenantContext.Principal.AdministratorId;
        _audit.Append(AuditEvents.InsightsObjectUpdated, "InsightsObject", entity.InsightsObjectId.ToString(), new { entity.ViewName, entity.IsApproved });
        await _context.SaveChangesAsync(cancellationToken);
        return (await LoadObjectsAsync(true, cancellationToken)).Single(o => o.ObjectId == objectId);
    }

    public async Task<InsightsColumnDto> UpdateColumnAsync(Guid columnId, UpdateInsightsColumnCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequirePlatformAdministrator();
        var entity = await _context.InsightsColumns.SingleOrDefaultAsync(c => c.InsightsColumnId == columnId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "insights_column_not_found");
        if (command.IsApproved && !entity.IsPresent)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "insights_column_removed");
        }

        entity.DisplayNameEn = Required(command.DisplayNameEn, "name_en_invalid");
        entity.DisplayNameAr = Required(command.DisplayNameAr, "name_ar_invalid");
        entity.Description = Trim(command.Description);
        entity.IsApproved = command.IsApproved;
        _audit.Append(AuditEvents.InsightsColumnUpdated, "InsightsColumn", entity.InsightsColumnId.ToString(), new { entity.ColumnName, entity.IsApproved });
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task<InsightsMetricDto> CreateMetricAsync(SaveInsightsMetricCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequirePlatformAdministrator();
        var code = ValidateCode(command.MetricCode);
        if (await _context.InsightsMetrics.AnyAsync(m => m.MetricCode == code, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "insights_metric_code_exists");
        }

        var entity = new InsightsMetric { InsightsMetricId = Ids.New(), MetricCode = code };
        await ApplyAsync(entity, command, cancellationToken);
        _context.InsightsMetrics.Add(entity);
        _audit.Append(AuditEvents.InsightsMetricChanged, "InsightsMetric", entity.InsightsMetricId.ToString(), new { entity.MetricCode, action = "created", entity.IsApproved });
        await _context.SaveChangesAsync(cancellationToken);
        return (await LoadMetricsAsync(true, cancellationToken)).Single(m => m.MetricId == entity.InsightsMetricId);
    }

    public async Task<InsightsMetricDto> UpdateMetricAsync(Guid metricId, SaveInsightsMetricCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequirePlatformAdministrator();
        var entity = await _context.InsightsMetrics.SingleOrDefaultAsync(m => m.InsightsMetricId == metricId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "insights_metric_not_found");
        var code = ValidateCode(command.MetricCode);
        if (await _context.InsightsMetrics.AnyAsync(m => m.MetricCode == code && m.InsightsMetricId != metricId, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "insights_metric_code_exists");
        }

        entity.MetricCode = code;
        await ApplyAsync(entity, command, cancellationToken);
        _audit.Append(AuditEvents.InsightsMetricChanged, "InsightsMetric", entity.InsightsMetricId.ToString(), new { entity.MetricCode, action = "updated", entity.IsApproved });
        await _context.SaveChangesAsync(cancellationToken);
        return (await LoadMetricsAsync(true, cancellationToken)).Single(m => m.MetricId == metricId);
    }

    public async Task DeleteMetricAsync(Guid metricId, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var entity = await _context.InsightsMetrics.SingleOrDefaultAsync(m => m.InsightsMetricId == metricId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "insights_metric_not_found");
        _context.InsightsMetrics.Remove(entity);
        _audit.Append(AuditEvents.InsightsMetricChanged, "InsightsMetric", entity.InsightsMetricId.ToString(), new { entity.MetricCode, action = "deleted" });
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<BuiltQuery> PreviewSqlAsync(Guid metricId, MetricQueryRequest request, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var metric = await _context.InsightsMetrics.AsNoTracking().SingleOrDefaultAsync(m => m.InsightsMetricId == metricId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "insights_metric_not_found");
        var shape = await ShapeAsync(_context, metric.InsightsObjectId, requireApproved: false, cancellationToken);
        return InsightsSqlBuilder.Build(ToDefinition(metric), shape, request, Guid.Empty);
    }

    /// <summary>Approved view shape used by the builder: approved, present columns only.</summary>
    internal static async Task<InsightsViewShape> ShapeAsync(ConfigurationDbContext context, Guid objectId, bool requireApproved, CancellationToken cancellationToken)
    {
        var entity = await context.InsightsObjects.AsNoTracking().SingleOrDefaultAsync(o => o.InsightsObjectId == objectId && o.IsPresent && (!requireApproved || o.IsApproved), cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "insights_object_not_found");
        var columns = await context.InsightsColumns.AsNoTracking().Where(c => c.InsightsObjectId == objectId && c.IsPresent && (!requireApproved || c.IsApproved)).ToListAsync(cancellationToken);
        return new InsightsViewShape(entity.ViewName, columns.ToDictionary(c => c.ColumnName, c => c.DataType, StringComparer.OrdinalIgnoreCase));
    }

    internal static MetricDefinition ToDefinition(InsightsMetric metric)
        => new(metric.MetricCode, metric.Aggregate, metric.ColumnName, metric.DateColumn, ParseFilters(metric.FiltersJson));

    internal static IReadOnlyList<MetricFilter> ParseFilters(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<MetricFilter>>(string.IsNullOrWhiteSpace(json) ? "[]" : json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "insights_filter_invalid");
        }
    }

    private async Task ApplyAsync(InsightsMetric entity, SaveInsightsMetricCommand command, CancellationToken cancellationToken)
    {
        if (!await _context.InsightsObjects.AnyAsync(o => o.InsightsObjectId == command.ObjectId && o.IsPresent, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "insights_object_not_found");
        }

        entity.InsightsObjectId = command.ObjectId;
        entity.DisplayNameEn = Required(command.DisplayNameEn, "name_en_invalid");
        entity.DisplayNameAr = Required(command.DisplayNameAr, "name_ar_invalid");
        entity.Description = Trim(command.Description);
        entity.Aggregate = command.Aggregate;
        entity.ColumnName = Trim(command.ColumnName);
        entity.DateColumn = Trim(command.DateColumn);
        entity.FiltersJson = JsonSerializer.Serialize(command.Filters ?? [], JsonOptions);
        entity.Unit = string.IsNullOrWhiteSpace(command.Unit) ? "count" : command.Unit.Trim();
        entity.IsApproved = command.IsApproved;
        entity.SortOrder = command.SortOrder;
        entity.UpdatedAt = _clock.UtcNow;
        entity.UpdatedByAdministratorId = _tenantContext.Principal.AdministratorId;

        // The definition must compile against the full shape of its view; errors surface as validation codes.
        var shape = await ShapeAsync(_context, command.ObjectId, requireApproved: false, cancellationToken);
        InsightsSqlBuilder.Build(ToDefinition(entity), shape, new MetricQueryRequest(null, null, null, null, null), Guid.Empty);
        if (entity.DateColumn is not null)
        {
            InsightsSqlBuilder.Build(ToDefinition(entity), shape, new MetricQueryRequest(_clock.UtcNow.AddDays(-1), _clock.UtcNow, Core.Platform.MetricBucket.Day, null, null), Guid.Empty);
        }
    }

    private async Task<List<InsightsObjectDto>> LoadObjectsAsync(bool includeUnapproved, CancellationToken cancellationToken)
    {
        var objects = await _context.InsightsObjects.AsNoTracking().Where(o => includeUnapproved || (o.IsApproved && o.IsPresent)).OrderBy(o => o.SortOrder).ThenBy(o => o.ViewName).ToListAsync(cancellationToken);
        var columns = await _context.InsightsColumns.AsNoTracking().Where(c => includeUnapproved || (c.IsApproved && c.IsPresent)).OrderBy(c => c.OrdinalPosition).ToListAsync(cancellationToken);
        return objects.Select(o => new InsightsObjectDto(o.InsightsObjectId, o.ViewName, o.DisplayNameEn, o.DisplayNameAr, o.Description, o.IsApproved, o.IsPresent, o.SortOrder, o.UpdatedAt,
            columns.Where(c => c.InsightsObjectId == o.InsightsObjectId).Select(ToDto).ToList())).ToList();
    }

    private async Task<List<InsightsMetricDto>> LoadMetricsAsync(bool includeUnapproved, CancellationToken cancellationToken)
    {
        var views = await _context.InsightsObjects.AsNoTracking().ToDictionaryAsync(o => o.InsightsObjectId, o => (o.ViewName, Visible: o.IsApproved && o.IsPresent), cancellationToken);
        var metrics = await _context.InsightsMetrics.AsNoTracking().Where(m => includeUnapproved || m.IsApproved).OrderBy(m => m.SortOrder).ThenBy(m => m.MetricCode).ToListAsync(cancellationToken);
        return metrics.Where(m => includeUnapproved || (views.TryGetValue(m.InsightsObjectId, out var v) && v.Visible))
            .Select(m => new InsightsMetricDto(m.InsightsMetricId, m.MetricCode, m.InsightsObjectId, views.GetValueOrDefault(m.InsightsObjectId).ViewName ?? string.Empty, m.DisplayNameEn, m.DisplayNameAr, m.Description, m.Aggregate, m.ColumnName, m.DateColumn, ParseFilters(m.FiltersJson), m.Unit, m.IsApproved, m.SortOrder, m.UpdatedAt))
            .ToList();
    }

    internal static InsightsColumnDto ToDto(InsightsColumn c) => new(c.InsightsColumnId, c.ColumnName, c.DataType, c.IsNullable, c.OrdinalPosition, c.DisplayNameEn, c.DisplayNameAr, c.Description, c.IsApproved, c.IsPresent);

    internal async Task<OrganizationInsightsCatalog> ApprovedCatalogAsync(CancellationToken cancellationToken)
        => new(await LoadObjectsAsync(false, cancellationToken), await LoadMetricsAsync(false, cancellationToken));

    private static string ValidateCode(string code)
    {
        var trimmed = (code ?? string.Empty).Trim();
        return MetricCode().IsMatch(trimmed) ? trimmed : throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "insights_metric_code_invalid");
    }

    private static string Required(string value, string code) => string.IsNullOrWhiteSpace(value) ? throw new DomainException(OrganizationOperationOutcome.ValidationFailed, code) : value.Trim();

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void RequirePlatformAdministrator()
    {
        if (!_tenantContext.IsPlatformAdministrator)
        {
            throw new TenantContextException("Only platform administrators manage the Platform Insights catalog.");
        }
    }

    [GeneratedRegex("^[a-z][a-zA-Z0-9]*(\\.[a-zA-Z0-9]+){0,3}$")]
    private static partial Regex MetricCode();
}
