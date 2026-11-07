using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Monitoring;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Monitoring;

/// <summary>
/// Maps the objects and columns a plan referenced (by name) to the organization's catalogue and adds one
/// <see cref="AnalysisAccessFact"/> per object to the ambient unit of work. Tenant filters scope every lookup to
/// the caller's organization; unknown names are kept as text with no object id.
/// </summary>
public sealed class AccessFactRecorder : IAccessFactRecorder
{
    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public AccessFactRecorder(ConfigurationDbContext context, ITenantContext tenantContext, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task RecordAsync(AccessFactRecording recording, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recording);
        var organizationId = _tenantContext.RequireOrganizationId();
        var principal = _tenantContext.Principal;
        var userId = principal.UserId ?? throw new TenantContextException("Access facts need an organization user.");
        var summaries = AccessFactExtractor.Summarize(recording.Queries);
        if (summaries.Count == 0)
        {
            return;
        }

        var catalogue = await _context.DatabaseObjects.AsNoTracking()
            .Where(o => o.ApplicationId == recording.ApplicationId)
            .Select(o => new { o.ObjectId, o.SchemaName, o.ObjectName })
            .ToListAsync(cancellationToken);
        var byName = catalogue.ToDictionary(o => $"{o.SchemaName}.{o.ObjectName}", o => o.ObjectId, StringComparer.OrdinalIgnoreCase);
        var objectIds = summaries.Select(s => byName.GetValueOrDefault($"{s.Schema}.{s.ObjectName}")).Where(id => id != Guid.Empty).Distinct().ToList();
        var columns = objectIds.Count == 0
            ? []
            : await _context.DatabaseColumns.AsNoTracking()
                .Where(c => objectIds.Contains(c.ObjectId))
                .Select(c => new { c.ColumnId, c.ObjectId, c.ColumnName, c.Classification, c.IsPrimaryKey, c.SemanticRole })
                .ToListAsync(cancellationToken);

        var now = _clock.UtcNow;
        var roles = string.Join(',', principal.Roles.Order(StringComparer.Ordinal));
        foreach (var summary in summaries)
        {
            var objectId = byName.GetValueOrDefault($"{summary.Schema}.{summary.ObjectName}");
            var objectColumns = objectId == Guid.Empty ? [] : columns.Where(c => c.ObjectId == objectId).ToList();
            var referenced = objectColumns.Where(c => summary.Columns.Contains(c.ColumnName)).ToList();
            var filtered = objectColumns.Where(c => summary.FilterColumns.Contains(c.ColumnName)).ToList();
            var identifierValues = filtered
                .Where(c => c.IsPrimaryKey || c.SemanticRole is ColumnSemanticRole.Identifier or ColumnSemanticRole.PersonIdentifier or ColumnSemanticRole.AccountIdentifier or ColumnSemanticRole.DeviceIdentifier)
                .Sum(c => summary.FilterValueCounts.GetValueOrDefault(c.ColumnName));

            _context.AnalysisAccessFacts.Add(new AnalysisAccessFact
            {
                FactId = Ids.New(),
                OrganizationId = organizationId,
                UserId = userId,
                DepartmentId = principal.DepartmentId,
                RoleCodes = roles.Length <= 400 ? roles : roles[..400],
                ApplicationId = recording.ApplicationId,
                HistoryVersionId = recording.HistoryVersionId,
                MetricId = recording.MetricId,
                ObjectId = objectId == Guid.Empty ? null : objectId,
                SchemaName = Truncate(summary.Schema, 128),
                ObjectName = Truncate(summary.ObjectName, 128),
                IsPrimaryObject = summary.IsPrimary,
                AccessedAt = now,
                RowCount = recording.Outcome == AccessOutcome.Succeeded ? summary.RowCount : 0,
                HighestClassification = referenced.Count == 0 ? DataClassification.Internal : referenced.Max(c => c.Classification),
                ColumnIdsJson = JsonSerializer.Serialize(referenced.Select(c => c.ColumnId)),
                ColumnCount = referenced.Count,
                FilterColumnIdsJson = JsonSerializer.Serialize(filtered.Select(c => c.ColumnId)),
                FilterValueCount = summary.FilterValueCounts.Values.Sum(),
                IdentifierFilterValueCount = identifierValues,
                IsExport = recording.IsExport,
                ExportFormat = recording.ExportFormat,
                Outcome = recording.Outcome,
                FailureCode = recording.FailureCode is null ? null : Truncate(recording.FailureCode, 100),
            });
        }
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
